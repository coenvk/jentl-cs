using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Input;

/// <summary>
/// A gamepad as one frame left it: its real buttons, sticks and triggers. The stick directions and triggers that
/// count as buttons (<see cref="Buttons.LeftThumbstickUp"/>...) follow from the sticks and triggers again.
/// </summary>
public readonly record struct PadFrame(bool Connected, Buttons Buttons, Vector2 LeftStick, Vector2 RightStick, float LeftTrigger, float RightTrigger)
{
    private static readonly Buttons[] Real =
    [
        Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.Back, Buttons.Start, Buttons.BigButton,
        Buttons.LeftShoulder, Buttons.RightShoulder, Buttons.LeftStick, Buttons.RightStick,
        Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
    ];

    public static PadFrame Of(GamePadState pad)
    {
        if (!pad.IsConnected)
        {
            return default;
        }

        var buttons = Real.Where(pad.IsButtonDown).Aggregate((Buttons)0, (all, b) => all | b);
        return new PadFrame(true, buttons, pad.ThumbSticks.Left, pad.ThumbSticks.Right, pad.Triggers.Left, pad.Triggers.Right);
    }

    public GamePadState ToState() =>
        Connected ? new GamePadState(LeftStick, RightStick, LeftTrigger, RightTrigger, Down(Buttons)) : default;

    private static Buttons[] Down(Buttons buttons) => [.. Real.Where(b => buttons.HasFlag(b))];
}

/// <summary>
/// Everything a game reads in one frame: how long it lasted, the keys down, the pointer, as <see cref="InputState.Cursor"/> (where the game saw it, after
/// the virtual screen and the pad cursor), the gamepad, the wheel and the text typed.
/// </summary>
public sealed record InputFrame(TimeSpan Elapsed, IReadOnlyList<Keys> Keys, PointerState Cursor, PadFrame Pad, int Wheel, string Typed)
{
    /// <summary>Whether two frames hold the same input (records compare lists by reference).</summary>
    public bool SameAs(InputFrame other) =>
        other is not null && Elapsed == other.Elapsed && Keys.SequenceEqual(other.Keys) && Cursor == other.Cursor && Pad == other.Pad && Wheel == other.Wheel && Typed == other.Typed;
}

/// <summary>
/// Input recorded frame by frame, to play back: a bug as it happened, a ghost of a best run, a test of a real-time
/// game. Played back into a game that runs the same way (the same seed, time only from the elapsed time it is given,
/// physics stepped by <c>PhysicsWorld.Update</c>), it does the same things again. <see cref="Save"/> writes it as
/// text, one line per frame and runs of the same frame on one line, which <see cref="Load"/> reads.
/// </summary>
public sealed class InputRecording
{
    private const string Header = "jentl-input 1";
    private readonly List<InputFrame> _frames = [];

    public IReadOnlyList<InputFrame> Frames => _frames;

    /// <summary>How long the recorded frames lasted together.</summary>
    public TimeSpan Duration => _frames.Aggregate(TimeSpan.Zero, (sum, f) => sum + f.Elapsed);

    public void Add(InputFrame frame) => _frames.Add(frame ?? throw new ArgumentNullException(nameof(frame)));

    /// <summary>The recording as text: a header, then <c>[count*]ticks;keys;x,y,down;pad;wheel;"typed"</c> per line.</summary>
    public string Save()
    {
        var text = new StringBuilder(Header).Append('\n');
        for (var i = 0; i < _frames.Count;)
        {
            var run = 1;
            while (i + run < _frames.Count && _frames[i + run].SameAs(_frames[i]))
            {
                run++;
            }

            if (run > 1)
            {
                text.Append(run.ToString(CultureInfo.InvariantCulture)).Append('*');
            }

            Write(text, _frames[i]);
            text.Append('\n');
            i += run;
        }

        return text.ToString();
    }

