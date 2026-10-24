using FontStashSharp;
using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.UI;

public sealed record MenuItem(string Label, Action Select);

/// <summary>
/// A vertical list of options: up/down and confirm, or tap/click an item. A mouse hovering over an item
/// selects it. When the list is taller than the space it is given, it scrolls: the selection stays in view,
/// arrows show there is more, and the list can be dragged. Hit areas are the rows as last drawn, which is
/// the frame the player is looking at.
/// </summary>
public sealed class Menu
{
    private const int RowPadding = 14;
    private const int PillPadding = 40;
    private readonly MenuItem[] _items;
    private readonly Rectangle[] _rows;
    private readonly SoundPlayer _sounds;
    private readonly ActionRepeater<UiAction> _repeater = new(
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(120),
        UiAction.Up,
        UiAction.Down);

    private Rectangle _area;
    private int _visible = int.MaxValue;
    private int _rowHeight = 1;
    private (float Y, int First)? _drag;

    public Menu(SoundPlayer sounds, params MenuItem[] items)
    {
        _sounds = sounds;
        if (items.Length == 0)
        {
            throw new ArgumentException("A menu needs at least one item.", nameof(items));
        }

        _items = items;
        _rows = new Rectangle[items.Length];
    }

    public IReadOnlyList<MenuItem> Items => _items;

    public int Selected { get; set; }

    /// <summary>The first row shown when the menu scrolls.</summary>
    public int FirstVisible { get; private set; }

    /// <summary>The first row to show so that <paramref name="selected"/> is visible, moving the window as little as possible.</summary>
    public static int Scroll(int first, int selected, int count, int visible)
    {
        if (visible >= count)
        {
            return 0;
        }

        first = Math.Min(first, selected);
        first = Math.Max(first, selected - visible + 1);
        return Math.Clamp(first, 0, count - visible);
    }

    public void Update(InputState input, ActionMap<UiAction> keys, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(keys);
        switch (_repeater.Update(input, keys, elapsed))
        {
            case UiAction.Up:
                Select((Selected - 1 + _items.Length) % _items.Length);
                break;
            case UiAction.Down:
                Select((Selected + 1) % _items.Length);
                break;
        }

        if (Dragged(input))
        {
            return;
        }

        for (var i = 0; i < _rows.Length; i++)
        {
            if (input.Tapped(_rows[i]))
            {
                Selected = i;
                _items[i].Select();
                return;
            }

            if (input.PointerMoved && !input.Cursor.IsDown && _rows[i].Contains(input.Cursor.Position))
            {
                Select(i);
            }
        }

        if (keys.WasPressed(input, UiAction.Confirm))
        {
            _items[Selected].Select();
        }
    }

    public int Height(SpriteFontBase font, int maxHeight = int.MaxValue) =>
        Math.Min(_items.Length, Math.Max(1, maxHeight / RowHeight(font))) * RowHeight(font);

    /// <summary>Draws the menu centred horizontally on <paramref name="centerX"/>, starting at <paramref name="top"/>, at most <paramref name="maxHeight"/> tall.</summary>
    public void Draw(SpriteBatch batch, SpriteFontBase font, int centerX, int top, int maxHeight = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(font);
        _rowHeight = RowHeight(font);
        _visible = Math.Max(1, Math.Min(_items.Length, maxHeight / _rowHeight));
        if (_drag is null)
        {
            FirstVisible = Scroll(FirstVisible, Selected, _items.Length, _visible);
        }

        var pillWidth = (int)_items.Max(i => font.MeasureString(i.Label).X) + 2 * PillPadding;
        _area = new Rectangle(centerX - pillWidth / 2, top, pillWidth, _visible * _rowHeight);
        Array.Clear(_rows);
        for (var i = FirstVisible; i < FirstVisible + _visible; i++)
        {
            var row = new Rectangle(centerX - pillWidth / 2, top + (i - FirstVisible) * _rowHeight, pillWidth, _rowHeight - 6);
            _rows[i] = row;
            var selected = i == Selected;
            if (selected)
            {
                batch.FillRectangle(row, Palette.Accent);
            }

            batch.DrawText(font, _items[i].Label, row, selected ? Palette.Ink : Palette.Text, shadow: !selected);
        }

        // Arrows beside the first and last rows when there is more to scroll to.
        var arrow = new Point(_rowHeight / 3, _rowHeight / 4);
        if (FirstVisible > 0)
        {
            batch.FillTriangle(new Rectangle(_area.Right + 8, top + _rowHeight / 2 - arrow.Y, arrow.X, arrow.Y), pointsUp: true, Palette.Muted);
        }

        if (FirstVisible + _visible < _items.Length)
        {
            batch.FillTriangle(new Rectangle(_area.Right + 8, _area.Bottom - _rowHeight / 2, arrow.X, arrow.Y), pointsUp: false, Palette.Muted);
        }
    }

    private static int RowHeight(SpriteFontBase font) => (int)font.LineHeight + 2 * RowPadding;

    /// <summary>Scrolls by dragging the list; returns true while a drag is going on.</summary>
    private bool Dragged(InputState input)
    {
        if (_visible >= _items.Length || !input.Cursor.IsDown)
        {
            _drag = null;
            return false;
        }

        if (_drag is null && input.Gestures.Pressed && _area.Contains(input.Cursor.Position))
        {
            _drag = (input.Cursor.Position.Y, FirstVisible);
        }

        if (_drag is not { } drag)
        {
            return false;
        }

        var rows = (int)MathF.Round((drag.Y - input.Cursor.Position.Y) / _rowHeight);
        FirstVisible = Math.Clamp(drag.First + rows, 0, _items.Length - _visible);
        Selected = Math.Clamp(Selected, FirstVisible, FirstVisible + _visible - 1);
        return rows != 0;
    }

    private void Select(int index)
    {
        if (index != Selected)
        {
            Selected = index;
            _sounds.Play(UiSounds.Navigate);
        }
    }
}
