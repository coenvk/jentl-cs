using System.Collections;

namespace Jentl.Core;

/// <summary>
/// Objects kept for reuse, so things made and dropped many times a second (bullets, sparks, floating damage numbers,
/// path-finding lists) don't feed the garbage collector, whose pauses show as hitches. <see cref="Get"/> takes one
/// (making it when none is free), <see cref="Return"/> gives it back, cleaned by the reset given.
/// </summary>
public sealed class Pool<T>
    where T : class
{
    private readonly Func<T> _create;
    private readonly Action<T>? _reset;
    private readonly Stack<T> _free = new();
    private readonly HashSet<T> _isFree = new(ReferenceEqualityComparer.Instance);

    /// <param name="create">Makes a new object when none is free.</param>
    /// <param name="reset">Cleans an object as it comes back, so the next <see cref="Get"/> finds it fresh.</param>
    /// <param name="maxFree">Most objects kept; more returned than that are let go.</param>
    public Pool(Func<T> create, Action<T>? reset = null, int maxFree = 1024)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _reset = reset;
        ArgumentOutOfRangeException.ThrowIfNegative(maxFree);
        MaxFree = maxFree;
    }

    public int MaxFree { get; }

    /// <summary>How many objects wait to be reused.</summary>
    public int Free => _free.Count;

    /// <summary>How many objects the pool has made in all: when this keeps growing, something is not returned.</summary>
    public int Created { get; private set; }

    public T Get()
    {
        if (_free.TryPop(out var item))
        {
            _isFree.Remove(item);
            return item;
        }

        Created++;
        return _create();
    }

    /// <summary>Gives <paramref name="item"/> back. Returning one twice would hand it out twice, so that throws.</summary>
    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_isFree.Add(item))
        {
            throw new InvalidOperationException("This object was already returned to the pool.");
        }

        _reset?.Invoke(item);
        if (_free.Count >= MaxFree)
        {
            _isFree.Remove(item);
            return;
        }

        _free.Push(item);
    }

    /// <summary>Makes objects up front (behind a loading screen) so the first busy frames don't have to.</summary>
    public void Prewarm(int count)
    {
        while (_free.Count < Math.Min(count, MaxFree))
        {
            var item = _create();
            Created++;
            _isFree.Add(item);
            _free.Push(item);
        }
    }
}

/// <summary>
/// Pooled objects that are alive until let go: <see cref="Spawn"/> brings one in, <see cref="Despawn"/> or
/// <see cref="DespawnWhere"/> lets it go, and enumerating gives the live ones (bullets to move, sparks to draw). Letting
/// go swaps the last one into its place, so it is cheap and order is not kept.
/// </summary>
#pragma warning disable CA1710 // A pool, not a collection first: "BulletCollection" would say less.
public sealed class ActivePool<T>(Func<T> create, Action<T>? reset = null, int maxFree = 1024) : IReadOnlyCollection<T>
#pragma warning restore CA1710
    where T : class
{
    private readonly Pool<T> _pool = new(create, reset, maxFree);
    private readonly List<T> _active = [];

    public int Count => _active.Count;

    /// <summary>The pool behind it, for <see cref="Pool{T}.Prewarm"/> and its counts.</summary>
    public Pool<T> Pool => _pool;

    /// <summary>The live ones, by index: loop with <c>for</c> to avoid even an enumerator.</summary>
    public T this[int index] => _active[index];

    public T Spawn()
    {
        var item = _pool.Get();
        _active.Add(item);
        return item;
    }

    public bool Despawn(T item)
    {
        var index = _active.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>Lets go of every live one <paramref name="done"/> says is finished; returns how many.</summary>
    public int DespawnWhere(Func<T, bool> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var count = 0;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            if (done(_active[i]))
            {
                RemoveAt(i);
                count++;
            }
        }

        return count;
    }

    public void Clear() => DespawnWhere(_ => true);

    public List<T>.Enumerator GetEnumerator() => _active.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void RemoveAt(int index)
    {
        var item = _active[index];
        _active[index] = _active[^1];
        _active.RemoveAt(_active.Count - 1);
        _pool.Return(item);
    }
}
