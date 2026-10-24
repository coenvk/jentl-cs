using Microsoft.Xna.Framework;

namespace Jentl.Core.Graphics;

/// <summary>
/// A 2D camera: which part of the world shows in a screen <see cref="Viewport"/>, and how big. <see cref="Position"/>
/// is the world point in the middle of the view and <see cref="Zoom"/> the screen pixels per world unit. Pass
/// <see cref="Transform"/> to <c>SpriteBatch.Begin</c> to draw in world units, and use <see cref="ScreenToWorld"/>
/// for clicks. Set <see cref="Bounds"/> to keep the view inside the world.
/// </summary>
public sealed class Camera2D
{
    private readonly Random _random;
    private TimeSpan _shakeLeft;
    private TimeSpan _shakeTime;
    private float _shakeStrength;

    public Camera2D(int seed = 0) => _random = new Random(seed);

    /// <summary>The world point shown in the middle of the viewport.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Screen pixels per world unit.</summary>
    public float Zoom { get; set; } = 1;

    /// <summary>Turns the world, in radians, around <see cref="Position"/>.</summary>
    public float Rotation { get; set; }

    /// <summary>The screen area the camera draws into.</summary>
    public Rectangle Viewport { get; set; }

    /// <summary>
    /// The world area the view stays inside, if any: the camera stops at its edges, and a world smaller than the
    /// view is centred. Ignores <see cref="Rotation"/>.
    /// </summary>
    public Rectangle? Bounds { get; set; }

    /// <summary>Snaps the view to whole screen pixels, so scaled pixel art doesn't shimmer while scrolling.</summary>
    public bool RoundToPixels { get; set; }

    /// <summary>How far the view is thrown off by <see cref="Shake"/> this frame, in screen pixels.</summary>
    public Vector2 ShakeOffset { get; private set; }

    /// <summary>World to screen, for <c>SpriteBatch.Begin(transformMatrix: ...)</c>.</summary>
    public Matrix Transform
    {
        get
        {
            var centre = new Vector2(Viewport.X + Viewport.Width / 2f, Viewport.Y + Viewport.Height / 2f) + ShakeOffset;
            if (Rotation == 0)
            {
                // Without rotation the whole transform is a scale and an offset, which can be snapped.
                var offset = centre - Position * Zoom;
                if (RoundToPixels)
                {
                    offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
                }

                return Matrix.CreateScale(Zoom, Zoom, 1) * Matrix.CreateTranslation(offset.X, offset.Y, 0);
            }

            return Matrix.CreateTranslation(-Position.X, -Position.Y, 0)
                * Matrix.CreateRotationZ(-Rotation)
                * Matrix.CreateScale(Zoom, Zoom, 1)
                * Matrix.CreateTranslation(centre.X, centre.Y, 0);
        }
    }

    /// <summary>The world area in view (the box around it when rotated).</summary>
    public Rectangle VisibleArea
    {
        get
        {
            Vector2[] corners =
            [
                ScreenToWorld(new Vector2(Viewport.Left, Viewport.Top)),
                ScreenToWorld(new Vector2(Viewport.Right, Viewport.Top)),
                ScreenToWorld(new Vector2(Viewport.Left, Viewport.Bottom)),
                ScreenToWorld(new Vector2(Viewport.Right, Viewport.Bottom)),
            ];
            var min = corners.Aggregate(Vector2.Min);
            var max = corners.Aggregate(Vector2.Max);
            var left = (int)MathF.Floor(min.X);
            var top = (int)MathF.Floor(min.Y);
            return new Rectangle(left, top, (int)MathF.Ceiling(max.X) - left, (int)MathF.Ceiling(max.Y) - top);
        }
    }

    public Vector2 WorldToScreen(Vector2 world) => Vector2.Transform(world, Transform);

    public Vector2 ScreenToWorld(Vector2 screen) => Vector2.Transform(screen, Matrix.Invert(Transform));

    /// <summary>Puts <paramref name="target"/> in the middle of the view (as far as <see cref="Bounds"/> allow).</summary>
    public void LookAt(Vector2 target)
    {
        Position = target;
        Clamp();
    }

