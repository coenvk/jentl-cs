using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jentl.Core.Audio;

/// <summary>A note in a <see cref="Pattern"/>: the step it starts on, its pitch as a MIDI number (60 is C4) and how many steps it lasts.</summary>
public sealed record ComposedNote(int Step, int Pitch, int Length = 1);

/// <summary>One bar of one channel's music, as written on a piano roll; notes may ring on into the next bar.</summary>
public sealed class Pattern
{
    public List<ComposedNote> Notes { get; } = [];
}

/// <summary>A row of the arrangement: an instrument (by its <see cref="Instruments.All"/> name), its patterns, and which plays in each bar.</summary>
public sealed class Channel
{
    public string Instrument { get; set; } = "Harp";

    /// <summary>A multiple of the instrument's own volume.</summary>
    public float Volume { get; set; } = 1;

    public List<Pattern> Patterns { get; } = [new()];

    /// <summary>The pattern played in each bar, by index; -1 (or a bar past the end) is silence.</summary>
    public List<int> Arrangement { get; } = [];

    public Pattern? PatternAt(int bar) =>
        bar >= 0 && bar < Arrangement.Count && Arrangement[bar] is var index && index >= 0 && index < Patterns.Count ? Patterns[index] : null;

    /// <summary>The ready-made instrument it plays, at its volume; a name not known is a harp.</summary>
    public Instrument Sound()
    {
        var instrument = Audio.Instruments.All.GetValueOrDefault(Instrument) ?? Audio.Instruments.Harp;
        return instrument with { Volume = instrument.Volume * Volume };
    }
}

/// <summary>
/// A song as a music maker writes it, in the manner of Bosca Ceoil: channels, each an instrument with patterns a bar
/// long, and an arrangement saying which pattern each channel plays in each bar. <see cref="ToTune"/> turns it into
/// a <see cref="Tune"/> for <see cref="Synth"/> (chords become several voices), <see cref="ToJson"/> saves it, and
/// <see cref="ToCode"/> writes it as the code of a tune a game can play.
/// </summary>
public sealed class Composition
{
    public const int Version = 1;

    public string Title { get; set; } = "Untitled";

    public int Tempo { get; set; } = 110;

    public int BeatsPerBar { get; set; } = 4;

    /// <summary>Grid steps to a beat: 4 gives sixteenth notes.</summary>
    public int StepsPerBeat { get; set; } = 4;

    public float Reverb { get; set; } = 0.25f;

    /// <summary>How late every second step comes, 0 (straight) to 0.5: see <see cref="Tune.Swing"/>.</summary>
    public float Swing { get; set; }

    /// <summary>How long the song is.</summary>
    public int Bars { get; set; } = 8;

    public List<Channel> Channels { get; } = [];

    public int StepsPerBar => StepsPerBeat * BeatsPerBar;

    public TimeSpan Duration => TimeSpan.FromSeconds(Bars * BeatsPerBar * 60.0 / Tempo);

    /// <summary>The note name of a MIDI pitch: 60 is "C4", 61 "C#4".</summary>
    public static string NoteName(int pitch) => Chords.Note(((pitch % 12) + 12) % 12, (int)Math.Floor(pitch / 12.0) - 1);

    /// <summary>
    /// Bars <paramref name="from"/> up to <paramref name="to"/> (the end when left out) as one tune: each channel's
    /// notes in the patterns its arrangement names, as many voices as it has notes sounding at once.
    /// </summary>
    public Tune ToTune(int from = 0, int? to = null)
    {
        var end = Math.Clamp(to ?? Bars, from, Bars);
        var voices = Channels.SelectMany(c => Lines(c, from, end).Select(steps => new Voice(c.Sound(), steps))).ToList();
        if (voices.Count == 0)
        {
            voices.Add(new Voice(Instruments.Harp, string.Join(' ', Enumerable.Repeat(".", Math.Max(1, (end - from) * StepsPerBar)))));
        }

        return new Tune(Tempo, voices, StepsPerBeat) { Reverb = Reverb, Swing = Swing };
    }

    /// <summary>The composition as the C# of a <see cref="Tune"/>, for pasting into a game.</summary>
    public string ToCode()
    {
        var voices = new List<string>();
        foreach (var channel in Channels)
        {
            var name = Instruments.All.ContainsKey(channel.Instrument)
                ? "Instruments." + string.Concat(channel.Instrument.Split(' ').Select(w => char.ToUpperInvariant(w[0]) + w[1..]))
                : "Instruments.Harp";
            var volume = Math.Abs(channel.Volume - 1) < 0.001f ? "" : string.Create(CultureInfo.InvariantCulture, $" with {{ Volume = {channel.Sound().Volume:0.###}f }}");
            voices.AddRange(Lines(channel, 0, Bars).Select(steps => $"    new Voice({name}{volume}, \"{InBars(steps)}\")"));
        }

        var swing = Swing > 0 ? string.Create(CultureInfo.InvariantCulture, $", Swing = {Swing:0.##}f") : "";
        return string.Create(CultureInfo.InvariantCulture, $"// {Title}\nnew Tune({Tempo}, [\n{string.Join(",\n", voices)},\n], {StepsPerBeat}) {{ Reverb = {Reverb:0.##}f{swing} }};\n");
    }

