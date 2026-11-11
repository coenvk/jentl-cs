using Microsoft.Xna.Framework;

namespace Jentl.Core.Graphics;

/// <summary>
/// A flash on a hit: <see cref="Trigger"/> it, and <see cref="Tint"/> turns a sprite's colour towards the flash's and
/// back over the time given.
/// </summary>
public sealed class Flash
{
    private TimeSpan _left;
    private TimeSpan _length;

    public Color Colour { get; private set; } = Color.White;

    /// <summary>How much of the flash shows now, 1 at the strike to 0.</summary>
    public float Amount => _length <= TimeSpan.Zero ? 0 : (float)Math.Clamp(_left / _length, 0, 1);

    public void Trigger(TimeSpan length, Color? colour = null)
    {
        _left = _length = length;
        Colour = colour ?? Color.White;
    }

    public void Update(TimeSpan elapsed) => _left = _left > elapsed ? _left - elapsed : TimeSpan.Zero;

    public Color Tint(Color colour) => Color.Lerp(colour, Colour, Amount);
}

/// <summary>
/// Squash and stretch: <see cref="Trigger"/> with a positive amount for a landing (wider, shorter) or a negative one
/// for a jump (narrower, taller); <see cref="Scale"/> springs back to (1, 1), keeping the area about the same.
/// </summary>
public sealed class SquashStretch
{
    private TimeSpan _time;
    private TimeSpan _length;
    private float _amount;

    public Vector2 Scale
    {
        get
        {
            if (_length <= TimeSpan.Zero || _time >= _length)
            {
                return Vector2.One;
            }

            var left = _amount * (1 - Easing.ElasticOut((float)(_time / _length)));
            return new Vector2(1 + left, 1 / (1 + left));
        }
    }

    public void Trigger(float amount, TimeSpan? length = null)
    {
        _amount = amount;
        _length = length ?? TimeSpan.FromMilliseconds(400);
        _time = TimeSpan.Zero;
    }

    public void Update(TimeSpan elapsed) => _time += elapsed;
}

/// <summary>
/// A shake for anything (a panel, a sprite, the screen): <see cref="Trigger"/> adds to its strength, which fades
/// over the time given; <see cref="Offset"/> is where it is thrown this frame, smooth noise rather than jitter. The
/// same triggers and frames always shake the same way.
/// </summary>
public sealed class Shake
{
    private TimeSpan _time;
    private TimeSpan _left;
    private TimeSpan _length;
    private float _strength;

    /// <summary>How fast it shakes, in wobbles a second.</summary>
    public float Frequency { get; set; } = 22;

    public Vector2 Offset
    {
        get
        {
            if (_left <= TimeSpan.Zero || _length <= TimeSpan.Zero)
            {
                return Vector2.Zero;
            }

            var fade = (float)(_left / _length);
            var t = (float)_time.TotalSeconds * Frequency;
            var x = MathF.Sin(t * 1.0f) + (0.5f * MathF.Sin((t * 2.3f) + 1.3f));
            var y = MathF.Sin((t * 1.17f) + 2.1f) + (0.5f * MathF.Sin((t * 2.71f) + 0.4f));
            return new Vector2(x, y) * (_strength * fade * fade / 1.5f);
        }
    }

    /// <summary>Shakes by up to <paramref name="strength"/> (in pixels, or whatever the offset is used in), fading over <paramref name="length"/>.</summary>
    public void Trigger(float strength, TimeSpan length)
    {
        _strength = Math.Max(_left > TimeSpan.Zero ? _strength * (float)(_left / _length) : 0, 0) + strength;
        _left = _length = length;
    }

    public void Update(TimeSpan elapsed)
    {
        _time += elapsed;
        _left = _left > elapsed ? _left - elapsed : TimeSpan.Zero;
    }
}
