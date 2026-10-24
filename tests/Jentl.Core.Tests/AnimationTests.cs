using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class AnimationTests
{
    private static readonly TimeSpan Tenth = TimeSpan.FromMilliseconds(100);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(250, 3)]
    [InlineData(300, 1)]
    [InlineData(1050, 2)]
    public void Loops_RepeatTheirFrames(int ms, int frame) =>
        new Animation(Tenth, AnimationMode.Loop, 1, 2, 3).FrameAt(TimeSpan.FromMilliseconds(ms)).Should().Be(frame);

    [Fact]
    public void Once_StopsOnTheLastFrame()
    {
        var animation = new Animation(Tenth, AnimationMode.Once, 1, 2, 3);

        animation.FrameAt(TimeSpan.FromSeconds(5)).Should().Be(3);
        animation.IsDone(TimeSpan.FromMilliseconds(299)).Should().BeFalse();
        animation.IsDone(TimeSpan.FromMilliseconds(300)).Should().BeTrue();
    }

    [Fact]
    public void PingPong_GoesThereAndBack()
    {
        var animation = new Animation(Tenth, AnimationMode.PingPong, 1, 2, 3);

        Enumerable.Range(0, 6).Select(i => animation.FrameAt(Tenth * i)).Should().Equal(1, 2, 3, 2, 1, 2);
        animation.Length.Should().Be(Tenth * 4);
    }

    [Fact]
    public void FramesCanHaveTheirOwnDurations()
    {
        var flicker = new Animation([7, 8], [TimeSpan.FromMilliseconds(180), TimeSpan.FromMilliseconds(140)]);

        flicker.FrameAt(TimeSpan.FromMilliseconds(170)).Should().Be(7);
        flicker.FrameAt(TimeSpan.FromMilliseconds(190)).Should().Be(8);
        flicker.FrameAt(TimeSpan.FromMilliseconds(330)).Should().Be(7);
    }

    [Fact]
    public void Animator_KeepsTime_AndSwitchesAnimations()
    {
        var idle = new Animation(Tenth, AnimationMode.Loop, 0);
        var walk = new Animation(Tenth, AnimationMode.Loop, 4, 5);
        var animator = new Animator();

        animator.Frame.Should().Be(-1);
        animator.Play(walk);
        animator.Update(Tenth);
        animator.Frame.Should().Be(5);
        animator.Play(walk);
        animator.Frame.Should().Be(5, "playing the same animation carries on");
        animator.Play(idle);
        animator.Frame.Should().Be(0);
    }

    [Fact]
    public void Particles_LiveTheirLife_ThenGo()
    {
        var particles = new ParticleSystem(capacity: 100, seed: 1);
        var effect = ParticleEffects.Sparkles with { MinLife = 0.5f, MaxLife = 0.5f };

        particles.Emit(Vector2.Zero, effect);
        particles.Count.Should().Be(effect.Count);
        particles.Update(TimeSpan.FromSeconds(0.4));
        particles.Count.Should().Be(effect.Count);
        particles.Update(TimeSpan.FromSeconds(0.2));
        particles.Count.Should().Be(0);
    }

    [Fact]
    public void AFullPool_ReplacesTheOldestParticles()
    {
        var particles = new ParticleSystem(capacity: 10, seed: 1);

        particles.Emit(Vector2.Zero, ParticleEffects.Confetti, count: 25);

        particles.Count.Should().Be(10);
    }

    [Fact]
    public void EmittingIntoAnArea_CountsEachParticle()
    {
        var particles = new ParticleSystem(seed: 2);

        particles.EmitArea(new Rectangle(0, 0, 100, 10), ParticleEffects.Dust, count: 7);

        particles.Count.Should().Be(7);
    }
}
