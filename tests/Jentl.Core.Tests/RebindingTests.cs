using Jentl.Core.Audio;
using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

public class RebindingTests
{
    private enum Move
    {
        Up,
        Down,
        Jump,
    }

    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);
    private readonly InputState _input = new();

    private static ActionMap<Move> NewMap() => new(
        new Dictionary<Move, Keys[]>
        {
            [Move.Up] = [Keys.Up, Keys.W],
            [Move.Down] = [Keys.Down, Keys.S],
            [Move.Jump] = [Keys.Space],
        },
        new Dictionary<Move, Buttons[]> { [Move.Jump] = [Buttons.A] });

    [Fact]
    public void Bind_ReplacesTheSlot_AndTakesTheKeyFromOtherActions()
    {
        var map = NewMap();

        var lost = map.Bind(Move.Jump, Keys.W);

        map.KeysFor(Move.Jump).Should().Equal(Keys.W);
        map.KeysFor(Move.Up).Should().Equal(Keys.Up);
        lost.Should().Equal(Move.Up);
    }

    [Fact]
    public void Bind_PastTheLastSlot_Adds_AndAKeyNeverSitsInTwoSlots()
    {
        var map = NewMap();

        map.Bind(Move.Jump, Keys.J, slot: 5);
        map.KeysFor(Move.Jump).Should().Equal(Keys.Space, Keys.J);

        map.Bind(Move.Jump, Keys.J, slot: 0);
        map.KeysFor(Move.Jump).Should().Equal(Keys.J);
    }

    [Fact]
    public void Unbind_AndReset()
    {
        var map = NewMap();
        map.UnbindKey(Move.Up, 1);
        map.Bind(Move.Jump, Buttons.B);
        map.IsChanged.Should().BeTrue();
        map.KeysFor(Move.Up).Should().Equal(Keys.Up);

        map.Reset();

        map.IsChanged.Should().BeFalse();
        map.KeysFor(Move.Up).Should().Equal(Keys.Up, Keys.W);
        map.ButtonsFor(Move.Jump).Should().Equal(Buttons.A);
    }

    [Fact]
    public void Copy_ChangesOnItsOwn()
    {
        var shared = NewMap();
        var mine = shared.Copy();

        mine.Bind(Move.Jump, Keys.J);

        shared.KeysFor(Move.Jump).Should().Equal(Keys.Space);
        // A copy keeps the defaults.
        mine.Reset();
        mine.KeysFor(Move.Jump).Should().Equal(Keys.Space);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_OnlyTheChangedActions()
    {
        var map = NewMap();
        map.Bind(Move.Jump, Keys.W);
        map.Bind(Move.Jump, Buttons.Y);

        var text = map.Save();
        var loaded = NewMap();
        loaded.Load(text);

        text.Should().NotContain("Down", because: "untouched actions follow the game's defaults");
        loaded.KeysFor(Move.Jump).Should().Equal(Keys.W);
        loaded.KeysFor(Move.Up).Should().Equal(Keys.Up);
        loaded.ButtonsFor(Move.Jump).Should().Equal(Buttons.Y);
    }

    [Fact]
    public void Load_SkipsWhatTheGameNoLongerHas()
    {
        var map = NewMap();

        map.Load("""{"keys":{"Fly":["F"],"Jump":["J","NoSuchKey",3]}}""");

        map.KeysFor(Move.Jump).Should().Equal(Keys.J);
        map.KeysFor(Move.Up).Should().Equal(Keys.Up, Keys.W);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Load_Garbage_ThrowsInvalidData(string text) =>
        FluentActions.Invoking(() => NewMap().Load(text)).Should().Throw<InvalidDataException>();

    [Fact]
    public void PressedKey_IsOnlyTheNewPress()
    {
        _input.Update(new KeyboardState(Keys.A));
        _input.PressedKey().Should().Be(Keys.A);

        _input.Update(new KeyboardState(Keys.A, Keys.B));
        _input.PressedKey().Should().Be(Keys.B);

        _input.Update(new KeyboardState(Keys.A, Keys.B));
        _input.PressedKey().Should().BeNull();
    }

    [Theory]
    [InlineData(Keys.LeftShift, "Left Shift")]
    [InlineData(Keys.D7, "7")]
    [InlineData(Keys.NumPad3, "Num 3")]
    [InlineData(Keys.OemComma, ",")]
    [InlineData(Keys.Space, "Space")]
    [InlineData(Keys.PageUp, "Page Up")]
    public void KeyNames(Keys key, string name) => InputNames.Of(key).Should().Be(name);

    [Fact]
    public void ButtonNames()
    {
        InputNames.Of(Buttons.LeftShoulder).Should().Be("LB");
        InputNames.Of(Buttons.A).Should().Be("A");
        InputNames.Of(Buttons.Start).Should().Be("Start");
    }

    [Fact]
    public void BindingRow_CapturesTheNextKey_AndTheFormStaysOutOfTheWay()
    {
        var map = NewMap();
        var saved = 0;
        var row = new BindingRow<Move>("Jump", map, Move.Jump, changed: () => saved++);
        var form = new Form(SoundPlayer.Silent(), row, new ButtonRow("Back", () => { }));

        Tap(form, Keys.Enter);
        row.IsCapturing.Should().BeTrue();
        form.IsEditing.Should().BeTrue();

        // Down would move the focus; while capturing it is the key to bind.
        Tap(form, Keys.Down);

        row.IsCapturing.Should().BeFalse();
        form.Focused.Should().Be(0);
        map.KeysFor(Move.Jump).Should().Equal(Keys.Down);
        map.KeysFor(Move.Down).Should().Equal(Keys.S);
        saved.Should().Be(1);
    }

    [Fact]
    public void BindingRow_EscapeCancels_AndTheFormSaysItWasEditingThatFrame()
    {
        var map = NewMap();
        var row = new BindingRow<Move>("Jump", map, Move.Jump);
        var form = new Form(SoundPlayer.Silent(), row);
        Tap(form, Keys.Enter);

        _input.Update(new KeyboardState(Keys.Escape));
        form.Update(_input, UiKeys.Default, Frame);

        row.IsCapturing.Should().BeFalse();
        form.IsEditing.Should().BeTrue("the screen must not also close on this Escape");
        map.KeysFor(Move.Jump).Should().Equal(Keys.Space);

        _input.Update(new KeyboardState());
        form.Update(_input, UiKeys.Default, Frame);
        form.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void BindingRow_BackspaceClearsTheSlot_AndGamepadRowsTakeButtons()
    {
        var map = NewMap();
        var keyRow = new BindingRow<Move>("Up", map, Move.Up, slot: 1);
        var padRow = new BindingRow<Move>("Jump", map, Move.Jump, device: BindingDevice.GamePad);
        var form = new Form(SoundPlayer.Silent(), keyRow, padRow);

        Tap(form, Keys.Enter);
        Tap(form, Keys.Back);
        map.KeysFor(Move.Up).Should().Equal(Keys.Up);

        Tap(form, Keys.Down);
        Tap(form, Keys.Enter);
        padRow.IsCapturing.Should().BeTrue();
        _input.Update(new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, [Buttons.X]));
        form.Update(_input, UiKeys.Default, Frame);

        map.ButtonsFor(Move.Jump).Should().Equal(Buttons.X);
    }

    private void Tap(Form form, Keys key)
    {
        _input.Update(new KeyboardState(key));
        form.Update(_input, UiKeys.Default, Frame);
        _input.Update(new KeyboardState());
        form.Update(_input, UiKeys.Default, Frame);
    }
}
