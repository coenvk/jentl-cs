using System.Numerics;

namespace Jentl.Traffic;

/// <summary>A vehicle on its way: where it is on its route, how fast it goes, and how long it has taken.</summary>
public sealed class Vehicle
{
    private readonly List<Lane> _route;

    internal Vehicle(int id, VehicleKind kind, IReadOnlyList<Lane> route, float eagerness, float startTime, Node? destination = null)
    {
        Id = id;
        Kind = kind;
        _route = [.. route];
        Destination = destination;
        Eagerness = eagerness;
        StartTime = startTime;
        FreeTime = route.Sum(l => l.Length / MathF.Min(l.SpeedLimit, kind.MaxSpeed));
    }

    public int Id { get; }

    public VehicleKind Kind { get; }

    /// <summary>The lanes it drives, start to finish; what lies ahead changes when it changes lanes.</summary>
    public IReadOnlyList<Lane> Route => _route;

    /// <summary>Where it is going: the edge its route ends at.</summary>
    public Node? Destination { get; }

    /// <summary>The lane it is moving across from, while changing lanes; null otherwise.</summary>
    public Lane? ChangingFrom { get; internal set; }

    /// <summary>How far across the lane change is, 0 to 1.</summary>
    public float ChangeProgress { get; internal set; }

    /// <summary>How many times it changed lanes.</summary>
    public int LaneChanges { get; internal set; }

    /// <summary>The time from which it may change lanes again.</summary>
    internal float NextChange { get; set; }

    /// <summary>Takes a new way from here on: <paramref name="rest"/> replaces the route from the current lane.</summary>
    internal void Reroute(IReadOnlyList<Lane> rest)
    {
        _route.RemoveRange(RouteIndex, _route.Count - RouteIndex);
        _route.AddRange(rest);
    }

    public int RouteIndex { get; internal set; }

    public Lane Lane => Route[RouteIndex];

    /// <summary>How far its front is along <see cref="Lane"/>, in metres.</summary>
    public float Position { get; internal set; }

    /// <summary>Metres a second.</summary>
    public float Speed { get; internal set; }

    public float Acceleration { get; internal set; }

    /// <summary>The driver: 1 drives at the limit, a little more or less for one in a hurry or a careful one.</summary>
    public float Eagerness { get; }

    public float StartTime { get; }

    /// <summary>How long the route takes with the roads empty.</summary>
    public float FreeTime { get; }

    /// <summary>How many times it came to a stop.</summary>
    public int Stops { get; internal set; }

    /// <summary>How long it has been standing at a junction, waiting to go.</summary>
    public float Waiting { get; internal set; }

    internal Lane? Committed { get; set; }

    internal bool Stopped { get; set; }

    /// <summary>Its middle and the way it faces, for drawing; part way between two lanes while changing.</summary>
    public (Vector2 Position, Vector2 Direction) Pose()
    {
        var middle = Position - (Kind.Length / 2);
        var (position, direction) = middle >= 0 || RouteIndex == 0
            ? Lane.Path.At(middle)
            : Route[RouteIndex - 1].Path.At(Route[RouteIndex - 1].Length + middle);
        if (ChangingFrom is not { } from)
        {
            return (position, direction);
        }

        // Eased across, from where it would be on the old lane to where it is.
        var t = ChangeProgress * ChangeProgress * (3 - (2 * ChangeProgress));
        var (was, _) = from.Path.At(Math.Clamp(middle * from.Length / Lane.Length, 0, from.Length));
        var sideways = Vector2.Normalize(position - was + new Vector2(1e-4f, 0));
        var heading = Vector2.Normalize(direction + (sideways * (1 - t) * 0.25f));
        return (Vector2.Lerp(was, position, t), heading);
    }

    public override string ToString() => $"{Kind.Name} {Id}";
}

/// <summary>The measurements of a run so far.</summary>
/// <param name="Active">Vehicles on the roads.</param>
/// <param name="Completed">Vehicles that reached where they were going.</param>
/// <param name="AverageTravelTime">Seconds from start to finish, on average.</param>
/// <param name="AverageDelay">Seconds more than with empty roads, on average.</param>
/// <param name="AverageStops">Times stopped on the way, on average.</param>
/// <param name="Throughput">Vehicles finishing per hour, over the last ten minutes.</param>
/// <param name="Standing">Vehicles standing still now.</param>
/// <param name="Queued">Vehicles that want to come in at the edges but have no room yet.</param>
public readonly record struct TrafficStats(int Active, int Completed, float AverageTravelTime, float AverageDelay, float AverageStops, float Throughput, int Standing, int Queued);

