using FontStashSharp;
using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

/// <summary>One row of a <see cref="Form"/>: a label on the left and a control on the right.</summary>
public abstract class FormRow(string label)
{
    public string Label { get; set; } = label;

    /// <summary>Confirm, or a tap on the row (outside any other part of it).</summary>
    internal virtual void Activate()
    {
    }

    /// <summary>Left (-1) or right (1) while the row has the focus.</summary>
    internal virtual void Adjust(int direction)
    {
    }

    /// <summary>The pointer pressed or dragged at <paramref name="x"/> on the row; true if the row used it.</summary>
    internal virtual bool Point(float x, Rectangle control) => false;

    internal abstract string Value { get; }

    /// <summary>Draws the control into <paramref name="control"/>, the right part of the row.</summary>
    internal virtual void DrawControl(SpriteBatch batch, SpriteFontBase font, Rectangle control, bool focused) =>
        batch.DrawText(font, Value, control, focused ? Palette.Ink : Palette.Text, Align.Right, shadow: false);
}

/// <summary>A switch: tap, confirm, or left/right to turn it on or off.</summary>
public sealed class ToggleRow(string label, Func<bool> get, Action<bool> set) : FormRow(label)
{
    internal override string Value => get() ? UiText.On : UiText.Off;

    public bool IsOn => get();

    internal override void Activate() => set(!get());

    internal override void Adjust(int direction) => set(direction > 0);

    internal override void DrawControl(SpriteBatch batch, SpriteFontBase font, Rectangle control, bool focused)
    {
        // A pill with a knob, green and to the right when on.
        var height = (int)(font.LineHeight * 0.8f);
        var width = height * 2;
        var track = new Rectangle(control.Right - width, control.Center.Y - height / 2, width, height);
        batch.FillRectangle(track, get() ? Palette.Solved : Palette.Background);
        var knob = new Rectangle(get() ? track.Right - height : track.X, track.Y, height, height);
        knob.Inflate(-3, -3);
        batch.FillRectangle(knob, Palette.Text);
        var text = new Rectangle(control.X, control.Y, control.Width - width - 12, control.Height);
        batch.DrawText(font, Value, text, focused ? Palette.Ink : Palette.Muted, Align.Right, shadow: false);
    }
}

/// <summary>A value between <paramref name="min"/> and <paramref name="max"/>: left/right step it, or tap and drag along the bar.</summary>
public sealed class SliderRow(string label, Func<float> get, Action<float> set, float min = 0, float max = 1, float step = 0.1f, Func<float, string>? format = null)
    : FormRow(label)
{
    public float Min { get; } = min;

    public float Max { get; } = max;

    public float Step { get; } = step;

    internal override string Value => format?.Invoke(get()) ?? $"{(int)MathF.Round((get() - Min) / (Max - Min) * 100)}%";

    /// <summary>Sets the value, kept within range and on a whole number of steps.</summary>
    public void SetValue(float value) => set(Math.Clamp(MathF.Round((value - Min) / Step) * Step + Min, Min, Max));

    internal override void Adjust(int direction) => SetValue(get() + direction * Step);

    internal override bool Point(float x, Rectangle control)
    {
        var track = Track(control);
        if (x < track.X - track.Height || x > track.Right + track.Height)
        {
            return false;
        }

        SetValue(Min + (x - track.X) / track.Width * (Max - Min));
        return true;
    }

    internal override void DrawControl(SpriteBatch batch, SpriteFontBase font, Rectangle control, bool focused)
    {
        var track = Track(control);
        var fraction = (get() - Min) / (Max - Min);
        batch.FillRectangle(track, Palette.Background);
        batch.FillRectangle(track with { Width = (int)(track.Width * fraction) }, Palette.Solved);
        var knob = (int)(font.LineHeight * 0.7f);
        batch.FillRectangle(new Rectangle(track.X + (int)(track.Width * fraction) - knob / 4, control.Center.Y - knob / 2, knob / 2, knob), Palette.Text);
        var text = new Rectangle(control.X, control.Y, control.Width - track.Width - 16, control.Height);
        batch.DrawText(font, Value, text, focused ? Palette.Ink : Palette.Muted, Align.Right, shadow: false);
    }

    private static Rectangle Track(Rectangle control)
    {
        var width = control.Width * 3 / 5;
        return new Rectangle(control.Right - width, control.Center.Y - 4, width, 8);
    }
}

/// <summary>One of a few choices (a language, a difficulty): left/right, confirm or a tap step through them.</summary>
public sealed class ChoiceRow(string label, IReadOnlyList<string> choices, Func<int> get, Action<int> set) : FormRow(label)
{
    internal override string Value => choices[Math.Clamp(get(), 0, choices.Count - 1)];

