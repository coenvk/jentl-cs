using Jentl.Core.Audio;

namespace Jentl.Core.Tests;

public class InstrumentTests
{
    public static TheoryData<string> Presets => [.. Instruments.All.Keys];

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryPreset_SoundsANote_AudiblyAndWithoutClipping(string name)
    {
        var instrument = Instruments.All[name];
        var samples = Synth.Render(new Tune(120, new Voice(instrument, "C4 - - -")));

        var peak = samples.Max(s => Math.Abs((int)s)) / (double)short.MaxValue;
        peak.Should().BeInRange(0.02, 0.75, "{0} should be heard over a band and leave room for the others", name);
    }

    [Theory]
    [InlineData(nameof(Instruments.Harp), 220)]
    [InlineData(nameof(Instruments.Harp), 440)]
    [InlineData(nameof(Instruments.Guitar), 880)]
    [InlineData(nameof(Instruments.Piano), 440)]
    [InlineData(nameof(Instruments.Flute), 440)]
    [InlineData(nameof(Instruments.Choir), 220)]
    [InlineData(nameof(Instruments.Horn), 220)]
    [InlineData(nameof(Instruments.Strings), 440)]
    public void TheNewWaves_PlayInTune(string name, double hz)
    {
        var instrument = typeof(Instruments).GetProperty(name)!.GetValue(null) as Instrument;
        var note = hz switch { 220 => "A3", 440 => "A4", _ => "A5" };
        var samples = Synth.Render(new Tune(60, new Voice(instrument!, $"{note} - -")));

        Pitch(samples, 0.25, 0.4).Should().BeApproximately(hz, hz * 0.01, "{0} should play {1}", name, note);
    }

