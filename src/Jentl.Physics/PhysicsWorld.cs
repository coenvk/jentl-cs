using System.Numerics;

namespace Jentl.Physics;

/// <summary>
/// Moves bodies and settles their collisions. <see cref="Update"/> it with the frame time: it runs whole steps of
/// <see cref="TimeStep"/>, so the simulation behaves the same at any frame rate. Each step applies gravity and
/// forces, finds touching pairs (sorted along x, so only neighbours are tested), bounces and rubs them with
/// impulses, moves everything and nudges overlaps apart. Contacts starting and ending are reported as events.
/// </summary>
public sealed class PhysicsWorld
{
    private readonly List<Body> _bodies = [];
    private readonly List<Contact> _contacts = [];
    private readonly List<float> _bounces = [];
    private HashSet<(int, int)> _touching = [];
    private TimeSpan _pending;
    private int _nextId;

    public Vector2 Gravity { get; set; }

    /// <summary>The length of one simulation step (120 a second by default).</summary>
    public TimeSpan TimeStep { get; set; } = TimeSpan.FromSeconds(1.0 / 120);

    /// <summary>Most steps one update may run; a long stall then skips time rather than freezing to catch up.</summary>
    public int MaxStepsPerUpdate { get; set; } = 8;

    /// <summary>Passes over the contacts per step; more settles stacks better.</summary>
    public int Iterations { get; set; } = 6;

    /// <summary>Impacts slower than this (units a second) don't bounce, so resting bodies stay at rest.</summary>
    public float BounceThreshold { get; set; } = 20;

    /// <summary>Overlap left alone (units), so touching bodies don't jitter.</summary>
    public float Slop { get; set; } = 0.25f;

    public IReadOnlyList<Body> Bodies => _bodies;

    /// <summary>The contacts found in the last step, sensors included.</summary>
    public IReadOnlyList<Contact> Contacts => _contacts;

    /// <summary>How far into the next step the leftover time reaches (0 to 1), to draw bodies between steps.</summary>
    public float Alpha => (float)(_pending / TimeStep);

    /// <summary>Two bodies started touching (or a sensor started overlapping).</summary>
    public event Action<Contact>? ContactBegan;

    /// <summary>Two bodies stopped touching.</summary>
    public event Action<Body, Body>? ContactEnded;

    public Body Add(Body body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Id >= 0)
        {
            throw new InvalidOperationException($"{body} is already in a world.");
        }

