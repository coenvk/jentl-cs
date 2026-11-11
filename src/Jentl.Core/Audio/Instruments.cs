namespace Jentl.Core.Audio;

/// <summary>
/// Ready-made <see cref="Instrument"/>s beyond the bare waves: the orchestra and folk band of a fantasy game's
/// soundtrack (strings, harp, lute, flutes, reeds, brass, choir, bells, drums), and a few chiptune voices. Their
/// volumes are matched, so a tune can swap one for another; <c>with</c> changes any of them.
/// </summary>
public static class Instruments
{
    // Plucked strings.
    public static Instrument Harp { get; } = new(Wave.Pluck, Volume: 0.42f, Attack: 0.002f, Decay: 2.6f, Release: 0.5f) { Brightness = 0.5f };

    public static Instrument Lute { get; } = new(Wave.Pluck, Volume: 0.36f, Attack: 0.002f, Decay: 1.3f, Release: 0.15f) { Brightness = 0.8f, Unison = 2, Detune = 7 };

    public static Instrument Guitar { get; } = new(Wave.Pluck, Volume: 0.4f, Attack: 0.002f, Decay: 1.9f, Release: 0.2f) { Brightness = 0.6f };

    public static Instrument Harpsichord { get; } = new(Wave.Pluck, Volume: 0.3f, Attack: 0.001f, Decay: 1.6f, Release: 0.1f) { Brightness = 1, Unison = 2, Detune = 4 };

    public static Instrument Pizzicato { get; } = new(Wave.Pluck, Volume: 0.5f, Attack: 0.002f, Decay: 0.45f, Release: 0.08f) { Brightness = 0.35f };

    public static Instrument UprightBass { get; } = new(Wave.Pluck, Volume: 0.7f, Attack: 0.003f, Decay: 1.2f, Release: 0.1f) { Brightness = 0.25f };

    // Bowed strings.
    public static Instrument Strings { get; } = new(Wave.Saw, Volume: 0.12f, Attack: 0.18f, Decay: 0.3f, Sustain: 0.85f, Release: 0.45f, Vibrato: 0.1f)
    {
        Unison = 3, Detune = 14, Cutoff = 2400, CutoffEnvelope = 0.4f,
    };

    public static Instrument Violin { get; } = new(Wave.Saw, Volume: 0.13f, Attack: 0.06f, Decay: 0.2f, Sustain: 0.85f, Release: 0.2f, Vibrato: 0.16f)
    {
        Unison = 2, Detune = 5, Cutoff = 3200, CutoffEnvelope = 0.3f,
    };

    public static Instrument Cello { get; } = new(Wave.Saw, Volume: 0.17f, Attack: 0.08f, Decay: 0.3f, Sustain: 0.85f, Release: 0.3f, Vibrato: 0.12f)
    {
        Unison = 2, Detune = 8, Cutoff = 1300,
    };

    // Winds.
    public static Instrument Flute { get; } = new(Wave.Partials, Volume: 0.32f, Attack: 0.06f, Decay: 0.2f, Sustain: 0.8f, Release: 0.15f, Vibrato: 0.12f)
    {
        Partials = [1, 0.22f, 0.08f], Breath = 0.05f, Cutoff = 3400,
    };

    public static Instrument Recorder { get; } = new(Wave.Partials, Volume: 0.3f, Attack: 0.03f, Decay: 0.1f, Sustain: 0.85f, Release: 0.08f, Vibrato: 0.04f)
    {
        Partials = [1, 0.12f, 0.2f, 0.04f], Breath = 0.035f, Cutoff = 3800,
    };

    public static Instrument PanPipes { get; } = new(Wave.Sine, Volume: 0.3f, Attack: 0.05f, Decay: 0.25f, Sustain: 0.7f, Release: 0.25f, Vibrato: 0.08f)
    {
        Breath = 0.14f, Cutoff = 2600, Unison = 2, Detune = 3,
    };