    public string ToJson()
    {
        var channels = new JsonArray();
        foreach (var channel in Channels)
        {
            var patterns = new JsonArray();
            foreach (var pattern in channel.Patterns)
            {
                patterns.Add(new JsonArray([.. pattern.Notes.Select(n => (JsonNode?)new JsonArray(n.Step, n.Pitch, n.Length))]));
            }

            channels.Add(new JsonObject
            {
                ["instrument"] = channel.Instrument,
                ["volume"] = channel.Volume,
                ["patterns"] = patterns,
                ["arrangement"] = new JsonArray([.. channel.Arrangement.Select(i => (JsonNode?)i)]),
            });
        }

        return new JsonObject
        {
            ["version"] = Version,
            ["title"] = Title,
            ["tempo"] = Tempo,
            ["beatsPerBar"] = BeatsPerBar,
            ["stepsPerBeat"] = StepsPerBeat,
            ["reverb"] = Reverb,
            ["swing"] = Swing,
            ["bars"] = Bars,
            ["channels"] = channels,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <exception cref="FormatException">The text is not a composition.</exception>
    public static Composition FromJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json) ?? throw new FormatException("An empty composition.");
            var composition = new Composition
            {
                Title = (string?)node["title"] ?? "Untitled",
                Tempo = Math.Clamp((int?)node["tempo"] ?? 110, 20, 400),
                BeatsPerBar = Math.Clamp((int?)node["beatsPerBar"] ?? 4, 2, 7),
                StepsPerBeat = Math.Clamp((int?)node["stepsPerBeat"] ?? 4, 1, 8),
                Reverb = Math.Clamp((float?)node["reverb"] ?? 0.25f, 0, 1),
                Swing = Math.Clamp((float?)node["swing"] ?? 0, 0, 0.5f),
                Bars = Math.Clamp((int?)node["bars"] ?? 8, 1, 256),
            };
            foreach (var item in node["channels"]?.AsArray() ?? [])
            {
                var channel = new Channel { Instrument = (string?)item?["instrument"] ?? "Harp", Volume = (float?)item?["volume"] ?? 1 };
                channel.Patterns.Clear();
                foreach (var pattern in item?["patterns"]?.AsArray() ?? [])
                {
                    var read = new Pattern();
                    read.Notes.AddRange((pattern?.AsArray() ?? []).Select(n => new ComposedNote((int)n![0]!, (int)n[1]!, (int)n[2]!)));
                    channel.Patterns.Add(read);
                }

                if (channel.Patterns.Count == 0)
                {
                    channel.Patterns.Add(new Pattern());
                }

                channel.Arrangement.AddRange((item?["arrangement"]?.AsArray() ?? []).Select(i => (int)i!));
                composition.Channels.Add(channel);
            }

            return composition;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new FormatException($"Not a composition: {e.Message}", e);
        }
    }

    public Composition Clone() => FromJson(ToJson());

    /// <summary>
    /// A channel's notes in bars <paramref name="from"/> to <paramref name="to"/>, shared out over as few voices as
    /// can hold them (a chord needs one a note), each as <see cref="Voice"/> steps.
    /// </summary>
    private List<string> Lines(Channel channel, int from, int to)
    {
        var length = (to - from) * StepsPerBar;
        var notes = new List<(int Start, int Pitch, int Length)>();
        for (var bar = from; bar < to; bar++)
        {
            if (channel.PatternAt(bar) is { } pattern)
            {
                var offset = (bar - from) * StepsPerBar;
                notes.AddRange(pattern.Notes.Where(n => n.Step >= 0 && n.Step < StepsPerBar).Select(n => (offset + n.Step, n.Pitch, Math.Max(1, n.Length))));
            }
        }

        var lines = new List<List<(int Start, int Pitch, int Length)>>();
        foreach (var note in notes.OrderBy(n => n.Start).ThenByDescending(n => n.Pitch))
        {
            var line = lines.FirstOrDefault(l => l[^1].Start + l[^1].Length <= note.Start);
            if (line is null)
            {
                lines.Add(line = []);
            }

            line.Add(note);
        }

        return [.. lines.Select(line =>
        {
            var steps = Enumerable.Repeat(".", length).ToArray();
            foreach (var (start, pitch, held) in line)
            {
                steps[start] = NoteName(pitch);
                for (var i = start + 1; i < Math.Min(length, start + held); i++)
                {
                    steps[i] = "-";
                }
            }

            return string.Join(' ', steps);
        })];
    }

    private string InBars(string steps) =>
        string.Join(" | ", steps.Split(' ').Chunk(StepsPerBar).Select(bar => string.Join(' ', bar)));
}
