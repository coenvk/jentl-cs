using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Graphics;

public enum ParticleShape
{
    /// <summary>A soft round dot.</summary>
    Dot,

    /// <summary>A spinning rectangle, half as tall as wide: confetti.</summary>
    Flake,
}

/// <summary>
/// What a burst of particles looks like. Speeds are in pixels a second and sizes in pixels at scale 1;
/// <see cref="Direction"/> (radians, 0 is right, -π/2 is up) and <see cref="Spread"/> (the full width of the
/// fan, 2π for every way) aim the burst. Particles shrink from <see cref="StartSize"/> to <see cref="EndSize"/>
/// and, with <see cref="Fade"/>, fade out over their life.
/// </summary>
public sealed record ParticleEffect(
    int Count,
    float MinSpeed,
    float MaxSpeed,
    float MinLife,
    float MaxLife,
    float StartSize,
    float EndSize,
    IReadOnlyList<Color> Colors,
    float Direction = -MathF.PI / 2,
    float Spread = MathF.Tau,
    float Gravity = 0,
    float Drag = 0,
    ParticleShape Shape = ParticleShape.Dot,
    float Spin = 0,
    bool Fade = true);

/// <summary>Ready-made effects, in pixels at UI scale 1.</summary>
public static class ParticleEffects
{
    /// <summary>Paper flakes in many colours, thrown up and fluttering down: for a win.</summary>
    public static ParticleEffect Confetti { get; } = new(
        Count: 120,
        MinSpeed: 120,
        MaxSpeed: 420,
        MinLife: 2.2f,
        MaxLife: 3.6f,
        StartSize: 12,
        EndSize: 10,
        Colors: [new Color(255, 199, 44), new Color(232, 72, 85), new Color(76, 175, 239), new Color(110, 204, 120), new Color(190, 120, 230), Color.White],
        Direction: MathF.PI / 2,
        Spread: MathF.PI / 3,
        Gravity: 160,
        Drag: 1.6f,
        Shape: ParticleShape.Flake,
        Spin: 9);

    /// <summary>A small burst of golden sparks: a card home, an item picked up.</summary>
    public static ParticleEffect Sparkles { get; } = new(
        Count: 18,
        MinSpeed: 40,
        MaxSpeed: 160,
        MinLife: 0.35f,
        MaxLife: 0.7f,
        StartSize: 6,
        EndSize: 1,
        Colors: [new Color(255, 230, 120), new Color(255, 199, 44), Color.White],
        Drag: 3);

    /// <summary>Sparks rising slowly: levelling up, a lock springing open.</summary>
    public static ParticleEffect Rise { get; } = new(
        Count: 30,
        MinSpeed: 30,
        MaxSpeed: 110,
        MinLife: 0.8f,
        MaxLife: 1.4f,
        StartSize: 7,
        EndSize: 2,
        Colors: [new Color(255, 230, 120), new Color(140, 220, 255), Color.White],
        Spread: MathF.PI / 2,
        Gravity: -40,
        Drag: 1);

    /// <summary>A quick red-orange spray: a blow landing.</summary>
    public static ParticleEffect Hit { get; } = new(
        Count: 14,
        MinSpeed: 80,
        MaxSpeed: 260,
        MinLife: 0.2f,
        MaxLife: 0.45f,
        StartSize: 7,
        EndSize: 2,
        Colors: [new Color(255, 96, 80), new Color(255, 170, 80), Color.White],
        Drag: 5);

    /// <summary>Grey puffs drifting up: something vanishing.</summary>
    public static ParticleEffect Smoke { get; } = new(
        Count: 16,
        MinSpeed: 10,
        MaxSpeed: 60,
        MinLife: 0.6f,
        MaxLife: 1.1f,
        StartSize: 8,
        EndSize: 20,
        Colors: [new Color(170, 170, 180), new Color(120, 120, 135)],
        Spread: MathF.PI,
        Gravity: -30,
        Drag: 1.5f);

    /// <summary>Water thrown up behind a wheel on a wet road; aim it backwards from the car.</summary>
    public static ParticleEffect Spray { get; } = new(
        Count: 3,
        MinSpeed: 20,
        MaxSpeed: 60,
        MinLife: 0.25f,
        MaxLife: 0.5f,
        StartSize: 3,
        EndSize: 6,
        Colors: [new Color(200, 215, 235, 170), new Color(170, 190, 215, 140)],
        Spread: MathF.PI / 3,
        Drag: 3);

    /// <summary>Grit or snow kicked up behind a wheel on a loose surface; tint it to the ground with a new colour list.</summary>
    public static ParticleEffect Kickup { get; } = new(
        Count: 2,
        MinSpeed: 15,
        MaxSpeed: 45,
        MinLife: 0.3f,
        MaxLife: 0.6f,
        StartSize: 3,
        EndSize: 5,
        Colors: [new Color(170, 140, 100, 200), new Color(130, 105, 75, 180)],
        Spread: MathF.PI / 2,
        Drag: 2.5f);

