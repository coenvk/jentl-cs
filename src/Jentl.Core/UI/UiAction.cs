using Jentl.Core.Input;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.UI;

/// <summary>What the UI widgets respond to on a keyboard or gamepad: moving the focus, choosing, going back.</summary>
public enum UiAction
{
    Up,
    Down,
    Left,
    Right,
    Confirm,
    Back,
}

/// <summary>Default keys and buttons for <see cref="UiAction"/>; a game can pass its own map instead.</summary>
public static class UiKeys
{
    public static ActionMap<UiAction> Default { get; } = new(
        new Dictionary<UiAction, Keys[]>
        {
            [UiAction.Up] = [Keys.Up, Keys.W],
            [UiAction.Down] = [Keys.Down, Keys.S],
            [UiAction.Left] = [Keys.Left, Keys.A],
            [UiAction.Right] = [Keys.Right, Keys.D],
            [UiAction.Confirm] = [Keys.Enter, Keys.Space],
            [UiAction.Back] = [Keys.Escape],
        },
        new Dictionary<UiAction, Buttons[]>
        {
            [UiAction.Up] = [Buttons.DPadUp, Buttons.LeftThumbstickUp],
            [UiAction.Down] = [Buttons.DPadDown, Buttons.LeftThumbstickDown],
            [UiAction.Left] = [Buttons.DPadLeft, Buttons.LeftThumbstickLeft],
            [UiAction.Right] = [Buttons.DPadRight, Buttons.LeftThumbstickRight],
            [UiAction.Confirm] = [Buttons.A],
            [UiAction.Back] = [Buttons.B, Buttons.Start],
        });
}

/// <summary>The few words the widgets say themselves; a game sets them to translate them.</summary>
public static class UiText
{
    public static string On { get; set; } = "On";

    public static string Off { get; set; } = "Off";
}

/// <summary>Sounds the widgets play through a <see cref="Audio.SoundPlayer"/>, by name.</summary>
public static class UiSounds
{
    /// <summary>Moving the selection or changing a value.</summary>
    public static string Navigate { get; set; } = "navigate";
}
