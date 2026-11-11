using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class MotionTests
{
    private static readonly TimeSpan Tenth = TimeSpan.FromSeconds(0.1);

    private static void Run(MotionPlayer player, int tenths)
    {
        for (var i = 0; i < tenths; i++)
        {
            player.Update(Tenth);
        }
    }

    [Fact]
    public void ATween_SetsItsStartAtOnce_AndItsEndWhenDone()
    {
        var value = -1f;
        var player = new MotionPlayer();

        player.Play(Motion.Tween(v => value = v, 0, 10, 1));
        value.Should().Be(0);
        Run(player, 5);
        value.Should().BeApproximately(5, 0.001f);
        Run(player, 10);
        value.Should().Be(10);
        player.IsDone.Should().BeTrue();
    }

    [Fact]
    public void ASequence_PlaysOneAfterAnother_AndCallsOnTheWay()
    {
        var log = new List<string>();
        var x = 0f;
        var y = 0f;
        var player = new MotionPlayer();

        player.Play(Motion.Sequence(
            Motion.Call(() => log.Add("start")),
            Motion.Tween(v => x = v, 0, 1, 0.5),
            Motion.Call(() => log.Add("middle")),
            Motion.Wait(0.2),
            Motion.Tween(v => y = v, 0, 1, 0.5),
            Motion.Call(() => log.Add("end"))));

        log.Should().Equal("start");
        Run(player, 3);
        (x, y).Should().Be((0.6f, 0f));
        Run(player, 4);
        log.Should().Equal("start", "middle");
        x.Should().Be(1);
        y.Should().BeApproximately(0, 0.001f);
        Run(player, 10);
        y.Should().Be(1);
        log.Should().Equal("start", "middle", "end");
    }

    [Fact]
    public void AFrameThatSkipsAPart_StillLeavesItAtItsEnd()
    {
        var a = 0f;
        var player = new MotionPlayer();

        player.Play(Motion.Sequence(Motion.Tween(v => a = v, 0, 1, 0.1), Motion.Wait(1)));
        player.Update(TimeSpan.FromSeconds(0.6));

        a.Should().Be(1);
    }

    [Fact]
    public void Together_RunsSideBySide_UntilTheLongestEnds()
    {
        var a = 0f;
        var colour = Color.Black;
        var motion = Motion.Together(Motion.Tween(v => a = v, 0, 1, 0.2), Motion.Tween(c => colour = c, Color.Black, Color.White, 1));
        var player = new MotionPlayer();

        player.Play(motion);
        Run(player, 5);

        a.Should().Be(1);
        colour.R.Should().BeInRange(120, 135);
        motion.Duration.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RepeatWithYoyo_GoesThereAndBack()
    {
        var value = 0f;
        var player = new MotionPlayer();

        player.Play(Motion.Repeat(Motion.Tween(v => value = v, 0, 1, 1), times: 2, yoyo: true));
        Run(player, 15);
        value.Should().BeApproximately(0.5f, 0.001f);
        Run(player, 10);
        value.Should().Be(0);
        player.IsDone.Should().BeTrue();
    }

    [Fact]
    public void RepeatForever_NeverEnds_AndCallsEachTime()
    {
        var calls = 0;
        var player = new MotionPlayer();

        player.Play(Motion.Repeat(Motion.Sequence(Motion.Call(() => calls++), Motion.Wait(0.5))));
        Run(player, 30);

        calls.Should().Be(7, "at 0, 0.5, ... 3.0 seconds");
        player.IsDone.Should().BeFalse();
    }

    [Fact]
    public void Motions_DropTheFinishedOnes()
    {
        var motions = new Motions();
        motions.Play(Motion.Wait(0.2));
        motions.Play(Motion.Wait(1));

        motions.Update(TimeSpan.FromSeconds(0.5));

        motions.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(nameof(Easing.QuadIn))]
    [InlineData(nameof(Easing.QuadOut))]
    [InlineData(nameof(Easing.CubicInOut))]
    [InlineData(nameof(Easing.SineInOut))]
    [InlineData(nameof(Easing.BackOut))]
    [InlineData(nameof(Easing.BackIn))]
    [InlineData(nameof(Easing.ElasticOut))]
    [InlineData(nameof(Easing.BounceOut))]
    public void EveryEasing_StartsAtZero_AndEndsAtOne(string name)
    {
        var easing = typeof(Easing).GetMethod(name)!.CreateDelegate<Func<float, float>>();

        easing(0).Should().BeApproximately(0, 0.001f);
        easing(1).Should().BeApproximately(1, 0.001f);
    }
}

public class AnimationStateTests
{
    private enum Pose
    {
        Idle,
        Run,
        Hurt,
    }

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void FrameEvents_FireAsTheirFramesComeUp_EachTimeRound()
    {
        var walk = new Animation(Frame, AnimationMode.Loop, 4, 5, 6, 7) { Events = new Dictionary<int, string> { [0] = "step-left", [2] = "step-right" } };
        var animator = new Animator();
        var heard = new List<string>();
        animator.FrameEvent += heard.Add;

        animator.Play(walk);
        for (var i = 0; i < 6; i++)
        {
            animator.Update(Frame);
        }

        heard.Should().Equal("step-left", "step-right", "step-left", "step-right");
    }

    [Fact]
    public void AOnceAnimation_FiresItsEventsOnce()
    {
        var swing = new Animation(Frame, AnimationMode.Once, 1, 2, 3) { Events = new Dictionary<int, string> { [1] = "hit" } };

        swing.EventsBetween(TimeSpan.FromTicks(-1), TimeSpan.FromSeconds(10)).Should().Equal("hit");
    }

    [Fact]
    public void States_MoveOnConditions_AndAfterAOneOffAnimation()
    {
        var speed = 0f;
        var hit = false;
        var states = new AnimationStates<Pose>(Pose.Idle)
            .Add(Pose.Idle, new Animation(Frame, AnimationMode.Loop, 0, 1))
            .Add(Pose.Run, new Animation(Frame, AnimationMode.Loop, 2, 3, 4))
            .Add(Pose.Hurt, new Animation(Frame, AnimationMode.Once, 5, 6))
            .WhenAny(Pose.Hurt, () => hit)
            .After(Pose.Hurt, Pose.Idle)
            .When(Pose.Idle, Pose.Run, () => speed > 0)
            .When(Pose.Run, Pose.Idle, () => speed == 0);
        var changes = new List<(Pose, Pose)>();
        states.Changed += (from, to) => changes.Add((from, to));

        states.Frame.Should().Be(0);
        speed = 1;
        states.Update(Frame);
        states.State.Should().Be(Pose.Run);
        states.Frame.Should().Be(2);

        hit = true;
        states.Update(Frame);
        hit = false;
        states.State.Should().Be(Pose.Hurt);
        states.Update(Frame);
        states.State.Should().Be(Pose.Hurt);
        states.Update(Frame);
        states.State.Should().Be(Pose.Idle, "the hurt animation has played out");
        states.Update(Frame);
        states.State.Should().Be(Pose.Run, "still running");
        changes.Should().Equal((Pose.Idle, Pose.Run), (Pose.Run, Pose.Hurt), (Pose.Hurt, Pose.Idle), (Pose.Idle, Pose.Run));
    }
}

public class AsepriteSheetTests
{
    private const string ArrayJson = """
        { "frames": [
            { "filename": "hero 0.aseprite", "frame": { "x": 0, "y": 0, "w": 16, "h": 24 }, "duration": 100 },
            { "filename": "hero 1.aseprite", "frame": { "x": 16, "y": 0, "w": 16, "h": 24 }, "duration": 150 },
            { "filename": "hero 2.aseprite", "frame": { "x": 32, "y": 0, "w": 16, "h": 24 }, "duration": 100 },
            { "filename": "hero 3.aseprite", "frame": { "x": 48, "y": 0, "w": 16, "h": 24 }, "duration": 200 }
          ],
          "meta": { "image": "hero.png", "frameTags": [
            { "name": "idle", "from": 0, "to": 1, "direction": "forward" },
            { "name": "back", "from": 1, "to": 3, "direction": "reverse" },
            { "name": "bob", "from": 0, "to": 2, "direction": "pingpong" },
            { "name": "die", "from": 2, "to": 3, "direction": "forward", "repeat": "1" }
          ] } }
        """;

    [Fact]
    public void Parse_ReadsFramesDurationsAndTags()
    {
        var sheet = AsepriteSheet.Parse(ArrayJson);

        sheet.Frames.Should().HaveCount(4);
        sheet.Frames[1].Should().Be(new Rectangle(16, 0, 16, 24));
        sheet.Image.Should().Be("hero.png");
        sheet.Tags["idle"].Frames.Should().Equal(0, 1);
        sheet.Tags["idle"].Durations.Should().Equal(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(150));
        sheet.Tags["back"].Frames.Should().Equal(3, 2, 1);
        sheet.Tags["bob"].Mode.Should().Be(AnimationMode.PingPong);
        sheet.Tags["die"].Mode.Should().Be(AnimationMode.Once);
        sheet.All.Length.Should().Be(TimeSpan.FromMilliseconds(550));
    }

    [Fact]
    public void Parse_ReadsTheHashForm()
    {
        const string hash = """{ "frames": { "a": { "frame": { "x": 0, "y": 0, "w": 8, "h": 8 }, "duration": 50 }, "b": { "frame": { "x": 8, "y": 0, "w": 8, "h": 8 }, "duration": 50 } }, "meta": {} }""";

        AsepriteSheet.Parse(hash).Frames.Should().Equal(new Rectangle(0, 0, 8, 8), new Rectangle(8, 0, 8, 8));
    }

    [Fact]
    public void Parse_RejectsOtherJson()
    {
        var act = () => AsepriteSheet.Parse("""{ "levels": [] }""");

        act.Should().Throw<FormatException>();
    }
}

public class JuiceTests
{
    [Fact]
    public void AFlash_FadesBackToTheSpritesColour()
    {
        var flash = new Flash();
        flash.Trigger(TimeSpan.FromSeconds(0.2), Color.Red);

        flash.Tint(Color.Blue).Should().Be(Color.Red);
        flash.Update(TimeSpan.FromSeconds(0.1));
        flash.Amount.Should().BeApproximately(0.5f, 0.001f);
        flash.Update(TimeSpan.FromSeconds(0.2));
        flash.Tint(Color.Blue).Should().Be(Color.Blue);
    }

    [Fact]
    public void ASquash_IsWiderAndShorter_ThenSpringsBack()
    {
        var squash = new SquashStretch();
        squash.Trigger(0.3f);

        squash.Scale.X.Should().BeGreaterThan(1.2f);
        squash.Scale.Y.Should().BeLessThan(0.8f);
        squash.Update(TimeSpan.FromSeconds(1));
        squash.Scale.Should().Be(Vector2.One);
    }

    [Fact]
    public void AShake_MovesWithinItsStrength_AndStops()
    {
        var shake = new Shake();
        shake.Trigger(10, TimeSpan.FromSeconds(0.5));
        var moved = 0f;
        for (var i = 0; i < 25; i++)
        {
            shake.Update(TimeSpan.FromSeconds(0.02));
            moved = MathF.Max(moved, shake.Offset.Length());
            shake.Offset.Length().Should().BeLessThanOrEqualTo(15);
        }

        moved.Should().BeGreaterThan(1);
        shake.Update(TimeSpan.FromSeconds(0.1));
        shake.Offset.Should().Be(Vector2.Zero);
    }
}
