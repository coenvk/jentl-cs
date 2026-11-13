namespace Jentl.Traffic;

/// <summary>A bus stop: the lane (so the way the buses go), how far along it they stop, and for how many seconds.</summary>
public sealed record BusStop(Lane Lane, float At, float Dwell = 20)
{
    /// <summary>A stop part way along a road's lane: <paramref name="fraction"/> of its length.</summary>
    public static BusStop On(Lane lane, float fraction = 0.6f, float dwell = 20)
    {
        ArgumentNullException.ThrowIfNull(lane);
        return new BusStop(lane, lane.Length * Math.Clamp(fraction, 0.05f, 0.95f), dwell);
    }
}

/// <summary>
/// A bus line: buses from one edge of the network to another through their stops in order, one every
/// <paramref name="Headway"/> seconds. Each bus stops at each stop for its dwell time, holding up who is behind on a
/// road of one lane; it doesn't change lanes by choice while it has stops to make.
/// </summary>
public sealed record BusLine(string Name, Node From, Node To, IReadOnlyList<BusStop> Stops, float Headway)
{
    /// <summary>Buses of the line that finished their run.</summary>
    public int Completed { get; internal set; }

    /// <summary>Seconds a run took, on average, stops and all; 0 before the first.</summary>
    public float AverageRunTime => Completed == 0 ? 0 : RunTimes / Completed;

    internal float RunTimes { get; set; }

    internal float NextBus { get; set; }
}