    internal override void Activate() => Adjust(1);

    internal override void Adjust(int direction) => set(((get() + direction) % choices.Count + choices.Count) % choices.Count);
}

/// <summary>A row that does something when chosen, like a menu item: "Back", "Reset progress".</summary>
public sealed class ButtonRow(string label, Action press, string value = "") : FormRow(label)
{
    internal override string Value => value;

    internal override void Activate() => press();
}

/// <summary>
/// A settings panel: rows of toggles, sliders and buttons. Up and down move the focus, left and right change
/// the focused value, confirm toggles or presses. Tapping a row does the same, and a slider can be dragged.
/// </summary>
public sealed class Form
{
    private const int RowPadding = 12;
    private readonly FormRow[] _rows;
    private readonly Rectangle[] _bounds;
    private readonly SoundPlayer _sounds;
    private readonly ActionRepeater<UiAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(110),
        UiAction.Up,
        UiAction.Down,
        UiAction.Left,
        UiAction.Right);

    private int? _dragging;

    public Form(SoundPlayer sounds, params FormRow[] rows)
    {
        if (rows.Length == 0)
        {
            throw new ArgumentException("A form needs at least one row.", nameof(rows));
        }

        _sounds = sounds;
        _rows = rows;
        _bounds = new Rectangle[rows.Length];
    }

    public IReadOnlyList<FormRow> Rows => _rows;

    public int Focused { get; set; }

    public void Update(InputState input, ActionMap<UiAction> keys, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(keys);
        switch (_repeater.Update(input, keys, elapsed))
        {
            case UiAction.Up:
                Focus((Focused - 1 + _rows.Length) % _rows.Length);
                return;
            case UiAction.Down:
                Focus((Focused + 1) % _rows.Length);
                return;
            case UiAction.Left:
                Change(() => _rows[Focused].Adjust(-1));
                return;
            case UiAction.Right:
                Change(() => _rows[Focused].Adjust(1));
                return;
        }

        if (keys.WasPressed(input, UiAction.Confirm))
        {
            Change(_rows[Focused].Activate);
            return;
        }

        // A slider follows the pointer while held; anything else acts on a tap.
        var cursor = input.Cursor.Position;
        if (input.Cursor.IsDown)
        {
            if (input.Gestures.Pressed)
            {
                _dragging = Array.FindIndex(_bounds, b => b.Contains(cursor)) is var hit and >= 0 && _rows[hit] is SliderRow ? hit : null;
            }

            if (_dragging is { } row)
            {
                Focused = row;
                _rows[row].Point(cursor.X, Control(_bounds[row]));
            }

            return;
        }

        _dragging = null;
        for (var i = 0; i < _rows.Length; i++)
        {
            if (input.Tapped(_bounds[i]))
            {
                Focused = i;
                if (!_rows[i].Point(input.Tap!.Value.X, Control(_bounds[i])))
                {
                    Change(_rows[i].Activate);
                }

                return;
            }

            if (input.PointerMoved && _bounds[i].Contains(cursor))
            {
                Focused = i;
            }
        }
    }

    public int Height(SpriteFontBase font) => _rows.Length * RowHeight(font);

    /// <summary>Draws the rows <paramref name="width"/> wide, centred on <paramref name="centerX"/>, from <paramref name="top"/>.</summary>
    public void Draw(SpriteBatch batch, SpriteFontBase font, int centerX, int top, int width)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(font);
        var height = RowHeight(font);
        for (var i = 0; i < _rows.Length; i++)
        {
            var bounds = new Rectangle(centerX - width / 2, top + i * height, width, height - 6);
            _bounds[i] = bounds;
            var focused = i == Focused;
            batch.FillRectangle(bounds, focused ? Palette.Accent : Palette.PanelLight);
            var label = bounds;
            label.Inflate(-RowPadding * 2, 0);
            batch.DrawText(font, _rows[i].Label, label, focused ? Palette.Ink : Palette.Text, Align.Left, shadow: false);
            _rows[i].DrawControl(batch, font, Control(bounds), focused);
        }
    }

    private static int RowHeight(SpriteFontBase font) => (int)font.LineHeight + 2 * RowPadding;

    /// <summary>The right part of a row, where its control goes.</summary>
    private static Rectangle Control(Rectangle row)
    {
        var width = row.Width / 2;
        return new Rectangle(row.Right - width - RowPadding * 2, row.Y, width, row.Height);
    }

    private void Focus(int index)
    {
        Focused = index;
        _sounds.Play(UiSounds.Navigate);
    }

    private void Change(Action change)
    {
        change();
        _sounds.Play(UiSounds.Navigate);
    }
}
