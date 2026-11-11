using Jentl.Core.Audio;

namespace Jentl.Core.Tests;

public class CompositionTests
{
    private static Composition TwoBars()
    {
        var song = new Composition { Title = "Test", Tempo = 120, Bars = 2, StepsPerBeat = 2, Reverb = 0 };
        var melody = new Channel { Instrument = "Flute" };
        melody.Patterns[0].Notes.AddRange([new ComposedNote(0, 72, 2), new ComposedNote(4, 76)]);
        melody.Arrangement.AddRange([0, 0]);
        var chords = new Channel { Instrument = "Piano", Volume = 0.5f };
        chords.Patterns[0].Notes.AddRange([new ComposedNote(0, 60, 8), new ComposedNote(0, 64, 8), new ComposedNote(0, 67, 8)]);
        chords.Arrangement.AddRange([-1, 0]);
        song.Channels.AddRange([melody, chords]);
        return song;
    }

    [Theory]
    [InlineData(60, "C4")]
    [InlineData(61, "C#4")]
    [InlineData(69, "A4")]
    [InlineData(48, "C3")]
    [InlineData(11, "B-1")]
    public void NoteName_CountsFromMidisMiddleC(int pitch, string name) => Composition.NoteName(pitch).Should().Be(name);

    [Fact]
    public void ToTune_PlaysEachChannelsPatternsInTheirBars_AChordAsSeveralVoices()
    {
        var tune = TwoBars().ToTune();

        tune.Voices.Should().HaveCount(4, "one for the flute, three for the piano's chord");
        tune.Voices[0].Steps.Should().Be("C5 - . . E5 . . . C5 - . . E5 . . .");
        tune.Voices.Skip(1).Select(v => v.Steps).Should().Equal(
            ". . . . . . . . G4 - - - - - - -",
            ". . . . . . . . E4 - - - - - - -",
            ". . . . . . . . C4 - - - - - - -");
        tune.Voices[1].Instrument.Volume.Should().BeApproximately(Instruments.Piano.Volume * 0.5f, 0.0001f);
        tune.Steps.Should().Be(16);
    }

    [Fact]
    public void ToTune_FromABar_StartsThere()
    {
        var tune = TwoBars().ToTune(from: 1);

        tune.Steps.Should().Be(8);
        tune.Voices[0].Steps.Should().Be("C5 - . . E5 . . .");
    }

    [Fact]
    public void ANoteRingingPastTheEnd_IsCutThere()
    {
        var song = TwoBars();
        song.Channels[0].Patterns[0].Notes.Add(new ComposedNote(7, 79, 20));

        song.ToTune().Voices[0].Steps.Split(' ').Should().HaveCount(16);
    }

    [Fact]
    public void ToJson_AndBack_KeepsEverything()
    {
        var song = TwoBars();

        var back = Composition.FromJson(song.ToJson());

        back.ToJson().Should().Be(song.ToJson());
        back.ToTune().Voices.Select(v => v.Steps).Should().Equal(song.ToTune().Voices.Select(v => v.Steps));
    }

    [Fact]
    public void FromJson_RejectsWhatIsNotASong()
    {
        var act = () => Composition.FromJson("{\"channels\": [{\"patterns\": [[[1]]]}]}");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToCode_WritesATuneAGameCanPlay()
    {
        var code = TwoBars().ToCode();

        code.Should().Contain("new Voice(Instruments.Flute, \"C5 - . . E5 . . . | C5 - . . E5 . . .\")");
        code.Should().Contain("new Voice(Instruments.Piano with { Volume = 0.21f }");
        code.Should().Contain("Reverb = 0f");
    }

    [Fact]
    public void AnEmptySong_StillRendersItsLength()
    {
        var song = new Composition { Bars = 2, Tempo = 120, StepsPerBeat = 2, Reverb = 0 };

        Synth.Render(song.ToTune(), loop: true).Should().HaveCount(Synth.SampleRate * 2 * 2);
    }

    [Fact]
    public void ToWav_HasAHeaderAndTheSamples()
    {
        var wav = Synth.ToWav([1, 2, 3]);

        wav.Should().HaveCount(44 + 6);
        System.Text.Encoding.ASCII.GetString(wav, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(wav, 36, 4).Should().Be("data");
        BitConverter.ToInt16(wav, 44).Should().Be(1);
    }
}
