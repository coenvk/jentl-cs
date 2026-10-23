using Jentl.Core.Input;
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
        _pending.Enqueue((
            () =>
            {
                while (_scenes.Count > 0)
                {
                    PopNow(uncover: false);
                }

                PushNow(scene);
            },
            transition));
    }

    public void Update(TimeSpan elapsed, InputState input)
    {
        ApplyPending();
        if (_transition is { } transition)
        {
            if (transition.Advance(elapsed))
            {
                _transition = null;
            }
        }
        else
        {
            Top?.Update(elapsed, input);
        }

        ApplyPending();
    }

    public void Draw(SpriteBatch batch, Rectangle viewport)
    {
        foreach (var scene in VisibleScenes())
        {
            scene.Draw(batch, viewport);
        }

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
