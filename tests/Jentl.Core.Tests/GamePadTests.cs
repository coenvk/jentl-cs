using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class GamePadTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly Rectangle Screen = new(0, 0, 800, 600);

    private readonly InputState _input = new();

    [Fact]
    public void Buttons_ArePressedOnTheirFirstFrame()
    {
        _input.Update(Pad(Buttons.A));
        _input.WasPressed(Buttons.A).Should().BeTrue();

        _input.Update(Pad(Buttons.A));
        _input.WasPressed(Buttons.A).Should().BeFalse();
        _input.IsDown(Buttons.A).Should().BeTrue();
    }

    [Fact]
    public void ActionMaps_FireFromKeysOrButtons()
    {
        var map = UiKeys.Default;

        _input.Update(Pad(Buttons.DPadDown));
        map.WasPressed(_input, UiAction.Down).Should().BeTrue();

        _input.Update(new KeyboardState(Keys.Enter));
        map.WasPressed(_input, UiAction.Confirm).Should().BeTrue();
        map.IsDown(_input, UiAction.Down).Should().BeFalse("the pad was released");
    }

    [Theory]
    [InlineData(0, 1, UiAction.Up)]
    [InlineData(0, -1, UiAction.Down)]
    [InlineData(-1, 0, UiAction.Left)]
    [InlineData(0.9f, 0.2f, UiAction.Right)]
    public void TheLeftStick_StandsInForTheDPad(float x, float y, UiAction expected)
    {
        _input.Update(Stick(left: new Vector2(x, y)));

        Enum.GetValues<UiAction>().Where(a => UiKeys.Default.IsDown(_input, a)).Should().Equal(expected);
    }

    [Fact]
    public void ASlightlyTiltedStick_IsNoDirection()
    {
        _input.Update(Stick(left: new Vector2(0.1f, 0.1f)));

        Enum.GetValues<UiAction>().Where(a => UiKeys.Default.IsDown(_input, a)).Should().BeEmpty();
    }

    [Fact]
    public void ARepeater_RepeatsAHeldButton()
    {
        var repeater = new ActionRepeater<UiAction>(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(80), UiAction.Up, UiAction.Down);

        _input.Update(Pad(Buttons.DPadUp));
        repeater.Update(_input, UiKeys.Default, TimeSpan.Zero).Should().Be(UiAction.Up);
        _input.Update(Pad(Buttons.DPadUp));
        repeater.Update(_input, UiKeys.Default, TimeSpan.FromMilliseconds(250)).Should().Be(UiAction.Up);
    }

    [Fact]
    public void PadCursor_SleepsUntilTheRightStickMoves()
    {
        var cursor = new PadCursor();

        cursor.Update(Stick(right: new Vector2(0.1f, 0)), Second, Screen, new Vector2(100, 100));
        cursor.IsActive.Should().BeFalse("the stick is inside its dead zone");

        cursor.Update(Stick(right: Vector2.UnitX), TimeSpan.FromSeconds(0.1), Screen, new Vector2(100, 100));
        cursor.IsActive.Should().BeTrue();
        cursor.Position.X.Should().BeApproximately(100 + 1.1f * 600 * 0.1f, 0.01f, "it wakes where the mouse was and moves at full speed");
        cursor.Position.Y.Should().Be(100);
    }

    [Fact]
    public void PadCursor_MovesDownForAStickPushedDown_AndStaysOnScreen()
    {
        var cursor = new PadCursor();

        cursor.Update(Stick(right: -Vector2.UnitY), Second * 10, Screen, new Vector2(100, 100));

        cursor.Position.Should().Be(new Vector2(100, 599));
    }

    [Fact]
    public void PadCursor_HalfTilt_IsSlowerThanHalfSpeed()
    {
        var cursor = new PadCursor();

        cursor.Update(Stick(right: new Vector2(0.6f, 0)), TimeSpan.FromSeconds(0.1), Screen, Vector2.Zero);

        cursor.Position.X.Should().BeLessThan(1.1f * 600 * 0.1f / 2);
        cursor.Position.X.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PadCursor_PressesWithTheRightTrigger_AndTapsThroughTheInput()
    {
        var cursor = new PadCursor();
        var at = new Vector2(300, 200);

        cursor.Update(Trigger(1), Second, Screen, at);
        cursor.State.Should().Be(new PointerState(true, at));
        _input.Update(default, cursor.State);

        cursor.Update(Trigger(0), Second, Screen, at);
        cursor.IsDown.Should().BeFalse();
        _input.Update(default, cursor.State);

        _input.Tap.Should().Be(at);
    }

    [Fact]
    public void PadCursor_Deactivates_WhenHandedBack()
    {
        var cursor = new PadCursor();
        cursor.Update(Trigger(1), Second, Screen, Vector2.One);

        cursor.Deactivate();

        cursor.IsActive.Should().BeFalse();
        cursor.IsDown.Should().BeFalse();
    }

    private static GamePadState Pad(params Buttons[] buttons) =>
        new(Vector2.Zero, Vector2.Zero, 0, 0, buttons);

    private static GamePadState Stick(Vector2 left = default, Vector2 right = default) =>
        new(left, right, 0f, 0f, []);

    private static GamePadState Trigger(float right) =>
        new(Vector2.Zero, Vector2.Zero, 0f, right, []);
}