    /// <summary>
    /// Eases towards <paramref name="target"/>, covering the same share of the distance every moment, so it
    /// starts quickly and settles gently; a higher <paramref name="rate"/> keeps closer. The target may wander
    /// <paramref name="deadZone"/> (world units each way) from the middle before the camera moves at all.
    /// </summary>
    public void Follow(Vector2 target, TimeSpan elapsed, float rate = 8, Vector2 deadZone = default)
    {
        var away = target - Position;
        var outside = new Vector2(
            Math.Sign(away.X) * MathF.Max(0, MathF.Abs(away.X) - deadZone.X),
            Math.Sign(away.Y) * MathF.Max(0, MathF.Abs(away.Y) - deadZone.Y));
        Position += outside * (1 - MathF.Exp(-rate * (float)elapsed.TotalSeconds));
        Clamp();
    }

    /// <summary>
    /// Follows something that travels, turning the view so it heads up the screen: the rotation eases towards
    /// <paramref name="heading"/> (the short way round) at <paramref name="rate"/>, and <paramref name="target"/> is
    /// kept at <paramref name="screenAnchor"/>, a fraction of the viewport ((0.5, 0.5), the middle, by default; lower
    /// shows more of what lies ahead). Ignores <see cref="Bounds"/>.
    /// </summary>
    /// <param name="target">Where the follower is, in world units.</param>
    /// <param name="heading">Its direction of travel in radians (0 along +X, π/2 down the screen).</param>
    /// <param name="elapsed">Time since the last call.</param>
    /// <param name="rate">How quickly the view turns; higher keeps closer.</param>
    /// <param name="screenAnchor">Where on the viewport the target stays.</param>
    public void FollowHeading(Vector2 target, float heading, TimeSpan elapsed, float rate = 6, Vector2? screenAnchor = null)
    {
        var turn = MathHelper.WrapAngle(heading + MathHelper.PiOver2 - Rotation);
        Rotation += turn * (1 - MathF.Exp(-rate * (float)elapsed.TotalSeconds));

        // The world offset that lands on the anchor once rotated and scaled by the transform.
        var fromCentre = ((screenAnchor ?? new Vector2(0.5f, 0.5f)) - new Vector2(0.5f, 0.5f)) * new Vector2(Viewport.Width, Viewport.Height) / Zoom;
        var (sin, cos) = MathF.SinCos(Rotation);
        Position = target - new Vector2((fromCentre.X * cos) - (fromCentre.Y * sin), (fromCentre.X * sin) + (fromCentre.Y * cos));
    }

    /// <summary>Shakes the view up to <paramref name="strength"/> screen pixels, dying away over <paramref name="duration"/>.</summary>
    public void Shake(float strength, TimeSpan duration)
    {
        // A stronger shake takes over; a weaker one never cuts a big one short.
        if (strength >= _shakeStrength * Fraction(_shakeLeft, _shakeTime))
        {
            _shakeStrength = strength;
            _shakeLeft = _shakeTime = duration;
        }
    }

    /// <summary>Advances the shake. Call once per frame.</summary>
    public void Update(TimeSpan elapsed)
    {
        _shakeLeft -= elapsed;
        if (_shakeLeft <= TimeSpan.Zero)
        {
            _shakeLeft = TimeSpan.Zero;
            ShakeOffset = Vector2.Zero;
            return;
        }

        var left = Fraction(_shakeLeft, _shakeTime);
#pragma warning disable CA5394 // Visual effect, not security relevant.
        var angle = (float)(_random.NextDouble() * MathF.Tau);
#pragma warning restore CA5394
        ShakeOffset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * _shakeStrength * left * left;
    }

    private static float Fraction(TimeSpan part, TimeSpan whole) =>
        whole <= TimeSpan.Zero ? 0 : (float)(part / whole);

    private void Clamp()
    {
        if (Bounds is not { } bounds || Zoom <= 0)
        {
            return;
        }

        static float Axis(float at, int start, int length, float view) =>
            length <= view ? start + length / 2f : Math.Clamp(at, start + view / 2, start + length - view / 2);

        Position = new Vector2(
            Axis(Position.X, bounds.X, bounds.Width, Viewport.Width / Zoom),
            Axis(Position.Y, bounds.Y, bounds.Height, Viewport.Height / Zoom));
    }
}
