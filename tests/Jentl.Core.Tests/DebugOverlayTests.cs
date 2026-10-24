using Jentl.Core.Diagnostics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class DebugOverlayTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private readonly DebugOverlay _debug = new();
    private readonly SceneManager _scenes = new();
    private readonly InputState _input = new();

    [Fact]
    public void FrameStats_AverageAndRate()
    {
        var stats = new FrameStats(capacity: 4);

        stats.Record(Ms(10), Ms(1), Ms(2));
        stats.Record(Ms(30), Ms(3), Ms(4));

        stats.Count.Should().Be(2);
        stats.AverageFrame.Should().Be(Ms(20));
        stats.AverageUpdate.Should().Be(Ms(2));
        stats.AverageDraw.Should().Be(Ms(3));
        stats.FramesPerSecond.Should().BeApproximately(50, 0.001);
        stats.WorstFrame.Should().Be(Ms(30));
    }

    [Fact]
    public void FrameStats_ForgetTheOldestFrames()
    {
        var stats = new FrameStats(capacity: 3);

        foreach (var ms in new[] { 100.0, 10, 20, 30 })
        {
            stats.Record(Ms(ms), TimeSpan.Zero, TimeSpan.Zero);
        }

        stats.Frames.Should().Equal(Ms(10), Ms(20), Ms(30));
        stats.WorstFrame.Should().Be(Ms(30));
    }

    [Fact]
    public void FrameStats_StartEmpty()
    {
        var stats = new FrameStats();

        stats.FramesPerSecond.Should().Be(0);
        stats.AverageFrame.Should().Be(TimeSpan.Zero);
        stats.WorstFrame.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TheToggleKey_ShowsAndHides()
    {
        _input.Update(new KeyboardState(Keys.F3));
        _debug.Update(TimeSpan.Zero, _input);
        _debug.IsVisible.Should().BeTrue();

        _input.Update(new KeyboardState(Keys.F3));
        _debug.Update(TimeSpan.Zero, _input);
        _debug.IsVisible.Should().BeTrue("holding the key doesn't toggle again");

        _input.Update(new KeyboardState());
        _input.Update(new KeyboardState(Keys.F3));
        _debug.Update(TimeSpan.Zero, _input);
        _debug.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Lines_ShowTheSceneStack_AndTheTopScenesInfo()
    {
        _scenes.Push(new InfoScene([]));
        _scenes.Push(new InfoScene(["Level 3", "Moves 12"]));
        _scenes.Update(TimeSpan.Zero, _input);

        var lines = _debug.Lines(_scenes, ["From the game"]);

        lines.Should().Contain("From the game");
        lines.Should().Contain("Scenes: InfoScene > InfoScene");
        lines.Should().ContainInOrder("# InfoScene", "Level 3", "Moves 12");
    }

    [Fact]
    public void Watches_ReadTheirValueEachTime_AndReplaceByName()
    {
        var score = 1;
        _debug.Watch("Score", () => score);
        _debug.Watch("Mode", () => "easy");
        _debug.Watch("Score", () => score * 10);
        score = 4;

        _debug.Lines(_scenes).Should().ContainInOrder("# Watches", "Mode: easy", "Score: 40");

        _debug.Unwatch("Mode");
        _debug.Lines(_scenes).Should().NotContain("Mode: easy");
    }

    [Fact]
    public void ABrokenWatch_ShowsItsError()
    {
        _debug.Watch("Broken", () => throw new InvalidOperationException());

        _debug.Lines(_scenes).Should().Contain("Broken: <InvalidOperationException>");
    }

    [Fact]
    public void TheLog_KeepsTheLatestLines_AndFades()
    {
        for (var i = 0; i < 10; i++)
        {
            _debug.Log($"line {i}");
        }

        var lines = _debug.Lines(_scenes);
        lines.Should().NotContain("line 1");
        lines.Should().ContainInOrder("# Log", "line 2", "line 9");

        _debug.Update(TimeSpan.FromSeconds(7), _input);
        _debug.Lines(_scenes).Should().NotContain("# Log");
    }

    private sealed class InfoScene(IReadOnlyList<string> info) : Scene
    {
        public override IEnumerable<string> DebugInfo => info;

        public override void Update(TimeSpan elapsed, InputState input)
        {
        }

        public override void Draw(SpriteBatch batch, Rectangle viewport)
        {
        }
    }
}
