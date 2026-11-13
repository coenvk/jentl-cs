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

    /// <summary>
    /// A plucked string (Karplus-Strong): a burst of noise ringing round a loop the length of one period, losing its
    /// highs as it goes, as a harp, lute or guitar string does. <see cref="Instrument.Decay"/> is how long the string
    /// rings (to a thousandth), <see cref="Instrument.Brightness"/> how hard and near the bridge it is plucked;
    /// vibrato and slides do not apply.
    /// </summary>
    Pluck,

    /// <summary>
    /// Frequency modulation: a sine whose phase is bent by a second sine at <see cref="Instrument.Ratio"/> times the
    /// pitch, <see cref="Instrument.Index"/> deep. Whole ratios give brass and electric pianos, odd ones bells.
    /// </summary>
    Fm,

    /// <summary>
    /// Harmonics added up, as loud as <see cref="Instrument.Partials"/> says (the first the pitch itself), the higher
    /// ones fading faster by <see cref="Instrument.PartialDecay"/>: organs, pianos, reeds.
    /// </summary>
    Partials,

    /// <summary>Voices singing "ah": harmonics shaped by the formants of the vowel. Best with unison and a slow attack.</summary>
    Choir,
}

/// <summary>
/// How a voice sounds: a waveform shaped by an envelope (attack, decay to a sustain level, release; in
/// seconds), with optional vibrato (semitones of wobble) and a pitch slide over each note (semitones, e.g. -24
/// for a kick drum's thump). The init-only properties shape the newer waves and colour any of them: several
/// detuned copies (an ensemble), a low-pass filter, breath noise. <see cref="Instruments"/> has ready-made ones.
/// </summary>
public sealed record Instrument(
    Wave Wave,
    float Volume = 0.4f,
    float Attack = 0.005f,
    float Decay = 0.1f,
    float Sustain = 0.6f,
    float Release = 0.08f,
    float Vibrato = 0,
    float Slide = 0)
{
    /// <summary>How many copies of the sound play together, spread over <see cref="Detune"/>: strings and choirs.</summary>
    public int Unison { get; init; } = 1;

    /// <summary>Cents between the lowest and highest unison copy.</summary>
    public float Detune { get; init; }

    /// <summary>A low-pass filter's cutoff in Hz, softening the sound; 0 for none.</summary>
    public float Cutoff { get; init; }

    /// <summary>How far the cutoff opens with the envelope (1 doubles it at full level): brass brightening as it swells.</summary>
    public float CutoffEnvelope { get; init; }

    /// <summary>The <see cref="Wave.Fm"/> modulator's frequency as a multiple of the pitch.</summary>
    public float Ratio { get; init; } = 1;

    /// <summary>The <see cref="Wave.Fm"/> modulation depth: 0 is a pure sine, 1 to 5 ever brighter.</summary>
    public float Index { get; init; }

    /// <summary>
    /// Seconds for the <see cref="Wave.Fm"/> depth to fall away after the strike (a bell's clang dying to a hum); 0
    /// has it follow the envelope instead, brighter as the note swells.
    /// </summary>
    public float IndexDecay { get; init; }

    /// <summary>Loudness of each harmonic for <see cref="Wave.Partials"/>.</summary>
    public IReadOnlyList<float>? Partials { get; init; }

    /// <summary>How much faster each harmonic above the first fades, per second.</summary>
    public float PartialDecay { get; init; }

    /// <summary>Noise mixed in with the tone, following its envelope: a flute's air, a drum's rattle.</summary>
    public float Breath { get; init; }

    /// <summary>For <see cref="Wave.Pluck"/>: 0 a soft, dark pluck, 1 a bright, hard one.</summary>
    public float Brightness { get; init; } = 0.5f;

    /// <summary>
    /// Whether the decay falls away as a struck or plucked thing does (quickly at first, then lingering) rather than
    /// in a straight line: pianos, bells, drums.
    /// </summary>
    public bool Ring { get; init; }
}

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

    /// <summary>How much of a room's echo is heard: 0 none, 0.3 a hall, 0.6 a cathedral.</summary>
    public float Reverb { get; init; }

    /// <summary>
    /// How late every second step comes, as a share of a step: 0 is straight, a third the lilt of a shuffle (two to
    /// one), a half as far as it goes. The steps are swung in pairs, so the beats themselves stay on time.
    /// </summary>
    public float Swing { get; init; }

    /// <summary>When a step starts, in steps from the start: the off steps a little late when <see cref="Swing"/>ing.</summary>
    public double StepTime(int step) => step + (step % 2 != 0 ? Math.Clamp(Swing, 0, 0.5f) : 0);
}

