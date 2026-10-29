using System.Numerics;

namespace Jentl.Scene3D.Tests;

public class AnimationTests
{
    private const float Close = 1e-4f;

    private static readonly Quaternion Quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);

    [Fact]
    public void Linear_GoesEvenlyBetweenKeys_AndHoldsTheEnds()
    {
        var channel = AnimationChannel.Move(0, (1, Vector3.Zero), (3, new Vector3(4, 0, 0)));

        channel.Sample(0).Should().Be(Vector4.Zero);
        channel.Sample(2).Should().Be(new Vector4(2, 0, 0, 0));
        channel.Sample(9).Should().Be(new Vector4(4, 0, 0, 0));
    }

    [Fact]
    public void Rotations_GoTheShortestWay()
    {
        var channel = AnimationChannel.Turn(0, (0, Quaternion.Identity), (1, Quarter));
        var node = new Node();

        channel.Apply(node, 0.5f);

        var eighth = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        MathF.Abs(Quaternion.Dot(node.Rotation, eighth)).Should().BeApproximately(1, Close);
    }

    [Fact]
    public void Steps_HoldEachKey()
    {
        var channel = new AnimationChannel(0, AnimationPath.Scale, [0, 1], [Vector4.One, new Vector4(2)], Interpolation.Step);

        channel.Sample(0.99f).Should().Be(Vector4.One);
        channel.Sample(1).Should().Be(new Vector4(2));
    }

    [Fact]
    public void CubicSplines_PassThroughTheirKeys_AlongTheirTangents()
    {
        // In-tangent, value, out-tangent per key; flat tangents give an ease in and out.
        var channel = new AnimationChannel(0, AnimationPath.Translation, [0, 2],
            [Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, new Vector4(10, 0, 0, 0), Vector4.Zero], Interpolation.CubicSpline);

        channel.Sample(0).X.Should().Be(0);
        channel.Sample(1).X.Should().BeApproximately(5, Close);
        channel.Sample(0.5f).X.Should().BeApproximately(10 * 0.15625f, Close);
        channel.Sample(2).X.Should().Be(10);
    }

    [Fact]
    public void BadChannels_AreRefused()
    {
        var unsorted = () => new AnimationChannel(0, AnimationPath.Scale, [1, 0], [Vector4.One, Vector4.One]);
        var tooFew = () => new AnimationChannel(0, AnimationPath.Scale, [0, 1], [Vector4.One], Interpolation.Linear);

        unsorted.Should().Throw<ArgumentException>();
        tooFew.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void APlayer_LoopsOrStopsAtTheEnd()
    {
        var node = new Node();
        var clip = new AnimationClip("slide", [AnimationChannel.Move(0, (0, Vector3.Zero), (2, new Vector3(2, 0, 0)))]);
        var animator = new AnimationPlayer([node]);

        animator.Play(clip);
        animator.Update(TimeSpan.FromSeconds(2.5));
        node.Position.X.Should().BeApproximately(0.5f, Close);

        animator.Play(clip, loop: false, restart: true);
        animator.Speed = 2;
        animator.Update(TimeSpan.FromSeconds(5));
        node.Position.X.Should().Be(2);
        animator.IsFinished.Should().BeTrue();
    }

    [Fact]
    public void APlayer_CrossFadesFromTheClipBefore()
    {
        var node = new Node();
        var left = new AnimationClip("left", [AnimationChannel.Move(0, (0, new Vector3(-10, 0, 0)), (1, new Vector3(-10, 0, 0)))]);
        var right = new AnimationClip("right", [AnimationChannel.Move(0, (0, new Vector3(10, 0, 0)), (1, new Vector3(10, 0, 0)))]);
        var animator = new AnimationPlayer([node]);
        animator.Play(left);
        animator.Update(TimeSpan.FromSeconds(0.1));

        animator.Play(right, fade: TimeSpan.FromSeconds(1));
        animator.Update(TimeSpan.FromSeconds(0.25));
        node.Position.X.Should().BeApproximately(-5, Close);
        animator.Update(TimeSpan.FromSeconds(0.5));
        node.Position.X.Should().BeApproximately(5, Close);
        animator.Update(TimeSpan.FromSeconds(0.5));

        node.Position.X.Should().Be(10);
    }
}
