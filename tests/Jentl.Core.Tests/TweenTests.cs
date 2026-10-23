using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class TweenTests
{
    private static readonly Vector2 From = new(0, 0);
    private static readonly Vector2 To = new(10, 20);

    [Fact]
    public void Value_InterpolatesOverDuration()
    {
        var tween = new Tween(From, To, TimeSpan.FromMilliseconds(100));

        tween.Value.Should().Be(From);
        tween.Update(TimeSpan.FromMilliseconds(50));
        tween.Value.Should().Be(new Vector2(5, 10));
        tween.IsDone.Should().BeFalse();
    }

    [Fact]
    public void Value_StaysAtTargetWhenDone()
    {
        var tween = new Tween(From, To, TimeSpan.FromMilliseconds(100));

        tween.Update(TimeSpan.FromSeconds(1));

        tween.IsDone.Should().BeTrue();
        tween.Value.Should().Be(To);
    }

    [Fact]
    public void Easing_ShapesProgress()
    {
        var tween = new Tween(From, To, TimeSpan.FromMilliseconds(100), Easing.CubicOut);

        tween.Update(TimeSpan.FromMilliseconds(50));

        tween.Value.X.Should().BeApproximately(10 * Easing.CubicOut(0.5f), 1e-4f);
    }

    [Fact]
    public void At_IsAlreadyDone()
    {
        var tween = Tween.At(To);

        tween.IsDone.Should().BeTrue();
        tween.Value.Should().Be(To);
    }
}
