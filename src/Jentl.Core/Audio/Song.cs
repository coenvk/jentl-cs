using System.Globalization;
using System.Text;

namespace Jentl.Core.Audio;

/// <summary>An instrument part of a <see cref="Song"/>, by name ("lead", "bass", "drums").</summary>
public sealed record SongPart(string Name, Instrument Instrument);

/// <summary>A section of a song (an intro, a verse, a chorus): <paramref name="Bars"/> bars, and what each part plays in it.</summary>
/// <param name="Parts">Part name to steps, in <see cref="Voice"/>'s notation; a part left out rests.</param>
public sealed record Section(string Name, int Bars, IReadOnlyDictionary<string, string> Parts);

/// <summary>
/// A song longer than a loop of a few bars: sections played in an <paramref name="Order"/> (intro, A, A, B, A), each
/// part's steps joined up into one <see cref="Tune"/> for <see cref="Synth"/>. Four beats to a bar, or
/// <paramref name="BeatsPerBar"/> (three for a waltz).
/// </summary>
public sealed record Song(string Title, int Tempo, IReadOnlyList<SongPart> Parts, IReadOnlyList<Section> Sections, IReadOnlyList<string> Order, int StepsPerBeat = 2, int BeatsPerBar = 4)
{
    public int StepsPerBar => StepsPerBeat * BeatsPerBar;

    /// <summary>The room it is played in: <see cref="Tune.Reverb"/>.</summary>
    public float Reverb { get; init; }

    public TimeSpan Duration => TimeSpan.FromSeconds(Order.Sum(name => SectionNamed(name).Bars) * BeatsPerBar * 60.0 / Tempo);

    /// <summary>The whole song as one tune: every part through the sections in order, each section filled out with rests to its length.</summary>
    /// <exception cref="ArgumentException">A part is longer than its section, or the order names a section that isn't there.</exception>
    public Tune ToTune()
    {
        var voices = new List<Voice>();
        foreach (var part in Parts)
        {
            var steps = new StringBuilder();
            foreach (var name in Order)
            {
                var section = SectionNamed(name);
                var length = section.Bars * StepsPerBar;
                var written = section.Parts.TryGetValue(part.Name, out var text) ? text : "";
                var count = Synth.Parse(written).Sum(s => s.Length);
                if (count > length)
                {
                    throw new ArgumentException($"{Title}: the {part.Name} in {name} has {count} steps, more than its {length}.");
                }

                steps.Append(written).Append(' ').Append(string.Concat(Enumerable.Repeat(". ", length - count))).Append(' ');
            }

            voices.Add(new Voice(part.Instrument, steps.ToString()));
        }

        return new Tune(Tempo, voices, StepsPerBeat) { Reverb = Reverb };
    }

    private Section SectionNamed(string name) =>
        Sections.FirstOrDefault(s => s.Name == name) ?? throw new ArgumentException($"{Title} has no section '{name}'.");
}

/// <summary>
/// Accompaniment from chord symbols, so a song only needs its tune written note by note. A progression is bars
/// separated by "|", each holding one or more chords ("C | Am F | Dm7 | G7"); a chord is a root (C, F#, Bb) and a kind
/// (none for major, m, 7, maj7, m7, dim, sus2, sus4, 5). Patterns name chord tones per step: 1 the root, 3 the third,
/// 5 the fifth, 7 the seventh (the octave when there is none), 8 the octave, 10 the third above it, "." a rest and "-"
/// holding on; a pattern repeats over each chord.
/// </summary>
public static class Chords
{
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>The chord's notes as semitones above C in its octave: the root, then the third, fifth and seventh as it has them.</summary>
    public static IReadOnlyList<int> Tones(string chord)
    {
        ArgumentException.ThrowIfNullOrEmpty(chord);
        var length = chord.Length > 1 && chord[1] is '#' or 'b' ? 2 : 1;
        var root = Array.IndexOf(Names, char.ToUpperInvariant(chord[0]).ToString());
        if (root < 0)
        {
            throw new FormatException($"'{chord}' is not a chord.");
        }

        root += length == 2 ? (chord[1] == '#' ? 1 : -1) : 0;
        int[] intervals = chord[length..] switch
        {
            "" => [0, 4, 7],
            "m" => [0, 3, 7],
            "7" => [0, 4, 7, 10],
            "maj7" => [0, 4, 7, 11],
            "m7" => [0, 3, 7, 10],
            "dim" => [0, 3, 6],
            "sus2" => [0, 2, 7],
            "sus4" => [0, 5, 7],
            "5" => [0, 7],
            var other => throw new FormatException($"'{other}' in '{chord}' is not a kind of chord (use m, 7, maj7, m7, dim, sus2, sus4 or 5)."),
        };
        return [.. intervals.Select(i => root + i)];
    }

