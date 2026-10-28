using Jentl.Core.Diagnostics;

namespace Jentl.Core.Tests;

public class ProfilerTests
{
    // A clock that only moves when told: one tick a millisecond.
    private long _now;
    private readonly Profiler _profiler;

    public ProfilerTests() => _profiler = new Profiler(() => _now, ticksPerSecond: 1000, window: 4);

    [Fact]
    public void Scopes_Nest_AndAverageOverTheWindow()
    {
        for (var frame = 0; frame < 4; frame++)
        {
            using (_profiler.Measure("update"))
            {
                _now += 2;
                using (_profiler.Measure("physics"))
                {
                    _now += frame == 3 ? 9 : 1;
                }
            }

            _profiler.EndFrame();
        }

        var entries = _profiler.Entries;
        entries.Select(e => e.Path).Should().Equal("update/physics", "update");
        var physics = entries[0];
        physics.Name.Should().Be("physics");
        physics.Depth.Should().Be(1);
        physics.Average.TotalMilliseconds.Should().BeApproximately(3, 1e-9);
        physics.Worst.TotalMilliseconds.Should().Be(9);
        entries[1].Average.TotalMilliseconds.Should().BeApproximately(5, 1e-9);
    }

    [Fact]
    public void ScopesRunTwiceAFrame_AddUp_AndCountTheirCalls()
    {
        for (var frame = 0; frame < 2; frame++)
        {
            for (var i = 0; i < 2; i++)
            {
                using (_profiler.Measure("ai"))
                {
                    _now += 3;
                }
            }

            _profiler.EndFrame();
        }

        var ai = _profiler.Entries.Single();
        ai.Average.TotalMilliseconds.Should().Be(6);
        ai.CallsPerFrame.Should().Be(2);
    }

    [Fact]
    public void OldFrames_LeaveTheWindow_AndAFrameWithoutTheScopeCountsAsZero()
    {
        for (var frame = 0; frame < 10; frame++)
        {
            if (frame < 8)
            {
                using (_profiler.Measure("load"))
                {
                    _now += 100;
                }
            }

            _profiler.EndFrame();
        }

        _profiler.Entries.Single().Average.TotalMilliseconds.Should().Be(50, "two of the last four frames had it");
    }

    [Fact]
    public void Disabled_ItRecordsNothing()
    {
        _profiler.Enabled = false;
        using (_profiler.Measure("x"))
        {
            _now += 5;
        }

        _profiler.EndFrame();

        _profiler.Entries.Should().BeEmpty();
    }

    [Fact]
    public void AScopeLeftOpen_DoesNotSwallowTheNextFrame()
    {
        var leaked = _profiler.Measure("outer");
        using (_profiler.Measure("inner"))
        {
            _now += 1;
        }

        leaked.Dispose();
        _profiler.EndFrame();
        using (_profiler.Measure("next"))
        {
            _now += 1;
        }

        _profiler.EndFrame();
        _profiler.Entries.Select(e => e.Path).Should().Contain("next");
    }
}
