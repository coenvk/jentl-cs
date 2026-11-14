using System.Numerics;

namespace Jentl.Traffic;

/// <summary>
/// Builds a <see cref="RoadNetwork"/> from nodes and roads: lays the lanes out either side of each road (driving on the
/// right), stops them short of junctions, makes the turning lanes through each junction, a ring for each roundabout,
/// the lights' phases (roads on one axis green together), and every place two paths cross or merge, with who goes
/// first. Positions are in metres, x east and y north; speeds in km/h here, metres a second in the network.
/// Roads can have cycle paths (<see cref="CyclePaths"/>): one each side, one way with the traffic beside it, joined
/// through junctions by cyclists' own turns and round roundabouts by a ring of their own outside the road's. Where a
/// cyclist's way crosses a car's, the usual rules decide, and a cyclist going straight on goes before a car turning
/// across from the same direction; on a roundabout the cyclists' ring goes first, as the road's ring does. Cyclists
/// turn left the Dutch way, in two: straight across to the far corner, where they wait (at lights, for the other
/// road's green), then straight across the other road.
/// </summary>
public sealed class NetworkBuilder
{
    private const float LateralAcceleration = 2.5f;

    private readonly List<Node> _nodes = [];
    private readonly List<(Road Road, int Forward, int Backward)> _roads = [];
    private readonly List<Lane> _lanes = [];
    private readonly Dictionary<Node, (float Green, float Amber, float AllRed, bool Actuated)> _signals = [];
    private readonly HashSet<Road> _cycled = [];
    private readonly Dictionary<Lane, Vector2> _corners = [];

    public float LaneWidth { get; set; } = 3.5f;

    /// <summary>How far a cycle path's middle lies outside the road's outer lane edge, in metres.</summary>
    public float CyclePathGap { get; set; } = 2.5f;

    /// <summary>Gives the roads cycle paths, one on each side.</summary>
    public void CyclePaths(params Road[] roads)
    {
        foreach (var road in roads)
        {
            ArgumentNullException.ThrowIfNull(road);
            if (!_roads.Any(r => r.Road == road))
            {
                throw new ArgumentException($"{road} is not from this builder.", nameof(roads));
            }

            _cycled.Add(road);
        }
    }

    private bool HasPaths(Node node) => node.Roads.Any(_cycled.Contains);

    public Node Node(float x, float y, NodeControl control = NodeControl.Uncontrolled)
    {
        var node = new Node(_nodes.Count, new Vector2(x, y), control);
        _nodes.Add(node);
        return node;
    }

    /// <summary>A road with <paramref name="lanes"/> lanes each way, straight or through <paramref name="via"/> points.</summary>
    public Road Road(Node from, Node to, int lanes = 1, float speedKmh = 50, params Vector2[] via) =>
        Road(from, to, lanes, lanes, speedKmh, via);

    /// <summary>A road with its own number of lanes each way (0 one way makes it one-way).</summary>
    public Road Road(Node from, Node to, int forward, int backward, float speedKmh, params Vector2[] via)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentOutOfRangeException.ThrowIfNegative(forward);
        ArgumentOutOfRangeException.ThrowIfNegative(backward);
        if (forward + backward == 0 || from == to)
        {
            throw new ArgumentException("A road needs a lane and two different ends.");
        }

