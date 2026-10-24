namespace Jentl.Ecs;

/// <summary>
/// A thing in a <see cref="World"/>: just an id, with a generation so that a handle to a destroyed entity never
/// mistakes the entity that later reuses its id for itself. Everything an entity is lives in its components.
/// </summary>
public readonly record struct Entity(int Id, int Generation)
{
    /// <summary>No entity; never alive.</summary>
    public static Entity None { get; } = new(-1, 0);

    public override string ToString() => $"#{Id}.{Generation}";
}

/// <summary>Adds components to a newly spawned entity in one expression: <c>world.Spawn().With(a).With(b)</c>.</summary>
public readonly struct EntityBuilder
{
    private readonly World _world;

    internal EntityBuilder(World world, Entity entity)
    {
        _world = world;
        Entity = entity;
    }

    public Entity Entity { get; }

    public EntityBuilder With<T>(T component)
    {
        _world.Add(Entity, component);
        return this;
    }

    public static implicit operator Entity(EntityBuilder builder) => builder.Entity;

    public Entity ToEntity() => Entity;
}
