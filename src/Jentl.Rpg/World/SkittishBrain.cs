using Fluffy;
using Jentl.Puzzle;
using Jentl.Rpg.Maps;

namespace Jentl.Rpg.World;

/// <summary>
/// A monster with a sense of self-preservation, run by a <see cref="StateMachine{TState, TTrigger}"/>:
/// it wanders until it spots the player, hunts them, flees once badly hurt, and after a few turns of
/// fleeing rests out of sight to heal before wandering again. Hunting and fleeing are both part of being
/// alert, which ends when the player is lost from view.
/// </summary>
public sealed class SkittishBrain : IBrain
{
    // One turn passes one second on the machine's clock, so timers count turns.
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(1);

    private readonly WanderBrain _wander = new() { Range = 4, Restlessness = 0.3 };
    private Actor? _self;
    private RpgWorld? _world;
    private Cell? _lastSeen;

    public SkittishBrain()
    {
        Machine = new StateMachine<Mood, Sense>(Mood.Wandering);
        Machine.Configure(Mood.Wandering)
            .Permit(Sense.Spotted, Mood.Hunting);
        Machine.Configure(Mood.Alert)
            .Permit(Sense.LostSight, Mood.Wandering);
        Machine.Configure(Mood.Hunting)
            .SubstateOf(Mood.Alert)
            .Permit(Sense.Hurt, Mood.Fleeing)
            .Ignore(Sense.Spotted);
        Machine.Configure(Mood.Fleeing)
            .SubstateOf(Mood.Alert)
            .OnEnter(_ => Say("scuttles away!"))
            .Ignore(Sense.Spotted)
            .Ignore(Sense.Hurt)
            .After(TimeSpan.FromTicks(Turn.Ticks * 5), Mood.Resting, () => !Sees, "out of sight");
        Machine.Configure(Mood.Resting)
            .PermitIf(Sense.Spotted, Mood.Fleeing, () => IsHurt, "hurt")
            .PermitIf(Sense.Spotted, Mood.Hunting, () => !IsHurt, "healed")
            .Permit(Sense.Healed, Mood.Wandering);
    }

    public enum Mood
    {
        Wandering,
        Alert,
        Hunting,
        Fleeing,
        Resting,
    }

    public enum Sense
    {
        Spotted,
        LostSight,
        Hurt,
        Healed,
    }

    /// <summary>The state machine, for inspection (its <c>ToMermaid()</c> draws this brain).</summary>
    public StateMachine<Mood, Sense> Machine { get; }

    public Mood Current => Machine.State;

    private bool Sees { get; set; }

    private bool IsHurt => _self is { } self && self.Hp * 3 <= self.MaxHp;

    public ActorAction Decide(Actor self, RpgWorld world)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(world);
        (_self, _world) = (self, world);
        var player = world.Player;
        var distance = Pathfinder.Distance(self.Position, player.Position);
        Sees = player.IsAlive && distance <= self.Definition.Sight
            && FieldOfView.HasLineOfSight(self.Position, player.Position, world.Area.Map.IsOpaque);
        if (Sees)
        {
            _lastSeen = player.Position;
        }

        // Senses first, then the clock, then act on whatever mood that leaves.
        if (Sees)
        {
            Machine.TryFire(Sense.Spotted);
        }

        if (IsHurt)
        {
            Machine.TryFire(Sense.Hurt);
        }

        if (Machine.State == Mood.Hunting && !Sees && _lastSeen is null)
        {
            Machine.TryFire(Sense.LostSight);
        }

        if (Machine.State == Mood.Resting && self.Hp >= self.MaxHp)
        {
            Machine.TryFire(Sense.Healed);
        }

        Machine.Update(Turn);
        return Machine.State switch
        {
            Mood.Hunting => Hunt(self, world),
            Mood.Fleeing => Flee(self, world, distance),
            Mood.Resting => Rest(self),
            _ => _wander.Decide(self, world),
        };
    }

    private ActorAction Hunt(Actor self, RpgWorld world)
    {
        var player = world.Player;
        if (Pathfinder.Distance(self.Position, player.Position) == 1)
        {
            return new AttackAction(player);
        }

        if (_lastSeen is not { } target || target == self.Position)
        {
            _lastSeen = null;
            return ActorAction.Wait;
        }

        if (Pathfinder.Find(self.Position, target, world.Area.CanEnter, maxSearch: 400) is [var next, ..] && self.Position.DirectionTo(next) is { } direction)
        {
            return new MoveAction(direction);
        }

        _lastSeen = null;
        return ActorAction.Wait;
    }

    /// <summary>Steps to the free neighbour furthest from the player; bites back when cornered.</summary>
    private static ActorAction Flee(Actor self, RpgWorld world, int distance)
    {
        var player = world.Player.Position;
        var away = GridDirections.All
            .Where(d => world.Area.CanEnter(self.Position.Step(d)))
            .Select(d => (Direction: d, Distance: Pathfinder.Distance(self.Position.Step(d), player)))
            .Where(o => o.Distance > distance)
            .OrderByDescending(o => o.Distance)
            .FirstOrDefault();
        if (away.Distance > 0)
        {
            return new MoveAction(away.Direction);
        }

        return distance == 1 ? new AttackAction(world.Player) : ActorAction.Wait;
    }

    private static ActorAction Rest(Actor self)
    {
        self.Heal(1);
        return ActorAction.Wait;
    }

    private void Say(string what)
    {
        if (_self is { } self && _world is { } world && world.Area.IsVisible(self.Position))
        {
            world.Log($"The {self.Name} {what}");
        }
    }
}
