using Jentl.Core.Input;
using Jentl.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using NVector2 = System.Numerics.Vector2;

namespace Jentl.Core.Tests;

public class ReplayTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(166_667);

    [Fact]
    public void ApplyingACapturedFrame_GivesTheSameInput()
    {
        var live = new InputState();
        live.Update(new KeyboardState());
        live.Type('x');
        live.Update(new KeyboardState(Keys.Space, Keys.A), new PointerState(true, new Vector2(12.5f, 40)), Pad(new Vector2(0.9f, 0), Buttons.A, Buttons.DPadLeft), wheel: -2);
        var frame = live.Capture(Frame);

        var replayed = new InputState();
        replayed.Type('y');
        replayed.Apply(frame);

        replayed.IsDown(Keys.Space).Should().BeTrue();
        replayed.IsDown(Keys.A).Should().BeTrue();
        replayed.Cursor.Should().Be(live.Cursor);
        replayed.Wheel.Should().Be(-2);
        replayed.Typed.Should().Be("x", "only the recorded text arrives");
        replayed.IsDown(Buttons.A).Should().BeTrue();
        replayed.IsDown(Buttons.DPadLeft).Should().BeTrue();
        replayed.IsDown(Buttons.LeftThumbstickRight).Should().BeTrue("stick directions follow from the stick");
        replayed.Pad.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_FoldingRunsOfTheSameFrame()
    {
        var recording = new InputRecording();
        var idle = new InputFrame(Frame, [], new PointerState(false, new Vector2(1, 2)), default, 0, "");
        for (var i = 0; i < 50; i++)
        {
            recording.Add(idle);
        }

        recording.Add(new InputFrame(Frame, [Keys.Left, Keys.Up], new PointerState(true, new Vector2(0.1f, 3e5f)), PadFrame.Of(Pad(new Vector2(-0.25f, 0.5f), Buttons.B)), 1, "a;\"b\" é"));
        recording.Add(idle);

        var text = recording.Save();
        var loaded = InputRecording.Load(text);

        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(4, "a header, the idle run, the busy frame and the last idle one");
        loaded.Frames.Should().HaveCount(52);
        loaded.Duration.Should().Be(recording.Duration);
        loaded.Frames.Zip(recording.Frames).Should().OnlyContain(p => p.First.SameAs(p.Second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a recording")]
    [InlineData("jentl-input 1\n16;;0,0")]
    [InlineData("jentl-input 1\nx;;0,0,0;-;0;")]
    public void Load_Garbage_ThrowsInvalidData(string text) =>
        FluentActions.Invoking(() => InputRecording.Load(text)).Should().Throw<InvalidDataException>();

    [Fact]
    public void AReplay_DrivesAPhysicsGameToTheSamePlace()
    {
        // A ball pushed right while Right is held, jumping on Space: played live while recording, then replayed.
        var recording = new InputRecording();
        var live = Play(frame => frame switch
        {
            < 30 => [Keys.Right],
            30 => [Keys.Right, Keys.Space],
            < 70 => [],
            _ => [Keys.Right],
        }, recording);

        var playback = new InputPlayback(InputRecording.Load(recording.Save()));
        var replayed = Play(_ => [], replay: playback);

        playback.IsFinished.Should().BeTrue();
        replayed.Should().Be(live);
        live.X.Should().BeGreaterThan(0);
    }

    private static NVector2 Play(Func<int, Keys[]> keys, InputRecording? record = null, InputPlayback? replay = null)
    {
        var input = new InputState();
        var world = new PhysicsWorld { Gravity = new NVector2(0, 400) };
        world.Add(new Body(new Box(2000, 20), new NVector2(0, 110), BodyType.Static));
        var ball = world.Add(new Body(new Circle(5), NVector2.Zero));
        for (var frame = 0; frame < 120; frame++)
        {
            var elapsed = Frame;
            if (replay?.Next() is { } recorded)
            {
                input.Apply(recorded);
                elapsed = recorded.Elapsed;
            }
            else
            {
                input.Update(new KeyboardState(keys(frame)));
            }

            record?.Add(input.Capture(elapsed));
            if (input.IsDown(Keys.Right))
            {
                ball.ApplyForce(new NVector2(300, 0));
            }

            if (input.WasPressed(Keys.Space))
            {
                ball.ApplyImpulse(new NVector2(0, -150));
            }

            world.Update(elapsed);
        }

        return ball.Position;
    }

    private static GamePadState Pad(Vector2 leftStick, params Buttons[] buttons) => new(leftStick, Vector2.Zero, 0, 0, buttons);
}
