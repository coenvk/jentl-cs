namespace Jentl.Ecs;

/// <summary>Game logic over a <see cref="World"/>: moves things, applies rules, cleans up. Runs once per update.</summary>
public interface ISystem
{
    void Update(World world, TimeSpan elapsed);
}

/// <summary>Systems run in the order they were added; each can be switched off. A group is itself a system.</summary>
public sealed class SystemGroup : ISystem
{
    private readonly List<(ISystem System, string Name)> _systems = [];
    private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);

    public IEnumerable<string> Names => _systems.Select(s => s.Name);

    public SystemGroup Add(ISystem system, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add((system, name ?? system.GetType().Name));
        return this;
    }

    /// <summary>Adds a system written as a lambda.</summary>
    public SystemGroup Add(string name, Action<World, TimeSpan> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return Add(new LambdaSystem(update), name);
    }

    /// <summary>Switches a system off (paused, or for testing others alone) or back on.</summary>
    public void Enable(string name, bool enabled)
    {
        if (enabled)
        {
            _disabled.Remove(name);
        }
        else
        {
            _disabled.Add(name);
        }
    }

    public void Update(World world, TimeSpan elapsed)
    {
        foreach (var (system, name) in _systems)
        {
            if (!_disabled.Contains(name))
            {
                system.Update(world, elapsed);
            }
        }
    }

    private sealed class LambdaSystem(Action<World, TimeSpan> update) : ISystem
    {
        public void Update(World world, TimeSpan elapsed) => update(world, elapsed);
    }
}
