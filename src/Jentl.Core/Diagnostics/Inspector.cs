using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Diagnostics;

/// <summary>
/// Looks inside the running game and changes it: <see cref="ToggleKey"/> (F4) opens a panel of what the top scene
/// offers (<see cref="Scene.Inspectables"/>; by default the scene itself, private fields and all) and pauses the game.
/// <list type="bullet">
/// <item>Up and down choose a value; left and right change it (Shift ten times as much, Ctrl a tenth); Enter opens an
/// object, flips a switch or types a new value; Backspace goes back up. The wheel over a value changes it too.</item>
/// <item>Click a thing in the scene to inspect it, and drag it to move it (when the scene lets it be moved).</item>
/// <item>P runs the game while the panel stays open, N steps it one frame while paused.</item>
/// <item>Every change is logged as <c>Paddle.speed: 300 → 420</c>; C copies the log, to put the values in the code.</item>
/// <item>Z takes the last change or drag back, again and again; M moves the panel to the other side.</item>
/// </list>
/// While it is open the game gets no input of its own, so the keys never do two things.
/// </summary>
public sealed class Inspector
{
    private static readonly Color PanelColor = new(10, 12, 20, 225);
    private static readonly Color Accent = new(255, 199, 44);
    private static readonly Color Muted = new(150, 160, 186);
    private static readonly Color Selected = new(255, 199, 44, 60);

    private readonly List<string> _path = [];
    private readonly List<int> _selection = [0];
    private readonly List<(string Key, string Before, string After)> _changes = [];
    private readonly InputState _idle = new();
    private IReadOnlyList<Inspectable> _things = [];
    private InspectedMember _root = InspectedMember.Group("scene", []);
    private string? _typing;
    private bool _step;
    private Rectangle _panel;
    private Rectangle[] _rows = [];
    private int _firstRow;
    private (Inspectable Thing, Vector2 From, Vector2 Moved)? _drag;
    private readonly Stack<Action> _undo = new();
    private bool _onLeft;

    public Keys ToggleKey { get; set; } = Keys.F4;

    public bool IsOpen { get; set; }

    /// <summary>Whether the game runs on while the panel is open (P); paused, it only steps (N).</summary>
    public bool IsRunning { get; set; }

    /// <summary>The changes made, oldest first, one line each: <c>Paddle.speed: 300 → 420</c>.</summary>
    public IReadOnlyList<string> Changes => [.. _changes.Select(c => $"{c.Key}: {c.Before} → {c.After}")];

    /// <summary>Where the panel is: the names opened from the top, the thing first.</summary>
    public IReadOnlyList<string> Path => _path;

    /// <summary>The values shown now, at the level opened.</summary>
    public IReadOnlyList<InspectedMember> Rows { get; private set; } = [];

    /// <summary>The value chosen in <see cref="Rows"/>, if there are any.</summary>
    public InspectedMember? Current => Rows.Count == 0 ? null : Rows[Math.Clamp(_selection[^1], 0, Rows.Count - 1)];

    /// <summary>The text being typed for <see cref="Current"/>, or null.</summary>
    public string? Typing => _typing;

    /// <summary>The input the game gets while the panel is open: nothing pressed.</summary>
    internal InputState IdleInput => _idle;

    /// <summary>
    /// Handles the keys and pointer for one frame and says whether the game should advance: always while closed or
    /// running, once for a step, otherwise not.
    /// </summary>
    public bool Update(InputState input, IEnumerable<Inspectable> things)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(things);
        if (input.WasPressed(ToggleKey) && _typing is null)
        {
            IsOpen = !IsOpen;
            _drag = null;
        }

        if (!IsOpen)
        {
            return true;
        }

        _idle.Update(default(KeyboardState));
        Refresh(things);
        if (_typing is not null)
        {
            Type(input);
        }
        else
        {
            HandleKeys(input);
        }