/// <summary>
/// Runs traffic on a <see cref="RoadNetwork"/>: vehicles come in at the edges as the demand says, pick the quickest
/// route to another edge, follow the vehicle ahead as the Intelligent Driver Model has it (slowing for bends), stop at
/// red and amber lights, give way where the rules say, never drive into a junction they can't get out of, and leave
/// at their destination. Advance it with <see cref="Step"/>; the same seed always runs the same.
/// </summary>
public sealed class TrafficSimulation
{
    private const float StepSize = 0.1f;
    private const float HardBraking = 9f;
    private const float GapTime = 1.5f;

    private readonly Random _random;
    private readonly List<Vehicle> _vehicles = [];
    private readonly Dictionary<Lane, List<Vehicle>> _onLane = [];
    private readonly Dictionary<Node, float> _demand = [];
    private readonly Dictionary<Node, int> _queued = [];
    private readonly Dictionary<Lane, int> _flow = [];
    private readonly Queue<float> _finishTimes = new();
    private readonly Dictionary<SignalGroup, List<Lane>> _signalled = [];
    private readonly Dictionary<Lane, float> _laneTimes = [];
    private readonly Dictionary<Vehicle, float> _entered = [];
    private float _leftOver;
    private int _nextId;
    private float _travelTotal;
    private float _delayTotal;
    private int _stopsTotal;

    public TrafficSimulation(RoadNetwork network, int seed)
    {
        ArgumentNullException.ThrowIfNull(network);
        Network = network;
        _random = new Random(seed);
        foreach (var lane in network.Lanes)
        {
            _onLane[lane] = [];
            if (lane.Signal is { } group)
            {
                if (!_signalled.TryGetValue(group, out var lanes))
                {
                    _signalled[group] = lanes = [];
                }

                lanes.Add(lane);
            }
        }
    }

    /// <summary>
    /// Whether new vehicles choose their routes by the travel times measured as the simulation runs (so they go round
    /// jams) rather than by the roads' lengths and limits alone.
    /// </summary>
    public bool AvoidJams { get; set; }

    /// <summary>Where generated traffic goes (a random one of them each time); every edge of the network when not set.</summary>
    public IReadOnlyList<Node>? Destinations { get; set; }

    /// <summary>How long a lane takes to drive now: the time measured over it lately, at least its free time.</summary>
    public float TravelTime(Lane lane)
    {
        ArgumentNullException.ThrowIfNull(lane);
        var free = lane.Length / lane.SpeedLimit;
        var measured = _laneTimes.GetValueOrDefault(lane, free);

        // Vehicles still on the lane count for how long they have been on it, so a jam shows before anyone gets out.
        var onIt = _onLane[lane].Select(v => Time - _entered.GetValueOrDefault(v, Time)).DefaultIfEmpty(0).Max();
        return MathF.Max(free, MathF.Max(measured, onIt));
    }

    public RoadNetwork Network { get; }

    /// <summary>Seconds since the start.</summary>
    public float Time { get; private set; }

    public IReadOnlyList<Vehicle> Vehicles => _vehicles;

    public int Completed { get; private set; }

    /// <summary>The kinds that come in, and how often each.</summary>
    public IReadOnlyList<(VehicleKind Kind, float Weight)> Mix { get; set; } = VehicleKind.TownMix;

    /// <summary>Raised as a vehicle reaches its destination.</summary>
    public event Action<Vehicle>? Arrived;

