using Jentl.Core.Graphics;

namespace Jentl.Core.Tests;

public class EasingTests
{
    public static TheoryData<string> Curves => new() { nameof(Easing.Linear), nameof(Easing.QuadInOut), nameof(Easing.CubicOut) };

    [Theory]
    [MemberData(nameof(Curves))]
    public void Curves_StartAtZeroAndEndAtOne(string name)
    {
        var curve = Curve(name);

        curve(0).Should().BeApproximately(0, 1e-6f);
        curve(1).Should().BeApproximately(1, 1e-6f);
    }

    [Theory]
    [MemberData(nameof(Curves))]
    public void Curves_AreMonotonic(string name)
    {
        var curve = Curve(name);
        var values = Enumerable.Range(0, 101).Select(i => curve(i / 100f)).ToList();

        values.Should().BeInAscendingOrder();
    }

    private static Func<float, float> Curve(string name) => name switch
    {
        nameof(Easing.Linear) => Easing.Linear,
        nameof(Easing.QuadInOut) => Easing.QuadInOut,
        nameof(Easing.CubicOut) => Easing.CubicOut,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}