        Point(input);
        Refresh(things);
        var advance = IsRunning || _step;
        _step = false;
        return advance;
    }

    /// <summary>Copies the change log to the clipboard; false where there is none (the browser), so show <see cref="Changes"/> instead.</summary>
    public bool CopyChanges() => Clipboard.TrySetText(string.Join(Environment.NewLine, Changes));

    public void ClearChanges() => _changes.Clear();

    /// <summary>Opens <paramref name="name"/> at the top level, as clicking it does.</summary>
    public void Select(string name)
    {
        _path.Clear();
        _path.Add(name);
        _selection.Clear();
        _selection.AddRange([Math.Max(0, IndexOf(_root.Children(), name)), 0]);
        _typing = null;
    }

    /// <summary>
    /// Draws the outlines of the things that have bounds and the panel on the right of <paramref name="viewport"/>, in
    /// the scene's coordinates (where the pointer is).
    /// </summary>
    public void Draw(SpriteBatch batch, Rectangle viewport, ITextDrawer? text, float size)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!IsOpen)
        {
            return;
        }

        var chosen = _path.Count > 0 ? _path[0] : null;
        foreach (var thing in _things)
        {
            if (thing.Bounds?.Invoke() is { } bounds)
            {
                var isChosen = thing.Name == chosen;
                batch.StrokeRectangle(bounds, isChosen ? Accent : Color.White * 0.25f, isChosen ? 2 : 1);
            }
        }

        if (text is null)
        {
            return;
        }

        var c = CultureInfo.InvariantCulture;
        var line = (int)(size * 1.35f);
        var pad = (int)(size * 0.6f);
        var width = Math.Min(viewport.Width / 2, (int)(size * 30));
        _panel = new Rectangle(_onLeft ? viewport.X + 8 : viewport.Right - width - 8, viewport.Y + 8, width, viewport.Height - 16);
        batch.FillRectangle(_panel, PanelColor);

        var y = _panel.Y + pad;
        var x = _panel.X + pad;
        text.Draw(batch, IsRunning ? "Inspector: running (P pause)" : "Inspector: paused (P run, N step)", new Vector2(x, y), size, Accent);
        y += line;
        text.Draw(batch, _path.Count == 0 ? "(choose something, or click it)" : string.Join(" > ", _path), new Vector2(x, y), size, Muted);
        y += line + pad / 2;

        var footer = string.Format(c, "Left/Right change  Enter open/type  Backspace back  C copy ({0})", _changes.Count);
        var visible = Math.Max(1, (_panel.Bottom - pad - line - y) / line);
        var chosenRow = Math.Clamp(_selection[^1], 0, Math.Max(0, Rows.Count - 1));
        _firstRow = Menu.Scroll(_firstRow, chosenRow, Rows.Count, visible);
        _rows = new Rectangle[Rows.Count];
        for (var i = _firstRow; i < Math.Min(Rows.Count, _firstRow + visible); i++)
        {
            var row = new Rectangle(_panel.X + pad / 2, y - 2, _panel.Width - pad, line);
            _rows[i] = row;
            var member = Rows[i];
            if (i == chosenRow)
            {
                batch.FillRectangle(row, Selected);
            }

            var value = i == chosenRow && _typing is not null ? _typing + "|" : member.Display + (member.Kind == MemberKind.Group ? "  >" : "");
            var colour = member.CanWrite || member.Kind == MemberKind.Group ? Color.White : Muted;
            text.Draw(batch, member.Name, new Vector2(x, y), size, colour);
            var shown = Fit(text, value, size, width / 2);
            text.Draw(batch, shown, new Vector2(_panel.Right - pad - text.Measure(shown, size).X, y), size, i == chosenRow && _typing is not null ? Accent : colour);
            y += line;
        }

        text.Draw(batch, footer, new Vector2(x, _panel.Bottom - pad - line + 4), size * 0.85f, Muted);
    }

    /// <summary>Re-reads the scene's things and walks the path down from them again, dropping what is gone.</summary>
    private void Refresh(IEnumerable<Inspectable> things)
    {
        _things = [.. things];
        _root = InspectedMember.Group("scene", [.. _things.Select(t => InspectedMember.Of(t.Name, t.Target))]);
        Rows = _root.Children();
        for (var depth = 0; depth < _path.Count; depth++)
        {
            var next = Rows.FirstOrDefault(m => m.Name == _path[depth]);
            if (next is null)
            {
                _path.RemoveRange(depth, _path.Count - depth);
                _selection.RemoveRange(depth + 1, _selection.Count - depth - 1);
                break;
            }

            Rows = next.Children();
        }

        while (_selection.Count < _path.Count + 1)
        {
            _selection.Add(0);
        }

        _selection[^1] = Math.Clamp(_selection[^1], 0, Math.Max(0, Rows.Count - 1));
    }

    private void HandleKeys(InputState input)
    {
        var fine = input.IsDown(Keys.LeftControl) || input.IsDown(Keys.RightControl);
        var coarse = input.IsDown(Keys.LeftShift) || input.IsDown(Keys.RightShift);
        var scale = coarse ? 10f : fine ? 0.1f : 1f;
        if (input.WasPressed(Keys.Up))
        {
            Move(-1);
        }
        else if (input.WasPressed(Keys.Down))
        {
            Move(1);
        }
        else if (input.WasPressed(Keys.PageUp))
        {
            Move(-10);
        }
        else if (input.WasPressed(Keys.PageDown))
        {
            Move(10);
        }
        else if (input.WasPressed(Keys.Left))
        {
            Change(m => m.Adjust(-1, scale));
        }
        else if (input.WasPressed(Keys.Right))
        {
            Change(m => m.Adjust(1, scale));
        }
        else if (input.WasPressed(Keys.Enter))
        {
            Enter();
        }
        else if (input.WasPressed(Keys.Back) || input.WasPressed(Keys.Escape))
        {
            if (_path.Count > 0)
            {
                Back();
            }
            else if (input.WasPressed(Keys.Escape))
            {
                IsOpen = false;
            }
        }
        else if (input.WasPressed(Keys.P))
        {
            IsRunning = !IsRunning;
        }
        else if (input.WasPressed(Keys.N))
        {
            _step = true;
        }
        else if (input.WasPressed(Keys.C))
        {
            CopyChanges();
        }
        else if (input.WasPressed(Keys.Z))
        {
            Undo();
        }
        else if (input.WasPressed(Keys.M))
        {
            _onLeft = !_onLeft;
        }
    }

    /// <summary>Takes the last change or drag back; false when there is nothing left to take back.</summary>
    public bool Undo()
    {
        if (!_undo.TryPop(out var back))
        {
            return false;
        }

        back();
        return true;
    }

    /// <summary>Typing a value: Enter sets it (if it parses), Escape drops it.</summary>
    private void Type(InputState input)
    {
        if (input.WasPressed(Keys.Escape))
        {
            _typing = null;
            return;
        }

        if (input.WasPressed(Keys.Enter))
        {
            var typed = _typing!;
            _typing = null;
            Change(m => m.TrySetText(typed));
            return;
        }

        if (input.WasPressed(Keys.Back) && _typing!.Length > 0)
        {
            _typing = _typing[..^1];
        }

        _typing += input.Typed;
    }

    private void Point(InputState input)
    {
        var at = input.Cursor.Position;

        // The wheel over a value changes it.
        if (input.Wheel != 0 && _panel.Contains(at) && RowAt(at) is { } wheeled)
        {
            _selection[^1] = wheeled;
            Change(m => m.Adjust(input.Wheel));
        }

        if (_drag is { } drag)
        {
            if (!input.Cursor.IsDown)
            {
                if (drag.Moved != Vector2.Zero)
                {
                    Log(drag.Thing.Name + " (dragged)", "(0, 0)", string.Create(CultureInfo.InvariantCulture, $"({drag.Moved.X:0}, {drag.Moved.Y:0})"));
                    var (thing, moved) = (drag.Thing, drag.Moved);
                    _undo.Push(() =>
                    {
                        thing.Move!(-moved);
                        Log(thing.Name + " (dragged)", "(0, 0)", "(0, 0)");
                    });
                }

                _drag = null;
                return;
            }

            var delta = at - drag.From;
            if (delta != Vector2.Zero)
            {
                drag.Thing.Move!(delta);
                _drag = (drag.Thing, at, drag.Moved + delta);
            }

            return;
        }

        if (!input.Gestures.Pressed)
        {
            return;
        }

        if (_panel.Contains(at))
        {
            if (RowAt(at) is { } row)
            {
                // A second click on the chosen row opens it.
                if (row == _selection[^1])
                {
                    Enter();
                }
                else
                {
                    _selection[^1] = row;
                    _typing = null;
                }
            }

            return;
        }

        // The last drawn is on top, so it is picked first.
        if (_things.LastOrDefault(t => t.Bounds?.Invoke().Contains(at) == true) is { } picked)
        {
            Select(picked.Name);
            if (picked.Move is not null)
            {
                _drag = (picked, at, Vector2.Zero);
            }
        }
    }

    private int? RowAt(Vector2 at)
    {
        for (var i = 0; i < _rows.Length; i++)
        {
            if (_rows[i].Contains(at))
            {
                return i;
            }
        }

        return null;
    }

    private void Move(int by)
    {
        if (Rows.Count > 0)
        {
            _selection[^1] = Math.Clamp(_selection[^1] + by, 0, Rows.Count - 1);
        }
    }

    private void Enter()
    {
        if (Current is not { } member)
        {
            return;
        }

        switch (member.Kind)
        {
            case MemberKind.Group when member.Children().Count > 0:
                _path.Add(member.Name);
                _selection.Add(0);
                break;
            case MemberKind.Toggle:
                Change(m => m.Adjust(1));
                break;
            case MemberKind.Whole or MemberKind.Number or MemberKind.Text or MemberKind.Choice or MemberKind.Time when member.CanWrite:
                _typing = member.Value switch
                {
                    string s => s,
                    TimeSpan t => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    _ => member.Display,
                };
                break;
        }
    }

    private void Back()
    {
        _path.RemoveAt(_path.Count - 1);
        _selection.RemoveAt(_selection.Count - 1);
        _typing = null;
    }

    /// <summary>Changes the chosen value and logs it, if the change took.</summary>
    private void Change(Func<InspectedMember, bool> change)
    {
        if (Current is not { } member)
        {
            return;
        }

        var before = member.Display;
        var old = member.Value;
        if (change(member) && member.Display is var after && after != before)
        {
            var key = string.Join(".", _path.Append(member.Name));
            Log(key, before, after);
            _undo.Push(() =>
            {
                if (member.Set(old))
                {
                    Log(key, after, member.Display);
                }
            });
        }
    }

    /// <summary>Logs a change; another change of the same value updates its line, keeping where it started.</summary>
    private void Log(string key, string before, string after)
    {
        var last = _changes.FindIndex(c => c.Key == key);
        if (last >= 0)
        {
            before = _changes[last].Before;
            _changes.RemoveAt(last);
        }

        if (before != after)
        {
            _changes.Add((key, before, after));
        }
    }

    private static int IndexOf(IReadOnlyList<InspectedMember> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The text cut short with "..." to fit <paramref name="width"/>.</summary>
    private static string Fit(ITextDrawer text, string value, float size, int width)
    {
        if (text.Measure(value, size).X <= width)
        {
            return value;
        }

        while (value.Length > 1 && text.Measure(value + "...", size).X > width)
        {
            value = value[..^1];
        }

        return value + "...";
    }
}
