using System.Numerics;

namespace Jentl.Raster;

/// <summary>A particle in 3D: where it is, where it goes per second, how long it has lived of its life, its size and colour.</summary>
public readonly record struct Particle(Vector3 Position, Vector3 Velocity, float Age, float Life, float Size, Vector4 Color)
{
    /// <summary>Its colour's alpha, fading to nothing over its life.</summary>
    public float Alpha => Color.W * Math.Clamp(1 - (Age / Life), 0, 1);
}

/// <summary>
/// Spray, dust, sparks: particles thrown into the world that fly, fall, slow and fade. Emit them with a velocity,
/// <see cref="Update"/> once a frame, and draw <see cref="Live"/> as points (<see cref="Renderer.DrawPoint"/>, or
/// <c>DrawParticles</c> on a mesh view). When full, a new particle replaces the oldest.
/// </summary>
public sealed class Particles(int capacity)
{
    private readonly List<Particle> _live = new(capacity);

    /// <summary>How fast particles fall, in units per second per second (down is -y).</summary>
    public float Gravity { get; set; }

    /// <summary>How quickly they slow: their speed shrinks by e for every 1/Drag seconds.</summary>
    public float Drag { get; set; }

    /// <summary>The ground height they settle on, or null to fall on forever.</summary>
    public float? Floor { get; set; }

    /// <summary>The living particles, oldest first.</summary>
    public IReadOnlyList<Particle> Live => _live;

    public void Emit(Vector3 at, Vector3 velocity, float life, float size, Vector4 color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(life);
        if (_live.Count == capacity)
        {
            _live.RemoveAt(0);
        }

        _live.Add(new Particle(at, velocity, 0, life, size, color));
    }

    /// <summary>Moves every particle on by <paramref name="seconds"/> and drops those whose life is over.</summary>
    public void Update(float seconds)
    {
        var slow = MathF.Exp(-Drag * seconds);
        var kept = 0;
        for (var i = 0; i < _live.Count; i++)
        {
            var p = _live[i];
            if (p.Age + seconds >= p.Life)
            {
                continue;
            }

            var velocity = (p.Velocity * slow) - new Vector3(0, Gravity * seconds, 0);
            var position = p.Position + (velocity * seconds);
            if (Floor is { } floor && position.Y < floor)
            {
                (position.Y, velocity.Y) = (floor, 0);
            }

            _live[kept++] = p with { Position = position, Velocity = velocity, Age = p.Age + seconds };
        }

        _live.RemoveRange(kept, _live.Count - kept);
    }

    public void Clear() => _live.Clear();
}
