namespace Jentl.Core.Graphics;

/// <summary>
/// Easing curves mapping progress in [0, 1] to eased progress, 0 at the start and 1 at the end. "In" curves start
/// slowly, "Out" ones end slowly; Back overshoots a little, Elastic springs and Bounce lands like a dropped ball.
/// </summary>
public static class Easing
{
    public static float Linear(float t) => t;

    public static float QuadIn(float t) => t * t;

    public static float QuadOut(float t) => 1 - ((1 - t) * (1 - t));

    public static float QuadInOut(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;

    public static float CubicIn(float t) => t * t * t;

    public static float CubicOut(float t) => 1 - MathF.Pow(1 - t, 3);

    public static float CubicInOut(float t) => t < 0.5f ? 4 * t * t * t : 1 - (MathF.Pow((-2 * t) + 2, 3) / 2);

    public static float SineInOut(float t) => -(MathF.Cos(MathF.PI * t) - 1) / 2;

    /// <summary>Overshoots the end a little and settles back.</summary>
    public static float BackOut(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;
        return 1 + (c3 * MathF.Pow(t - 1, 3)) + (c1 * MathF.Pow(t - 1, 2));
    }

    /// <summary>Pulls back a little before going.</summary>
    public static float BackIn(float t)
    {
        const float c1 = 1.70158f;
        return ((c1 + 1) * t * t * t) - (c1 * t * t);
    }

    /// <summary>Springs past the end and wobbles to rest.</summary>
    public static float ElasticOut(float t) => t <= 0 ? 0 : t >= 1 ? 1 : (MathF.Pow(2, -10 * t) * MathF.Sin(((t * 10) - 0.75f) * (2 * MathF.PI / 3))) + 1;

    /// <summary>Lands and bounces, each bounce lower.</summary>
    public static float BounceOut(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        return t < 1 / d1 ? n1 * t * t
            : t < 2 / d1 ? (n1 * (t -= 1.5f / d1) * t) + 0.75f
            : t < 2.5 / d1 ? (n1 * (t -= 2.25f / d1) * t) + 0.9375f
            : (n1 * (t -= 2.625f / d1) * t) + 0.984375f;
    }
}
