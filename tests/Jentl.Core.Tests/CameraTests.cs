using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class CameraTests
{
    private static readonly Rectangle View = new(0, 0, 200, 100);

    [Fact]
    public void Position_IsInTheMiddleOfTheView()
    {
        var camera = new Camera2D { Viewport = View, Position = new Vector2(50, 50), Zoom = 2 };

        camera.WorldToScreen(new Vector2(50, 50)).Should().Be(new Vector2(100, 50));
        camera.WorldToScreen(new Vector2(60, 50)).Should().Be(new Vector2(120, 50));
        camera.VisibleArea.Should().Be(new Rectangle(0, 25, 100, 50));
    }

    [Fact]
    public void ScreenToWorld_UndoesWorldToScreen_EvenRotated()
    {
        var camera = new Camera2D { Viewport = View, Position = new Vector2(30, -20), Zoom = 1.5f, Rotation = 0.7f };
        var world = new Vector2(12, 34);

        var back = camera.ScreenToWorld(camera.WorldToScreen(world));

        back.X.Should().BeApproximately(world.X, 0.001f);
        back.Y.Should().BeApproximately(world.Y, 0.001f);
    }

    [Fact]
    public void ARotatedView_TurnsTheWorld()
    {
        var camera = new Camera2D { Viewport = View, Position = Vector2.Zero, Rotation = MathF.PI / 2 };

        var right = camera.WorldToScreen(new Vector2(10, 0));

        right.X.Should().BeApproximately(100, 0.001f);
        right.Y.Should().BeApproximately(40, 0.001f, "turning the camera clockwise turns the world the other way");
    }

    [Fact]
    public void Bounds_StopTheViewAtTheWorldsEdge()
    {
        var camera = new Camera2D { Viewport = View, Bounds = new Rectangle(0, 0, 1000, 1000) };

        camera.LookAt(new Vector2(10, 10));
        camera.Position.Should().Be(new Vector2(100, 50));

        camera.LookAt(new Vector2(990, 500));
        camera.Position.Should().Be(new Vector2(900, 500));
    }

    [Fact]
    public void AWorldSmallerThanTheView_IsCentred()
    {
        var camera = new Camera2D { Viewport = View, Zoom = 2, Bounds = new Rectangle(0, 0, 60, 40) };

        camera.LookAt(new Vector2(55, 5));

        camera.Position.Should().Be(new Vector2(30, 20));
    }

    [Fact]
    public void Follow_EasesTowardsTheTarget()
    {
        var camera = new Camera2D { Viewport = View };

        camera.Follow(new Vector2(100, 0), TimeSpan.FromSeconds(0.1), rate: 8);
        var first = camera.Position.X;
        camera.Follow(new Vector2(100, 0), TimeSpan.FromSeconds(0.1), rate: 8);

        first.Should().BeApproximately(100 * (1 - MathF.Exp(-0.8f)), 0.01f);
        (camera.Position.X - first).Should().BeLessThan(first, "each step covers the same share of what is left");
    }

    [Fact]
    public void Follow_IgnoresATargetInsideTheDeadZone()
    {
        var camera = new Camera2D { Viewport = View };

        camera.Follow(new Vector2(15, -5), TimeSpan.FromSeconds(10), deadZone: new Vector2(20, 10));
        camera.Position.Should().Be(Vector2.Zero);

        camera.Follow(new Vector2(30, 0), TimeSpan.FromSeconds(10), deadZone: new Vector2(20, 10));
        camera.Position.X.Should().BeApproximately(10, 0.001f, "it moves only until the target is back on the dead zone's edge");
    }

    [Fact]
    public void RoundToPixels_SnapsTheView()
    {
        var camera = new Camera2D { Viewport = View, Position = new Vector2(10.3f, 0), Zoom = 3, RoundToPixels = true };

        camera.WorldToScreen(Vector2.Zero).Should().Be(new Vector2(69, 50));
    }

    [Fact]
    public void Shake_MovesTheView_ThenDiesAway()
    {
        var camera = new Camera2D(seed: 3) { Viewport = View };

        camera.Shake(8, TimeSpan.FromSeconds(0.5));
        camera.Update(TimeSpan.FromSeconds(0.1));
        var early = camera.ShakeOffset.Length();
        camera.Update(TimeSpan.FromSeconds(0.3));
        var late = camera.ShakeOffset.Length();
        camera.Update(TimeSpan.FromSeconds(0.2));

        early.Should().BeApproximately(8 * 0.8f * 0.8f, 0.001f);
        late.Should().BeApproximately(8 * 0.2f * 0.2f, 0.001f);
        camera.ShakeOffset.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void AWeakShake_DoesNotCutAStrongOneShort()
    {
        var camera = new Camera2D { Viewport = View };

        camera.Shake(10, TimeSpan.FromSeconds(1));
        camera.Shake(1, TimeSpan.FromSeconds(0.1));
        camera.Update(TimeSpan.FromSeconds(0.5));

        camera.ShakeOffset.Length().Should().BeApproximately(10 * 0.25f, 0.001f);
    }

    [Theory]
    [InlineData(ScaleMode.Fit, 1000, 600, 2.5f, 0, 50, 1000, 500)]
    [InlineData(ScaleMode.PixelPerfect, 1000, 600, 2f, 100, 100, 800, 400)]
    [InlineData(ScaleMode.Fit, 300, 600, 0.75f, 0, 225, 300, 150)]
    [InlineData(ScaleMode.PixelPerfect, 300, 100, 1f, -50, -50, 400, 200)]
    public void VirtualScreen_ScalesIntoTheWindow(ScaleMode mode, int width, int height, float scale, int x, int y, int w, int h)
    {
        var screen = new VirtualScreen(400, 200, mode);

        screen.Fit(new Rectangle(0, 0, width, height));

        screen.Scale.Should().Be(scale);
        screen.Destination.Should().Be(new Rectangle(x, y, w, h));
        screen.Bounds.Should().Be(new Rectangle(0, 0, 400, 200));
    }

    [Fact]
    public void VirtualScreen_Expand_ShowsMoreInsteadOfBars()
    {
        var screen = new VirtualScreen(400, 200, ScaleMode.Expand);

        screen.Fit(new Rectangle(0, 0, 1000, 600));

        screen.Scale.Should().Be(2.5f);
        screen.Size.Should().Be(new Point(400, 240));
        screen.Destination.Should().Be(new Rectangle(0, 0, 1000, 600));
    }

    [Fact]
    public void VirtualScreen_MapsTheMouseBothWays()
    {
        var screen = new VirtualScreen(400, 200);
        screen.Fit(new Rectangle(0, 0, 1000, 600));

        screen.ToVirtual(new Vector2(500, 300)).Should().Be(new Vector2(200, 100));
        screen.ToWindow(new Vector2(0, 0)).Should().Be(new Vector2(0, 50));
    }
}
