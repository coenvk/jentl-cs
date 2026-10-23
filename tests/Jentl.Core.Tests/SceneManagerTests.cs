using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Tests;

public class SceneManagerTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly List<string> _log = [];
    private readonly SceneManager _scenes = new();
    private readonly InputState _input = new();

    [Fact]
    public void Push_IsAppliedOnNextUpdate()
    {
        var a = Scene("A");

        _scenes.Push(a);
        _scenes.Top.Should().BeNull();
        Update();

        _scenes.Top.Should().BeSameAs(a);
        _log.Should().Equal("A.enter", "A.update");
    }

    [Fact]
    public void Update_OnlyUpdatesTopScene()
    {
        _scenes.Push(Scene("A"));
        _scenes.Push(Scene("B"));
        Update();
        _log.Clear();

        Update();

        _log.Should().Equal("B.update");
    }

    [Fact]
    public void Push_CoversSceneBelow()
    {
        _scenes.Push(Scene("A"));
        Update();
        _log.Clear();

        _scenes.Push(Scene("B"));
        Update();

        _log.Should().Equal("A.covered", "B.enter", "B.update");
    }

    [Fact]
    public void Pop_ExitsTopAndUncoversSceneBelow()
    {
        _scenes.Push(Scene("A"));
        _scenes.Push(Scene("B"));
        Update();
        _log.Clear();

        _scenes.Pop();
        Update();

        _log.Should().Equal("B.exit", "A.uncovered", "A.update");
    }

    [Fact]
    public void Replace_SwapsTopWithoutUncoveringSceneBelow()
    {
        _scenes.Push(Scene("A"));
        _scenes.Push(Scene("B"));
        Update();
        _log.Clear();

        _scenes.Replace(Scene("C"));
        Update();

        _log.Should().Equal("B.exit", "C.enter", "C.update");
        _scenes.Count.Should().Be(2);
    }

    [Fact]
    public void Reset_ExitsAllScenesAndLeavesOnlyNewOne()
    {
        _scenes.Push(Scene("A"));
        _scenes.Push(Scene("B"));
        Update();
        _log.Clear();

        _scenes.Reset(Scene("C"));
        Update();

        _log.Should().Equal("B.exit", "A.exit", "C.enter", "C.update");
        _scenes.Count.Should().Be(1);
    }

    [Fact]
    public void Reset_WithSeveralScenes_PushesThemBottomFirst()
    {
        var b = Scene("B");
        var c = Scene("C");
        _scenes.Push(Scene("A"));
        Update();
        _log.Clear();

        _scenes.Reset([b, c]);
        Update();

        _log.Should().Equal("A.exit", "B.enter", "B.covered", "C.enter", "C.update");
        _scenes.VisibleScenes().Should().Equal(c);
    }

    [Fact]
    public void ChangeRequestedDuringUpdate_IsAppliedAfterThatUpdate()
    {
        var b = Scene("B");
        _scenes.Push(Scene("A", onUpdate: s => s.Manager.Push(b)));
        Update();

        _scenes.Top.Should().BeSameAs(b);
        _log.Should().Equal("A.enter", "A.update", "A.covered", "B.enter");
    }

    [Fact]
    public void Pop_LastScene_MakesManagerEmpty()
    {
        _scenes.Push(Scene("A"));
        Update();

        _scenes.Pop();
        _scenes.IsEmpty.Should().BeFalse("the pop is still pending");
        Update();

        _scenes.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Pop_OnEmptyManager_Throws()
    {
        _scenes.Pop();

        Action act = Update;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Transition_AppliesChangeHalfwayAndFreezesUpdates()
    {
        var a = Scene("A");
        var b = Scene("B");
        _scenes.Push(a);
        Update();
        _log.Clear();

        _scenes.Push(b, new TestTransition(TimeSpan.FromSeconds(1)));
        Update(TimeSpan.Zero); // starts the transition
        Update(TimeSpan.FromMilliseconds(400));
        _scenes.Top.Should().BeSameAs(a);
        _scenes.IsTransitioning.Should().BeTrue();

        Update(TimeSpan.FromMilliseconds(200)); // past halfway
        _scenes.Top.Should().BeSameAs(b);
        _scenes.IsTransitioning.Should().BeTrue();

        Update(TimeSpan.FromMilliseconds(400)); // finished
        _scenes.IsTransitioning.Should().BeFalse();
        _log.Should().Equal("A.covered", "B.enter");

        Update();
        _log.Should().EndWith("B.update");
    }

    [Fact]
    public void ChangesRequestedDuringTransition_WaitUntilItEnds()
    {
        _scenes.Push(Scene("A"), new TestTransition(TimeSpan.FromMilliseconds(100)));
        _scenes.Push(Scene("B"));

        Update(TimeSpan.Zero);
        Update(TimeSpan.FromMilliseconds(60));
        _scenes.Count.Should().Be(1);

        Update(TimeSpan.FromMilliseconds(60)); // transition ends, B is pushed right after
        _scenes.Count.Should().Be(2);
    }

    [Fact]
    public void ZeroDurationTransition_AppliesOnFirstUpdate()
    {
        var a = Scene("A");

        _scenes.Push(a, new TestTransition(TimeSpan.Zero));
        Update(TimeSpan.Zero);

        _scenes.Top.Should().BeSameAs(a);
        _scenes.IsTransitioning.Should().BeFalse();
    }

    [Fact]
    public void VisibleScenes_IncludesScenesBelowOverlays()
    {
        var a = Scene("A");
        var b = Scene("B");
        var pause = Scene("Pause", overlay: true);
        var confirm = Scene("Confirm", overlay: true);
        _scenes.Push(a);
        _scenes.Push(b);
        _scenes.Push(pause);
        _scenes.Push(confirm);
        Update();

        _scenes.VisibleScenes().Should().Equal(b, pause, confirm);
    }

    [Fact]
    public void Transition_RejectsNegativeDuration()
    {
        var act = () => new TestTransition(TimeSpan.FromSeconds(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private void Update() => Update(Frame);

    private void Update(TimeSpan elapsed) => _scenes.Update(elapsed, _input);

    private RecordingScene Scene(string name, bool overlay = false, Action<RecordingScene>? onUpdate = null) =>
        new(name, _log, overlay, onUpdate);

    private sealed class RecordingScene(string name, List<string> log, bool overlay, Action<RecordingScene>? onUpdate)
        : Scene
    {
        public override bool IsOverlay => overlay;

        public SceneManager Manager => Scenes;

        public override void Update(TimeSpan elapsed, InputState input)
        {
            log.Add($"{name}.update");
            onUpdate?.Invoke(this);
        }

        public override void Draw(SpriteBatch batch, Rectangle viewport)
        {
        }

        protected internal override void OnEnter() => log.Add($"{name}.enter");

        protected internal override void OnExit() => log.Add($"{name}.exit");

        protected internal override void OnCovered() => log.Add($"{name}.covered");

        protected internal override void OnUncovered() => log.Add($"{name}.uncovered");

        public override string ToString() => name;
    }

    private sealed class TestTransition(TimeSpan duration) : Transition(duration)
    {
        public override void Draw(SpriteBatch batch, Rectangle viewport, float coverage)
        {
        }
    }
}
