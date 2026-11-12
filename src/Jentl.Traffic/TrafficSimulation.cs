using System.Numerics;

namespace Jentl.Traffic;

/// <summary>A vehicle on its way: where it is on its route, how fast it goes, and how long it has taken.</summary>
public sealed class Vehicle
{
    internal Vehicle(int id, VehicleKind kind, IReadOnlyList<Lane> route, float eagerness, float startTime)
    {
        Id = id;
        Kind = kind;
        Route = route;
        Eagerness = eagerness;
        StartTime = startTime;
        FreeTime = route.Sum(l => l.Length / MathF.Min(l.SpeedLimit, kind.MaxSpeed));
    }

    public int Id { get; }

    public VehicleKind Kind { get; }

    /// <summary>The lanes it drives, start to finish.</summary>
    public IReadOnlyList<Lane> Route { get; }

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

    /// <summary>Its middle and the way it faces, for drawing.</summary>
    public (Vector2 Position, Vector2 Direction) Pose()
    {
        var middle = Position - (Kind.Length / 2);
        if (middle >= 0 || RouteIndex == 0)
        {
            return Lane.Path.At(middle);
        }

        var previous = Route[RouteIndex - 1];
        return previous.Path.At(previous.Length + middle);
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

        var starts = from.Roads.SelectMany(r => r.LeavingFrom(from)).ToList();
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
        }

        return null;
    }

    private float Cost(Lane lane) => AvoidJams ? TravelTime(lane) : lane.Length / lane.SpeedLimit;

    /// <summary>Puts a vehicle at the start of its route from one edge to another, if there is a way and room; null if not.</summary>
    public Vehicle? Spawn(Node from, Node to, VehicleKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (Route(from, to) is not { } route)
        {
            return null;
        }

        var first = route[0];
        var lane = _onLane[first];
        if (lane.Count > 0 && lane[^1].Position - lane[^1].Kind.Length < kind.Length + kind.MinGap)
        {
            return null;
        }

#pragma warning disable CA5394 // Simulation, not security.
        var vehicle = new Vehicle(_nextId++, kind, route, 0.9f + ((float)_random.NextDouble() * 0.2f), Time)
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

    private void Move(Vehicle vehicle, float dt)
    {
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
