using Jentl.Core.Audio;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class WidgetTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private readonly InputState _input = new();

    [Fact]
    public void Typed_IsWhatWasTypedSinceTheLastFrame_WithoutControlCharacters()
    {
        _input.Type('h');
        _input.Type('\b');
        _input.Type('é');
        _input.Update(new KeyboardState());

        _input.Typed.Should().Be("hé");
        _input.Update(new KeyboardState());
        _input.Typed.Should().BeEmpty();
    }

    [Fact]
    public void TextRow_TakesTypedText_AndKeepsItOnEnter()
    {
        var name = "Ann";
        var row = new TextRow("Name", () => name, v => name = v, maxLength: 5);
        var form = new Form(SoundPlayer.Silent(), row, new ButtonRow("Back", () => { }));

        Press(form, Keys.Enter);
        row.IsTyping.Should().BeTrue();

        // S would move the focus down, and Space confirm; while typing they are text.
        Type(form, "e S");
        Type(form, "abc");
        name.Should().Be("Ann", "nothing is kept until Enter");
        row.Draft.Should().Be("Anne ", "the text stops at five characters");

        Press(form, Keys.Enter);
        name.Should().Be("Anne ");
        row.IsTyping.Should().BeFalse();
        form.Focused.Should().Be(0);
    }

    [Fact]
    public void TextRow_EscapeGoesBack_AndBackspaceDeletes()
    {
        var name = "Bob";
        var row = new TextRow("Name", () => name, v => name = v);
        var form = new Form(SoundPlayer.Silent(), row);

        Press(form, Keys.Enter);
        Press(form, Keys.Back);
        row.Draft.Should().Be("Bo");
        _input.Update(new KeyboardState(Keys.Escape));
        form.Update(_input, UiKeys.Default, Frame);

        name.Should().Be("Bob");
        row.IsTyping.Should().BeFalse();
        form.IsEditing.Should().BeTrue("the screen must not also close on this Escape");
    }

    [Fact]
    public void TextRow_HeldBackspace_RepeatsAfterADelay()
    {
        var name = "abcdefghij";
        var row = new TextRow("Name", () => name, v => name = v);
        var form = new Form(SoundPlayer.Silent(), row);
        Press(form, Keys.Enter);

        for (var t = 0; t < 30; t++)
        {
            _input.Update(new KeyboardState(Keys.Back));
            form.Update(_input, UiKeys.Default, TimeSpan.FromMilliseconds(20));
        }

        // 600 ms: one on the press, then from 400 ms one every 50 ms.
        row.Draft.Length.Should().BeInRange(4, 6);
    }

    [Fact]
    public void TextRow_Allow_FiltersCharacters()
    {
        var code = "";
        var row = new TextRow("Room", () => code, v => code = v, allow: char.IsDigit);
        var form = new Form(SoundPlayer.Silent(), row);

        Press(form, Keys.Enter);
        Type(form, "a1b2");
        Press(form, Keys.Enter);

        code.Should().Be("12");
    }

    [Theory]
    [InlineData(0, 0, -1, 1, 1)]
    [InlineData(0, 2, -1, 1, 2)]
    [InlineData(5, 7, 1, 4, 7)]
    [InlineData(5, 8, 1, 4, 7)]
    [InlineData(6, 6, -3, 6, 6)]
    [InlineData(0, 0, 2, 0, 0)]
    public void WheelScroll_MovesTheView_AndTheSelectionOnlyAsFarAsItMust(int first, int selected, int wheel, int expectedFirst, int expectedSelected) =>
        Menu.WheelScroll(first, selected, count: 10, visible: 4, wheel).Should().Be((expectedFirst, expectedSelected));

    [Fact]
    public void WheelScroll_ListThatFits_StaysPut() =>
        Menu.WheelScroll(0, 1, count: 3, visible: 4, wheel: -2).Should().Be((0, 1));

    [Fact]
    public void Hint_IsTheFocusedRows()
    {
        var form = new Form(
            SoundPlayer.Silent(),
            new ButtonRow("A", () => { }) { Hint = "Does A" },
            new ButtonRow("B", () => { }));

        form.HasHints.Should().BeTrue();
        form.Hint.Should().Be("Does A");
        form.Focused = 1;
        form.Hint.Should().BeNull();
    }

    [Fact]
    public void Tooltip_GoesBelow_OrAboveWhenThereIsNoRoom_AndStaysOnScreen()
    {
        var screen = new Rectangle(0, 0, 800, 600);

        Tooltip.Place(new Point(200, 50), new Rectangle(100, 100, 40, 40), screen).Should().Be(new Rectangle(20, 146, 200, 50));
        Tooltip.Place(new Point(200, 50), new Rectangle(100, 540, 40, 40), screen).Y.Should().Be(540 - 6 - 50);
        Tooltip.Place(new Point(200, 50), new Rectangle(0, 100, 10, 10), screen).X.Should().Be(6, "clear of the side");
        Tooltip.Place(new Point(200, 50), new Rectangle(790, 100, 10, 10), screen).Right.Should().Be(794);
    }

    [Fact]
    public void NineSlice_KeepsTheCorners_AndStretchesTheRest()
    {
        var slices = NineSlice.Slices(new Rectangle(10, 20, 100, 60), 8, 6, 4, 2);

        slices[0].Should().Be(new Rectangle(10, 20, 8, 6));
        slices[4].Should().Be(new Rectangle(18, 26, 88, 52));
        slices[8].Should().Be(new Rectangle(106, 78, 4, 2));
    }

    [Fact]
    public void NineSlice_SmallerThanItsCorners_ShrinksThemInProportion()
    {
        var slices = NineSlice.Slices(new Rectangle(0, 0, 10, 10), 10, 10, 10, 10);

        slices[0].Should().Be(new Rectangle(0, 0, 5, 5));
        slices[4].Width.Should().Be(0);
        slices[8].Should().Be(new Rectangle(5, 5, 5, 5));
    }

    private void Press(Form form, Keys key)
    {
        _input.Update(new KeyboardState(key));
        form.Update(_input, UiKeys.Default, Frame);
        _input.Update(new KeyboardState());
        form.Update(_input, UiKeys.Default, Frame);
    }

    /// <summary>Types <paramref name="text"/> as the window would: each character with its key held.</summary>
    private void Type(Form form, string text)
    {
        foreach (var c in text)
        {
            var key = c == ' ' ? Keys.Space : char.IsLetter(c) ? Enum.Parse<Keys>(char.ToUpperInvariant(c).ToString()) : Keys.None;
            _input.Type(c);
            _input.Update(new KeyboardState(key));
            form.Update(_input, UiKeys.Default, Frame);
            _input.Update(new KeyboardState());
            form.Update(_input, UiKeys.Default, Frame);
        }
    }
}
