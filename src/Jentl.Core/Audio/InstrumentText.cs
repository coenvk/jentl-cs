using System.Globalization;
using System.Text.Json.Nodes;

namespace Jentl.Core.Audio;

/// <summary>
/// An <see cref="Instrument"/> written down: as JSON, to keep one edited in a music maker, and as C#, as a preset
/// <c>with</c> the settings that differ from it.
/// </summary>
public static class InstrumentText
{
    /// <summary>Each setting's name, how it reads in C#, and how it is kept in and read from JSON.</summary>
    private static readonly (string Name, Func<Instrument, string> Code, Func<Instrument, JsonNode?> Write, Func<Instrument, JsonNode, Instrument> Read)[] Settings =
    [
        ("Wave", i => "Wave." + i.Wave, i => i.Wave.ToString(), (i, n) => i with { Wave = Enum.Parse<Wave>((string)n!) }),
        Float("Volume", i => i.Volume, (i, v) => i with { Volume = v }),
        Float("Attack", i => i.Attack, (i, v) => i with { Attack = v }),
        Float("Decay", i => i.Decay, (i, v) => i with { Decay = v }),
        Float("Sustain", i => i.Sustain, (i, v) => i with { Sustain = v }),
        Float("Release", i => i.Release, (i, v) => i with { Release = v }),
        Float("Vibrato", i => i.Vibrato, (i, v) => i with { Vibrato = v }),
        Float("Slide", i => i.Slide, (i, v) => i with { Slide = v }),
        ("Unison", i => i.Unison.ToString(CultureInfo.InvariantCulture), i => i.Unison, (i, n) => i with { Unison = (int)n }),
        Float("Detune", i => i.Detune, (i, v) => i with { Detune = v }),
        Float("Cutoff", i => i.Cutoff, (i, v) => i with { Cutoff = v }),
        Float("CutoffEnvelope", i => i.CutoffEnvelope, (i, v) => i with { CutoffEnvelope = v }),
        Float("Ratio", i => i.Ratio, (i, v) => i with { Ratio = v }),
        Float("Index", i => i.Index, (i, v) => i with { Index = v }),
        Float("IndexDecay", i => i.IndexDecay, (i, v) => i with { IndexDecay = v }),
        (
            "Partials",
            i => i.Partials is null ? "null" : "[" + string.Join(", ", i.Partials.Select(Code)) + "]",
            i => i.Partials is null ? null : new JsonArray([.. i.Partials.Select(p => (JsonNode?)(double)p)]),
            (i, n) => i with { Partials = [.. n.AsArray().Select(p => (float)(double)p!)] }),
        Float("PartialDecay", i => i.PartialDecay, (i, v) => i with { PartialDecay = v }),
        Float("Breath", i => i.Breath, (i, v) => i with { Breath = v }),
        Float("Brightness", i => i.Brightness, (i, v) => i with { Brightness = v }),
        ("Ring", i => i.Ring ? "true" : "false", i => i.Ring, (i, n) => i with { Ring = (bool)n }),
    ];

    /// <summary>Every setting of an instrument, by its C# name in camel case.</summary>
    public static JsonObject ToJson(Instrument instrument)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        var json = new JsonObject();
        foreach (var setting in Settings)
        {
            if (setting.Write(instrument) is { } value)
            {
                json[Camel(setting.Name)] = value;
            }
        }

        return json;
    }

    /// <summary>An instrument read from <see cref="ToJson"/>: the settings it has over <paramref name="start"/>'s.</summary>
    /// <exception cref="FormatException">A setting can't be read.</exception>
    public static Instrument FromJson(JsonNode json, Instrument start)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(start);
        var instrument = start;
        foreach (var setting in Settings)
        {
            if (json[Camel(setting.Name)] is { } value)
            {
                try
                {
                    instrument = setting.Read(instrument, value);
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException)
                {
                    throw new FormatException($"The instrument's {setting.Name} can't be read: {e.Message}", e);
                }
            }
        }

        return instrument;
    }

    /// <summary>
    /// The C# of an instrument: <paramref name="preset"/> (named <paramref name="name"/>, such as
    /// "Instruments.Flute") alone when it is the same, otherwise <c>with</c> the settings that differ.
    /// </summary>
    public static string ToCode(Instrument instrument, string name, Instrument preset)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(preset);
        var changed = Settings.Where(s => s.Code(instrument) != s.Code(preset)).Select(s => $"{s.Name} = {s.Code(instrument)}").ToList();
        return changed.Count == 0 ? name : $"{name} with {{ {string.Join(", ", changed)} }}";
    }

    private static (string, Func<Instrument, string>, Func<Instrument, JsonNode?>, Func<Instrument, JsonNode, Instrument>) Float(
        string name, Func<Instrument, float> get, Func<Instrument, float, Instrument> set) =>
        (name, i => Code(get(i)), i => (double)MathF.Round(get(i), 4), (i, n) => set(i, (float)(double)n));

    private static string Code(float value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "f";

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
