using Jentl.Core.Diagnostics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Physics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class InspectorTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private enum Mood
    {
        Calm,
        Angry,
        Asleep,
    }

    private sealed class Paddle
    {
        public float Speed { get; set; } = 300;

        public int Lives { get; set; } = 3;

        public bool Sticky { get; set; }

        public Mood Mood { get; set; }

        public string Name { get; set; } = "paddle";

        public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(1);

        public Vector2 Position { get; set; } = new(10, 20);

        public Color Tint { get; set; } = Color.Red;

        public int Score { get; } = 42;

        public List<int> Combos { get; } = [1, 2, 3];

        public Action? Hit { get; set; }

        public float Broken => Speed > 0 ? throw new InvalidOperationException("no") : 0;
    }

    private sealed class Level : Scene
    {
        private int _bricks = 48;

        public Paddle Paddle { get; } = new();

        public int Updates { get; private set; }

        public List<Keys> Seen { get; } = [];

        public int Bricks => _bricks;

        public override IEnumerable<Inspectable> Inspectables =>
        [
            new(nameof(Level), this),
            new(nameof(Paddle), Paddle, () => new Rectangle((int)Paddle.Position.X, (int)Paddle.Position.Y, 40, 10), d => Paddle.Position += d),
        ];

        public override void Update(TimeSpan elapsed, InputState input)
        {
            Updates++;
            if (input.WasPressed(Keys.Right))
            {
                Seen.Add(Keys.Right);
            }
        }

        public override void Draw(SpriteBatch batch, Rectangle viewport)
        {
        }

        internal void Spend() => _bricks--;
    }

    private static InspectedMember Child(InspectedMember parent, string name) => parent.Children().Single(m => m.Name == name);

    [Fact]
    public void Members_AreTheObjectsPropertiesAndFields_WithTheRightKinds()
    {
        var level = new Level();
        var root = InspectedMember.Of("level", level);

        root.Children().Select(m => m.Name).Should().Contain(["Paddle", "Updates", "Bricks", "bricks"]);
        var paddle = Child(root, "Paddle");
        paddle.Children().Select(m => (m.Name, m.Kind)).Should().Contain(
        [
            ("Speed", MemberKind.Number), ("Lives", MemberKind.Whole), ("Sticky", MemberKind.Toggle), ("Mood", MemberKind.Choice),
            ("Name", MemberKind.Text), ("Cooldown", MemberKind.Time), ("Position", MemberKind.Group), ("Combos", MemberKind.Group),
        ]);
        paddle.Children().Select(m => m.Name).Should().NotContain("Hit", "delegates tell nothing");
        Child(InspectedMember.Of("b", new Body(new Circle(1), System.Numerics.Vector2.Zero)), "Mass").CanWrite.Should().BeTrue();
        InspectedMember.Of("b", new Body(new Circle(1), System.Numerics.Vector2.Zero)).Children().Select(m => m.Name)
            .Should().NotContain("mass", "the settable Mass shows its field already");
        Child(paddle, "Score").CanWrite.Should().BeFalse();
        Child(paddle, "Broken").Display.Should().Be("<InvalidOperationException>");
    }

    [Fact]
    public void Values_ShowShortAndInvariant()
    {
        var paddle = InspectedMember.Of("p", new Paddle());

        Child(paddle, "Speed").Display.Should().Be("300");
        Child(paddle, "Position").Display.Should().Be("(10, 20)");
        Child(paddle, "Tint").Display.Should().Be("#FF0000FF");
        Child(paddle, "Name").Display.Should().Be("\"paddle\"");
        Child(paddle, "Cooldown").Display.Should().Be("1 s");
        Child(paddle, "Combos").Display.Should().Be("List<Int32> [3]");
    }

    [Fact]
    public void Adjust_StepsEachKindItsOwnWay()
    {
        var p = new Paddle();
        var paddle = InspectedMember.Of("p", p);

        Child(paddle, "Speed").Adjust(1).Should().BeTrue();
        p.Speed.Should().Be(310, "a tenth of its order of magnitude");
        Child(paddle, "Speed").Adjust(-2, scale: 10);
        p.Speed.Should().Be(110);
        Child(paddle, "Lives").Adjust(-1);
        p.Lives.Should().Be(2);
        Child(paddle, "Sticky").Adjust(1);
        p.Sticky.Should().BeTrue();
        Child(paddle, "Mood").Adjust(-1);
        p.Mood.Should().Be(Mood.Asleep, "it wraps round");
        Child(paddle, "Cooldown").Adjust(5);
        p.Cooldown.Should().Be(TimeSpan.FromSeconds(1.5));
        Child(paddle, "Score").Adjust(1).Should().BeFalse();
    }

    [Theory]
    [InlineData(0.5, 0.1)]
    [InlineData(3, 0.1)]
    [InlineData(42, 1)]
    [InlineData(420, 10)]
    [InlineData(-5000, 100)]
    public void NumbersStep_ByATenthOfTheirSize(double value, double step) =>
        InspectedMember.Step(value).Should().BeApproximately(step, 1e-9);

    [Fact]
    public void TrySetText_ParsesForTheKind()
    {
        var p = new Paddle();
        var paddle = InspectedMember.Of("p", p);

        Child(paddle, "Speed").TrySetText("425.5").Should().BeTrue();
        Child(paddle, "Lives").TrySetText("7.4").Should().BeTrue();
        Child(paddle, "Mood").TrySetText("angry").Should().BeTrue();
        Child(paddle, "Name").TrySetText("bat").Should().BeTrue();
        Child(paddle, "Cooldown").TrySetText("2.5").Should().BeTrue();
        Child(paddle, "Sticky").TrySetText("maybe").Should().BeFalse();
        Child(paddle, "Mood").TrySetText("Furious").Should().BeFalse();

        (p.Speed, p.Lives, p.Mood, p.Name, p.Cooldown).Should().Be((425.5f, 7, Mood.Angry, "bat", TimeSpan.FromSeconds(2.5)));
    }

    [Fact]
    public void AStructsMember_WritesTheStructBack()
    {
        var p = new Paddle();
        var position = Child(InspectedMember.Of("p", p), "Position");

        Child(position, "X").TrySetText("99").Should().BeTrue();
        Child(Child(InspectedMember.Of("p", p), "Tint"), "G").Adjust(10);

        p.Position.Should().Be(new Vector2(99, 20));
        p.Tint.G.Should().Be(10);
    }

    [Fact]
    public void AStructWithNowhereToGoBack_IsReadOnly()
    {
        var position = InspectedMember.Of("v", new Vector2(1, 2));

        Child(position, "X").CanWrite.Should().BeFalse();
    }

    [Fact]
    public void ListItems_CanBeChanged()
    {
        var p = new Paddle();
        var combos = Child(InspectedMember.Of("p", p), "Combos");

        combos.Children().Select(m => m.Name).Should().Equal("[0]", "[1]", "[2]");
        combos.Children()[1].Adjust(5);

        p.Combos.Should().Equal(1, 7, 3);
    }

    [Fact]
    public void PrivateFields_ShowWithoutTheirUnderscore_AndCanBeSet()
    {
        var level = new Level();
        var bricks = Child(InspectedMember.Of("level", level), "bricks");

        bricks.TrySetText("10").Should().BeTrue();

        level.Bricks.Should().Be(10);
    }

    [Fact]
    public void F4_OpensItAndPausesTheGame_PStepsItRunning_NStepsOnce()
    {
        var (scenes, level, input) = Start();

        Press(scenes, input, Keys.F4);
        scenes.Inspector.IsOpen.Should().BeTrue();
        var paused = level.Updates;
        Idle(scenes, input, 3);
        level.Updates.Should().Be(paused);

        Press(scenes, input, Keys.N);
        level.Updates.Should().Be(paused + 1);

        Press(scenes, input, Keys.P);
        Idle(scenes, input, 3);
        level.Updates.Should().BeGreaterThan(paused + 3);

        Press(scenes, input, Keys.F4);
        scenes.Inspector.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void WhileItIsOpen_TheGameGetsNoKeys()
    {
        var (scenes, level, input) = Start();
        Press(scenes, input, Keys.F4);
        Press(scenes, input, Keys.P);

        Press(scenes, input, Keys.Right);

        level.Seen.Should().BeEmpty();
    }

    [Fact]
    public void Z_TakesChangesBack_OneAtATime()
    {
        var (scenes, level, input) = Start();
        Press(scenes, input, Keys.F4);
        var inspector = scenes.Inspector;
        Press(scenes, input, Keys.Down);
        Press(scenes, input, Keys.Enter);
        GoTo(scenes, input, "Speed");
        Press(scenes, input, Keys.Right);
        Press(scenes, input, Keys.Right);
        level.Paddle.Speed.Should().Be(320);

        Press(scenes, input, Keys.Z);
        level.Paddle.Speed.Should().Be(310);
        Press(scenes, input, Keys.Z);
        level.Paddle.Speed.Should().Be(300);
        inspector.Undo().Should().BeFalse("nothing is left to take back");
        inspector.Changes.Should().BeEmpty("the speed is back where it started");
    }

    [Fact]
    public void TheKeys_OpenAThing_ChangeAValue_AndLogIt()
    {
        var (scenes, level, input) = Start();
        Press(scenes, input, Keys.F4);
        var inspector = scenes.Inspector;

        inspector.Rows.Select(r => r.Name).Should().Equal("Level", "Paddle");
        Press(scenes, input, Keys.Down);
        Press(scenes, input, Keys.Enter);
        inspector.Path.Should().Equal("Paddle");

        GoTo(scenes, input, "Speed");
        Press(scenes, input, Keys.Right);
        Press(scenes, input, Keys.Right);
        level.Paddle.Speed.Should().Be(320);

        GoTo(scenes, input, "Lives");
        Press(scenes, input, Keys.Enter);
        inspector.Typing.Should().Be("3");
        Press(scenes, input, Keys.Back);
        Type(scenes, input, "9");
        Press(scenes, input, Keys.Enter);
        level.Paddle.Lives.Should().Be(9);

        inspector.Changes.Should().Equal("Paddle.Speed: 300 → 320", "Paddle.Lives: 3 → 9");

        Press(scenes, input, Keys.Back);
        inspector.Path.Should().BeEmpty();
        Press(scenes, input, Keys.Escape);
        inspector.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void EscapeWhileTyping_DropsTheText()
    {
        var (scenes, level, input) = Start();
        Press(scenes, input, Keys.F4);
        scenes.Inspector.Select("Paddle");
        GoTo(scenes, input, "Speed");

        Press(scenes, input, Keys.Enter);
        Type(scenes, input, "1");
        Press(scenes, input, Keys.Escape);

        level.Paddle.Speed.Should().Be(300);
        scenes.Inspector.IsOpen.Should().BeTrue("Escape ended the typing, not the inspector");
        scenes.Inspector.Changes.Should().BeEmpty();
    }

    [Fact]
    public void ClickingAThing_InspectsIt_AndDraggingMovesIt()
    {
        var (scenes, level, input) = Start();
        Press(scenes, input, Keys.F4);

        Pointer(scenes, input, true, new Vector2(20, 25));
        scenes.Inspector.Path.Should().Equal("Paddle");
        Pointer(scenes, input, true, new Vector2(35, 22));
        Pointer(scenes, input, true, new Vector2(40, 25));
        Pointer(scenes, input, false, new Vector2(40, 25));

        level.Paddle.Position.Should().Be(new Vector2(30, 20));
        scenes.Inspector.Changes.Should().Equal("Paddle (dragged): (0, 0) → (20, 0)");
    }

    [Fact]
    public void ThePath_IsDroppedWhenWhatItLeadsToIsGone()
    {
        var (scenes, level, input) = Start();
        Press(scenes, input, Keys.F4);
        scenes.Inspector.Select("Paddle");

        scenes.Replace(new Level());
        Press(scenes, input, Keys.P);
        Idle(scenes, input, 2);

        scenes.Inspector.Path.Should().Equal(["Paddle"], "the new level has a paddle too");
        scenes.Inspector.Select("Gone");
        Idle(scenes, input, 1);
        scenes.Inspector.Path.Should().BeEmpty();
    }

    [Fact]
    public void Bodies_AreOutlined_AndMovedInWorldUnits()
    {
        var world = new PhysicsWorld();
        var ball = world.Add(new Body(new Circle(5), new System.Numerics.Vector2(10, 10)) { UserData = "Ball" });
        world.Add(new Body(new Box(20, 4), System.Numerics.Vector2.Zero, BodyType.Static));

        var things = Inspectable.Bodies(world, p => new Vector2(p.X * 2, p.Y * 2), scale: 2).ToList();

        things.Select(t => t.Name).Should().Equal("Ball", "Static 1");
        things[0].Bounds!().Should().Be(new Rectangle(10, 10, 20, 20));
        things[0].Move!(new Vector2(8, 0));
        ball.Position.Should().Be(new System.Numerics.Vector2(14, 10));
        things[0].Target.Should().BeSameAs(ball);
    }

    private static (SceneManager Scenes, Level Level, InputState Input) Start()
    {
        var scenes = new SceneManager();
        var level = new Level();
        scenes.Push(level);
        var input = new InputState();
        scenes.Update(Frame, input);
        return (scenes, level, input);
    }

    private static void Press(SceneManager scenes, InputState input, Keys key)
    {
        input.Update(new KeyboardState(key));
        scenes.Update(Frame, input);
        input.Update(new KeyboardState());
        scenes.Update(Frame, input);
    }

    private static void Type(SceneManager scenes, InputState input, string text)
    {
        foreach (var c in text)
        {
            input.Type(c);
            input.Update(new KeyboardState());
            scenes.Update(Frame, input);
        }
    }

    private static void Idle(SceneManager scenes, InputState input, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            input.Update(new KeyboardState());
            scenes.Update(Frame, input);
        }
    }

    private static void Pointer(SceneManager scenes, InputState input, bool down, Vector2 at)
    {
        input.Update(new KeyboardState(), new PointerState(down, at));
        scenes.Update(Frame, input);
    }

    private static void GoTo(SceneManager scenes, InputState input, string name)
    {
        var rows = scenes.Inspector.Rows.Select(r => r.Name).ToList();
        var current = rows.IndexOf(scenes.Inspector.Current!.Name);
        var target = rows.IndexOf(name);
        for (var i = current; i < target; i++)
        {
            Press(scenes, input, Keys.Down);
        }

        for (var i = current; i > target; i--)
        {
            Press(scenes, input, Keys.Up);
        }
    }
}
