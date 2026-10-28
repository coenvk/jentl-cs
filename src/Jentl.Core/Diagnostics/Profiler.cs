using System.Diagnostics;

namespace Jentl.Core.Diagnostics;

/// <summary>One named scope's time: on average and at worst over the recent frames, and how often it ran a frame.</summary>
/// <param name="Path">The scope's name under the scopes it ran inside: <c>update/physics/contacts</c>.</param>
public readonly record struct ProfileEntry(string Path, int Depth, TimeSpan Average, TimeSpan Worst, double CallsPerFrame)
{
    /// <summary>The scope's own name, the last part of its path.</summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// Where the frame's time goes. Wrap work in <see cref="Measure"/> (<c>using (profiler.Measure("physics")) { ... }</c>);
/// scopes inside scopes nest, so a slow frame shows which part of which part was slow. <see cref="EndFrame"/> closes
/// each frame (<c>JentlGame</c> does, and measures update and draw itself), and <see cref="Entries"/> averages the last
/// <see cref="Window"/> frames: the debug overlay lists them.
/// </summary>
public sealed class Profiler
{
    private readonly Func<long> _now;
    private readonly double _ticksPerSecond;
    private readonly Dictionary<string, Scope> _scopes = [];
    private readonly List<string> _order = [];
    private readonly Stack<string> _open = new();
    private int _frame;

    public Profiler(int window = 60)
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency, window)
    {
    }

    /// <summary>A profiler on another clock: <paramref name="now"/> in ticks, <paramref name="ticksPerSecond"/> of them a second.</summary>
    internal Profiler(Func<long> now, double ticksPerSecond, int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        _now = now;
        _ticksPerSecond = ticksPerSecond;
        Window = window;
    }

    /// <summary>How many frames the averages cover.</summary>
    public int Window { get; }

    /// <summary>Off, <see cref="Measure"/> costs nothing and records nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Times what runs until the returned timing is disposed, under <paramref name="name"/> inside the scopes open now.</summary>
    public Timing Measure(string name)
    {
        if (!Enabled)
        {
            return default;
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        var path = _open.TryPeek(out var parent) ? parent + "/" + name : name;
        _open.Push(path);
        return new Timing(this, path, _now());
    }

    /// <summary>Ends a frame: what was measured since the last call becomes one more frame of the averages.</summary>
    public void EndFrame()
    {
        var slot = _frame % Window;
        foreach (var scope in _scopes.Values)
        {
            scope.Ticks[slot] = scope.ThisFrame;
            scope.Calls[slot] = scope.CallsThisFrame;
            scope.ThisFrame = 0;
            scope.CallsThisFrame = 0;
        }

        _frame++;
    }

    /// <summary>Forgets every scope, as after the game moved on to another screen.</summary>
    public void Clear()
    {
        _scopes.Clear();
        _order.Clear();
        _open.Clear();
        _frame = 0;
    }

    /// <summary>Every scope measured, in the order first seen (so children follow their parents), over the recent frames.</summary>
    public IReadOnlyList<ProfileEntry> Entries
    {
        get
        {
            var frames = Math.Min(_frame, Window);
            if (frames == 0)
            {
                return [];
            }

            return [.. _order.Select(path =>
            {
                var scope = _scopes[path];
                long total = 0, worst = 0, calls = 0;
                for (var i = 0; i < frames; i++)
                {
                    total += scope.Ticks[i];
                    worst = Math.Max(worst, scope.Ticks[i]);
                    calls += scope.Calls[i];
                }

                return new ProfileEntry(path, path.Count(c => c == '/'), Time(total / (double)frames), Time(worst), calls / (double)frames);
            })];
        }
    }

    private void Close(string path, long started)
    {
        // Closing out of order (a scope left open) unwinds to this one.
        while (_open.TryPop(out var open) && open != path)
        {
        }

        if (!_scopes.TryGetValue(path, out var scope))
        {
            scope = _scopes[path] = new Scope(Window);
            _order.Add(path);
        }

        scope.ThisFrame += _now() - started;
        scope.CallsThisFrame++;
    }

    private TimeSpan Time(double ticks) => TimeSpan.FromSeconds(ticks / _ticksPerSecond);

    /// <summary>A scope being measured; disposing it records the time.</summary>
    public readonly struct Timing : IDisposable
    {
        private readonly Profiler? _profiler;
        private readonly string _path;
        private readonly long _started;

        internal Timing(Profiler profiler, string path, long started) => (_profiler, _path, _started) = (profiler, path, started);

        public void Dispose() => _profiler?.Close(_path, _started);
    }

    private sealed class Scope(int window)
    {
        public long[] Ticks { get; } = new long[window];

        public int[] Calls { get; } = new int[window];

        public long ThisFrame { get; set; }

        public int CallsThisFrame { get; set; }
    }
}
