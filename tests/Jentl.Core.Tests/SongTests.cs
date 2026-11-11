using Jentl.Core.Audio;

namespace Jentl.Core.Tests;

public class SongTests
{
    private static readonly Instrument Tone = new(Wave.Square);

    [Theory]
    [InlineData("C", new[] { 0, 4, 7 })]
    [InlineData("Am", new[] { 9, 12, 16 })]
    [InlineData("G7", new[] { 7, 11, 14, 17 })]
    [InlineData("Bb", new[] { 10, 14, 17 })]
    [InlineData("F#m7", new[] { 6, 9, 13, 16 })]
    [InlineData("Dsus4", new[] { 2, 7, 9 })]
    public void Chords_KnowTheirTones(string chord, int[] tones) => Chords.Tones(chord).Should().Equal(tones);

    [Fact]
    public void Patterns_PlayOverEachChord()
    {
        Chords.Play("C | Am F", 2, "1 . 5 .").Should().Be("C2 . G2 . C2 . G2 . A2 . E3 . F2 . C3 .");
        Chords.Play("G7", 3, "1 3 5 7 8 10 - .").Should().Be("G3 B3 D4 F4 G4 B4 - .");
        Chords.Hold("C | G", 4, 1).Should().Be("E4 - - - - - - - B4 - - - - - - -");
        Chords.Note(-1, 4).Should().Be("B3");
        var bad = () => Chords.Tones("Cmaj9");
        bad.Should().Throw<FormatException>();
    }

    [Fact]
    public void ASong_JoinsItsSectionsInOrder_FillingRests()
    {
        var song = new Song(
            "Test",
            120,
            [new SongPart("lead", Tone), new SongPart("bass", Tone)],
            [
                new Section("a", 1, new Dictionary<string, string> { ["lead"] = "C4 D4", ["bass"] = Chords.Play("C", 2, "1 .") }),
                new Section("b", 1, new Dictionary<string, string> { ["lead"] = "E4 - - - - - - -" }),
            ],
            ["a", "b", "a"]);

        var tune = song.ToTune();

        tune.Steps.Should().Be(24);
        Synth.Parse(tune.Voices[0].Steps).Select(s => s.Length).Should().Equal(1, 1, 1, 1, 1, 1, 1, 1, 8, 1, 1, 1, 1, 1, 1, 1, 1);
        song.Duration.Should().Be(TimeSpan.FromSeconds(6));
        Synth.Render(tune, loop: true).Length.Should().Be((int)Math.Round(24 * tune.StepSeconds * Synth.SampleRate));
    }

    [Fact]
    public void ASong_RefusesAPartLongerThanItsSection()
    {
        var song = new Song("Long", 100, [new SongPart("lead", Tone)], [new Section("a", 1, new Dictionary<string, string> { ["lead"] = string.Join(' ', Enumerable.Repeat("C4", 9)) })], ["a"]);

        var act = () => song.ToTune();

        act.Should().Throw<ArgumentException>().WithMessage("Long: the lead in a has 9 steps, more than its 8.");
    }
}