    /// <exception cref="InvalidDataException">The text is not a recording.</exception>
    public static InputRecording Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0] != Header)
        {
            throw new InvalidDataException($"An input recording starts with \"{Header}\".");
        }

        var recording = new InputRecording();
        for (var i = 1; i < lines.Length; i++)
        {
            try
            {
                var line = lines[i];
                var run = 1;
                var star = line.IndexOf('*', StringComparison.Ordinal);
                var semicolon = line.IndexOf(';', StringComparison.Ordinal);
                if (star >= 0 && (semicolon < 0 || star < semicolon))
                {
                    run = int.Parse(line[..star], CultureInfo.InvariantCulture);
                    line = line[(star + 1)..];
                }

                var frame = Read(line);
                for (var r = 0; r < run; r++)
                {
                    recording._frames.Add(frame);
                }
            }
            catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or JsonException or ArgumentException)
            {
                throw new InvalidDataException($"Line {i + 1} of the recording is not a frame: {e.Message}", e);
            }
        }

        return recording;
    }

    private static void Write(StringBuilder text, InputFrame frame)
    {
        var c = CultureInfo.InvariantCulture;
        text.Append(frame.Elapsed.Ticks.ToString(c)).Append(';');
        text.AppendJoin(',', frame.Keys.Select(k => ((int)k).ToString(c))).Append(';');
        text.Append(Number(frame.Cursor.Position.X)).Append(',').Append(Number(frame.Cursor.Position.Y)).Append(',').Append(frame.Cursor.IsDown ? '1' : '0').Append(';');
        var pad = frame.Pad;
        if (pad.Connected)
        {
            text.Append(((int)pad.Buttons).ToString(c)).Append(',')
                .AppendJoin(',', [Number(pad.LeftStick.X), Number(pad.LeftStick.Y), Number(pad.RightStick.X), Number(pad.RightStick.Y), Number(pad.LeftTrigger), Number(pad.RightTrigger)]);
        }
        else
        {
            text.Append('-');
        }

        text.Append(';').Append(frame.Wheel.ToString(c)).Append(';');
        if (frame.Typed.Length > 0)
        {
            text.Append(JsonSerializer.Serialize(frame.Typed));
        }
    }

    private static InputFrame Read(string line)
    {
        // The typed text comes last and may hold ';' itself.
        var fields = line.Split(';', 6);
        if (fields.Length != 6)
        {
            throw new FormatException("A frame has six fields.");
        }

        var keys = fields[1].Length == 0 ? [] : fields[1].Split(',').Select(k => (Keys)int.Parse(k, CultureInfo.InvariantCulture)).ToArray();
        var pointer = fields[2].Split(',');
        var pad = default(PadFrame);
        if (fields[3] != "-")
        {
            var p = fields[3].Split(',');
            pad = new PadFrame(true, (Buttons)int.Parse(p[0], CultureInfo.InvariantCulture), new Vector2(Float(p[1]), Float(p[2])), new Vector2(Float(p[3]), Float(p[4])), Float(p[5]), Float(p[6]));
        }

        return new InputFrame(
            TimeSpan.FromTicks(long.Parse(fields[0], CultureInfo.InvariantCulture)),
            keys,
            new PointerState(pointer[2] == "1", new Vector2(Float(pointer[0]), Float(pointer[1]))),
            pad,
            int.Parse(fields[4], CultureInfo.InvariantCulture),
            fields[5].Length == 0 ? "" : JsonSerializer.Deserialize<string>(fields[5]) ?? "");
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static float Float(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>Plays an <see cref="InputRecording"/> back, a frame at a time.</summary>
public sealed class InputPlayback(InputRecording recording)
{
    private readonly InputRecording _recording = recording ?? throw new ArgumentNullException(nameof(recording));
    private int _next;

    public bool IsFinished => _next >= _recording.Frames.Count;

    /// <summary>How many frames have been played.</summary>
    public int Played => _next;

    /// <summary>The next frame, or null once all have been played.</summary>
    public InputFrame? Next() => IsFinished ? null : _recording.Frames[_next++];
}
