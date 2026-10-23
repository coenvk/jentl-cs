using Jentl.Core.Input;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class InputTests
{
    private enum TestAction
    {
        Up,
        Down,
        Undo,
    }

    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(80);

    private readonly InputState _input = new();

    private readonly ActionMap<TestAction> _map = new(new Dictionary<TestAction, Keys[]>
    {
        [TestAction.Up] = [Keys.Up, Keys.W],
        [TestAction.Down] = [Keys.Down],
    });

    [Fact]
    public void InputState_WasPressed_OnlyOnFirstFrame()
    {
        _input.Update(new KeyboardState(Keys.Up));
        _input.WasPressed(Keys.Up).Should().BeTrue();

        _input.Update(new KeyboardState(Keys.Up));
        _input.WasPressed(Keys.Up).Should().BeFalse();
        _input.IsDown(Keys.Up).Should().BeTrue();
    }

    [Fact]
    public void ActionMap_MatchesAnyBoundKey()
    {
        _input.Update(new KeyboardState(Keys.W));

        _map.WasPressed(_input, TestAction.Up).Should().BeTrue();
        _map.WasPressed(_input, TestAction.Down).Should().BeFalse();
    }

    [Fact]
    public void ActionMap_UnboundActionIsNeverPressed()
    {
        _input.Update(new KeyboardState(Keys.Up, Keys.Down));

        _map.IsDown(_input, TestAction.Undo).Should().BeFalse();
        _map.KeysFor(TestAction.Undo).Should().BeEmpty();
    }

    [Fact]
    public void ActionRepeater_FiresOnPressThenRepeatsAfterDelay()
    {
        var repeater = new ActionRepeater<TestAction>(Delay, Interval, TestAction.Up, TestAction.Down);

        Hold(repeater, TimeSpan.Zero, Keys.Up).Should().Be(TestAction.Up);
        Hold(repeater, TimeSpan.FromMilliseconds(199), Keys.Up).Should().BeNull();
        Hold(repeater, TimeSpan.FromMilliseconds(1), Keys.Up).Should().Be(TestAction.Up);
        Hold(repeater, TimeSpan.FromMilliseconds(79), Keys.Up).Should().BeNull();
        Hold(repeater, TimeSpan.FromMilliseconds(1), Keys.Up).Should().Be(TestAction.Up);
    }

    [Fact]
    public void ActionRepeater_StopsWhenReleased()
    {
        var repeater = new ActionRepeater<TestAction>(Delay, Interval, TestAction.Up);
        Hold(repeater, TimeSpan.Zero, Keys.Up);

        Hold(repeater, TimeSpan.FromMilliseconds(100)).Should().BeNull();
        Hold(repeater, TimeSpan.FromMilliseconds(500)).Should().BeNull();
    }

    [Fact]
    public void ActionRepeater_LatestPressTakesOver()
    {
        var repeater = new ActionRepeater<TestAction>(Delay, Interval, TestAction.Up, TestAction.Down);
        Hold(repeater, TimeSpan.Zero, Keys.Up);
        Hold(repeater, TimeSpan.FromMilliseconds(300), Keys.Up);

        Hold(repeater, TimeSpan.FromMilliseconds(16), Keys.Up, Keys.Down).Should().Be(TestAction.Down);
        Hold(repeater, TimeSpan.FromMilliseconds(100), Keys.Up, Keys.Down).Should().BeNull("Down restarted the delay");
        Hold(repeater, TimeSpan.FromMilliseconds(100), Keys.Up, Keys.Down).Should().Be(TestAction.Down);
    }

    [Fact]
    public void ActionRepeater_Reset_ForgetsHeldAction()
    {
        var repeater = new ActionRepeater<TestAction>(Delay, Interval, TestAction.Up);
        Hold(repeater, TimeSpan.Zero, Keys.Up);

        repeater.Reset();

        Hold(repeater, TimeSpan.FromSeconds(1), Keys.Up).Should().BeNull();
    }

    private TestAction? Hold(ActionRepeater<TestAction> repeater, TimeSpan elapsed, params Keys[] keys)
    {
        _input.Update(new KeyboardState(keys));
        return repeater.Update(_input, _map, elapsed);
    }
}
