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

    /// <summary>The row has the keyboard and gamepad to itself (waiting for a key to bind, taking typed text).</summary>
    internal virtual bool IsEditing => false;

    /// <summary>Takes this frame's input while <see cref="IsEditing"/>.</summary>
    internal virtual void Edit(InputState input, TimeSpan elapsed)
    {
    }

    /// <summary>A line about the row, shown while it has the focus: what a setting does, why a button is greyed.</summary>
    public string? Hint { get; set; }

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

/// <summary>
/// A row that does something when chosen, like a menu item: "Back", "Reset progress". It can show a value on the
/// right, fixed or worked out each time it is drawn ("Name: Swift Otter 27", with a press making a new one).
/// </summary>
public sealed class ButtonRow : FormRow
{
    private readonly Action _press;
    private readonly Func<string> _value;

    public ButtonRow(string label, Action press, string value = "")
        : this(label, press, () => value)
    {
    }

    public ButtonRow(string label, Action press, Func<string> value)
        : base(label)
    {
        _press = press ?? throw new ArgumentNullException(nameof(press));
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal override string Value => _value();

    internal override void Activate() => _press();
}

/// <summary>
/// A settings panel: rows of toggles, sliders, buttons, text and key bindings. Up and down move the focus, left and
/// right change the focused value, confirm toggles or presses. Tapping a row does the same, and a slider can be
/// dragged. Given less height than its rows need, it scrolls: the focus stays in view, arrows show there is more, and
/// the mouse wheel scrolls it.
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
    private bool _edited;
    private int _visible = int.MaxValue;
    private Rectangle _area;

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

    /// <summary>The first row shown when the form scrolls.</summary>
    public int FirstVisible { get; private set; }

    /// <summary>The focused row's hint, for the screen to show (an <see cref="OverlayPanel"/> does), or null.</summary>
    public string? Hint => _rows[Focused].Hint;

    /// <summary>Whether any row has a hint, so a screen can keep room for one.</summary>
    public bool HasHints => _rows.Any(r => r.Hint is not null);

    /// <summary>
    /// A row has the keys to itself, or had them this frame (the Escape that ended a key capture): the screen skips
    /// its own keys, such as Back closing it, while this is true.
    /// </summary>
    public bool IsEditing => _edited || _rows[Focused].IsEditing;

    public void Update(InputState input, ActionMap<UiAction> keys, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(keys);
        _edited = _rows[Focused].IsEditing;
        if (_edited)
        {
            _rows[Focused].Edit(input, elapsed);
            _repeater.Reset();
            return;
        }

        if (input.Wheel != 0 && _area.Contains(input.Cursor.Position))
        {
            (FirstVisible, Focused) = Menu.WheelScroll(FirstVisible, Focused, _rows.Length, _visible, input.Wheel);
        }

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

    /// <summary>How tall the form is drawn at most <paramref name="maxHeight"/> tall: whole rows, at least one.</summary>
    public int Height(SpriteFontBase font, int maxHeight = int.MaxValue) =>
        Math.Min(_rows.Length, Math.Max(1, maxHeight / RowHeight(font))) * RowHeight(font);

    /// <summary>
    /// Draws the rows <paramref name="width"/> wide, centred on <paramref name="centerX"/>, from <paramref name="top"/>,
    /// at most <paramref name="maxHeight"/> tall: more rows than fit scroll, keeping the focused one in view.
    /// </summary>
    public void Draw(SpriteBatch batch, SpriteFontBase font, int centerX, int top, int width, int maxHeight = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(font);
        var height = RowHeight(font);
        _visible = Math.Max(1, Math.Min(_rows.Length, maxHeight / height));
        FirstVisible = Menu.Scroll(FirstVisible, Focused, _rows.Length, _visible);
        _area = new Rectangle(centerX - width / 2, top, width, _visible * height);
        Array.Clear(_bounds);
        for (var i = FirstVisible; i < FirstVisible + _visible; i++)
        {
            var bounds = new Rectangle(centerX - width / 2, top + (i - FirstVisible) * height, width, height - 6);
            _bounds[i] = bounds;
            var focused = i == Focused;
            Theme.Current.Control.Draw(batch, bounds, focused ? Palette.Accent : Palette.PanelLight);
            var label = bounds;
            label.Inflate(-RowPadding * 2, 0);
            batch.DrawText(font, _rows[i].Label, label, focused ? Palette.Ink : Palette.Text, Align.Left, shadow: false);
            _rows[i].DrawControl(batch, font, Control(bounds), focused);
        }

        // Arrows beside the first and last rows when there is more to scroll to.
        var arrow = new Point(height / 3, height / 4);
        if (FirstVisible > 0)
        {
            batch.FillTriangle(new Rectangle(_area.Right + 8, top + height / 2 - arrow.Y, arrow.X, arrow.Y), pointsUp: true, Palette.Muted);
        }

        if (FirstVisible + _visible < _rows.Length)
        {
            batch.FillTriangle(new Rectangle(_area.Right + 8, _area.Bottom - height / 2, arrow.X, arrow.Y), pointsUp: false, Palette.Muted);
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
