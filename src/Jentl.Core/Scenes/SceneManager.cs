using Jentl.Core.Diagnostics;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Timing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Scenes;

/// <summary>
/// A stack of scenes. Changes requested during a frame are queued and applied after the top
/// scene's update, so a scene can safely push, pop or replace itself. A change with a
/// <see cref="Transition"/> is applied halfway through the transition; while a transition
/// runs no scene is updated and further changes wait their turn.
/// </summary>
public sealed class SceneManager
{
    private readonly List<Scene> _scenes = [];
    private readonly Queue<(Action Apply, Transition? Transition)> _pending = new();
    private ActiveTransition? _transition;

    /// <summary>Particles drawn above every scene (confetti over a win panel); in screen pixels.</summary>
    public ParticleSystem Effects { get; } = new();

    /// <summary>Timers and coroutines that run whatever scene is showing, even during transitions.</summary>
    public Scheduler Timers { get; } = new();

    /// <summary>The debug panel (F3), and whether scenes draw their debug shapes.</summary>
    public DebugOverlay Debug { get; } = new();

    /// <summary>Every scene on the stack, bottom first.</summary>
    public IReadOnlyList<Scene> Stack => _scenes;

    /// <summary>The area scenes were last drawn into, e.g. to aim effects at the screen's edges.</summary>
    public Rectangle Viewport { get; private set; }

    public Scene? Top => _scenes.Count > 0 ? _scenes[^1] : null;

    public int Count => _scenes.Count;

    /// <summary>True when there are no scenes and nothing left to do; the game should exit.</summary>
    public bool IsEmpty => _scenes.Count == 0 && _pending.Count == 0 && _transition is null;

    public bool IsTransitioning => _transition is not null;

    public void Push(Scene scene, Transition? transition = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _pending.Enqueue((() => PushNow(scene), transition));
    }

    public void Pop(Transition? transition = null) =>
        _pending.Enqueue((() => PopNow(uncover: true), transition));

    /// <summary>Replaces the top scene; the scene below is not uncovered in between.</summary>
    public void Replace(Scene scene, Transition? transition = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _pending.Enqueue((
            () =>
            {
                PopNow(uncover: false);
                PushNow(scene, cover: false);
            },
            transition));
    }

    /// <summary>Clears the stack and makes <paramref name="scene"/> the only scene.</summary>
    public void Reset(Scene scene, Transition? transition = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Reset([scene], transition);
    }

    /// <summary>Clears the stack and pushes <paramref name="scenes"/>, bottom first, as one change.</summary>
    public void Reset(IReadOnlyList<Scene> scenes, Transition? transition = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        if (scenes.Count == 0)
        {
            throw new ArgumentException("Reset needs at least one scene.", nameof(scenes));
        }

        _pending.Enqueue((
            () =>
            {
                while (_scenes.Count > 0)
                {
                    PopNow(uncover: false);
                }

                foreach (var scene in scenes)
                {
                    PushNow(scene);
                }
            },
            transition));
    }

    public void Update(TimeSpan elapsed, InputState input)
    {
        Effects.Update(elapsed);
        Timers.Update(elapsed);
        ApplyPending();
        if (_transition is { } transition)
        {
            if (transition.Advance(elapsed))
            {
                _transition = null;
            }
        }
        else if (Top is { } top)
        {
            top.Timers.Update(elapsed);
            top.Update(elapsed, input);
        }

        ApplyPending();
    }

    public void Draw(SpriteBatch batch, Rectangle viewport)
    {
        Viewport = viewport;
        foreach (var scene in VisibleScenes())
        {
            scene.Draw(batch, viewport);
            if (Debug.IsVisible)
            {
                scene.DrawDebug(batch, viewport);
            }
        }

        Effects.Draw(batch);
        _transition?.Draw(batch, viewport);
    }

    /// <summary>The top scene plus every overlay chain below it, bottom first.</summary>
    internal IEnumerable<Scene> VisibleScenes()
    {
        if (_scenes.Count == 0)
        {
            return [];
        }

        var first = _scenes.Count - 1;
        while (first > 0 && _scenes[first].IsOverlay)
        {
            first--;
        }

        return _scenes.Skip(first);
    }

    private void ApplyPending()
    {
        while (_transition is null && _pending.TryDequeue(out var change))
        {
            if (change.Transition is null)
            {
                change.Apply();
            }
            else
            {
                _transition = new ActiveTransition(change.Transition, change.Apply);
            }
        }
    }

    private void PushNow(Scene scene, bool cover = true)
    {
        if (cover)
        {
            Top?.OnCovered();
        }

        _scenes.Add(scene);
        scene.Attach(this);
        scene.OnEnter();
    }

    private void PopNow(bool uncover)
    {
        var top = Top ?? throw new InvalidOperationException("Cannot pop an empty scene stack.");
        _scenes.RemoveAt(_scenes.Count - 1);
        top.OnExit();
        top.Attach(null);
        top.Dispose();
        if (uncover)
        {
            Top?.OnUncovered();
        }
    }

    private sealed class ActiveTransition(Transition transition, Action change)
    {
        private TimeSpan _elapsed;
        private bool _applied;

        /// <summary>Advances time; returns true once the transition has finished.</summary>
        public bool Advance(TimeSpan elapsed)
        {
            _elapsed += elapsed;
            if (!_applied && _elapsed >= transition.Duration / 2)
            {
                _applied = true;
                change();
            }

            return _elapsed >= transition.Duration;
        }

        public void Draw(SpriteBatch batch, Rectangle viewport)
        {
            if (transition.Duration <= TimeSpan.Zero)
            {
                return;
            }

            var t = (float)Math.Clamp(_elapsed / transition.Duration, 0, 1);
            var coverage = t < 0.5f ? t * 2 : (1 - t) * 2;
            transition.Draw(batch, viewport, transition.Easing(coverage));
        }
    }
}
