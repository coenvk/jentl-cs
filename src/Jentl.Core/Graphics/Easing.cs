namespace Jentl.Core.Graphics;

/// <summary>Easing curves mapping progress in [0, 1] to eased progress in [0, 1].</summary>
public static class Easing
{
    public static float Linear(float t) => t;

    public static float QuadInOut(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;

    public static float CubicOut(float t) => 1 - MathF.Pow(1 - t, 3);
}