    /// <summary>Each chord of a progression with how many steps it lasts (a bar shared evenly between its chords).</summary>
    public static IReadOnlyList<(string Chord, int Steps)> Spans(string progression, int stepsPerBar = 8)
    {
        ArgumentNullException.ThrowIfNull(progression);
        var spans = new List<(string, int)>();
        foreach (var bar in progression.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var chords = bar.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < chords.Length; i++)
            {
                spans.Add((chords[i], (stepsPerBar * (i + 1) / chords.Length) - (stepsPerBar * i / chords.Length)));
            }
        }

        return spans;
    }

    /// <summary>Plays <paramref name="pattern"/> over every chord of <paramref name="progression"/>, its root in <paramref name="octave"/>.</summary>
    public static string Play(string progression, int octave, string pattern, int stepsPerBar = 8)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var steps = new List<string>();
        foreach (var (chord, length) in Spans(progression, stepsPerBar))
        {
            var tones = Tones(chord);
            for (var i = 0; i < length; i++)
            {
                var token = tokens[i % tokens.Length];
                steps.Add(token is "." or "-" ? token : Note(Degree(tones, token), octave));
            }
        }

        return string.Join(' ', steps);
    }

    /// <summary>One tone of each chord held for the whole chord: a pad's line. <paramref name="tone"/> counts from 0, the root.</summary>
    public static string Hold(string progression, int octave, int tone, int stepsPerBar = 8)
    {
        var steps = new List<string>();
        foreach (var (chord, length) in Spans(progression, stepsPerBar))
        {
            var tones = Tones(chord);
            steps.Add(Note(tones[Math.Min(tone, tones.Count - 1)], octave));
            steps.AddRange(Enumerable.Repeat("-", length - 1));
        }

        return string.Join(' ', steps);
    }

    /// <summary>A bar's rhythm repeated: drum lines ("x . x .").</summary>
    public static string Repeat(string bar, int bars) => string.Join(' ', Enumerable.Repeat(bar.Trim(), bars));

    /// <summary>A note name for semitones above C in <paramref name="octave"/>, carrying into the octaves above: 14 in octave 3 is "D4".</summary>
    public static string Note(int semitones, int octave)
    {
        var (shift, index) = (Math.DivRem(semitones, 12) is var (q, r) && r < 0 ? (q - 1, r + 12) : (q, r));
        return Names[index] + (octave + shift).ToString(CultureInfo.InvariantCulture);
    }

    private static int Degree(IReadOnlyList<int> tones, string token) => token switch
    {
        "1" => tones[0],
        "3" => tones[Math.Min(1, tones.Count - 1)],
        "5" => tones[tones.Count > 2 ? 2 : tones.Count - 1],
        "7" => tones.Count > 3 ? tones[3] : tones[0] + 12,
        "8" => tones[0] + 12,
        "10" => tones[Math.Min(1, tones.Count - 1)] + 12,
        "-5" => tones[tones.Count > 2 ? 2 : tones.Count - 1] - 12,
        _ => throw new FormatException($"'{token}' is not a chord tone (use 1, 3, 5, 7, 8, 10, -5, '.' or '-')."),
    };
}