    /// <summary>A few brown specks kicked up: a step, a thud.</summary>
    public static ParticleEffect Dust { get; } = new(
        Count: 8,
        MinSpeed: 20,
        MaxSpeed: 70,
        MinLife: 0.25f,
        MaxLife: 0.5f,
        StartSize: 5,
        EndSize: 2,
        Colors: [new Color(170, 140, 100), new Color(130, 105, 75)],
        Spread: MathF.PI,
        Gravity: 60,
        Drag: 2);
}

/// <summary>
/// A pool of short-lived particles: emit bursts, update once per frame, draw. Positions are in whatever units
/// the owner chooses (screen pixels, or a map's pixels with an offset and scale when drawn). When full, the
/// oldest particles make room for new ones.
/// </summary>
public sealed class ParticleSystem(int capacity = 2000, int seed = 0)
{
    private readonly Particle[] _particles = new Particle[capacity];
    private readonly Random _random = new(seed);
    private int _next;

    /// <summary>How many particles are alive.</summary>
    public int Count => _particles.Count(p => p.Age < p.Life);

    /// <summary>Bursts <paramref name="effect"/> out of <paramref name="at"/>, sized and sped up by <paramref name="scale"/>.</summary>
    public void Emit(Vector2 at, ParticleEffect effect, float scale = 1, int? count = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        for (var i = 0; i < (count ?? effect.Count); i++)
        {
            Spawn(at, effect, scale);
        }
    }

    /// <summary>Bursts <paramref name="effect"/> from random points of <paramref name="area"/> (e.g. confetti across the top of the screen).</summary>
    public void EmitArea(Rectangle area, ParticleEffect effect, float scale = 1, int? count = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        for (var i = 0; i < (count ?? effect.Count); i++)
        {
            Spawn(new Vector2(area.X + Next() * area.Width, area.Y + Next() * area.Height), effect, scale);
        }
    }

    public void Update(TimeSpan elapsed)
    {
        var dt = (float)elapsed.TotalSeconds;
        for (var i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (p.Age >= p.Life)
            {
                continue;
            }

            p.Age += dt;
            p.Velocity *= MathF.Max(0, 1 - p.Effect.Drag * dt);
            p.Velocity.Y += p.Effect.Gravity * p.Scale * dt;
            p.Position += p.Velocity * dt;
            p.Rotation += p.Spin * dt;
        }
    }

    /// <summary>Draws every live particle, at <paramref name="offset"/> + position × <paramref name="scale"/>.</summary>
    public void Draw(SpriteBatch batch, Vector2 offset = default, float scale = 1)
    {
        ArgumentNullException.ThrowIfNull(batch);
        foreach (var p in _particles)
        {
            if (p.Age >= p.Life)
            {
                continue;
            }

            var t = p.Age / p.Life;
            var size = MathHelper.Lerp(p.Effect.StartSize, p.Effect.EndSize, t) * p.Scale * scale;
            var color = p.Effect.Fade ? p.Color * (1 - t * t) : p.Color;
            var at = offset + p.Position * scale;
            if (p.Effect.Shape == ParticleShape.Flake)
            {
                // Flakes tumble: their apparent height swings as they turn over.
                var flip = MathF.Abs(MathF.Cos(p.Rotation * 0.7f));
                batch.FillRotated(at, new Vector2(size, size * 0.5f * (0.3f + 0.7f * flip)), p.Rotation, color);
            }
            else
            {
                batch.FillCircle(at, size / 2, color);
            }
        }
    }

    public void Clear() => Array.Clear(_particles);

    private float Next()
    {
#pragma warning disable CA5394 // Visual effects, not security relevant.
        return (float)_random.NextDouble();
#pragma warning restore CA5394
    }

    private void Spawn(Vector2 at, ParticleEffect effect, float scale)
    {
        var angle = effect.Direction + (Next() - 0.5f) * effect.Spread;
        var speed = MathHelper.Lerp(effect.MinSpeed, effect.MaxSpeed, Next()) * scale;
        _particles[_next] = new Particle
        {
            Effect = effect,
            Position = at,
            Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
            Life = MathHelper.Lerp(effect.MinLife, effect.MaxLife, Next()),
            Color = effect.Colors[(int)(Next() * effect.Colors.Count) % effect.Colors.Count],
            Rotation = Next() * MathF.Tau,
            Spin = (Next() - 0.5f) * 2 * effect.Spin,
            Scale = scale,
        };
        _next = (_next + 1) % _particles.Length;
    }

    private struct Particle
    {
        public ParticleEffect Effect;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public Color Color;
        public float Rotation;
        public float Spin;
        public float Scale;
    }
}