        var road = new Road(_roads.Count, from, to, speedKmh / 3.6f, Smooth([from.Position, .. via ?? [], to.Position]));
        from.Roads.Add(road);
        to.Roads.Add(road);
        _roads.Add((road, forward, backward));
        return road;
    }

    public void Roundabout(Node node, float radius = 14)
    {
        Own(node);
        node.Control = NodeControl.Roundabout;
        node.RingRadius = radius;
    }

    /// <summary>Traffic lights at <paramref name="node"/>; <paramref name="actuated"/> ones follow the traffic (see <see cref="SignalPlan"/>).</summary>
    public void Signals(Node node, float green = 20, float amber = 3, float allRed = 2, bool actuated = false)
    {
        Own(node);
        node.Control = NodeControl.Signals;
        _signals[node] = (green, amber, allRed, actuated);
    }

    /// <summary>Gives two roads through <paramref name="node"/> priority; traffic from the others gives way.</summary>
    public void Priority(Node node, Road first, Road second)
    {
        Own(node);
        if (!node.Roads.Contains(first) || !node.Roads.Contains(second))
        {
            throw new ArgumentException("Both priority roads must meet at the node.");
        }

        node.Control = NodeControl.Priority;
        node.PriorityRoads.Clear();
        node.PriorityRoads.AddRange([first, second]);
    }

    /// <summary>A line through the points, curving smoothly round the ones between (a Catmull-Rom spline).</summary>
    private static LanePath Smooth(IReadOnlyList<Vector2> points)
    {
        if (points.Count == 2)
        {
            return new LanePath(points);
        }

        var curve = new List<Vector2>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var (p0, p1, p2, p3) = (points[Math.Max(0, i - 1)], points[i], points[i + 1], points[Math.Min(points.Count - 1, i + 2)]);
            for (var step = 0; step < 12; step++)
            {
                var t = step / 12f;
                var (t2, t3) = (t * t, t * t * t);
                curve.Add(0.5f * ((2 * p1) + ((p2 - p0) * t) + (((2 * p0) - (5 * p1) + (4 * p2) - p3) * t2) + (((3 * p1) - p0 - (3 * p2) + p3) * t3)));
            }
        }

        curve.Add(points[^1]);
        return new LanePath(curve);
    }

    private void Own(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_nodes.Contains(node))
        {
            throw new ArgumentException($"{node} is not from this builder.", nameof(node));
        }
    }

    public RoadNetwork Build()
    {
        foreach (var (road, forward, backward) in _roads)
        {
            LayLanes(road, forward, backward);
        }

        foreach (var node in _nodes.Where(n => !n.IsEdge))
        {
            if (node.Control == NodeControl.Roundabout)
            {
                BuildRing(node);
                if (HasPaths(node))
                {
                    BuildCycleRing(node);
                }
            }
            else
            {
                BuildTurns(node);
                BuildCycleTurns(node);
            }

            if (node.Control == NodeControl.Signals)
            {
                BuildSignals(node);
            }

            FindConflicts(node);
        }

        return new RoadNetwork(_nodes, [.. _roads.Select(r => r.Road)], _lanes);
    }

    private float Setback(Node node) => node.Roads.Count switch
    {
        1 => 0,
        2 when node.Control is NodeControl.Uncontrolled => 2,
        _ when node.Control == NodeControl.Roundabout => node.RingRadius + LaneWidth + 5 + (HasPaths(node) ? CyclePathGap + 4 : 0),
        _ => 6 + (LaneWidth * node.Roads.Max(r => Math.Max(r.Forward.Count + r.Backward.Count, 2)) / 2) + (node.Control == NodeControl.Signals ? 2 : 0)
            + (HasPaths(node) ? 1 : 0),
    };

    private Lane Add(LaneKind kind, LanePath path, float speedLimit)
    {
        // Bends are taken no faster than a comfortable sideways pull allows.
        var radius = path.MinRadius();
        var limit = float.IsInfinity(radius) ? speedLimit : MathF.Min(speedLimit, MathF.Max(4, MathF.Sqrt(LateralAcceleration * radius)));
        var lane = new Lane(_lanes.Count, kind, path, limit);
        _lanes.Add(lane);
        return lane;
    }

    private static void Link(Lane from, Lane to)
    {
        from.Next.Add(to);
        to.Previous.Add(from);
    }

    private void LayLanes(Road road, int forward, int backward)
    {
        var centre = road.Centre.Between(Setback(road.From), road.Centre.Length - Setback(road.To));
        var oneWay = forward == 0 || backward == 0;
        for (var i = 0; i < forward; i++)
        {
            var offset = (i + 0.5f - (oneWay ? forward / 2f : 0)) * LaneWidth;
            var lane = Add(LaneKind.Road, centre.Offset(offset), road.SpeedLimit);
            lane.Road = road;
            road.Forward.Add(lane);
        }

        var back = centre.Reversed();
        for (var i = 0; i < backward; i++)
        {
            var offset = (i + 0.5f - (oneWay ? backward / 2f : 0)) * LaneWidth;
            var lane = Add(LaneKind.Road, back.Offset(offset), road.SpeedLimit);
            lane.Road = road;
            road.Backward.Add(lane);
        }

        if (_cycled.Contains(road))
        {
            // Each path outside the lanes on its side: the half of a one-way road, or the lanes going that way.
            var right = (oneWay ? (forward + backward) / 2f : forward) * LaneWidth;
            var left = (oneWay ? (forward + backward) / 2f : backward) * LaneWidth;
            road.CycleForward = Add(LaneKind.Road, centre.Offset(right + CyclePathGap), CycleSpeed);
            road.CycleBackward = Add(LaneKind.Road, back.Offset(left + CyclePathGap), CycleSpeed);
            foreach (var path in new[] { road.CycleForward, road.CycleBackward })
            {
                path.Road = road;
                path.ForBikes = true;
            }
        }
    }

    private const float CycleSpeed = 25 / 3.6f;

    /// <summary>The cyclists' turns through a junction: from each path arriving to every other road's path leaving.</summary>
    private void BuildCycleTurns(Node node)
    {
        var through = node.Roads.Count == 2;
        foreach (var road in node.Roads)
        {
            if (road.PathArriving(node) is not { } from)
            {
                continue;
            }

            foreach (var other in node.Roads.Where(r => r != road))
            {
                if (other.PathLeaving(node) is not { } to)
                {
                    continue;
                }

                var turn = through ? TurnKind.Straight : TurnOf(from.Path.EndDirection, to.Path.StartDirection);
                if (turn == TurnKind.Left && Corner(from.Path.End, from.Path.EndDirection, to.Path.Start, to.Path.StartDirection) is { } corner)
                {
                    TurnInTwo(node, from, to, corner);
                    continue;
                }

                CycleLink(node, from, to, LanePath.Turn(from.Path.End, from.Path.EndDirection, to.Path.Start, to.Path.StartDirection), turn);
            }
        }
    }

    private void CycleLink(Node node, Lane from, Lane to, LanePath path, TurnKind turn)
    {
        var connector = Add(LaneKind.Connector, path, CycleSpeed);
        connector.Node = node;
        connector.Turn = turn;
        connector.ForBikes = true;
        Link(from, connector);
        Link(connector, to);
    }

    /// <summary>Where the way straight on meets the way in to the road turned into: the far corner of a left turn in two.</summary>
    private static Vector2? Corner(Vector2 from, Vector2 fromDirection, Vector2 to, Vector2 toDirection)
    {
        var cross = (fromDirection.X * toDirection.Y) - (fromDirection.Y * toDirection.X);
        if (MathF.Abs(cross) < 0.5f)
        {
            return null;
        }

        var along = (((to.X - from.X) * toDirection.Y) - ((to.Y - from.Y) * toDirection.X)) / cross;
        return along > 0 ? from + (fromDirection * along) : null;
    }

    /// <summary>
    /// A cyclist's left turn in two: across to the far corner (a junction lane), into a space to wait in there, beyond
    /// the path crossed and beside the one to be followed, facing the new way (a road lane long enough for a bicycle, so
    /// they stop at its end and look, or wait for their lights, clear of the crossing), then across the other road.
    /// </summary>
    private void TurnInTwo(Node node, Lane from, Lane to, Vector2 corner)
    {
        var direction = to.Path.StartDirection;
        var back = corner + (from.Path.EndDirection * 1.6f);
        var waitFrom = back - (direction * 5f);
        var wait = Add(LaneKind.Road, LanePath.Line(waitFrom, back - (direction * 0.5f)), CycleSpeed);
        wait.ForBikes = true;
        _corners[wait] = direction;
        var first = Add(LaneKind.Connector, LanePath.Turn(from.Path.End, from.Path.EndDirection, waitFrom, direction), CycleSpeed);
        first.Node = node;
        first.Turn = TurnKind.Straight;
        first.ForBikes = true;
        Link(from, first);
        Link(first, wait);
        CycleLink(node, wait, to, LanePath.Turn(wait.Path.End, direction, to.Path.Start, to.Path.StartDirection), TurnKind.Straight);
    }

    internal static TurnKind TurnOf(Vector2 from, Vector2 to)
    {
        var angle = MathF.Atan2((from.X * to.Y) - (from.Y * to.X), Vector2.Dot(from, to));
        return MathF.Abs(angle) < 0.6f ? TurnKind.Straight : angle > 0 ? TurnKind.Left : TurnKind.Right;
    }

    private void BuildTurns(Node node)
    {
        var through = node.Roads.Count == 2;
        foreach (var road in node.Roads)
        {
            var arriving = road.ArrivingAt(node);
            foreach (var other in node.Roads.Where(r => r != road))
            {
                var leaving = other.LeavingFrom(node);
                if (arriving.Count == 0 || leaving.Count == 0)
                {
                    continue;
                }

                var turn = through ? TurnKind.Straight : TurnOf(arriving[0].Path.EndDirection, leaving[0].Path.StartDirection);
                for (var i = 0; i < arriving.Count; i++)
                {
                    // Left turns from the leftmost lane, right turns from the rightmost, straight on from any.
                    var last = arriving.Count - 1;
                    if ((turn == TurnKind.Left && i != 0) || (turn == TurnKind.Right && i != last))
                    {
                        continue;
                    }

                    var target = leaving[turn switch
                    {
                        TurnKind.Left => 0,
                        TurnKind.Right => leaving.Count - 1,
                        _ => Math.Min(i, leaving.Count - 1),
                    }];
                    var from = arriving[i];
                    var connector = Add(LaneKind.Connector, LanePath.Turn(from.Path.End, from.Path.EndDirection, target.Path.Start, target.Path.StartDirection), MathF.Min(road.SpeedLimit, other.SpeedLimit));
                    connector.Node = node;
                    connector.Turn = turn;
                    Link(from, connector);
                    Link(connector, target);
                }
            }
        }
    }

    private void BuildRing(Node node) =>
        BuildRing(node, node.RingRadius, LaneWidth, r => r.ArrivingAt(node), r => r.LeavingFrom(node), 50 / 3.6f, bikes: false);

    /// <summary>The cyclists' ring round a roundabout, outside the road's, joining the paths of the roads that have them.</summary>
    private void BuildCycleRing(Node node)
    {
        // Out as far as the paths are from the road's middle; one-way, the same way round as the traffic.
        var offset = (LaneWidth * node.Roads.Where(_cycled.Contains).Max(r => Math.Max(r.Forward.Count, r.Backward.Count))) + CyclePathGap;
        var radius = node.RingRadius + LaneWidth + CyclePathGap + 1;
        BuildRing(node, radius, offset, r => r.PathArriving(node) is { } p ? [p] : [], r => r.PathLeaving(node) is { } p ? [p] : [], CycleSpeed, bikes: true);
    }

    private void BuildRing(Node node, float radius, float offset, Func<Road, IReadOnlyList<Lane>> arrivingOn, Func<Road, IReadOnlyList<Lane>> leavingOn, float speed, bool bikes)
    {
        var centre = node.Position;
        var spread = MathF.Asin(MathF.Min(0.9f, offset / radius));
        Vector2 Point(float angle) => centre + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        Vector2 Tangent(float angle) => new(-MathF.Sin(angle), MathF.Cos(angle));

        // Anticlockwise, as traffic on the right goes round: each road enters just after its own angle and leaves just before.
        var points = new List<(float Angle, Road Road, bool Entry)>();
        foreach (var road in node.Roads.Where(r => arrivingOn(r).Count + leavingOn(r).Count > 0))
        {
            var end = arrivingOn(road) is { Count: > 0 } arriving ? arriving[0].Path.End : leavingOn(road)[0].Path.Start;
            var toRoad = end - centre;
            var angle = MathF.Atan2(toRoad.Y, toRoad.X);
            points.Add((Normalise(angle + spread), road, true));
            points.Add((Normalise(angle - spread), road, false));
        }

        points.Sort((a, b) => a.Angle.CompareTo(b.Angle));
        var ring = new Lane[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var next = points[(i + 1) % points.Count];
            ring[i] = Add(LaneKind.Ring, LanePath.Arc(centre, radius, points[i].Angle, next.Angle), speed);
            ring[i].Node = node;
            ring[i].ForBikes = bikes;
        }

        for (var i = 0; i < points.Count; i++)
        {
            Link(ring[i], ring[(i + 1) % points.Count]);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var (angle, road, entry) = points[i];
            var (at, tangent) = (Point(angle), Tangent(angle));
            if (entry)
            {
                foreach (var from in arrivingOn(road))
                {
                    var connector = Add(LaneKind.Connector, LanePath.Turn(from.Path.End, from.Path.EndDirection, at, tangent), bikes ? speed : road.SpeedLimit);
                    connector.Node = node;
                    connector.Turn = TurnKind.Right;
                    connector.ForBikes = bikes;
                    Link(from, connector);
                    Link(connector, ring[i]);
                }
            }
            else
            {
                var before = ring[(i - 1 + points.Count) % points.Count];
                foreach (var to in leavingOn(road))
                {
                    var connector = Add(LaneKind.Connector, LanePath.Turn(at, tangent, to.Path.Start, to.Path.StartDirection), bikes ? speed : road.SpeedLimit);
                    connector.Node = node;
                    connector.Turn = TurnKind.Right;
                    connector.ForBikes = bikes;
                    Link(before, connector);
                    Link(connector, to);
                }
            }
        }

        static float Normalise(float angle) => ((angle % MathF.Tau) + MathF.Tau) % MathF.Tau;
    }

    private void BuildSignals(Node node)
    {
        var (green, amber, allRed, actuated) = _signals[node];

        // Roads on one axis share a phase: north and south together, then east and west.
        var axes = new List<Vector2>();
        var phaseOf = new Dictionary<Road, int>();
        foreach (var road in node.Roads)
        {
            var arriving = road.ArrivingAt(node);
            if (arriving.Count == 0)
            {
                continue;
            }

            var direction = arriving[0].Path.EndDirection;
            var phase = axes.FindIndex(axis => MathF.Abs(Vector2.Dot(axis, direction)) > 0.7f);
            if (phase < 0)
            {
                axes.Add(direction);
                phase = axes.Count - 1;
            }

            phaseOf[road] = phase;
        }

        var plan = new SignalPlan(Math.Max(1, axes.Count), green, amber, allRed) { Actuated = actuated };
        foreach (var (road, phase) in phaseOf)
        {
            foreach (var lane in road.ArrivingAt(node).Append(road.PathArriving(node)).OfType<Lane>())
            {
                lane.Signal = plan.Group(phase);
            }
        }

        // Cyclists waiting at a corner to turn left go with the road they cross towards: on its axis's green.
        foreach (var (wait, direction) in _corners.Where(c => c.Key.Next[0].Node == node))
        {
            var phase = axes.FindIndex(axis => MathF.Abs(Vector2.Dot(axis, direction)) > 0.7f);
            if (phase >= 0)
            {
                wait.Signal = plan.Group(phase);
            }
        }

        node.Signals = plan;
        plan.Advance(0);
    }

    private void FindConflicts(Node node)
    {
        var lanes = _lanes.Where(l => l.Node == node).ToList();
        foreach (var a in lanes)
        {
            foreach (var b in lanes.Where(b => b != a))
            {
                // Lanes from the same lane split apart; lanes one after the other follow on.
                if (a.Previous.Intersect(b.Previous).Any() || a.Next.Contains(b) || b.Next.Contains(a))
                {
                    continue;
                }

                if (a.Next.Intersect(b.Next).Any())
                {
                    a.Conflicts.Add(new Conflict(b, a.Length, b.Length, GoesFirst(node, b, a)));
                }
                else if (LanePath.Cross(a.Path, b.Path, out var atA, out var atB))
                {
                    a.Conflicts.Add(new Conflict(b, atA, atB, GoesFirst(node, b, a)));
                }
            }
        }
    }

    /// <summary>Whether <paramref name="first"/> has priority over <paramref name="second"/> where their paths meet.</summary>
    private static bool GoesFirst(Node node, Lane first, Lane second)
    {
        if (first.Kind != second.Kind)
        {
            // On a roundabout, traffic on the ring goes first.
            return first.Kind == LaneKind.Ring;
        }

        if (first.ForBikes != second.ForBikes && Vector2.Dot(first.Path.StartDirection, second.Path.StartDirection) > 0.5f
            && (first.Turn == TurnKind.Straight) != (second.Turn == TurnKind.Straight))
        {
            // A cyclist and a car from the same way: whichever goes straight on goes first, the other turning across.
            return first.Turn == TurnKind.Straight;
        }

        if (node.Control == NodeControl.Priority)
        {
            var (major, minor) = (node.PriorityRoads.Contains(Approach(first)!), node.PriorityRoads.Contains(Approach(second)!));
            if (major != minor)
            {
                return major;
            }
        }

        var (a, b) = (second.Path.StartDirection, first.Path.StartDirection);
        var cross = (a.X * b.Y) - (a.Y * b.X);
        var dot = Vector2.Dot(a, b);
        if (dot < -0.5f)
        {
            // Oncoming: a left turn gives way to what goes straight on or right.
            if (second.Turn == TurnKind.Left && first.Turn != TurnKind.Left)
            {
                return true;
            }

            if (first.Turn == TurnKind.Left && second.Turn != TurnKind.Left)
            {
                return false;
            }
        }
        else if (MathF.Abs(cross) > 0.5f && node.Control != NodeControl.Signals)
        {
            // From the right goes first.
            return cross > 0;
        }

        // Otherwise one rule, the same both ways round, so exactly one of them goes first.
        return first.Id < second.Id;
    }

    private static Road? Approach(Lane connector) => connector.Previous.FirstOrDefault()?.Road;
}