    /// <summary>Vehicles an hour coming in at an edge node (0 for none), each going to another edge.</summary>
    public void SetDemand(Node edge, float perHour)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _demand[edge] = perHour;
    }

    /// <summary>The same demand at every edge.</summary>
    public void SetDemand(float perHour)
    {
        foreach (var edge in Network.Entries)
        {
            _demand[edge] = perHour;
        }
    }

    /// <summary>The vehicles on a lane, front first.</summary>
    public IReadOnlyList<Vehicle> On(Lane lane) => _onLane[lane];

    /// <summary>How many vehicles have driven off the end of a lane.</summary>
    public int Flow(Lane lane) => _flow.GetValueOrDefault(lane);

    public TrafficStats Stats()
    {
        while (_finishTimes.Count > 0 && _finishTimes.Peek() < Time - 600)
        {
            _finishTimes.Dequeue();
        }

        var window = MathF.Min(Time, 600);
        return new TrafficStats(
            _vehicles.Count,
            Completed,
            Completed == 0 ? 0 : _travelTotal / Completed,
            Completed == 0 ? 0 : _delayTotal / Completed,
            Completed == 0 ? 0 : _stopsTotal / (float)Completed,
            window <= 0 ? 0 : _finishTimes.Count * 3600 / window,
            _vehicles.Count(v => v.Speed < 0.5f),
            _queued.Values.Sum());
    }

    /// <summary>The quickest lanes from one edge to another (with empty roads), or null when there is no way.</summary>
    public IReadOnlyList<Lane>? Route(Node from, Node to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (!Network.Nodes.Contains(from) || !Network.Nodes.Contains(to))
        {
            throw new ArgumentException("Both nodes must be in the network.");
        }

        return RouteFrom(from.Roads.SelectMany(r => r.LeavingFrom(from)), to);
    }

    /// <summary>The quickest lanes from any of <paramref name="starts"/> to an edge, or null when there is no way.</summary>
    private List<Lane>? RouteFrom(IEnumerable<Lane> starts, Node to)
    {
        var ends = to.Roads.SelectMany(r => r.ArrivingAt(to)).ToHashSet();
        var cost = new Dictionary<Lane, float>();
        var previous = new Dictionary<Lane, Lane>();
        var queue = new PriorityQueue<Lane, float>();
        foreach (var lane in starts)
        {
            cost[lane] = Cost(lane);
            queue.Enqueue(lane, cost[lane]);
        }

        while (queue.TryDequeue(out var lane, out var spent))
        {
            if (spent > cost[lane])
            {
                continue;
            }

            if (ends.Contains(lane))
            {
                var route = new List<Lane> { lane };
                while (previous.TryGetValue(route[^1], out var before))
                {
                    route.Add(before);
                }

                route.Reverse();
                return route;
            }

            foreach (var next in lane.Next)
            {
                var through = spent + Cost(next);
                if (through < cost.GetValueOrDefault(next, float.PositiveInfinity))
                {
                    cost[next] = through;
                    previous[next] = lane;
                    queue.Enqueue(next, through);
                }
            }

            // With lane changes, the lane beside on the same road: driven alongside, for a few seconds more.
            foreach (var beside in LaneChanges ? Beside(lane) : [])
            {
                var through = spent + ChangeCost;
                if (through < cost.GetValueOrDefault(beside, float.PositiveInfinity))
                {
                    cost[beside] = through;
                    previous[beside] = lane;
                    queue.Enqueue(beside, through);
                }
            }
        }

        return null;
    }

    private float Cost(Lane lane) => AvoidJams ? TravelTime(lane) : lane.Length / lane.SpeedLimit;

    /// <summary>Seconds a route counts for changing to the lane beside: what it costs to find a gap and move across.</summary>
    public float ChangeCost { get; set; } = 4;

    /// <summary>The lanes either side of one on a road, going the same way.</summary>
    private static IEnumerable<Lane> Beside(Lane lane)
    {
        if (lane.IsJunction || lane.Road is not { } road)
        {
            yield break;
        }

        var side = road.Forward.Contains(lane) ? road.Forward : road.Backward;
        var index = side.IndexOf(lane);
        if (index > 0)
        {
            yield return side[index - 1];
        }

        if (index + 1 < side.Count)
        {
            yield return side[index + 1];
        }
    }

    /// <summary>Whether two lanes run side by side on a road, the same way: a route going from one to the other changes lanes.</summary>
    private static bool AreBeside(Lane a, Lane b) => Beside(a).Contains(b);

    /// <summary>Whether the vehicle's route has it change to the lane beside before the end of this one.</summary>
    private static bool MustChange(Vehicle vehicle) =>
        vehicle.RouteIndex + 1 < vehicle.Route.Count && AreBeside(vehicle.Lane, vehicle.Route[vehicle.RouteIndex + 1]);

    /// <summary>
    /// Whether vehicles change lanes on multi-lane roads (MOBIL): to overtake, to leave a lane held up (by someone
    /// waiting to turn, say) or to keep right, wherever a route to their destination goes on from the other lane and
    /// the vehicle behind there needn't brake hard. Routes may then change lanes too (to the lane a turn is made from),
    /// the vehicle moving across at the first safe gap and waiting at the lane's end for one; and a vehicle whose
    /// best lane is full when it sets out starts in another. Off by default.
    /// </summary>
    public bool LaneChanges { get; set; }

    /// <summary>How much a driver weighs what a lane change costs or gains the others (MOBIL's politeness), 0 to 1.</summary>
    public float Politeness { get; set; } = 0.3f;

    /// <summary>Puts a vehicle at the start of its route from one edge to another, if there is a way and room; null if not.</summary>
    public Vehicle? Spawn(Node from, Node to, VehicleKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (Route(from, to) is not { } best)
        {
            return null;
        }

        var route = best;
        bool Full(Lane start) => _onLane[start] is { Count: > 0 } on && on[^1].Position - on[^1].Kind.Length < kind.Length + kind.MinGap;
        if (Full(route[0]))
        {
            // With lane changes, the first lane with room, changing over on the way.
            if (!LaneChanges || from.Roads.SelectMany(r => r.LeavingFrom(from)).FirstOrDefault(l => !Full(l)) is not { } free
                || RouteFrom([free], to) is not { } other)
            {
                return null;
            }

            route = other;
        }

        var first = route[0];
        var lane = _onLane[first];

#pragma warning disable CA5394 // Simulation, not security.
        var vehicle = new Vehicle(_nextId++, kind, route, 0.9f + ((float)_random.NextDouble() * 0.2f), Time, to)
        {
            Position = kind.Length,
        };
#pragma warning restore CA5394
        var room = lane.Count == 0 ? float.PositiveInfinity : lane[^1].Position - lane[^1].Kind.Length - kind.Length;
        vehicle.Speed = room > 30 ? MathF.Min(first.SpeedLimit, kind.MaxSpeed) * 0.7f : 0;
        _vehicles.Add(vehicle);
        lane.Add(vehicle);
        _entered[vehicle] = Time;
        return vehicle;
    }

    /// <summary>Advances the simulation, in steps of a tenth of a second.</summary>
    public void Step(float seconds)
    {
        _leftOver += seconds;
        while (_leftOver >= StepSize)
        {
            _leftOver -= StepSize;
            Tick(StepSize);
        }
    }

    private void Tick(float dt)
    {
        Time += dt;
        foreach (var plan in Network.Signals)
        {
            plan.Advance(dt, plan.Actuated ? phase => Waiting(plan, phase) : null);
        }

        Demand(dt);
        foreach (var vehicle in _vehicles)
        {
            vehicle.Acceleration = AccelerationOf(vehicle);
        }

        if (LaneChanges)
        {
            foreach (var vehicle in _vehicles.ToList())
            {
                ConsiderChange(vehicle);
            }
        }

        foreach (var vehicle in _vehicles.ToList())
        {
            Move(vehicle, dt);
        }

        foreach (var lane in _onLane.Values)
        {
            lane.Sort((a, b) => b.Position.CompareTo(a.Position));
        }
    }

    /// <summary>Whether a phase of a junction's lights has vehicles on its approaches near enough to want green.</summary>
    private bool Waiting(SignalPlan plan, int phase) => plan.Groups.Where(g => g.Phase == phase)
        .SelectMany(g => _signalled.GetValueOrDefault(g) ?? [])
        .Any(lane => _onLane[lane].Any(v => v.Position > lane.Length - 70));

    private void Demand(float dt)
    {
        foreach (var (edge, perHour) in _demand)
        {
#pragma warning disable CA5394 // Simulation, not security.
            if (perHour > 0 && _random.NextDouble() < perHour * dt / 3600)
            {
                _queued[edge] = _queued.GetValueOrDefault(edge) + 1;
            }

            if (_queued.GetValueOrDefault(edge) == 0)
            {
                continue;
            }

            var destinations = (Destinations ?? Network.Entries).Where(e => e != edge).ToList();
            if (destinations.Count == 0)
            {
                continue;
            }

            var to = destinations[_random.Next(destinations.Count)];
            var pick = (float)_random.NextDouble() * Mix.Sum(m => m.Weight);
#pragma warning restore CA5394
            var kind = Mix.First(m => (pick -= m.Weight) <= 0 || m == Mix[^1]).Kind;
            if (Spawn(edge, to, kind) is not null)
            {
                _queued[edge]--;
            }
        }
    }

    private float AccelerationOf(Vehicle vehicle)
    {
        var kind = vehicle.Kind;
        var v = vehicle.Speed;
        var lookahead = MathF.Max(80, v * 6);

        // The speed wanted: the limit here, lowered for slower lanes ahead in time to brake for them.
        var desired = MathF.Min(kind.MaxSpeed, vehicle.Lane.SpeedLimit) * vehicle.Eagerness;
        var ahead = vehicle.Lane.Length - vehicle.Position;
        for (var i = vehicle.RouteIndex + 1; i < vehicle.Route.Count && ahead < lookahead; i++)
        {
            var limit = vehicle.Route[i].SpeedLimit * vehicle.Eagerness;
            desired = MathF.Min(desired, MathF.Sqrt((limit * limit) + (2 * kind.Braking * ahead)));
            ahead += vehicle.Route[i].Length;
        }

        // What is in front: the nearest vehicle along the route, or a stop line it may not cross.
        var (gap, leaderSpeed) = Leader(vehicle, lookahead);
        if (StopLine(vehicle, lookahead) is { } line && line < gap)
        {
            (gap, leaderSpeed) = (line, 0);
        }

        var free = 1 - MathF.Pow(v / MathF.Max(desired, 0.1f), 4);
        var acceleration = kind.Acceleration * free;
        if (gap < float.PositiveInfinity)
        {
            var wanted = kind.MinGap + MathF.Max(0, (v * kind.TimeGap) + (v * (v - leaderSpeed) / (2 * MathF.Sqrt(kind.Acceleration * kind.Braking))));
            acceleration -= kind.Acceleration * MathF.Pow(wanted / MathF.Max(gap, 0.1f), 2);
        }

        return MathF.Max(acceleration, -HardBraking);
    }

    /// <summary>The gap to the nearest vehicle ahead along the route (its rear to this one's front), and its speed.</summary>
    private (float Gap, float Speed) Leader(Vehicle vehicle, float lookahead)
    {
        var distance = -vehicle.Position;
        for (var i = vehicle.RouteIndex; i < vehicle.Route.Count && distance < lookahead; i++)
        {
            var lane = vehicle.Route[i];
            Vehicle? nearest = null;
            foreach (var other in _onLane[lane])
            {
                if (other != vehicle && (i > vehicle.RouteIndex || other.Position > vehicle.Position))
                {
                    nearest = other;
                }
            }

            // Someone already merging in from a turning lane, about to come onto this lane ahead.
            var best = nearest is null ? float.PositiveInfinity : distance + nearest.Position - nearest.Kind.Length;
            var bestSpeed = nearest?.Speed ?? 0;
            if (i > vehicle.RouteIndex)
            {
                foreach (var feeder in lane.Previous)
                {
                    if (!feeder.IsJunction || feeder == vehicle.Route[i - 1])
                    {
                        continue;
                    }

                    foreach (var other in _onLane[feeder])
                    {
                        // Only one that gets there first counts, or two merging would each wait for the other.
                        var theirs = feeder.Length - other.Position;
                        if (other.RouteIndex + 1 < other.Route.Count && other.Route[other.RouteIndex + 1] == lane && theirs < 8
                            && (theirs < distance || (MathF.Abs(theirs - distance) < 0.01f && other.Id < vehicle.Id)))
                        {
                            var rear = distance - (feeder.Length - other.Position) - other.Kind.Length;
                            if (rear < best && distance > 0)
                            {
                                (best, bestSpeed) = (rear, other.Speed);
                            }
                        }
                    }
                }
            }

            if (best < float.PositiveInfinity)
            {
                return (best, bestSpeed);
            }

            if (i + 1 < vehicle.Route.Count && AreBeside(lane, vehicle.Route[i + 1]))
            {
                break;
            }

            distance += lane.Length;
        }

        return (float.PositiveInfinity, 0);
    }

    /// <summary>The distance to the first junction along the route this vehicle may not enter yet, if any.</summary>
    private float? StopLine(Vehicle vehicle, float lookahead)
    {
        var distance = vehicle.Lane.Length - vehicle.Position;
        for (var i = vehicle.RouteIndex; i + 1 < vehicle.Route.Count && distance < lookahead; i++)
        {
            var (here, next) = (vehicle.Route[i], vehicle.Route[i + 1]);
            if (AreBeside(here, next))
            {
                if (distance < vehicle.Kind.MinGap + 2 && vehicle.Speed < 0.5f)
                {
                    vehicle.Waiting += StepSize;
                }

                return MathF.Max(0, distance - 1);
            }

            if (next.IsJunction && !here.IsJunction || next.Kind == LaneKind.Connector && here.Kind == LaneKind.Ring)
            {
                if (!MayEnter(vehicle, here, next, i + 1, distance))
                {
                    if (distance < vehicle.Kind.MinGap + 2 && vehicle.Speed < 0.5f)
                    {
                        vehicle.Waiting += StepSize;
                    }

                    return MathF.Max(0, distance - 0.5f);
                }

                return null;
            }

            distance += next.Length;
        }

        return null;
    }

    private bool MayEnter(Vehicle vehicle, Lane from, Lane into, int index, float distance)
    {
        if (vehicle.Committed == into)
        {
            return true;
        }

        var v = vehicle.Speed;
        var stopping = (v * v / (2 * vehicle.Kind.Braking)) + 1;
        if (from.Signal is { } signal)
        {
            if (signal.State == SignalState.Red && distance > 0.5f || signal.State == SignalState.Amber && distance > stopping)
            {
                return false;
            }
        }

        // Too late to stop comfortably: go on, rather than brake hard on the line.
        if (distance < v * v / (2 * HardBraking))
        {
            vehicle.Committed = into;
            return true;
        }

        // Keep the junction clear: don't go in while the way out is backed up.
        if (index + 1 < vehicle.Route.Count && _onLane[vehicle.Route[index + 1]] is { Count: > 0 } outLane
            && outLane[^1].Position - outLane[^1].Kind.Length < vehicle.Kind.Length + 2 && outLane[^1].Speed < 3)
        {
            return false;
        }

        foreach (var conflict in into.Conflicts)
        {
            // Anyone already on the other path, short of where the paths meet, is in the way; so is a tail still on it.
            foreach (var other in _onLane[conflict.Other])
            {
                if (other.Position - other.Kind.Length < conflict.OtherAt + 1)
                {
                    return false;
                }
            }

            foreach (var beyond in conflict.Other.Next)
            {
                foreach (var other in _onLane[beyond])
                {
                    if (other.RouteIndex > 0 && other.Route[other.RouteIndex - 1] == conflict.Other
                        && conflict.Other.Length + other.Position - other.Kind.Length < conflict.OtherAt + 1)
                    {
                        return false;
                    }
                }
            }

            if (!conflict.OtherFirst)
            {
                continue;
            }

            // Who has priority and is near enough to get there before this one is through.
            var clear = TimeToCover(vehicle, distance + conflict.At + vehicle.Kind.Length + 2) + GapTime;
            foreach (var feeder in conflict.Other.Previous)
            {
                // Front first: nobody can get there before a vehicle standing still in front of them.
                var held = false;
                foreach (var other in _onLane[feeder])
                {
                    if (held)
                    {
                        break;
                    }

                    held = other.Speed < 0.3f;
                    if (other.RouteIndex + 1 >= other.Route.Count || other.Route[other.RouteIndex + 1] != conflict.Other)
                    {
                        continue;
                    }

                    var theirs = feeder.Length - other.Position + conflict.OtherAt;
                    if (theirs > 80 || feeder.Signal is { State: SignalState.Red })
                    {
                        continue;
                    }

                    // A priority vehicle that is itself standing still at its line doesn't hold everyone up for ever.
                    if (other.Speed < 0.3f && vehicle.Waiting > 4 && other.Waiting > 0 && vehicle.Id < other.Id)
                    {
                        continue;
                    }

                    // Their time to get there, pulling away if they stand: a car waiting at the line goes on green at once.
                    if (TimeToCover(other, theirs) < clear)
                    {
                        return false;
                    }
                }
            }
        }

        if (distance < stopping)
        {
            vehicle.Committed = into;
        }

        return true;
    }

    /// <summary>
    /// Seconds for a vehicle to cover <paramref name="distance"/> from its speed now, speeding up as it can towards the
    /// limit of the junction.
    /// </summary>
    private static float TimeToCover(Vehicle vehicle, float distance)
    {
        var (v, a) = (vehicle.Speed, vehicle.Kind.Acceleration * 0.8f);
        var top = MathF.Max(v, MathF.Min(vehicle.Lane.SpeedLimit, 10));
        var speedingUp = (top - v) / a;
        var covered = (v * speedingUp) + (a * speedingUp * speedingUp / 2);
        if (covered >= distance)
        {
            return (-v + MathF.Sqrt((v * v) + (2 * a * distance))) / a;
        }

        return speedingUp + ((distance - covered) / top);
    }

    private const float ChangeTime = 2f;
    private const float SafeBraking = 4f;
    private const float ChangeThreshold = 0.1f;
    private const float KeepRight = 0.3f;

    /// <summary>
    /// MOBIL: whether to move to a lane beside this one on the same road, and does it. A neighbour counts when a route
    /// to the destination goes on from it and there is room; the change is safe when the vehicle that would follow there
    /// needn't brake harder than <see cref="SafeBraking"/>; and worth it when what the driver gains, plus
    /// <see cref="Politeness"/> times what the followers gain or lose, beats a threshold. As on European roads one keeps
    /// right: moving right is favoured by a bias, moving left is held back by it, and no one pulls out to the left to
    /// let the one behind past.
    /// </summary>
    private void ConsiderChange(Vehicle vehicle)
    {
        if (MustChange(vehicle))
        {
            ChangeAsRouted(vehicle);
            return;
        }

        var lane = vehicle.Lane;
        if (lane.IsJunction || lane.Road is not { } road || vehicle.Destination is not { } destination || vehicle.ChangingFrom is not null
            || vehicle.Committed is not null || Time < vehicle.NextChange || vehicle.Position < vehicle.Kind.Length + 2
            || lane.Length - vehicle.Position < 20)
        {
            return;
        }

        var side = road.Forward.Contains(lane) ? road.Forward : road.Backward;
        var index = side.IndexOf(lane);
        var (ahead, behind) = Around(lane, vehicle.Position, vehicle);
        var now = Idm(vehicle, ahead);
        var oldFollowerNow = behind is null ? 0 : Idm(behind, vehicle);
        var oldFollowerThen = behind is null ? 0 : Idm(behind, ahead);

        (Lane Lane, float Gain, List<Lane> Route)? best = null;
        foreach (var step in new[] { 1, -1 })
        {
            if (index + step < 0 || index + step >= side.Count)
            {
                continue;
            }

            var other = side[index + step];
            var position = vehicle.Position * other.Length / lane.Length;
            var (newAhead, newBehind) = Around(other, position, null);
            if (newAhead is not null && newAhead.Position - newAhead.Kind.Length - position < vehicle.Kind.MinGap
                || newBehind is not null && position - vehicle.Kind.Length - newBehind.Position < newBehind.Kind.MinGap)
            {
                continue;
            }

            var then = Idm(vehicle, newAhead, gapFrom: position);
            var newFollowerNow = newBehind is null ? 0 : Idm(newBehind, newAhead);
            var newFollowerThen = newBehind is null ? 0 : Idm(newBehind, vehicle, leaderAt: position);
            if (newFollowerThen < -SafeBraking)
            {
                continue;
            }

            // No one pulls out to the left to let the one behind past (the European rules): only moving right counts
            // what the follower left behind gains.
            var behindGains = step > 0 ? oldFollowerThen - oldFollowerNow : 0;
            var gain = then - now + (Politeness * ((newFollowerThen - newFollowerNow) + behindGains));

            // Keeping right: moving right needs less, moving left more.
            gain += step > 0 ? KeepRight : -KeepRight;
            // Not into a lane it would have to leave again for its turn.
            if (gain > ChangeThreshold && (best is null || gain > best.Value.Gain) && RouteFrom([other], destination) is { } route
                && !(route.Count > 1 && AreBeside(route[0], route[1])))
            {
                best = (other, gain, route);
            }
        }

        if (best is not { } change)
        {
            return;
        }

        ChangeTo(vehicle, change.Lane, change.Route);
    }

    /// <summary>
    /// The lane change the route needs (to the lane a turn is made from, say): made as soon as there is room beside and
    /// the one that would follow there needn't brake hard; till then the vehicle slows to wait at the lane's end.
    /// </summary>
    private void ChangeAsRouted(Vehicle vehicle)
    {
        var lane = vehicle.Lane;
        var target = vehicle.Route[vehicle.RouteIndex + 1];
        if (vehicle.ChangingFrom is not null || vehicle.Position < vehicle.Kind.Length + 2)
        {
            return;
        }

        var position = Math.Min(vehicle.Position * target.Length / lane.Length, target.Length - 0.5f);
        var (ahead, behind) = Around(target, position, null);
        if (ahead is not null && ahead.Position - ahead.Kind.Length - position < vehicle.Kind.MinGap
            || behind is not null && position - vehicle.Kind.Length - behind.Position < behind.Kind.MinGap
            || behind is not null && Idm(behind, vehicle, leaderAt: position) < -SafeBraking)
        {
            return;
        }

        ChangeTo(vehicle, target, [.. vehicle.Route.Skip(vehicle.RouteIndex + 1)]);
    }

    private void ChangeTo(Vehicle vehicle, Lane target, List<Lane> route)
    {
        var lane = vehicle.Lane;
        _onLane[lane].Remove(vehicle);
        vehicle.Position = Math.Min(vehicle.Position * target.Length / lane.Length, target.Length - 0.5f);
        vehicle.Reroute(route);
        _onLane[target].Add(vehicle);
        vehicle.ChangingFrom = lane;
        vehicle.ChangeProgress = 0;
        vehicle.LaneChanges++;
        vehicle.NextChange = Time + (ChangeTime * 2);
    }

    /// <summary>Moves a vehicle onto another lane at the same place, its route going on from there (for tests).</summary>
    internal void PutOn(Vehicle vehicle, Lane lane)
    {
        _onLane[vehicle.Lane].Remove(vehicle);
        vehicle.Reroute(vehicle.Destination is { } to && RouteFrom([lane], to) is { } route ? route : [lane]);
        _onLane[lane].Add(vehicle);
    }

    /// <summary>The nearest vehicles ahead of and behind a position on a lane (leaving out <paramref name="skip"/>).</summary>
    private (Vehicle? Ahead, Vehicle? Behind) Around(Lane lane, float position, Vehicle? skip)
    {
        Vehicle? ahead = null, behind = null;
        foreach (var other in _onLane[lane])
        {
            if (other == skip)
            {
                continue;
            }

            if (other.Position > position)
            {
                if (ahead is null || other.Position < ahead.Position)
                {
                    ahead = other;
                }
            }
            else if (behind is null || other.Position > behind.Position)
            {
                behind = other;
            }
        }

        return (ahead, behind);
    }

    /// <summary>
    /// The Intelligent Driver Model's acceleration for a vehicle following <paramref name="leader"/> on the same lane
    /// (none: free road), its front at <paramref name="gapFrom"/> (its own position unless given) and the leader's at
    /// <paramref name="leaderAt"/> (the leader's unless given). Only the lane itself counts, not what lies beyond it.
    /// </summary>
    private static float Idm(Vehicle vehicle, Vehicle? leader, float? gapFrom = null, float? leaderAt = null)
    {
        var kind = vehicle.Kind;
        var v = vehicle.Speed;
        var desired = MathF.Min(kind.MaxSpeed, vehicle.Lane.SpeedLimit) * vehicle.Eagerness;
        var acceleration = kind.Acceleration * (1 - MathF.Pow(v / MathF.Max(desired, 0.1f), 4));
        if (leader is not null)
        {
            var gap = (leaderAt ?? leader.Position) - leader.Kind.Length - (gapFrom ?? vehicle.Position);
            var wanted = kind.MinGap + MathF.Max(0, (v * kind.TimeGap) + (v * (v - leader.Speed) / (2 * MathF.Sqrt(kind.Acceleration * kind.Braking))));
            acceleration -= kind.Acceleration * MathF.Pow(wanted / MathF.Max(gap, 0.1f), 2);
        }

        return MathF.Max(acceleration, -HardBraking);
    }

    private void Move(Vehicle vehicle, float dt)
    {
        if (vehicle.ChangingFrom is not null)
        {
            vehicle.ChangeProgress += dt / ChangeTime;
            if (vehicle.ChangeProgress >= 1)
            {
                vehicle.ChangingFrom = null;
            }
        }

        var before = vehicle.Speed;
        var speed = MathF.Max(0, before + (vehicle.Acceleration * dt));
        vehicle.Position += (before + speed) / 2 * dt;
        vehicle.Speed = speed;
        if (speed < 0.3f && !vehicle.Stopped)
        {
            vehicle.Stopped = true;
            vehicle.Stops++;
        }
        else if (speed > 2)
        {
            vehicle.Stopped = false;
        }

        // A lane the route leaves sideways is not driven off the end of: it waits there for its gap.
        if (MustChange(vehicle) && vehicle.Position > vehicle.Lane.Length - 0.1f)
        {
            (vehicle.Position, vehicle.Speed) = (vehicle.Lane.Length - 0.1f, 0);
        }

        while (vehicle.Position > vehicle.Lane.Length)
        {
            var lane = vehicle.Lane;
            vehicle.Position -= lane.Length;
            if (_entered.TryGetValue(vehicle, out var entered))
            {
                // What this lane took, folded into what it has been taking.
                var took = Time - entered;
                _laneTimes[lane] = _laneTimes.TryGetValue(lane, out var was) ? (was * 0.8f) + (took * 0.2f) : took;
            }

            _entered[vehicle] = Time;
            _onLane[lane].Remove(vehicle);
            _flow[lane] = _flow.GetValueOrDefault(lane) + 1;
            if (lane.IsJunction || vehicle.Route[vehicle.RouteIndex + Math.Min(1, vehicle.Route.Count - vehicle.RouteIndex - 1)].IsJunction)
            {
                vehicle.Waiting = 0;
            }

            if (vehicle.RouteIndex + 1 >= vehicle.Route.Count)
            {
                Arrive(vehicle);
                return;
            }

            vehicle.RouteIndex++;
            if (vehicle.Committed is not null && vehicle.Lane == vehicle.Committed)
            {
                vehicle.Committed = null;
            }

            _onLane[vehicle.Lane].Add(vehicle);
        }
    }

    private void Arrive(Vehicle vehicle)
    {
        _vehicles.Remove(vehicle);
        _entered.Remove(vehicle);
        Completed++;
        var travel = Time - vehicle.StartTime;
        _travelTotal += travel;
        _delayTotal += MathF.Max(0, travel - vehicle.FreeTime);
        _stopsTotal += vehicle.Stops;
        _finishTimes.Enqueue(Time);
        Arrived?.Invoke(vehicle);
    }
}
