using Jentl.Core.Input;

namespace Jentl.Core.Tests;

public class RumbleTests
{
    private static readonly TimeSpan Ms100 = TimeSpan.FromMilliseconds(100);
    private readonly List<(float Low, float High)> _sent = [];
    private readonly Rumble _rumble;

    public RumbleTests() => _rumble = new Rumble((low, high) => _sent.Add((low, high)));

    [Fact]
    public void AnEffect_FadesAway_ThenStopsTheMotors()
    {
        _rumble.Play(1, 0.5f, TimeSpan.FromMilliseconds(400));

        _rumble.Update(Ms100);
        _rumble.Motors.Should().Be((0.75f, 0.375f));
        _rumble.Update(Ms100);
        _rumble.Update(Ms100);
        _rumble.Motors.Low.Should().BeApproximately(0.25f, 0.0001f);
        _rumble.Update(Ms100);

        _rumble.Motors.Should().Be((0f, 0f));
        _rumble.IsPlaying.Should().BeFalse();
        _sent.Last().Should().Be((0f, 0f));
    }

    [Fact]
    public void OverlappingEffects_TakeTheStrongestOfEachMotor()
    {
        _rumble.Play(0.2f, 0.9f, TimeSpan.FromSeconds(1), fade: false);
        _rumble.Play(0.8f, 0.1f, TimeSpan.FromSeconds(1), fade: false);

        _rumble.Update(Ms100);

        _rumble.Motors.Should().Be((0.8f, 0.9f));
    }

    [Fact]
    public void TheMotorsAreSentOnlyWhenTheyChange()
    {
        _rumble.Play(0.5f, 0.5f, TimeSpan.FromSeconds(1), fade: false);

        _rumble.Update(Ms100);
        _rumble.Update(Ms100);
        _rumble.Update(Ms100);

        _sent.Should().HaveCount(1);
    }

    [Fact]
    public void Strength_ScalesIt_Disabled_OrQuiet_StopsIt()
    {
        _rumble.Play(1, 1, TimeSpan.FromSeconds(1), fade: false);
        _rumble.Strength = 0.5f;
        _rumble.Update(Ms100);
        _rumble.Motors.Should().Be((0.5f, 0.5f));

        _rumble.Update(Ms100, quiet: true);
        _rumble.Motors.Should().Be((0f, 0f));

        _rumble.Enabled = false;
        _rumble.Update(Ms100);
        _rumble.Motors.Should().Be((0f, 0f));
        _rumble.IsPlaying.Should().BeTrue("the effect runs on unheard");
    }

    [Fact]
    public void Stop_EndsEverything()
    {
        _rumble.Play(RumbleEffect.Crash);
        _rumble.Update(Ms100);

        _rumble.Stop();
        _rumble.Update(TimeSpan.Zero);

        _rumble.Motors.Should().Be((0f, 0f));
    }
}