    public static Instrument Clarinet { get; } = new(Wave.Partials, Volume: 0.26f, Attack: 0.04f, Decay: 0.2f, Sustain: 0.8f, Release: 0.1f, Vibrato: 0.05f)
    {
        Partials = [1, 0.04f, 0.55f, 0.03f, 0.32f, 0.02f, 0.16f, 0.01f, 0.08f], Cutoff = 3200,
    };

    public static Instrument Oboe { get; } = new(Wave.Partials, Volume: 0.24f, Attack: 0.03f, Decay: 0.2f, Sustain: 0.8f, Release: 0.1f, Vibrato: 0.1f)
    {
        Partials = [0.5f, 1, 0.85f, 0.55f, 0.35f, 0.22f, 0.12f, 0.06f], Cutoff = 3600,
    };

    public static Instrument Accordion { get; } = new(Wave.Pulse, Volume: 0.07f, Attack: 0.04f, Decay: 0.1f, Sustain: 0.9f, Release: 0.08f)
    {
        Unison = 2, Detune = 14, Cutoff = 2400,
    };

    // Brass.
    public static Instrument Horn { get; } = new(Wave.Fm, Volume: 0.26f, Attack: 0.08f, Decay: 0.3f, Sustain: 0.8f, Release: 0.25f, Vibrato: 0.04f)
    {
        Index = 1.6f, Cutoff = 1500, CutoffEnvelope = 0.4f,
    };

    public static Instrument Trumpet { get; } = new(Wave.Fm, Volume: 0.2f, Attack: 0.04f, Decay: 0.2f, Sustain: 0.8f, Release: 0.12f, Vibrato: 0.06f)
    {
        Index = 3, Cutoff = 2800, CutoffEnvelope = 0.6f,
    };

    public static Instrument Brass { get; } = new(Wave.Saw, Volume: 0.12f, Attack: 0.06f, Decay: 0.3f, Sustain: 0.75f, Release: 0.2f, Vibrato: 0.04f)
    {
        Unison = 3, Detune = 8, Cutoff = 900, CutoffEnvelope = 1.6f,
    };

    // Voices and keys.
    public static Instrument Choir { get; } = new(Wave.Choir, Volume: 0.42f, Attack: 0.3f, Decay: 0.3f, Sustain: 0.9f, Release: 0.6f, Vibrato: 0.14f)
    {
        Unison = 3, Detune = 18,
    };

    public static Instrument Organ { get; } = new(Wave.Partials, Volume: 0.24f, Attack: 0.01f, Decay: 0.05f, Sustain: 1, Release: 0.08f)
    {
        Partials = [1, 0.8f, 0, 0.6f, 0, 0.3f, 0, 0.4f],
    };

    public static Instrument Piano { get; } = new(Wave.Partials, Volume: 0.42f, Attack: 0.002f, Decay: 3, Sustain: 0, Release: 0.25f)
    {
        Partials = [1, 0.55f, 0.32f, 0.22f, 0.12f, 0.08f, 0.05f], PartialDecay = 0.9f, Ring = true, Unison = 2, Detune = 3,
    };

    public static Instrument ElectricPiano { get; } = new(Wave.Fm, Volume: 0.32f, Attack: 0.002f, Decay: 2.2f, Sustain: 0, Release: 0.3f)
    {
        Index = 1.8f, IndexDecay = 0.3f, Ring = true,
    };

    // Struck.
    public static Instrument Celesta { get; } = new(Wave.Fm, Volume: 0.28f, Attack: 0.002f, Decay: 1.3f, Sustain: 0, Release: 0.8f)
    {
        Ratio = 4, Index = 1.2f, IndexDecay = 0.12f, Ring = true,
    };

    public static Instrument Bells { get; } = new(Wave.Fm, Volume: 0.22f, Attack: 0.002f, Decay: 3, Sustain: 0, Release: 2)
    {
        Ratio = 3.5f, Index = 2.5f, IndexDecay = 0.6f, Ring = true,
    };

    public static Instrument Marimba { get; } = new(Wave.Fm, Volume: 0.42f, Attack: 0.002f, Decay: 0.5f, Sustain: 0, Release: 0.2f)
    {
        Ratio = 4, Index = 1.5f, IndexDecay = 0.04f, Ring = true,
    };

