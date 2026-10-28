using System.Text;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>Short names for keys and buttons, to show players what an action is bound to ("Left Shift", "LB").</summary>
public static class InputNames
{
    private static readonly Dictionary<Keys, string> KeyNames = new()
    {
        [Keys.Back] = "Backspace",
        [Keys.Enter] = "Enter",
        [Keys.Escape] = "Esc",
        [Keys.PageUp] = "Page Up",
        [Keys.PageDown] = "Page Down",
        [Keys.OemPlus] = "=",
        [Keys.OemMinus] = "-",
        [Keys.OemComma] = ",",
        [Keys.OemPeriod] = ".",
        [Keys.OemQuestion] = "/",
        [Keys.OemSemicolon] = ";",
        [Keys.OemQuotes] = "'",
        [Keys.OemTilde] = "`",
        [Keys.OemOpenBrackets] = "[",
        [Keys.OemCloseBrackets] = "]",
        [Keys.OemPipe] = "\\",
        [Keys.OemBackslash] = "\\",
        [Keys.Add] = "Num +",
        [Keys.Subtract] = "Num -",
        [Keys.Multiply] = "Num *",
        [Keys.Divide] = "Num /",
        [Keys.Decimal] = "Num .",
    };

    private static readonly Dictionary<Buttons, string> ButtonNames = new()
    {
        [Buttons.DPadUp] = "D-pad Up",
        [Buttons.DPadDown] = "D-pad Down",
        [Buttons.DPadLeft] = "D-pad Left",
        [Buttons.DPadRight] = "D-pad Right",
        [Buttons.LeftShoulder] = "LB",
        [Buttons.RightShoulder] = "RB",
        [Buttons.LeftTrigger] = "LT",
        [Buttons.RightTrigger] = "RT",
        [Buttons.LeftStick] = "Left Stick Click",
        [Buttons.RightStick] = "Right Stick Click",
        [Buttons.LeftThumbstickUp] = "Left Stick Up",
        [Buttons.LeftThumbstickDown] = "Left Stick Down",
        [Buttons.LeftThumbstickLeft] = "Left Stick Left",
        [Buttons.LeftThumbstickRight] = "Left Stick Right",
        [Buttons.RightThumbstickUp] = "Right Stick Up",
        [Buttons.RightThumbstickDown] = "Right Stick Down",
        [Buttons.RightThumbstickLeft] = "Right Stick Left",
        [Buttons.RightThumbstickRight] = "Right Stick Right",
    };

    public static string Of(Keys key)
    {
        if (KeyNames.TryGetValue(key, out var name))
        {
            return name;
        }

        var text = key.ToString();
        return key switch
        {
            >= Keys.D0 and <= Keys.D9 => text[1..],
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + text[^1],
            _ => Words(text),
        };
    }

    public static string Of(Buttons button) =>
        ButtonNames.TryGetValue(button, out var name) ? name : Words(button.ToString());

    /// <summary>"LeftShift" as "Left Shift".</summary>
    private static string Words(string name)
    {
        var words = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                words.Append(' ');
            }

            words.Append(name[i]);
        }

        return words.ToString();
    }
}
