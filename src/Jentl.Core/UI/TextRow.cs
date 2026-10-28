using FontStashSharp;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.UI;

/// <summary>
/// A line of text the player types: a name, a room code. Chosen, it takes the keyboard: typed characters are added
/// (up to <paramref name="maxLength"/>, and only those <paramref name="allow"/> lets through), Backspace deletes and
/// repeats while held, Enter (or the pad's Start) keeps the text and Escape (or Back) goes back to what it was. A tap
/// anywhere keeps it too. The text reaches <paramref name="set"/> only when kept.
/// </summary>
public sealed class TextRow(string label, Func<string> get, Action<string> set, int maxLength = 24, Func<char, bool>? allow = null)
    : FormRow(label)
{
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(50);

    private string _draft = "";
    private TimeSpan _backspaceHeld;

    public int MaxLength { get; } = maxLength > 0 ? maxLength : throw new ArgumentOutOfRangeException(nameof(maxLength));

    /// <summary>Taking typed text.</summary>
    public bool IsTyping { get; private set; }

    /// <summary>The text as typed so far, while <see cref="IsTyping"/>.</summary>
    public string Draft => _draft;

    internal override bool IsEditing => IsTyping;

    internal override string Value => IsTyping ? _draft : get();

    internal override void Activate()
    {
        _draft = get();
        _backspaceHeld = TimeSpan.Zero;
        IsTyping = true;
    }

    internal override void Edit(InputState input, TimeSpan elapsed)
    {
        if (input.WasPressed(Keys.Escape) || input.WasPressed(Buttons.Back) || input.WasPressed(Buttons.B))
        {
            IsTyping = false;
            return;
        }

        if (input.WasPressed(Keys.Enter) || input.WasPressed(Buttons.Start) || input.Tap is not null)
        {
            IsTyping = false;
            set(_draft);
            return;
        }

        if (Backspace(input, elapsed) && _draft.Length > 0)
        {
            // A surrogate pair (an emoji) goes as one character.
            _draft = _draft[..^(_draft.Length > 1 && char.IsLowSurrogate(_draft[^1]) ? 2 : 1)];
        }

        foreach (var c in input.Typed)
        {
            if (_draft.Length < MaxLength && (allow?.Invoke(c) ?? true))
            {
                _draft += c;
            }
        }
    }

    internal override void DrawControl(SpriteBatch batch, SpriteFontBase font, Rectangle control, bool focused)
    {
        if (!IsTyping)
        {
            base.DrawControl(batch, font, control, focused);
            return;
        }

        // A field with the text and a caret after it, the end kept in view when the text is wider than the field.
        var field = control;
        field.Inflate(0, -4);
        batch.FillRectangle(field, Palette.Background);
        var inner = field;
        inner.Inflate(-8, 0);
        var text = _draft;
        while (text.Length > 0 && font.MeasureString(text).X > inner.Width - 4)
        {
            text = text[1..];
        }

        batch.DrawText(font, text, inner, Palette.Text, Align.Left, shadow: false);
        var caretX = inner.X + (int)font.MeasureString(text).X + 1;
        batch.FillRectangle(new Rectangle(caretX, field.Y + 4, 2, field.Height - 8), Palette.Accent);
    }

    /// <summary>Backspace pressed this frame, or held long enough to delete again.</summary>
    private bool Backspace(InputState input, TimeSpan elapsed)
    {
        if (!input.IsDown(Keys.Back))
        {
            _backspaceHeld = TimeSpan.Zero;
            return false;
        }

        if (input.WasPressed(Keys.Back))
        {
            _backspaceHeld = TimeSpan.Zero;
            return true;
        }

        var before = _backspaceHeld;
        _backspaceHeld += elapsed;
        if (_backspaceHeld < RepeatDelay)
        {
            return false;
        }

        // Once past the delay, a deletion whenever another interval has passed (at most one a frame).
        return before < RepeatDelay || (int)((_backspaceHeld - RepeatDelay) / RepeatInterval) > (int)((before - RepeatDelay) / RepeatInterval);
    }
}