    // Drums: tuned ones take notes ("D2"), the rest "x".
    public static Instrument Timpani { get; } = new(Wave.Fm, Volume: 0.6f, Attack: 0.003f, Decay: 1.6f, Sustain: 0, Release: 0.8f, Slide: -0.3f)
    {
        Ratio = 1.5f, Index = 1, IndexDecay = 0.15f, Ring = true, Cutoff = 1200, Breath = 0.15f,
    };

    public static Instrument FrameDrum { get; } = new(Wave.Sine, Volume: 0.55f, Attack: 0.002f, Decay: 0.3f, Sustain: 0, Release: 0.1f, Slide: -9)
    {
        Ring = true, Breath = 0.35f, Cutoff = 1400,
    };

    public static Instrument Kick { get; } = new(Wave.Sine, Volume: 0.5f, Decay: 0.12f, Sustain: 0, Release: 0.02f, Slide: -24);

    public static Instrument Snare { get; } = new(Wave.Noise, Volume: 0.16f, Attack: 0.001f, Decay: 0.16f, Sustain: 0, Release: 0.05f)
    {
        Ring = true, Cutoff = 5000,
    };

    public static Instrument Shaker { get; } = new(Wave.Noise, Volume: 0.06f, Attack: 0.004f, Decay: 0.05f, Sustain: 0, Release: 0.02f)
    {
        Ring = true, Cutoff = 7000,
    };

    public static Instrument Cymbal { get; } = new(Wave.Noise, Volume: 0.07f, Attack: 0.002f, Decay: 1.4f, Sustain: 0, Release: 0.6f)
    {
        Ring = true,
    };

    // Chiptune.
    public static Instrument ChipLead { get; } = new(Wave.Pulse, Volume: 0.15f, Decay: 0.15f, Sustain: 0.5f, Release: 0.12f, Vibrato: 0.08f);

    public static Instrument ChipArp { get; } = new(Wave.Square, Volume: 0.06f, Decay: 0.08f, Sustain: 0.3f, Release: 0.05f);

    public static Instrument ChipBass { get; } = new(Wave.Triangle, Volume: 0.42f, Decay: 0.2f, Sustain: 0.6f, Release: 0.06f);

    public static Instrument ChipHat { get; } = new(Wave.Noise, Volume: 0.05f, Decay: 0.03f, Sustain: 0, Release: 0.01f);

    /// <summary>Every preset by name ("Harp", "Choir"), for a music maker's instrument list.</summary>
    public static IReadOnlyDictionary<string, Instrument> All { get; } = new Dictionary<string, Instrument>
    {
        ["Harp"] = Harp, ["Lute"] = Lute, ["Guitar"] = Guitar, ["Harpsichord"] = Harpsichord, ["Pizzicato"] = Pizzicato,
        ["Upright bass"] = UprightBass, ["Strings"] = Strings, ["Violin"] = Violin, ["Cello"] = Cello,
        ["Flute"] = Flute, ["Recorder"] = Recorder, ["Pan pipes"] = PanPipes, ["Clarinet"] = Clarinet, ["Oboe"] = Oboe,
        ["Accordion"] = Accordion, ["Horn"] = Horn, ["Trumpet"] = Trumpet, ["Brass"] = Brass, ["Choir"] = Choir,
        ["Organ"] = Organ, ["Piano"] = Piano, ["Electric piano"] = ElectricPiano, ["Celesta"] = Celesta, ["Bells"] = Bells,
        ["Marimba"] = Marimba, ["Timpani"] = Timpani, ["Frame drum"] = FrameDrum, ["Kick"] = Kick, ["Snare"] = Snare,
        ["Shaker"] = Shaker, ["Cymbal"] = Cymbal, ["Chip lead"] = ChipLead, ["Chip arpeggio"] = ChipArp,
        ["Chip bass"] = ChipBass, ["Chip hat"] = ChipHat,
    };
}
