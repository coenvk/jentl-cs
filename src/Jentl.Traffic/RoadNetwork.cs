using System.Numerics;

namespace Jentl.Traffic;

/// <summary>How a node's traffic is controlled.</summary>
public enum NodeControl
{
    /// <summary>Priority from the right (and left turns give way to oncoming traffic).</summary>
    Uncontrolled,

    /// <summary>Two roads have priority; the others give way to them.</summary>
    Priority,

    Signals,

    Roundabout,
}

/// <summary>Which way a movement through a junction turns.</summary>
public enum TurnKind
{
    Straight,
    Left,
    Right,
}

/// <summary>What a lane is: part of a road, a turn through a junction, or a piece of a roundabout's ring.</summary>
public enum LaneKind
{
    Road,
    Connector,
    Ring,
}

/// <summary>A place where roads meet (or end, at the edge of the map, where traffic comes from and goes to).</summary>
public sealed class Node
{
    internal Node(int id, Vector2 position, NodeControl control)
    {
        Id = id;
        Position = position;
        Control = control;
    }

    public int Id { get; }

    public Vector2 Position { get; }

    public NodeControl Control { get; internal set; }

    public List<Road> Roads { get; } = [];

    /// <summary>For <see cref="NodeControl.Priority"/>: the roads that have it.</summary>
    public List<Road> PriorityRoads { get; } = [];

    /// <summary>For <see cref="NodeControl.Roundabout"/>: the ring's radius.</summary>
    public float RingRadius { get; internal set; } = 14;

    /// <summary>For <see cref="NodeControl.Signals"/>: the lights.</summary>
    public SignalPlan? Signals { get; internal set; }

    /// <summary>A road's end: where traffic enters and leaves the network.</summary>
    public bool IsEdge => Roads.Count == 1;

    public override string ToString() => $"node {Id} ({Control})";
}

/// <summary>A road between two nodes, with lanes each way.</summary>
public sealed class Road
{
    internal Road(int id, Node from, Node to, float speedLimit, LanePath centre)
    {
        Id = id;
        From = from;
        To = to;
        SpeedLimit = speedLimit;
        Centre = centre;
    }

    public int Id { get; }

    public Node From { get; }

    public Node To { get; }

    /// <summary>In metres a second.</summary>
    public float SpeedLimit { get; }

    /// <summary>The centre line, from <see cref="From"/> to <see cref="To"/>.</summary>
    public LanePath Centre { get; }

    /// <summary>Lanes from <see cref="From"/> to <see cref="To"/>, the leftmost first.</summary>
    public List<Lane> Forward { get; } = [];

    /// <summary>Lanes from <see cref="To"/> to <see cref="From"/>, the leftmost first.</summary>
    public List<Lane> Backward { get; } = [];

    public IEnumerable<Lane> Lanes => Forward.Concat(Backward);

    /// <summary>The cycle path on the right going from <see cref="From"/> to <see cref="To"/>, if the road has paths.</summary>
    public Lane? CycleForward { get; internal set; }

    /// <summary>The cycle path on the other side, going back.</summary>
    public Lane? CycleBackward { get; internal set; }

    /// <summary>The lanes leaving <paramref name="node"/> along this road.</summary>
    public IReadOnlyList<Lane> LeavingFrom(Node node) => node == From ? Forward : Backward;

    /// <summary>The lanes arriving at <paramref name="node"/> along this road.</summary>
    public IReadOnlyList<Lane> ArrivingAt(Node node) => node == To ? Forward : Backward;

    /// <summary>The cycle path leaving <paramref name="node"/>, if any.</summary>
    public Lane? PathLeaving(Node node) => node == From ? CycleForward : CycleBackward;

    /// <summary>The cycle path arriving at <paramref name="node"/>, if any.</summary>
    public Lane? PathArriving(Node node) => node == To ? CycleForward : CycleBackward;

    public Node Other(Node node) => node == From ? To : From;

    public override string ToString() => $"road {Id} ({From.Id}–{To.Id})";
}

/// <summary>Where two lanes' paths cross (or merge), and which of them goes first.</summary>
/// <param name="Other">The other lane.</param>
/// <param name="At">How far along this lane the conflict is.</param>
/// <param name="OtherAt">How far along the other lane.</param>
/// <param name="OtherFirst">Whether the other lane has priority over this one.</param>
public sealed record Conflict(Lane Other, float At, float OtherAt, bool OtherFirst);

/// <summary>A lane: a path vehicles follow one behind the other, and the lanes they can go on to.</summary>
public sealed class Lane
{
    internal Lane(int id, LaneKind kind, LanePath path, float speedLimit)
    {
        Id = id;
        Kind = kind;
        Path = path;
        SpeedLimit = speedLimit;
    }

    public int Id { get; }

    public LaneKind Kind { get; }

    public LanePath Path { get; }

    /// <summary>In metres a second; turns and rings are slower, by their bends.</summary>
    public float SpeedLimit { get; }

    public float Length => Path.Length;

    /// <summary>The road it is on, for road lanes.</summary>
    public Road? Road { get; internal set; }

    /// <summary>The junction it crosses, for connectors and rings.</summary>
    public Node? Node { get; internal set; }

    /// <summary>For connectors: which way it turns.</summary>
    public TurnKind Turn { get; internal set; }

    /// <summary>Whether it is a cycle path (or a cyclists' way through a junction): bicycles only, and only bicycles.</summary>
    public bool ForBikes { get; internal set; }

    /// <summary>For a road lane ending at lights: the lights it stops at.</summary>
    public SignalGroup? Signal { get; internal set; }

    public List<Lane> Next { get; } = [];

    public List<Lane> Previous { get; } = [];

    public List<Conflict> Conflicts { get; } = [];

    /// <summary>Whether vehicles must check before entering it (a turn through a junction, or an entry onto a ring).</summary>
    public bool IsJunction => Kind != LaneKind.Road;

    public override string ToString() => ForBikes ? $"lane {Id} ({Kind}, cycle)" : $"lane {Id} ({Kind})";
}

/// <summary>A built network: nodes, roads, and every lane, with where traffic comes in and goes out.</summary>
public sealed class RoadNetwork
{
    internal RoadNetwork(IReadOnlyList<Node> nodes, IReadOnlyList<Road> roads, IReadOnlyList<Lane> lanes)
    {
        Nodes = nodes;
        Roads = roads;
        Lanes = lanes;
        Entries = [.. nodes.Where(n => n.IsEdge)];
    }

    public IReadOnlyList<Node> Nodes { get; }

    public IReadOnlyList<Road> Roads { get; }

    public IReadOnlyList<Lane> Lanes { get; }

    /// <summary>The edge nodes: traffic comes in and goes out there.</summary>
    public IReadOnlyList<Node> Entries { get; }

    public IEnumerable<SignalPlan> Signals => Nodes.Select(n => n.Signals).OfType<SignalPlan>();

    /// <summary>The box round every lane.</summary>
    public (Vector2 Min, Vector2 Max) Bounds()
    {
        var points = Lanes.SelectMany(l => l.Path.Points).ToList();
        return (points.Aggregate(Vector2.Min), points.Aggregate(Vector2.Max));
    }
}
