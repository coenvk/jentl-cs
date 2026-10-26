using Jentl.Core.Audio;
using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class UiTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private readonly InputState _input = new();
    private readonly SoundPlayer _sounds = SoundPlayer.Silent();

    [Theory]
    [InlineData(0, 0, 10, 4, 0)]
    [InlineData(0, 5, 10, 4, 2)]
    [InlineData(2, 3, 10, 4, 2)]
    [InlineData(5, 1, 10, 4, 1)]
    [InlineData(0, 9, 10, 4, 6)]
    [InlineData(3, 2, 3, 5, 0)]
    public void Scroll_KeepsTheSelectionInView_MovingAsLittleAsPossible(int first, int selected, int count, int visible, int expected) =>
        Menu.Scroll(first, selected, count, visible).Should().Be(expected);

    [Fact]
    public void Menu_MovesWithTheKeys_WrapsRound_AndConfirms()
    {
        var chosen = "";
        var menu = new Menu(_sounds, new MenuItem("A", () => chosen = "A"), new MenuItem("B", () => chosen = "B"), new MenuItem("C", () => chosen = "C"));

        Press(Keys.Up);
        menu.Update(_input, UiKeys.Default, Frame);
        menu.Selected.Should().Be(2, "up from the first item wraps to the last");
        Release();
        menu.Update(_input, UiKeys.Default, Frame);

        Press(Keys.Down);
        menu.Update(_input, UiKeys.Default, Frame);
        menu.Selected.Should().Be(0);
        Release();
        menu.Update(_input, UiKeys.Default, Frame);

        Press(Keys.Enter);
        menu.Update(_input, UiKeys.Default, Frame);
        chosen.Should().Be("A");
    }

    [Fact]
    public void Toggles_FlipOnConfirm_AndLeftRightSetThem()
    {
        var on = false;
        var form = new Form(_sounds, new ToggleRow("Sound", () => on, v => on = v));

        Tap(form, Keys.Enter);
        on.Should().BeTrue();
        Tap(form, Keys.Enter);
        on.Should().BeFalse();
        Tap(form, Keys.Right);
        on.Should().BeTrue();
        Tap(form, Keys.Right);
        on.Should().BeTrue("right means on, not flip");
    }

    [Fact]
    public void Sliders_StepWithLeftAndRight_WithinTheirRange()
    {
        var value = 0.5f;
        var form = new Form(_sounds, new SliderRow("Volume", () => value, v => value = v, step: 0.25f));

        Tap(form, Keys.Right);
        value.Should().Be(0.75f);
        Tap(form, Keys.Right);
        Tap(form, Keys.Right);
        value.Should().Be(1f, "it stops at the top");
        Tap(form, Keys.Left);
        value.Should().Be(0.75f);
    }

    [Theory]
    [InlineData(0.33f, 0.3f)]
    [InlineData(0.36f, 0.4f)]
    [InlineData(-2f, 0f)]
    [InlineData(7f, 1f)]
    public void SliderValues_SnapToSteps_AndStayInRange(float set, float expected)
    {
        var value = 0f;
        var slider = new SliderRow("Volume", () => value, v => value = v, step: 0.1f);

        slider.SetValue(set);

        value.Should().BeApproximately(expected, 0.0001f);
    }

    [Fact]
    public void FormFocus_MovesBetweenRows_AndButtonsPress()
    {
        var pressed = false;
        var on = false;
        var form = new Form(_sounds, new ToggleRow("Sound", () => on, v => on = v), new ButtonRow("Back", () => pressed = true));

        Tap(form, Keys.Down);
        form.Focused.Should().Be(1);
        Tap(form, Keys.Enter);
        pressed.Should().BeTrue();
        on.Should().BeFalse("only the focused row reacts");
        Tap(form, Keys.Down);
        form.Focused.Should().Be(0, "focus wraps round");
    }

    private void Press(Keys key) => _input.Update(new KeyboardState(key));

    private void Release() => _input.Update(new KeyboardState());

    private void Tap(Form form, Keys key)
    {
        Press(key);
        form.Update(_input, UiKeys.Default, Frame);
        Release();
        form.Update(_input, UiKeys.Default, Frame);
    }

    [Fact]
    public void Wrap_BreaksAtSpaces_KeepingEachLineWithinTheWidth()
    {
        // One unit per character.
        static float Measure(string text) => text.Length;

        TextExtensions.Wrap("a long subtitle that runs on", 12, Measure).Should().Equal("a long", "subtitle", "that runs on");
        TextExtensions.Wrap("short", 12, Measure).Should().Equal("short");
        TextExtensions.Wrap("", 12, Measure).Should().BeEmpty();
        TextExtensions.Wrap("unbreakableword ok", 6, Measure).Should().Equal("unbreakableword", "ok");
    }
}