/// <summary>
/// A small synthesizer that renders <see cref="Tune"/>s to 16-bit mono samples, so games get music and sound
/// effects without audio files. Everything is computed, so the same tune always renders the same samples.
/// </summary>
public static class Synth
{
    public const int SampleRate = 22050;

    private const int TableSize = 4096;

    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static readonly float[] SineTable = [.. Enumerable.Range(0, TableSize + 1).Select(i => MathF.Sin(2 * MathF.PI * i / TableSize))];

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
    /// Renders <paramref name="tune"/>. A looping tune wraps each note's release (and the room's echo) round to the
    /// start, so the end flows into the beginning without a click; otherwise the samples run on until the last
    /// release, and the echo, ends.
    /// </summary>
    public static short[] Render(Tune tune, bool loop = false, int sampleRate = SampleRate)
    {
        ArgumentNullException.ThrowIfNull(tune);
        var stepSamples = tune.StepSeconds * sampleRate;
        var length = (int)Math.Round(tune.Steps * stepSamples);
        var echo = tune.Reverb > 0 ? 3.5 : 0;
        var tail = loop ? 0 : (int)((tune.Voices.Max(v => v.Instrument.Release) + echo) * sampleRate) + 1;
        var mix = new float[length + tail];
        var noise = new Random(1);
        foreach (var voice in tune.Voices)
        {
            var position = 0;
            foreach (var step in Parse(voice.Steps))
            {
                if (step.Frequency > 0)
                {
                    var start = (int)Math.Round(tune.StepTime(position) * stepSamples);
                    var held = (tune.StepTime(position + step.Length) - tune.StepTime(position)) * stepSamples;
                    Note(mix, start, held, step.Frequency, voice.Instrument, sampleRate, loop ? length : 0, noise);
                }

                position += step.Length;
            }
        }

        if (tune.Reverb > 0)
        {
            Reverberate(mix, tune.Reverb, sampleRate, loop);
        }

        // A soft limiter instead of hard clipping when voices pile up.
        var samples = new short[mix.Length];
        for (var i = 0; i < mix.Length; i++)
        {
            samples[i] = (short)(MathF.Tanh(mix[i]) * short.MaxValue * 0.9f);
        }

        return samples;
    }

    /// <summary>
    /// Scales <paramref name="samples"/> to an even loudness (<paramref name="loudness"/> as a root mean square of
    /// full scale), so songs of different sizes play at one level; never so far that the peaks clip.
    /// </summary>
    public static short[] Normalise(short[] samples, float loudness = 0.14f)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            return samples;
        }

        var sum = 0.0;
        var peak = 1;
        foreach (var sample in samples)
        {
            sum += (double)sample * sample;
            peak = Math.Max(peak, Math.Abs((int)sample));
        }

        var rms = Math.Sqrt(sum / samples.Length);
        var gain = rms <= 0 ? 1 : Math.Min(loudness * short.MaxValue / rms, 0.95 * short.MaxValue / peak);
        return [.. samples.Select(s => (short)Math.Round(s * gain))];
    }

    /// <summary>The samples as a WAV file (16-bit mono PCM), for saving or sharing what was made.</summary>
    public static byte[] ToWav(short[] samples, int sampleRate = SampleRate)
    {
        var pcm = ToPcm(samples);
        using var stream = new MemoryStream(44 + pcm.Length);
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + pcm.Length);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }

        return stream.ToArray();
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

    /// <summary>A sine of a phase in cycles, from a table: the newer waves sum many, so they need them cheap.</summary>
    private static float Sin(double cycles)
    {
        var position = (cycles - Math.Floor(cycles)) * TableSize;
        var index = (int)position;
        var fraction = (float)(position - index);
        return SineTable[index] + ((SineTable[index + 1] - SineTable[index]) * fraction);
    }