    [Fact]
    public void APluckedString_RingsDown_DarkerAsItGoes()
    {
        var samples = Synth.Render(new Tune(60, new Voice(Instruments.Harp, "A4 - - - - - - -")));

        var early = Loudness(samples, 0.05, 0.25);
        var late = Loudness(samples, 2.0, 2.2);
        late.Should().BeLessThan(early * 0.5);
        late.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AFilter_TakesTheEdgeOff()
    {
        var saw = new Instrument(Wave.Saw, Volume: 0.3f, Sustain: 1);
        var bright = Synth.Render(new Tune(60, new Voice(saw, "A3 -")));
        var soft = Synth.Render(new Tune(60, new Voice(saw with { Cutoff = 500 }, "A3 -")));

        Roughness(soft).Should().BeLessThan(Roughness(bright) * 0.5);
    }

    [Fact]
    public void Unison_ThickensTheSound_WithoutMovingItsPitch()
    {
        var one = new Instrument(Wave.Saw, Volume: 0.2f, Sustain: 1);
        var three = one with { Unison = 3, Detune = 12 };
        var single = Synth.Render(new Tune(60, new Voice(one, "A3 - -")));
        var ensemble = Synth.Render(new Tune(60, new Voice(three, "A3 - -")));

        ensemble.Should().NotEqual(single);
        Pitch(ensemble, 0.2, 0.5).Should().BeApproximately(220, 3);
    }

    [Fact]
    public void Reverb_LeavesAnEchoAfterTheMusicStops_AndALoopKeepsItsLength()
    {
        var tune = new Tune(120, new Voice(Instruments.Pizzicato, "C5 . . ."));
        var dry = Synth.Render(tune);
        var wet = Synth.Render(tune with { Reverb = 0.4f });

        wet.Length.Should().BeGreaterThan(dry.Length);
        Loudness(wet, 1.0, 1.2).Should().BeGreaterThan(0.001);
        Loudness(wet, 3.0, wet.Length / (double)Synth.SampleRate).Should().BeLessThan(0.002, "the echo dies away before the end");
        Synth.Render(tune with { Reverb = 0.4f }, loop: true).Should().HaveCount(Synth.Render(tune, loop: true).Length);
    }

    [Fact]
    public void TheNewWaves_RenderTheSameEveryTime()
    {
        var tune = new Tune(100, new Voice(Instruments.Lute, "C4 E4 G4 C5"), new Voice(Instruments.Choir, "C3 - - -"), new Voice(Instruments.Snare, "x . x .")) { Reverb = 0.3f };

        Synth.Render(tune, loop: true).Should().Equal(Synth.Render(tune, loop: true));
    }

    [Fact]
    public void TheFirstWaves_SoundAsTheyAlwaysDid()
    {
        // A pulse, a triangle, a noise drum and a sliding sine with nothing new set: the same samples as before the
        // newer waves came (a checksum taken from the synthesizer as it was).
        var tune = new Tune(
            120,
            new Voice(Instruments.ChipLead, "E5 G5"),
            new Voice(Instruments.ChipBass, "C3 -"),
            new Voice(Instruments.ChipHat, "x x"),
            new Voice(Instruments.Kick, "C2 ."));
        long sum = 0;
        long i = 0;
        foreach (var sample in Synth.Render(tune, loop: true))
        {
            sum += sample * (++i % 97);
        }

        sum.Should().Be(-841653692);
    }

    [Fact]
    public void Normalise_BringsSongsToOneLevel_WithoutClipping()
    {
        var quiet = Synth.Render(new Tune(120, new Voice(Instruments.Flute with { Volume = 0.05f }, "A4 - - -")), loop: true);
        var loud = Synth.Render(new Tune(120, new Voice(Instruments.Brass with { Volume = 0.5f }, "A3 - - -")), loop: true);

        var (a, b) = (Synth.Normalise(quiet), Synth.Normalise(loud));

        Loudness(a, 0, 1).Should().BeApproximately(Loudness(b, 0, 1), 0.02);
        a.Concat(b).Max(s => Math.Abs((int)s)).Should().BeLessThanOrEqualTo((int)(0.95 * short.MaxValue) + 1);
    }

    /// <summary>The pitch between two times, by autocorrelation: the lag at which the sound best matches itself.</summary>
    private static double Pitch(short[] samples, double from, double to)
    {
        var start = (int)(from * Synth.SampleRate);
        var length = (int)((to - from) * Synth.SampleRate);
        double Match(int lag)
        {
            double sum = 0;
            for (var i = start; i < start + length; i++)
            {
                sum += samples[i] * (double)samples[i + lag];
            }

            return sum;
        }

        // The first peak after the correlation has dipped below zero that comes near the highest, refined between
        // its neighbours (later peaks, at two and three periods, are nearly as high).
        var lags = Enumerable.Range(0, 400).Select(Match).ToArray();
        var dip = Array.FindIndex(lags, v => v < 0);
        var highest = lags.Skip(dip).Max();
        var best = Enumerable.Range(dip + 1, lags.Length - dip - 2)
            .First(lag => lags[lag] >= 0.9 * highest && lags[lag] >= lags[lag - 1] && lags[lag] >= lags[lag + 1]);
        var (a, b, c) = (lags[best - 1], lags[best], lags[best + 1]);
        var shift = 0.5 * (a - c) / (a - (2 * b) + c);
        return Synth.SampleRate / (best + shift);
    }

    private static double Loudness(short[] samples, double from, double to) =>
        Math.Sqrt(samples.Skip((int)(from * Synth.SampleRate)).Take((int)((to - from) * Synth.SampleRate)).Average(s => (s / 32768.0) * (s / 32768.0)));

    /// <summary>How jagged the samples are: how sharply they bend from one to the next, against how loud they are.</summary>
    private static double Roughness(short[] samples)
    {
        var bends = Enumerable.Range(1, samples.Length - 2).Average(i => Math.Abs(samples[i + 1] - (2.0 * samples[i]) + samples[i - 1]));
        return bends / samples.Average(s => Math.Abs((double)s));
    }
}
