using System.Buffers;

namespace Jentl.Ecs;

/// <summary>Called for each matching entity by <see cref="World.ForEach{T1}"/>, with its component by reference to change in place.</summary>
public delegate void RefAction<T1>(Entity entity, ref T1 first);

public delegate void RefAction<T1, T2>(Entity entity, ref T1 first, ref T2 second);

public delegate void RefAction<T1, T2, T3>(Entity entity, ref T1 first, ref T2 second, ref T3 third);

/// <summary>
/// The entities, their components, shared resources and events. Components are plain values (structs or
/// classes) kept per type; queries find the entities that have a set of them. Destroying or changing entities
/// while running a query is safe: each entity is checked again just before its turn.
/// </summary>
public sealed class World
{
    private readonly List<int> _generations = [];
    private readonly List<bool> _alive = [];
    private readonly Stack<int> _free = new();
    private readonly Dictionary<Type, IComponentStore> _stores = [];
    private readonly Dictionary<Type, object> _resources = [];
    private readonly Dictionary<Type, System.Collections.IList> _events = [];

    /// <summary>How many entities are alive.</summary>
    public int Count { get; private set; }

    /// <summary>Makes a new entity with no components.</summary>
    public Entity Create()
    {
        int id;
        if (_free.Count > 0)
        {
            id = _free.Pop();
            _alive[id] = true;
        }
        else
        {
            id = _generations.Count;
            _generations.Add(0);
            _alive.Add(true);
        }

        Count++;
        return new Entity(id, _generations[id]);
    }

    /// <summary>Makes a new entity and adds components to it fluently: <c>world.Spawn().With(new Position()).With(new Ball())</c>.</summary>
    public EntityBuilder Spawn() => new(this, Create());

    public bool IsAlive(Entity entity) =>
        entity.Id >= 0 && entity.Id < _generations.Count && _alive[entity.Id] && _generations[entity.Id] == entity.Generation;

    /// <summary>Removes the entity and all its components; its handle stops being alive. Destroying twice is harmless.</summary>
    public void Destroy(Entity entity)
    {
        if (!IsAlive(entity))
        {
            return;
        }

        foreach (var store in _stores.Values)
        {
            store.Remove(entity.Id);
        }

        _alive[entity.Id] = false;
        _generations[entity.Id]++;
        _free.Push(entity.Id);
        Count--;
    }

    /// <summary>Gives the entity a component, replacing any it had of that type.</summary>
    public void Add<T>(Entity entity, T component)
    {
        Check(entity);
        Store<T>().Set(entity.Id, component);
    }

    public bool Has<T>(Entity entity) => IsAlive(entity) && Find<T>() is { } store && store.Has(entity.Id);

    /// <summary>The entity's component, by reference: change it in place.</summary>
    /// <exception cref="KeyNotFoundException">The entity has none.</exception>
    public ref T Get<T>(Entity entity)
    {
        Check(entity);
        return ref Store<T>().Get(entity.Id);
    }

    public bool TryGet<T>(Entity entity, out T component)
    {
        if (Has<T>(entity))
        {
            component = Store<T>().Get(entity.Id);
            return true;
        }

        component = default!;
        return false;
    }

    public bool Remove<T>(Entity entity) => IsAlive(entity) && Find<T>() is { } store && store.Remove(entity.Id);

    /// <summary>How many entities have a <typeparamref name="T"/>.</summary>
    public int CountOf<T>() => Find<T>()?.Count ?? 0;

    /// <summary>The entities that have every one of the component types, as of now.</summary>
    public IReadOnlyList<Entity> Query<T1>() => Matching(typeof(T1));

    public IReadOnlyList<Entity> Query<T1, T2>() => Matching(typeof(T1), typeof(T2));

    public IReadOnlyList<Entity> Query<T1, T2, T3>() => Matching(typeof(T1), typeof(T2), typeof(T3));