#pragma warning disable CA5394 // Sound, not security relevant.
    private static float NextNoise(Random noise) => (float)(noise.NextDouble() * 2 - 1);
#pragma warning restore CA5394

    private static void Note(float[] mix, int start, double held, double frequency, Instrument instrument, int sampleRate, int wrap, Random noise)
    {
        var release = instrument.Release * sampleRate;
        var total = (int)(held + release);
        var unison = Math.Max(1, instrument.Unison);
        var spread = new double[unison];
        var phases = new double[unison];
        var modulators = new double[unison];
        var strings = instrument.Wave == Wave.Pluck ? new PluckedString[unison] : null;
        for (var u = 0; u < unison; u++)
        {
            var cents = unison == 1 ? 0 : instrument.Detune * ((u / (double)(unison - 1)) - 0.5);
            spread[u] = Math.Pow(2, cents / 1200);

            // Copies starting together would sound as one loud one for a moment, so they start apart.
            phases[u] = unison == 1 ? 0 : u * 0.381966;
            if (strings is not null)
            {
                strings[u] = new PluckedString(frequency * spread[u], sampleRate, instrument.Brightness, instrument.Decay, noise);
            }
        }

        var harmonics = instrument.Wave switch
        {
            Wave.Partials => Normalised(instrument.Partials ?? [1f]),
            Wave.Choir => Vowel(frequency, sampleRate),
            _ => null,
        };
        var gain = 1 / MathF.Sqrt(unison);
        var (low, lower) = (0f, 0f);
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
            float value;
            if (unison == 1 && instrument.Wave <= Wave.Noise)
            {
                // The first waves, exactly as they always sounded.
                var before = phase;
                phase = (phase + hz / sampleRate) % 1;
                value = instrument.Wave switch
                {
                    Wave.Square => phase < 0.5 ? 1f : -1f,
                    Wave.Pulse => phase < 0.25 ? 1f : -1f,
                    Wave.Triangle => (float)(4 * Math.Abs(phase - 0.5) - 1),
                    Wave.Sine => (float)Math.Sin(2 * Math.PI * phase),
                    Wave.Saw => (float)(2 * phase - 1),
                    _ => phase < before ? noiseValue = NextNoise(noise) : noiseValue,
                };
            }
            else
            {
                value = 0;
                for (var u = 0; u < unison; u++)
                {
                    var step = hz * spread[u] / sampleRate;
                    var before = phases[u];
                    phases[u] = (phases[u] + step) % 1;
                    value += instrument.Wave switch
                    {
                        Wave.Square => phases[u] < 0.5 ? 1f : -1f,
                        Wave.Pulse => phases[u] < 0.25 ? 1f : -1f,
                        Wave.Triangle => (float)(4 * Math.Abs(phases[u] - 0.5) - 1),
                        Wave.Sine => Sin(phases[u]),
                        Wave.Saw => (float)(2 * phases[u] - 1),
                        Wave.Noise => phases[u] < before ? noiseValue = NextNoise(noise) : noiseValue,
                        Wave.Pluck => strings![u].Next(),
                        Wave.Fm => Fm(instrument, phases[u], ref modulators[u], step, t, envelope),
                        _ => Additive(harmonics!, phases[u], instrument.PartialDecay, t, step),
                    };
                }

                value *= gain;
            }

            if (instrument.Breath > 0)
            {
                value += instrument.Breath * NextNoise(noise);
            }

            if (instrument.Cutoff > 0)
            {
                // Two one-pole low-passes in a row: gentle, and stable at any cutoff.
                var cutoff = instrument.Cutoff * (1 + (instrument.CutoffEnvelope * (float)envelope));
                var a = 1 - MathF.Exp(-2 * MathF.PI * Math.Min(cutoff, sampleRate * 0.45f) / sampleRate);
                low += a * (value - low);
                lower += a * (low - lower);
                value = lower;
            }

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

    private static float Fm(Instrument instrument, double phase, ref double modulator, double step, double t, double envelope)
    {
        modulator = (modulator + (step * instrument.Ratio)) % 1;
        var depth = instrument.Index * (instrument.IndexDecay > 0 ? Math.Exp(-t / instrument.IndexDecay) : envelope);
        return Sin(phase + (depth * Sin(modulator) / (2 * Math.PI)));
    }

    private static float Additive(float[] harmonics, double phase, float decay, double t, double step)
    {
        var sum = 0f;
        for (var k = 0; k < harmonics.Length && step * (k + 1) < 0.5; k++)
        {
            if (harmonics[k] != 0)
            {
                var fade = decay > 0 && k > 0 ? (float)Math.Exp(-t * decay * k) : 1;
                sum += harmonics[k] * fade * Sin(phase * (k + 1));
            }
        }

        return sum;
    }

    private static float[] Normalised(IReadOnlyList<float> partials)
    {
        var total = partials.Sum(MathF.Abs);
        return [.. partials.Select(p => total > 0 ? p / total : 0)];
    }

    /// <summary>The harmonics of a voice singing "ah" at <paramref name="frequency"/>: the vowel's formants pick out those near them.</summary>
    private static float[] Vowel(double frequency, int sampleRate)
    {
        var count = Math.Max(1, Math.Min(48, (int)(Math.Min(4000, sampleRate * 0.45) / frequency)));
        static double Peak(double hz, double centre, double width) => Math.Exp(-Math.Pow((hz - centre) / width, 2));
        var amplitudes = new float[count];
        for (var k = 0; k < count; k++)
        {
            var hz = frequency * (k + 1);
            var formants = Peak(hz, 730, 160) + (0.6 * Peak(hz, 1090, 180)) + (0.25 * Peak(hz, 2440, 260)) + 0.06;
            amplitudes[k] = (float)(formants / Math.Sqrt(k + 1));
        }

        return Normalised(amplitudes);
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

    private static double Level(double t, Instrument i)
    {
        if (t < i.Attack)
        {
            return t / Math.Max(0.0001, i.Attack);
        }

        if (i.Wave == Wave.Pluck)
        {
            // The string rings down by itself; the decay is its ringing.
            return 1;
        }

        var since = t - i.Attack;
        if (i.Ring)
        {
            return i.Sustain + ((1 - i.Sustain) * Math.Exp(-5 * since / Math.Max(0.0001, i.Decay)));
        }

        return since < i.Decay ? 1 - (1 - i.Sustain) * since / Math.Max(0.0001, i.Decay) : i.Sustain;
    }

    /// <summary>
    /// A room's echo on the whole mix (a small Freeverb: parallel damped combs, then all-passes to smear them). A
    /// loop is run through the room twice, the first time only to fill it, so the echo of the end sounds over the start.
    /// </summary>
    private static void Reverberate(float[] mix, float amount, int sampleRate, bool loop)
    {
        var room = new Room(sampleRate);
        if (loop)
        {
            foreach (var sample in mix)
            {
                room.Process(sample);
            }
        }

        for (var i = 0; i < mix.Length; i++)
        {
            mix[i] += amount * room.Process(mix[i]);
        }
    }

    private sealed class Room
    {
        private const float Feedback = 0.92f;
        private const float Damping = 0.2f;

        // Freeverb's delays, in samples at 44.1 kHz.
        private static readonly int[] CombDelays = [1116, 1188, 1277, 1356, 1422, 1491];
        private static readonly int[] PassDelays = [556, 441, 341];

        private readonly float[][] _combs;
        private readonly float[] _stored;
        private readonly float[][] _passes;
        private readonly int[] _combAt;
        private readonly int[] _passAt;

        public Room(int sampleRate)
        {
            int Scaled(int samples) => Math.Max(1, samples * sampleRate / 44100);
            _combs = [.. CombDelays.Select(d => new float[Scaled(d)])];
            _passes = [.. PassDelays.Select(d => new float[Scaled(d)])];
            _stored = new float[_combs.Length];
            _combAt = new int[_combs.Length];
            _passAt = new int[_passes.Length];
        }

        public float Process(float input)
        {
            var x = input * 0.15f;
            var sum = 0f;
            for (var c = 0; c < _combs.Length; c++)
            {
                var buffer = _combs[c];
                var y = buffer[_combAt[c]];
                _stored[c] = (y * (1 - Damping)) + (_stored[c] * Damping);
                buffer[_combAt[c]] = x + (_stored[c] * Feedback);
                _combAt[c] = (_combAt[c] + 1) % buffer.Length;
                sum += y;
            }

            for (var p = 0; p < _passes.Length; p++)
            {
                var buffer = _passes[p];
                var stored = buffer[_passAt[p]];
                buffer[_passAt[p]] = sum + (stored * 0.5f);
                sum = stored - sum;
                _passAt[p] = (_passAt[p] + 1) % buffer.Length;
            }

            return sum;
        }
    }

    /// <summary>One string of a <see cref="Wave.Pluck"/>: a loop of samples one period long, averaged as it goes round.</summary>
    private sealed class PluckedString
    {
        private readonly float[] _ring;
        private readonly double _delay;
        private readonly float _loss;
        private readonly float _stretch;
        private readonly int _burst;
        private int _n;
        private float _input;
        private float _output;

        public PluckedString(double frequency, int sampleRate, float brightness, float ringTime, Random noise)
        {
            // Each time round, the loop averages neighbouring samples, (1 - s) of one and s of the next, which
            // dulls the highs. Plain averaging (s = 1/2) would also kill high notes long before their ring time,
            // so s shrinks until the fundamental keeps what it should; the rest of the loss is a plain factor.
            var target = Math.Pow(10, -3 / (Math.Max(0.05, ringTime) * frequency));
            var half = Math.Sin(Math.PI * frequency / sampleRate);
            var room = (1 - (target * target)) / (4 * half * half);
            var stretch = room >= 0.25 ? 0.5 : (1 - Math.Sqrt(1 - (4 * room))) / 2;
            var kept = Math.Sqrt(1 - (4 * stretch * (1 - stretch) * half * half));
            _stretch = (float)stretch;
            _loss = (float)Math.Min(1, target / kept);

            // The averaging delays by s samples, so the loop is that much shorter than the period.
            _delay = Math.Max(2, (sampleRate / frequency) - stretch);
            _ring = new float[(int)_delay + 4];
            _burst = (int)Math.Ceiling(_delay) + 1;

            // The pluck: noise, smoothed for a softer one, with its average taken out so it doesn't thump.
            var smoothing = 0.12f + (0.88f * Math.Clamp(brightness, 0, 1));
            var y = 0f;
            for (var i = 0; i < _burst; i++)
            {
                y += smoothing * (NextNoise(noise) - y);
                _ring[i] = y;
            }

            var mean = _ring.Take(_burst).Average();
            var peak = Math.Max(0.0001f, _ring.Take(_burst).Max(v => MathF.Abs(v - mean)));
            for (var i = 0; i < _burst; i++)
            {
                _ring[i] = (_ring[i] - mean) / peak;
            }
        }

        public float Next()
        {
            float y;
            if (_n < _burst)
            {
                y = _ring[_n];
            }
            else
            {
                y = _loss * (((1 - _stretch) * Read(_n - _delay)) + (_stretch * Read(_n - _delay - 1)));
                _ring[_n % _ring.Length] = y;
            }

            _n++;

            // A DC blocker: what the loop keeps of any offset would otherwise sit under the note.
            _output = y - _input + (0.995f * _output);
            _input = y;
            return _output;
        }

        private float Read(double position)
        {
            var index = (int)Math.Floor(position);
            var fraction = (float)(position - index);
            var a = _ring[index % _ring.Length];
            var b = _ring[(index + 1) % _ring.Length];
            return a + ((b - a) * fraction);
        }
    }
}
