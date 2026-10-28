namespace Jentl.Geometry.Tests;

public class NoiseTests
{
    private static IEnumerable<(float X, float Y)> Samples() =>
        Enumerable.Range(0, 4000).Select(i => (i * 0.137f - 200, i * 0.291f % 97 - 40));

    [Fact]
    public void TheSameSeed_GivesTheSameField_AndAnotherSeedAnother()
    {
        var a = new Noise(42);
        var b = new Noise(42);
        var c = new Noise(43);

        Samples().Select(p => a.At(p.X, p.Y)).Should().Equal(Samples().Select(p => b.At(p.X, p.Y)));
        Samples().Count(p => a.At(p.X, p.Y) != c.At(p.X, p.Y)).Should().BeGreaterThan(3500);
    }

    [Fact]
    public void ASeed_GivesTheSameValuesOnEveryMachine()
    {
        // Pinned: a change here changes every world generated from a saved seed.
        new Noise(7).At(12.3f, -4.56f).Should().BeApproximately(-0.07931476f, 1e-6f);
    }

    [Fact]
    public void ItIsZeroAtWholeNumbers_AndStaysAboutWithinOne()
    {
        var noise = new Noise(1);

        noise.At(3, -5).Should().Be(0);
        noise.At(3f).Should().Be(0);
        noise.At(2, 7, -1).Should().Be(0);
        Samples().Select(p => noise.At(p.X, p.Y)).Should().OnlyContain(v => v >= -1.01f && v <= 1.01f);
        Samples().Select(p => noise.At(p.X)).Should().OnlyContain(v => v >= -1.01f && v <= 1.01f);
        Samples().Select(p => noise.At(p.X, p.Y, p.X * 0.3f)).Should().OnlyContain(v => v >= -1.01f && v <= 1.01f);
    }

    [Fact]
    public void ItUsesItsRange_AndChangesSmoothly()
    {
        var noise = new Noise(3);
        var values = Samples().Select(p => noise.At(p.X, p.Y)).ToList();

        values.Max().Should().BeGreaterThan(0.5f);
        values.Min().Should().BeLessThan(-0.5f);
        values.Average().Should().BeApproximately(0, 0.1f);
        Samples().Should().OnlyContain(p => MathF.Abs(noise.At(p.X, p.Y) - noise.At(p.X + 0.01f, p.Y)) < 0.05f);
    }

    [Fact]
    public void Fractal_AndRidged_KeepToTheirRanges()
    {
        var noise = new Noise(5);

        Samples().Select(p => noise.Fractal(p.X, p.Y)).Should().OnlyContain(v => v >= -1.01f && v <= 1.01f);
        Samples().Select(p => noise.Ridged(p.X, p.Y)).Should().OnlyContain(v => v >= 0 && v <= 1);
        noise.Fractal(1.3f, 2.7f, octaves: 1).Should().Be(noise.At(1.3f, 2.7f));
    }

    [Fact]
    public void Map_IsAGridFromZeroToOne()
    {
        var map = new Noise(9).Map(32, 16, scale: 8);

        map.GetLength(0).Should().Be(32);
        map.GetLength(1).Should().Be(16);
        map.Cast<float>().Should().OnlyContain(v => v >= 0 && v <= 1);
        map.Cast<float>().Distinct().Count().Should().BeGreaterThan(400);
    }
}
