namespace Jentl.Collect.Tests;

public class StableSeedTests
{
    [Fact]
    public void Of_IsFnv1a_AndPositive()
    {
        StableSeed.Of("a").Should().Be(0x640C292C); // FNV-1a of "a" is 0xE40C292C, top bit cleared
        StableSeed.Of("").Should().Be((int)(2166136261u & 0x7fffffff));
        StableSeed.Of("harbour-ring").Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Of_DiffersByText() => StableSeed.Of("event@1.2#3").Should().NotBe(StableSeed.Of("event@1.2#4"));
}

public class EmptyInputTests
{
    [Fact]
    public void Rotation_PickFromNothing_Throws()
    {
        var rotation = new Rotation(DateTimeOffset.UnixEpoch, TimeSpan.FromDays(1));

        var pick = () => rotation.Pick(Array.Empty<string>(), DateTimeOffset.UnixEpoch);

        pick.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rotation_WithoutALength_Throws()
    {
        var make = () => new Rotation(DateTimeOffset.UnixEpoch, TimeSpan.Zero);

        make.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RechargingCounter_NeedsAnIntervalAndAMax()
    {
        var noInterval = () => new RechargingCounter(5, TimeSpan.Zero);
        var noMax = () => new RechargingCounter(0, TimeSpan.FromMinutes(1));

        noInterval.Should().Throw<ArgumentOutOfRangeException>();
        noMax.Should().Throw<ArgumentOutOfRangeException>();
    }
}