    /// <summary>The only entity with a <typeparamref name="T"/> (a player, a paddle), or <see cref="Entity.None"/>.</summary>
    public Entity FindOne<T>() => Find<T>() is { Count: > 0 } store ? EntityAt(store.Ids[0]) : Entity.None;

    public void ForEach<T1>(RefAction<T1> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var first = Store<T1>();
        Run([first], id => action(EntityAt(id), ref first.Get(id)));
    }

    public void ForEach<T1, T2>(RefAction<T1, T2> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var (first, second) = (Store<T1>(), Store<T2>());
        Run([first, second], id => action(EntityAt(id), ref first.Get(id), ref second.Get(id)));
    }

    public void ForEach<T1, T2, T3>(RefAction<T1, T2, T3> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var (first, second, third) = (Store<T1>(), Store<T2>(), Store<T3>());
        Run([first, second, third], id => action(EntityAt(id), ref first.Get(id), ref second.Get(id), ref third.Get(id)));
    }

    /// <summary>Shares one value world-wide (a score, the physics world, settings), one per type.</summary>
    public void SetResource<T>(T resource)
        where T : notnull => _resources[typeof(T)] = resource;

    /// <exception cref="KeyNotFoundException">No resource of that type was set.</exception>
    public T Resource<T>()
        where T : notnull => _resources.TryGetValue(typeof(T), out var value)
            ? (T)value
            : throw new KeyNotFoundException($"No {typeof(T).Name} resource.");

    public bool TryResource<T>(out T resource)
        where T : notnull
    {
        var found = _resources.TryGetValue(typeof(T), out var value);
        resource = found ? (T)value! : default!;
        return found;
    }

    /// <summary>Queues an event for whichever system reads that kind (a brick broke, a coin was picked up).</summary>
    public void Emit<T>(T e) => Events<T>().Add(e);

    /// <summary>Takes every queued event of a kind, oldest first, leaving none.</summary>
    public IReadOnlyList<T> Drain<T>()
    {
        var queue = Events<T>();
        if (queue.Count == 0)
        {
            return [];
        }

        var taken = queue.ToArray();
        queue.Clear();
        return taken;
    }

    private List<T> Events<T>()
    {
        if (!_events.TryGetValue(typeof(T), out var list))
        {
            list = new List<T>();
            _events[typeof(T)] = list;
        }

        return (List<T>)list;
    }

    private Entity EntityAt(int id) => new(id, _generations[id]);

    private void Check(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is not alive.");
        }
    }

    private ComponentStore<T> Store<T>()
    {
        if (!_stores.TryGetValue(typeof(T), out var store))
        {
            store = new ComponentStore<T>();
            _stores[typeof(T)] = store;
        }

        return (ComponentStore<T>)store;
    }

    private ComponentStore<T>? Find<T>() => _stores.TryGetValue(typeof(T), out var store) ? (ComponentStore<T>)store : null;

    private List<Entity> Matching(params Type[] types)
    {
        var stores = new IComponentStore[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            if (!_stores.TryGetValue(types[i], out var store))
            {
                return [];
            }

            stores[i] = store;
        }

        var result = new List<Entity>();
        Run(stores, id => result.Add(EntityAt(id)));
        return result;
    }

    /// <summary>
    /// Calls <paramref name="visit"/> for each entity in every store. Runs through a copy of the smallest store's
    /// ids, so the stores may change underneath; each id is checked again right before its visit.
    /// </summary>
    private static void Run(IComponentStore[] stores, Action<int> visit)
    {
        var smallest = stores.MinBy(s => s.Count)!;
        var count = smallest.Count;
        var ids = ArrayPool<int>.Shared.Rent(count);
        try
        {
            smallest.Ids.CopyTo(ids);
            for (var i = 0; i < count; i++)
            {
                var id = ids[i];
                if (stores.All(s => s.Has(id)))
                {
                    visit(id);
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(ids);
        }
    }
}
