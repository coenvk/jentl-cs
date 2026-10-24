using System.Numerics;

namespace Jentl.Tracks.Tests;

public class TrackPathTests
{
    private static readonly TrackPath Oval = TrackPath.Build([new Straight(100), new Arc(20, 180), new Straight(100), new Arc(20, 180)]);

    [Fact]
    public void Straight_Then_At_IsLinear()
    {
        var path = TrackPath.Build([new Straight(100)]);

        var pose = path.At(50);

        pose.Position.Should().Be(new Vector2(50, 0));
        pose.Heading.Should().Be(0);
        path.Length.Should().Be(100);
        path.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void QuarterArc_EndsWhereExpected()
    {
        // A left turn with y pointing down turns towards negative y.
        var path = TrackPath.Build([new Arc(10, 90)]);

        var end = path.At(path.Length);

        path.Length.Should().BeApproximately(15.708, 1e-3);
        end.Position.X.Should().BeApproximately(10, 1e-4f);
        end.Position.Y.Should().BeApproximately(-10, 1e-4f);
        end.Heading.Should().BeApproximately(-MathF.PI / 2, 1e-4f);
    }

    [Fact]
    public void RightArc_TurnsTowardsPositiveY()
    {
        var end = TrackPath.Build([new Arc(10, -90)]).At(15.70796);

        end.Position.X.Should().BeApproximately(10, 1e-3f);
        end.Position.Y.Should().BeApproximately(10, 1e-3f);
        end.Heading.Should().BeApproximately(MathF.PI / 2, 1e-3f);
    }

    [Fact]
    public void Oval_IsClosed_AndWraps()
    {
        Oval.IsClosed.Should().BeTrue();
        Oval.Length.Should().BeApproximately(200 + (2 * Math.PI * 20), 1e-9);
        var wrapped = Oval.At(Oval.Length + 5);
        var first = Oval.At(5);
        Vector2.Distance(wrapped.Position, first.Position).Should().BeLessThan(1e-3f);
    }

    [Fact]
    public void OpenPath_ClampsBeyondItsEnds()
    {
        var path = TrackPath.Build([new Straight(100)]);

        path.At(-5).Position.Should().Be(Vector2.Zero);
        path.At(150).Position.Should().Be(new Vector2(100, 0));
    }

    [Fact]
    public void Curvature_IsSignedInverseRadius()
    {
        Oval.CurvatureAt(50).Should().Be(0);
        Oval.CurvatureAt(110).Should().BeApproximately(1 / 20.0, 1e-12);
        TrackPath.Build([new Arc(40, -30)]).CurvatureAt(1).Should().BeApproximately(-1 / 40.0, 1e-12);
    }

    [Fact]
    public void StartAndHeading_MoveThePath()
    {
        var path = TrackPath.Build([new Straight(10)], new Vector2(5, 5), MathF.PI / 2);

        var end = path.At(10).Position;

        end.X.Should().BeApproximately(5, 1e-4f);
        end.Y.Should().BeApproximately(15, 1e-4f);
    }

    [Fact]
    public void Polyline_StepsEvenly()
    {
        var points = TrackPath.Build([new Straight(10)]).Polyline(2.5);

        points.Should().HaveCount(5);
        points[1].Should().Be(new Vector2(2.5f, 0));
        points[^1].Should().Be(new Vector2(10, 0));
    }

    [Fact]
    public void Bounds_EncloseThePath()
    {
        var (min, max) = Oval.Bounds;

        min.X.Should().BeApproximately(-20, 0.5f);
        min.Y.Should().BeApproximately(-40, 0.5f);
        max.X.Should().BeApproximately(120, 0.5f);
        max.Y.Should().BeApproximately(0, 0.5f);
    }
}
