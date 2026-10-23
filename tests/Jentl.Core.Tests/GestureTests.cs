using Jentl.Core.Input;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class GestureTests
{
    private readonly GestureTracker _gestures = new() { TapRadius = 10, SwipeDistance = 40 };

    [Fact]
    public void PressAndRelease_InPlace_IsATap()
    {
        Frame(true, 100, 100);
        Frame(true, 103, 98);
        Frame(false, 104, 99);

        _gestures.Tap.Should().Be(new Vector2(104, 99));
        _gestures.Swipe.Should().BeNull();
    }

    [Fact]
    public void Tap_IsOnlyReportedOnTheReleaseFrame()
    {
        Frame(true, 100, 100);
        Frame(false, 100, 100);
        Frame(false, 100, 100);

        _gestures.Tap.Should().BeNull();
    }

    [Fact]
    public void Drag_FiresASwipeOncePastTheDistance()
    {
        Frame(true, 100, 100);
        Frame(true, 130, 105).Should().BeNull();

        Frame(true, 141, 104).Should().Be(SwipeDirection.Right);
    }

    [Fact]
    public void LongDrag_StepsRepeatedly()
    {
        Frame(true, 100, 100);

        Frame(true, 100, 60).Should().Be(SwipeDirection.Up);
        Frame(true, 100, 40).Should().BeNull("only half a step further");
        Frame(true, 100, 19).Should().Be(SwipeDirection.Up);
    }

    [Fact]
    public void Drag_CanTurnACorner()
    {
        Frame(true, 100, 100);
        Frame(true, 145, 100).Should().Be(SwipeDirection.Right);

        Frame(true, 145, 150).Should().Be(SwipeDirection.Down);
    }

    [Fact]
    public void Swipe_IsNotAlsoATap()
    {
        Frame(true, 100, 100);
        Frame(true, 150, 100);

        Frame(false, 100, 100);

        _gestures.Tap.Should().BeNull("the drag returned to the start, but it already swiped");
    }

    [Fact]
    public void ShortDragAndRelease_IsNeither()
    {
        Frame(true, 100, 100);
        Frame(false, 125, 100);

        _gestures.Tap.Should().BeNull();
        _gestures.Swipe.Should().BeNull();
    }

    [Fact]
    public void InputState_TappedChecksBounds()
    {
        var input = new InputState();
        input.Update(default, new PointerState(true, new Vector2(20, 20)));
        input.Update(default, new PointerState(false, new Vector2(20, 20)));

        input.Tapped(new Rectangle(10, 10, 20, 20)).Should().BeTrue();
        input.Tapped(new Rectangle(40, 10, 20, 20)).Should().BeFalse();
    }

    private SwipeDirection? Frame(bool down, float x, float y)
    {
        _gestures.Update(new PointerState(down, new Vector2(x, y)));
        return _gestures.Swipe;
    }
}
