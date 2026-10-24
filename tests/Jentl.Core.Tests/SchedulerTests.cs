using Jentl.Core.Graphics;
using Jentl.Core.Timing;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class SchedulerTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);

    private readonly Scheduler _timers = new();
    private readonly List<string> _log = [];

    [Fact]
    public void After_CallsOnceTheDelayHasPassed()
    {
        var calls = 0;
        _timers.After(TimeSpan.FromMilliseconds(250), () => calls++);

        _timers.Update(Frame);
        _timers.Update(Frame);
        calls.Should().Be(0);
        _timers.Update(Frame);
        calls.Should().Be(1);
        _timers.Update(Frame);
        calls.Should().Be(1);
        _timers.Count.Should().Be(0);
    }

    [Fact]
    public void Every_CatchesUpOnALongFrame_AndStopsAfterItsTimes()
    {
        var calls = 0;
        _timers.Every(TimeSpan.FromMilliseconds(30), () => calls++, times: 5);

        _timers.Update(Frame);
        calls.Should().Be(3);
        _timers.Update(Frame);
        calls.Should().Be(5);
    }

    [Fact]
    public void ACancelledTimer_NeverFires()
    {
        var calls = 0;
        var timer = _timers.After(Frame, () => calls++);

        timer.Cancel();
        _timers.Update(Frame * 3);

        calls.Should().Be(0);
        timer.IsDone.Should().BeTrue();
    }

    [Fact]
    public void ACoroutine_RunsAtOnce_UpToItsFirstWait()
    {
        _timers.Start(Steps());

        _log.Should().Equal("start");
        _timers.Update(Frame);
        _log.Should().Equal("start", "next frame");
        _timers.Update(TimeSpan.FromMilliseconds(400));
        _log.Should().Equal("start", "next frame");
        _timers.Update(Frame);
        _log.Should().Equal("start", "next frame", "half a second later");
    }

    [Fact]
    public void LeftoverTime_CarriesIntoTheNextWait()
    {
        _timers.Start(Ticks(TimeSpan.FromMilliseconds(40), 10));

        _timers.Update(Frame);
        _log.Should().HaveCount(2);
        _timers.Update(Frame);
        _log.Should().HaveCount(5, "after 200 ms, five 40 ms waits have passed");
    }

    [Fact]
    public void Frames_CountUpdates()
    {
        _timers.Start(Log("waited", Wait.Frames(3)));

        _timers.Update(Frame);
        _timers.Update(Frame);
        _log.Should().BeEmpty();
        _timers.Update(Frame);
        _log.Should().Equal("waited");
    }

    [Fact]
    public void YieldingNull_WaitsOneFrame()
    {
        _timers.Start(Log("next", null));

        _log.Should().BeEmpty();
        _timers.Update(TimeSpan.Zero);
        _log.Should().Equal("next");
    }

    [Fact]
    public void Until_CarriesOnOnceTheConditionHolds()
    {
        var open = false;
        _timers.Start(Log("opened", Wait.Until(() => open)));

        _timers.Update(Frame);
        _log.Should().BeEmpty();
        open = true;
        _timers.Update(Frame);
        _log.Should().Equal("opened");
    }

    [Fact]
    public void Until_ThatAlreadyHolds_DoesNotWait()
    {
        _timers.Start(Log("now", Wait.Until(() => true)));

        _log.Should().Equal("now");
    }

    [Fact]
    public void NestedRoutines_RunInLine()
    {
        _timers.Start(Outer());

        _timers.Update(TimeSpan.FromSeconds(1));

        _log.Should().Equal("outer", "inner 1", "inner 2", "outer again");
    }

    [Fact]
    public void ACoroutine_CanWaitForAnother()
    {
        var first = _timers.Start(Log("first", Wait.Seconds(0.3)));
        _timers.Start(Log("second", Wait.For(first)));

        _timers.Update(Frame);
        _log.Should().BeEmpty();
        _timers.Update(TimeSpan.FromMilliseconds(250));
        _log.Should().Equal(["first", "second"], "a coroutine started later sees the first one finish in the same update");
    }

    [Fact]
    public void ACoroutine_CanWaitForATween()
    {
        var tween = new Tween(Vector2.Zero, Vector2.One, TimeSpan.FromMilliseconds(150), Easing.Linear);
        _timers.Start(Log("tweened", Wait.For(tween)));

        tween.Update(Frame);
        _timers.Update(Frame);
        _log.Should().BeEmpty();
        tween.Update(Frame);
        _timers.Update(Frame);
        _log.Should().Equal("tweened");
    }

    [Fact]
    public void Pausing_StopsTheClock()
    {
        var timer = _timers.After(TimeSpan.FromMilliseconds(150), () => _log.Add("fired"));

        timer.IsPaused = true;
        _timers.Update(Frame * 5);
        timer.IsPaused = false;
        _timers.Update(Frame);
        _log.Should().BeEmpty();
        _timers.Update(Frame);
        _log.Should().Equal("fired");
    }

    [Fact]
    public void TimeScale_SlowsTimeDown()
    {
        _timers.TimeScale = 0.5;
        _timers.After(Frame, () => _log.Add("fired"));

        _timers.Update(Frame);
        _log.Should().BeEmpty();
        _timers.Update(Frame);
        _log.Should().Equal("fired");
    }

    [Fact]
    public void ARoutine_CanCancelItself_AndItsFinallyRuns()
    {
        Coroutine? self = null;
        self = _timers.Start(CancelsItself(() => self!));

        _timers.Update(Frame);

        _log.Should().Equal("cancelling", "finally");
        self.IsDone.Should().BeTrue();
        _timers.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_CancelsEverything()
    {
        _timers.Every(Frame, () => _log.Add("tick"));
        _timers.Start(Log("later", Wait.Seconds(1)));

        _timers.Clear();
        _timers.Update(TimeSpan.FromSeconds(2));

        _log.Should().BeEmpty();
        _timers.Count.Should().Be(0);
    }

    [Fact]
    public void CoroutinesStartedDuringAnUpdate_WaitForTheNextOne()
    {
        _timers.After(Frame, () => _timers.Start(Log("started", Wait.Seconds(0.05))));

        _timers.Update(Frame * 2);
        _log.Should().BeEmpty("the new coroutine has only run up to its first wait");
        _timers.Update(Frame);
        _log.Should().Equal("started");
    }

    [Fact]
    public void Every_RejectsAnEmptyInterval()
    {
        var act = () => _timers.Every(TimeSpan.Zero, () => { });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AZeroWait_LastsOneFrame()
    {
        _timers.Start(Ticks(TimeSpan.Zero, 3));

        _log.Should().BeEmpty();
        _timers.Update(Frame);
        _log.Should().HaveCount(1);
    }

    private IEnumerable<Wait?> Steps()
    {
        _log.Add("start");
        yield return Wait.NextFrame;
        _log.Add("next frame");
        yield return Wait.Seconds(0.5);
        _log.Add("half a second later");
    }

    private IEnumerable<Wait?> Ticks(TimeSpan each, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return Wait.For(each);
            _log.Add($"tick {i}");
        }
    }

    private IEnumerable<Wait?> Log(string message, Wait? wait)
    {
        yield return wait;
        _log.Add(message);
    }

    private IEnumerable<Wait?> Outer()
    {
        _log.Add("outer");
        yield return Wait.For(Inner());
        _log.Add("outer again");
    }

    private IEnumerable<Wait?> Inner()
    {
        yield return Wait.Seconds(0.1);
        _log.Add("inner 1");
        yield return Wait.Seconds(0.1);
        _log.Add("inner 2");
    }

    private IEnumerable<Wait?> CancelsItself(Func<Coroutine> self)
    {
        try
        {
            yield return Wait.NextFrame;
            _log.Add("cancelling");
            self().Cancel();
            yield return Wait.NextFrame;
            _log.Add("never");
        }
        finally
        {
            _log.Add("finally");
        }
    }
}
