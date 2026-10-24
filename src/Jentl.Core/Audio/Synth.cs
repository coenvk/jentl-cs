using System.Globalization;

namespace Jentl.Core.Audio;

public enum Wave
{
    Square,

    /// <summary>A thin square (a quarter on), brighter and reedier.</summary>
    Pulse,
    Triangle,
    Sine,
    Saw,

    /// <summary>White noise, for drums and hits; the note's pitch is ignored.</summary>
    Noise,
}

/// <summary>
/// How a voice sounds: a waveform shaped by an envelope (attack, decay to a sustain level, release; in
/// seconds), with optional vibrato (semitones of wobble) and a pitch slide over each note (semitones, e.g. -24
/// for a kick drum's thump).
/// </summary>
public sealed record Instrument(
    Wave Wave,
    float Volume = 0.4f,
    float Attack = 0.005f,
    float Decay = 0.1f,
    float Sustain = 0.6f,
    float Release = 0.08f,
    float Vibrato = 0,
    float Slide = 0);

/// <summary>
/// One line of music: steps separated by spaces. A note ("C4", "F#3", "Bb2") starts a note, "-" holds the
/// previous one for another step, "." is a rest, and "x" strikes the instrument at a fixed pitch (drums).
/// "|" marks bars and is ignored.
/// </summary>
public sealed record Voice(Instrument Instrument, string Steps);

/// <summary>A piece of music: voices played together, <paramref name="StepsPerBeat"/> steps to a beat at <paramref name="Tempo"/> beats a minute.</summary>
public sealed record Tune(int Tempo, IReadOnlyList<Voice> Voices, int StepsPerBeat = 2)
{
    public Tune(int tempo, params Voice[] voices)
        : this(tempo, (IReadOnlyList<Voice>)voices)
    {
    }

    public double StepSeconds => 60.0 / Tempo / StepsPerBeat;

    /// <summary>The longest voice's steps.</summary>
    public int Steps => Voices.Max(v => Synth.Parse(v.Steps).Sum(s => s.Length));
}

/// <summary>
/// A small synthesizer that renders <see cref="Tune"/>s to 16-bit mono samples, so games get music and sound
/// effects without audio files. Everything is computed, so the same tune always renders the same samples.
/// </summary>
public static class Synth
{
    public const int SampleRate = 22050;

    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>A note's step in a voice: its frequency (0 for a rest) and how many steps it lasts.</summary>
    public readonly record struct NoteStep(double Frequency, int Length);

