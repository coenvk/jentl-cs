namespace Jentl.Core.Diagnostics;

/// <summary>How long recent frames took: the whole frame, and the update and draw work in it.</summary>
public sealed class FrameStats
{
    private readonly TimeSpan[] _frames;
    private readonly TimeSpan[] _updates;
    private readonly TimeSpan[] _draws;
    private int _next;

    /// <param name="capacity">How many frames to remember (two seconds at 60 frames a second by default).</param>
    public FrameStats(int capacity = 120)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _frames = new TimeSpan[capacity];
        _updates = new TimeSpan[capacity];
        _draws = new TimeSpan[capacity];
    }

    public int Capacity => _frames.Length;

    /// <summary>How many frames are remembered so far.</summary>
    public int Count { get; private set; }

    /// <summary>Frames per second over the remembered frames.</summary>
    public double FramesPerSecond
    {
        get
        {
            var total = Sum(_frames);
            return total <= TimeSpan.Zero ? 0 : Count / total.TotalSeconds;
        }
    }

    public TimeSpan AverageFrame => Average(_frames);

    public TimeSpan AverageUpdate => Average(_updates);

    public TimeSpan AverageDraw => Average(_draws);

    /// <summary>The slowest remembered frame: a hitch shows here long after the average has forgotten it.</summary>
    public TimeSpan WorstFrame => Recent().Max();

    /// <summary>The remembered frame times, oldest first.</summary>
    public IEnumerable<TimeSpan> Frames => Recent();

    /// <summary>Remembers one frame, forgetting the oldest when full.</summary>
    public void Record(TimeSpan frame, TimeSpan update, TimeSpan draw)
    {
        _frames[_next] = frame;
        _updates[_next] = update;
        _draws[_next] = draw;
        _next = (_next + 1) % Capacity;
        Count = Math.Min(Count + 1, Capacity);
    }

    private IEnumerable<TimeSpan> Recent()
    {
        var start = Count < Capacity ? 0 : _next;
        return Enumerable.Range(0, Count).Select(i => _frames[(start + i) % Capacity]).DefaultIfEmpty();
    }

    private TimeSpan Sum(TimeSpan[] values) =>
        values.Take(Count < Capacity ? Count : Capacity).Aggregate(TimeSpan.Zero, (sum, t) => sum + t);

    private TimeSpan Average(TimeSpan[] values) => Count == 0 ? TimeSpan.Zero : Sum(values) / Count;
}
