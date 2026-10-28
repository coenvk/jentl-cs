using Jentl.Core.Audio;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class AudioMixerTests
{
    private static readonly TimeSpan Ms50 = TimeSpan.FromMilliseconds(50);
    private readonly AudioMixer _mixer = new();

    [Fact]
    public void ASoundsGain_IsItsBusTimesTheMaster()
    {
        _mixer.Master.Volume = 0.5f;
        _mixer[AudioMixer.Effects].Volume = 0.8f;

        _mixer.GainOf(AudioMixer.Effects).Should().BeApproximately(0.4f, 1e-6f);
        _mixer.GainOf(AudioMixer.Music).Should().Be(0.5f);

        _mixer[AudioMixer.Effects].Muted = true;
        _mixer.GainOf(AudioMixer.Effects).Should().Be(0);
        _mixer.Master.Muted = true;
        _mixer.GainOf(AudioMixer.Music).Should().Be(0);
    }

    [Fact]
    public void Buses_AreMadeWhenFirstNamed_AndVolumesStayInRange()
    {
        _mixer["engines"].Volume = 3;

        _mixer["engines"].Volume.Should().Be(1);
        _mixer.Buses.Select(b => b.Name).Should().Contain(["effects", "music", "voice", "ui", "engines"]);
    }

    [Fact]
    public void AVoiceLine_DucksTheMusic_ThenItComesBackUp()
    {
        _mixer.AddDucking(AudioMixer.Voice, AudioMixer.Music, level: 0.3f);

        _mixer.Played(AudioMixer.Voice, TimeSpan.FromSeconds(1));
        Advance(TimeSpan.FromMilliseconds(300));
        _mixer[AudioMixer.Music].Ducked.Should().BeApproximately(0.3f, 0.001f, "four a second takes it down within a quarter second");
        _mixer.GainOf(AudioMixer.Effects).Should().Be(1, "other buses are left alone");

        Advance(TimeSpan.FromMilliseconds(800));
        _mixer[AudioMixer.Music].Ducked.Should().BeLessThan(1, "it fades back up rather than jumping");
        Advance(TimeSpan.FromMilliseconds(300));
        _mixer[AudioMixer.Music].Ducked.Should().Be(1);
    }

    [Fact]
    public void OverlappingDucks_HoldTheLowest_UntilTheLastEnds()
    {
        var music = _mixer[AudioMixer.Music];
        music.Duck(0.5f, TimeSpan.FromSeconds(2));
        music.Duck(0.2f, TimeSpan.FromSeconds(0.5));

        Advance(TimeSpan.FromMilliseconds(400));
        music.Ducked.Should().BeApproximately(0.2f, 0.001f);
        Advance(TimeSpan.FromMilliseconds(600));
        music.Ducked.Should().BeApproximately(0.5f, 0.001f);
        music.Gain.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void Place_IsFainterFurtherAway_AndPannedToItsSide()
    {
        var listener = new Vector2(100, 100);

        AudioMixer.Place(listener, listener, 200).Should().Be((1f, 0f));
        var right = AudioMixer.Place(new Vector2(200, 100), listener, 200);
        right.Volume.Should().BeApproximately(0.25f, 1e-6f);
        right.Pan.Should().BeApproximately(0.8f, 1e-6f);
        AudioMixer.Place(new Vector2(60, 100), listener, 200).Pan.Should().BeLessThan(0);
        AudioMixer.Place(new Vector2(100, 400), listener, 200).Volume.Should().Be(0);
    }

    [Fact]
    public void ThePlayersOldSwitches_AreItsBuses()
    {
        var sounds = SoundPlayer.Silent();

        sounds.MusicVolume.Should().Be(0.6f);
        sounds.Volume = 0.3f;
        sounds.Enabled = false;
        sounds.MusicEnabled = false;

        sounds.Mixer[AudioMixer.Effects].Volume.Should().Be(0.3f);
        sounds.Mixer[AudioMixer.Effects].Muted.Should().BeTrue();
        sounds.Mixer[AudioMixer.Music].Muted.Should().BeTrue();
        FluentActions.Invoking(() => sounds.PlayAt("missing", Vector2.Zero, Vector2.One, 10)).Should().NotThrow();
    }

    private void Advance(TimeSpan time)
    {
        for (var t = TimeSpan.Zero; t < time; t += Ms50)
        {
            _mixer.Update(Ms50);
        }
    }
}