    /// <summary>The frequency of a note name such as "A4" (440 Hz), "C#5" or "Bb2"; "x" is a fixed 8 kHz.</summary>
    public static double Frequency(string note)
    {
        ArgumentException.ThrowIfNullOrEmpty(note);
        if (note == "x")
        {
            return 8000;
        }

        var accidental = note.Length > 2 && note[1] is '#' or 'b' ? note[1] : '\0';
        var name = note[..1].ToUpperInvariant() + (accidental == '#' ? "#" : "");
        var index = Array.IndexOf(Names, name);
        if (index < 0 || !int.TryParse(note.AsSpan(accidental == '\0' ? 1 : 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave))
        {
            throw new FormatException($"'{note}' is not a note.");
        }

        if (accidental == 'b')
        {
            index--;
        }

        var semitones = index - 9 + (octave - 4) * 12;
        return 440 * Math.Pow(2, semitones / 12.0);
    }

    /// <summary>The notes of a voice, each with the steps it is held (rests are frequency 0).</summary>
    public static IReadOnlyList<NoteStep> Parse(string steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var result = new List<NoteStep>();
        foreach (var token in steps.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "|":
                    break;
                case "-" when result.Count > 0:
                    result[^1] = result[^1] with { Length = result[^1].Length + 1 };
                    break;
                case "-" or ".":
                    result.Add(new NoteStep(0, 1));
                    break;
                default:
                    result.Add(new NoteStep(Frequency(token), 1));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Renders <paramref name="tune"/>. A looping tune wraps each note's release round to the start, so the
    /// end flows into the beginning without a click; otherwise the samples run on until the last release ends.
    /// </summary>
    public static short[] Render(Tune tune, bool loop = false, int sampleRate = SampleRate)
    {
        ArgumentNullException.ThrowIfNull(tune);
        var stepSamples = tune.StepSeconds * sampleRate;
        var length = (int)Math.Round(tune.Steps * stepSamples);
        var tail = loop ? 0 : (int)(tune.Voices.Max(v => v.Instrument.Release) * sampleRate) + 1;
        var mix = new float[length + tail];
        var noise = new Random(1);
        foreach (var voice in tune.Voices)
        {
            var position = 0;
            foreach (var step in Parse(voice.Steps))
            {
                if (step.Frequency > 0)
                {
                    var start = (int)Math.Round(position * stepSamples);
                    var held = step.Length * stepSamples;
                    Note(mix, start, held, step.Frequency, voice.Instrument, sampleRate, loop ? length : 0, noise);
                }

                position += step.Length;
            }
        }

        // A soft limiter instead of hard clipping when voices pile up.
        var samples = new short[mix.Length];
        for (var i = 0; i < mix.Length; i++)
        {
            samples[i] = (short)(MathF.Tanh(mix[i]) * short.MaxValue * 0.9f);
        }

        return samples;
    }

    /// <summary>16-bit little-endian PCM bytes, the form MonoGame's <c>SoundEffect</c> takes.</summary>
    public static byte[] ToPcm(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < bytes.Length; i += 2)
            {
                (bytes[i], bytes[i + 1]) = (bytes[i + 1], bytes[i]);
            }
        }

        return bytes;
    }

    private static void Note(float[] mix, int start, double held, double frequency, Instrument instrument, int sampleRate, int wrap, Random noise)
    {
        var release = instrument.Release * sampleRate;
        var total = (int)(held + release);
        var phase = 0.0;
        var noiseValue = 0f;
        for (var i = 0; i < total; i++)
        {
            var t = i / (double)sampleRate;
            var envelope = Envelope(t, held / sampleRate, instrument);
            if (envelope <= 0 && i > held)
            {
                break;
            }

            // Pitch: a slide over the held part of the note, plus a gentle 5 Hz vibrato.
            var slide = instrument.Slide * Math.Min(1, i / Math.Max(1, held));
            var wobble = instrument.Vibrato * Math.Sin(2 * Math.PI * 5 * t);
            var hz = frequency * Math.Pow(2, (slide + wobble) / 12);
            var before = phase;
            phase = (phase + hz / sampleRate) % 1;
            var value = instrument.Wave switch
            {
                Wave.Square => phase < 0.5 ? 1f : -1f,
                Wave.Pulse => phase < 0.25 ? 1f : -1f,
                Wave.Triangle => (float)(4 * Math.Abs(phase - 0.5) - 1),
                Wave.Sine => (float)Math.Sin(2 * Math.PI * phase),
                Wave.Saw => (float)(2 * phase - 1),
#pragma warning disable CA5394 // Sound, not security relevant.
                _ => phase < before ? noiseValue = (float)(noise.NextDouble() * 2 - 1) : noiseValue,
#pragma warning restore CA5394
            };

            var index = start + i;
            if (wrap > 0)
            {
                index %= wrap;
            }
            else if (index >= mix.Length)
            {
                break;
            }

            mix[index] += value * (float)envelope * instrument.Volume;
        }
    }

    private static double Envelope(double t, double held, Instrument i)
    {
        if (t >= held)
        {
            var level = Level(held, i);
            return level * Math.Max(0, 1 - (t - held) / Math.Max(0.0001, i.Release));
        }

        return Level(t, i);
    }

    private static double Level(double t, Instrument i) =>
        t < i.Attack ? t / Math.Max(0.0001, i.Attack)
        : t < i.Attack + i.Decay ? 1 - (1 - i.Sustain) * (t - i.Attack) / Math.Max(0.0001, i.Decay)
        : i.Sustain;
}
