using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class CameraHeadingTests
{
    private static readonly Rectangle View = new(0, 0, 200, 100);

    [Fact]
    public void FollowHeading_TakesShortestTurn()
    {
        var camera = new Camera2D { Viewport = View, Rotation = 3.1f + (MathF.PI / 2) };

        camera.FollowHeading(Vector2.Zero, -3.1f, TimeSpan.FromSeconds(1));

        MathF.Abs(camera.Rotation - (3.1f + (MathF.PI / 2))).Should().BeLessThan(0.2f);
    }

    [Fact]
    public void FollowHeading_PointsTravelUpTheScreen_OnceSettled()
    {
        var camera = new Camera2D { Viewport = View };
        var target = new Vector2(40, 10);

        for (var i = 0; i < 60; i++)
        {
            camera.FollowHeading(target, 0.8f, TimeSpan.FromSeconds(0.1));
        }

        var ahead = camera.WorldToScreen(target + new Vector2(MathF.Cos(0.8f), MathF.Sin(0.8f)) * 10);
        var here = camera.WorldToScreen(target);
        (ahead.X - here.X).Should().BeApproximately(0, 0.01f);
        (ahead.Y - here.Y).Should().BeApproximately(-10, 0.01f);
    }

    [Fact]
    public void FollowHeading_PutsTargetAtAnchor()
    {
        var camera = new Camera2D { Viewport = View, Zoom = 2 };
        var target = new Vector2(-30, 75);

        camera.FollowHeading(target, 2f, TimeSpan.FromSeconds(0.05), screenAnchor: new Vector2(0.5f, 0.72f));

        var screen = camera.WorldToScreen(target);
        screen.X.Should().BeApproximately(100, 0.01f);
        screen.Y.Should().BeApproximately(72, 0.01f);
    }
}

public class CameraAnchorTests
{
    [Fact]
    public void FollowHeading_AnchorAtTheTopLeft_IsHonoured()
    {
        var camera = new Jentl.Core.Graphics.Camera2D { Viewport = new Microsoft.Xna.Framework.Rectangle(0, 0, 200, 100) };
        var target = new Microsoft.Xna.Framework.Vector2(10, 20);

        camera.FollowHeading(target, -MathF.PI / 2, TimeSpan.FromSeconds(1), screenAnchor: Microsoft.Xna.Framework.Vector2.Zero);

        var screen = camera.WorldToScreen(target);
        screen.X.Should().BeApproximately(0, 0.01f);
        screen.Y.Should().BeApproximately(0, 0.01f);
    }
}
