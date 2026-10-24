using Jentl.Core.Audio;

namespace Jentl.Core.Tests;

public class SynthTests
{
    private static readonly Instrument Beep = new(Wave.Square, Volume: 0.5f, Release: 0.05f);

    [Theory]
    [InlineData("A4", 440)]
    [InlineData("A5", 880)]
    [InlineData("C4", 261.626)]
    [InlineData("C#4", 277.183)]
    [InlineData("Bb2", 116.541)]
    [InlineData("x", 8000)]
    public void Frequency_FollowsEqualTemperamentFromA440(string note, double hz) =>
        Synth.Frequency(note).Should().BeApproximately(hz, 0.01);

    [Fact]
    public void Frequency_RejectsNonsense()
    {
        var act = () => Synth.Frequency("H4");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_HoldsNotes_RestsAndIgnoresBars()
    {
        var steps = Synth.Parse("C4 - - | . E4 . -");

        steps.Select(s => s.Length).Should().Equal(3, 1, 1, 2);
        steps.Select(s => s.Frequency > 0).Should().Equal(true, false, true, false);
    }

    [Fact]
    public void Render_LastsTheTunesSteps_AndALoopEndsExactlyThere()
    {
        // 120 beats a minute, two steps a beat: a quarter second a step.
        var tune = new Tune(120, new Voice(Beep, "C4 - E4 G4"));

        Synth.Render(tune, loop: true).Should().HaveCount(Synth.SampleRate);
        Synth.Render(tune).Length.Should().BeGreaterThan(Synth.SampleRate, "a one-off keeps its last note's release");
    }

    [Fact]
    public void Render_IsTheSameEveryTime_AndNotSilent()
    {
        var tune = new Tune(140, new Voice(Beep, "C4 E4 G4 C5"), new Voice(new Instrument(Wave.Noise, Decay: 0.03f, Sustain: 0), "x . x ."));

        var first = Synth.Render(tune, loop: true);

        first.Should().Equal(Synth.Render(tune, loop: true));
        first.Max(s => Math.Abs((int)s)).Should().BeGreaterThan(5000);
    }

    [Fact]
    public void Rests_AreSilent_AndNotesStopAfterTheirRelease()
    {
        var tune = new Tune(60, new Voice(Beep with { Release = 0.1f }, "A4 . ."));

        var samples = Synth.Render(tune, loop: true);
        var step = Synth.SampleRate / 2;

        samples.Take(step).Max(s => Math.Abs((int)s)).Should().BeGreaterThan(1000);
        samples.Skip(step + Synth.SampleRate / 5).Should().OnlyContain(s => s == 0, "the release lasts a tenth of a second");
    }

    [Fact]
    public void ManyLoudVoices_AreLimited_NotClipped()
    {
        var loud = new Instrument(Wave.Square, Volume: 1f);
        var tune = new Tune(120, [.. Enumerable.Range(0, 6).Select(_ => new Voice(loud, "C4 - - -"))]);

        Synth.Render(tune, loop: true).Max(s => Math.Abs((int)s)).Should().BeLessThan(short.MaxValue);
    }

    [Fact]
    public void ToPcm_WritesLittleEndianSixteenBit()
    {
        Synth.ToPcm([0x1234, -2]).Should().Equal(0x34, 0x12, 0xFE, 0xFF);
    }
}