        body.Id = _nextId++;
        _bodies.Add(body);
        return body;
    }

    /// <summary>Takes the body out. Its contacts go quietly, without ended events.</summary>
    public bool Remove(Body body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!_bodies.Remove(body))
        {
            return false;
        }

        _touching.RemoveWhere(p => p.Item1 == body.Id || p.Item2 == body.Id);
        _contacts.RemoveAll(c => ReferenceEquals(c.A, body) || ReferenceEquals(c.B, body));
        body.Id = -1;
        return true;
    }

    /// <summary>Advances by <paramref name="elapsed"/> in whole steps; the remainder waits for the next update. Returns the steps run.</summary>
    public int Update(TimeSpan elapsed)
    {
        _pending += elapsed;
        var steps = 0;
        while (_pending >= TimeStep && steps < MaxStepsPerUpdate)
        {
            Step((float)TimeStep.TotalSeconds);
            _pending -= TimeStep;
            steps++;
        }

        if (steps == MaxStepsPerUpdate && _pending > TimeStep)
        {
            _pending = TimeStep;
        }

        return steps;
    }

    /// <summary>Runs one step of <paramref name="seconds"/>.</summary>
    public void Step(float seconds)
    {
        foreach (var body in _bodies.Where(b => b.Type == BodyType.Dynamic))
        {
            var velocity = body.Velocity + (Gravity * body.GravityScale + body.Force * body.InverseMass) * seconds;
            body.Velocity = velocity / (1 + seconds * body.LinearDamping);
            body.Force = Vector2.Zero;
        }

        FindContacts();
        _bounces.Clear();
        foreach (var contact in _contacts)
        {
            var approach = Vector2.Dot(contact.B.Velocity - contact.A.Velocity, contact.Normal);
            _bounces.Add(approach < -BounceThreshold ? MathF.Max(contact.A.Restitution, contact.B.Restitution) : 0);
        }

        for (var i = 0; i < Iterations; i++)
        {
            for (var c = 0; c < _contacts.Count; c++)
            {
                if (!_contacts[c].A.IsSensor && !_contacts[c].B.IsSensor)
                {
                    Resolve(_contacts[c], _bounces[c]);
                }
            }
        }

        foreach (var body in _bodies.Where(b => b.Type != BodyType.Static))
        {
            body.Position += body.Velocity * seconds;
        }

        foreach (var contact in _contacts.Where(c => !c.A.IsSensor && !c.B.IsSensor))
        {
            Separate(contact);
        }

        ReportContacts();
    }

    /// <summary>The bodies whose bounds overlap <paramref name="area"/> and that are on a layer in <paramref name="mask"/>.</summary>
    public IEnumerable<Body> QueryArea(Aabb area, uint mask = uint.MaxValue) =>
        _bodies.Where(b => (b.Layer & mask) != 0 && b.Bounds.Overlaps(area)).ToList();

    /// <summary>The bodies whose shape covers <paramref name="point"/>.</summary>
    public IEnumerable<Body> QueryPoint(Vector2 point, uint mask = uint.MaxValue) =>
        _bodies.Where(b => (b.Layer & mask) != 0 && Collision.Contains(b, point)).ToList();

    /// <summary>The nearest body along a ray (sensors skipped unless asked for), within <paramref name="maxDistance"/>.</summary>
    public RayHit? Raycast(Vector2 origin, Vector2 direction, float maxDistance, uint mask = uint.MaxValue, bool sensors = false)
    {
        if (direction == Vector2.Zero)
        {
            return null;
        }

        direction = Vector2.Normalize(direction);
        RayHit? best = null;
        foreach (var body in _bodies)
        {
            if ((body.Layer & mask) == 0 || (body.IsSensor && !sensors))
            {
                continue;
            }

            if (Collision.Ray(body, origin, direction, out var normal) is { } distance && distance <= maxDistance && (best is null || distance < best.Value.Distance))
            {
                best = new RayHit(body, origin + direction * distance, normal, distance);
            }
        }

        return best;
    }

    /// <summary>Sweep and prune: bodies sorted by left edge only need testing against those whose left edge comes before their right.</summary>
    private void FindContacts()
    {
        _contacts.Clear();
        var sorted = _bodies.Select(b => (Body: b, Bounds: b.Bounds)).OrderBy(b => b.Bounds.Min.X).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            var (a, boundsA) = sorted[i];
            for (var j = i + 1; j < sorted.Length && sorted[j].Bounds.Min.X < boundsA.Max.X; j++)
            {
                var (b, boundsB) = sorted[j];
                if (!boundsA.Overlaps(boundsB) || !a.CollidesWith(b) || (a.Type != BodyType.Dynamic && b.Type != BodyType.Dynamic && !a.IsSensor && !b.IsSensor))
                {
                    continue;
                }

                // Keep a stable order (lower id first) so events and results don't depend on positions.
                var (first, second) = a.Id < b.Id ? (a, b) : (b, a);
                if (Collision.Test(first, second) is { } contact)
                {
                    _contacts.Add(contact);
                }
            }
        }
    }

    private static void Resolve(Contact contact, float bounce)
    {
        var (a, b, normal) = (contact.A, contact.B, contact.Normal);
        var inverseMass = a.InverseMass + b.InverseMass;
        if (inverseMass == 0)
        {
            return;
        }

        var relative = b.Velocity - a.Velocity;
        var approach = Vector2.Dot(relative, normal);
        if (approach > 0)
        {
            return;
        }

        var impulse = -(1 + bounce) * approach / inverseMass;
        a.Velocity -= normal * impulse * a.InverseMass;
        b.Velocity += normal * impulse * b.InverseMass;

        // Friction: oppose sliding along the surface, at most the grip times the push.
        relative = b.Velocity - a.Velocity;
        var sliding = relative - normal * Vector2.Dot(relative, normal);
        if (sliding.LengthSquared() < 1e-8f)
        {
            return;
        }

        var tangent = Vector2.Normalize(sliding);
        var grip = MathF.Sqrt(a.Friction * b.Friction);
        var rub = Math.Clamp(-Vector2.Dot(relative, tangent) / inverseMass, -impulse * grip, impulse * grip);
        a.Velocity -= tangent * rub * a.InverseMass;
        b.Velocity += tangent * rub * b.InverseMass;
    }

    private void Separate(Contact contact)
    {
        var (a, b) = (contact.A, contact.B);
        var inverseMass = a.InverseMass + b.InverseMass;
        if (inverseMass == 0 || Collision.Test(a, b) is not { } now)
        {
            return;
        }

        var push = now.Normal * (MathF.Max(now.Depth - Slop, 0) / inverseMass * 0.8f);
        a.Position -= push * a.InverseMass;
        b.Position += push * b.InverseMass;
    }

    private void ReportContacts()
    {
        var touching = _contacts.Select(c => (c.A.Id, c.B.Id)).ToHashSet();
        var began = _contacts.Where(c => !_touching.Contains((c.A.Id, c.B.Id))).ToList();
        var ended = _touching.Where(p => !touching.Contains(p)).ToList();
        _touching = touching;

        foreach (var contact in began)
        {
            ContactBegan?.Invoke(contact);
        }

        foreach (var (first, second) in ended)
        {
            if (_bodies.FirstOrDefault(b => b.Id == first) is { } a && _bodies.FirstOrDefault(b => b.Id == second) is { } b)
            {
                ContactEnded?.Invoke(a, b);
            }
        }
    }
}
