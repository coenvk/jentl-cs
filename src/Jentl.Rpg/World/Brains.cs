using Jentl.Puzzle;
using Jentl.Rpg.Maps;

namespace Jentl.Rpg.World;

/// <summary>What an actor does with its turn.</summary>
public abstract record ActorAction
{
    public static ActorAction Wait { get; } = new WaitAction();
}

public sealed record WaitAction : ActorAction;

public sealed record MoveAction(GridDirection Direction) : ActorAction;

public sealed record AttackAction(Actor Target) : ActorAction;

/// <summary>Decides an actor's turns. A brain belongs to one actor, so it may remember things between turns.</summary>
public interface IBrain
{
    ActorAction Decide(Actor self, RpgWorld world);
}

/// <summary>Brains by the name content uses for them; games can register their own.</summary>
public static class Brains
{
    private static readonly Dictionary<string, Func<IBrain>> Factories = new(StringComparer.Ordinal)
    {
        ["still"] = () => new StillBrain(),
        ["wander"] = () => new WanderBrain(),
        ["hunter"] = () => new HunterBrain(),
    };

    public static void Register(string name, Func<IBrain> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        lock (Factories)
        {
            Factories[name] = factory;
        }
    }

    public static bool IsKnown(string name)
    {
        lock (Factories)
        {
            return Factories.ContainsKey(name);
        }
    }

    public static IBrain Create(string name)
    {
        lock (Factories)
        {
            return Factories.TryGetValue(name, out var factory)
                ? factory()
                : throw new ContentException($"There is no brain '{name}'; known: {string.Join(", ", Factories.Keys)}.");
        }
    }
}

/// <summary>Stands still: shopkeepers, quest givers.</summary>
public sealed class StillBrain : IBrain
{
    public ActorAction Decide(Actor self, RpgWorld world) => ActorAction.Wait;
}

/// <summary>Ambles about now and then, staying within <see cref="Range"/> steps of home.</summary>
public sealed class WanderBrain : IBrain
{
    public int Range { get; init; } = 3;

    /// <summary>The chance of taking a step on a turn.</summary>
    public double Restlessness { get; init; } = 0.35;

    public ActorAction Decide(Actor self, RpgWorld world)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(world);
#pragma warning disable CA5394 // Behaviour, not security relevant.
        if (world.Random.NextDouble() >= Restlessness)
        {
            return ActorAction.Wait;
        }

        var options = GridDirections.All
            .Where(d => world.Area.CanEnter(self.Position.Step(d)) && Pathfinder.Distance(self.Position.Step(d), self.Home) <= Range)
            .ToArray();
        return options.Length == 0 ? ActorAction.Wait : new MoveAction(options[world.Random.Next(options.Length)]);
#pragma warning restore CA5394
    }
}

/// <summary>
/// Hunts the player: attacks when next to them, walks towards them while it can see them (within its
/// sight and not behind opaque tiles), goes to where it last saw them, and otherwise wanders.
/// </summary>
public sealed class HunterBrain : IBrain
{
    private readonly WanderBrain _idle = new() { Range = 4, Restlessness = 0.25 };
    private Cell? _lastSeen;

    public ActorAction Decide(Actor self, RpgWorld world)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(world);
        var player = world.Player;
        if (!player.IsAlive)
        {
            return ActorAction.Wait;
        }

        var distance = Pathfinder.Distance(self.Position, player.Position);
        if (distance == 1)
        {
            return new AttackAction(player);
        }

        if (distance <= self.Definition.Sight && FieldOfView.HasLineOfSight(self.Position, player.Position, world.Area.Map.IsOpaque))
        {
            _lastSeen = player.Position;
        }

        if (_lastSeen is { } target)
        {
            if (target == self.Position)
            {
                _lastSeen = null;
            }
            else if (Pathfinder.Find(self.Position, target, world.Area.CanEnter, maxSearch: 400) is [var next, ..]
                && self.Position.DirectionTo(next) is { } direction)
            {
                return new MoveAction(direction);
            }
            else
            {
                _lastSeen = null;
            }
        }

        return _idle.Decide(self, world);
    }
}
