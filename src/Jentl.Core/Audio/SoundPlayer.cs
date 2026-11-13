using Jentl.Core.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace Jentl.Core.Audio;

/// <summary>
/// The game's sound: named effects (from <c>.wav</c> files or rendered by <see cref="Synth"/>) and looping
/// music that fades in, out and across between tracks. Effects and music have their own switch and volume.
/// Without a working audio device every call is a no-op, so a missing sound card never crashes the game.
/// </summary>
/// <remarks>Call <see cref="Update"/> once per frame for fades.</remarks>
public sealed class SoundPlayer : IDisposable
{
    // A browser only lets a page make sound after the player has touched it, so music that could not start
    // is tried again now and then.
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, SoundEffect> _sounds;
    private readonly Dictionary<string, Func<short[]>> _tracks = [];
    private readonly Dictionary<string, SoundEffect> _rendered = [];
    private readonly Dictionary<string, Task<short[]>> _rendering = [];
    private Playing? _music;
    private (string Name, TimeSpan Fade)? _waiting;
    private Playing? _fading;
    private bool _suspended;
    private TimeSpan _retry;

    private SoundPlayer(Dictionary<string, SoundEffect> sounds, bool isAvailable)
    {
        _sounds = sounds;
        IsAvailable = isAvailable;
        Mixer[AudioMixer.Music].Volume = 0.6f;
    }

    /// <summary>False when audio could not be initialised; sounds are then silently skipped.</summary>
    public bool IsAvailable { get; }

    /// <summary>The buses sounds play on, with their volumes, mutes and ducking.</summary>
    public AudioMixer Mixer { get; } = new();

    /// <summary>
    /// Holds all sound while the app is away (a phone app in the background): the music pauses where it is and
    /// carries on when released, and effects asked for meanwhile stay silent. The player's own settings are untouched.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value)
            {
                return;
            }

            _suspended = value;
            foreach (var playing in new[] { _music, _fading })
            {
                if (playing is null)
                {
                    continue;
                }

                if (value)
                {
                    playing.Instance.Pause();
                }
                else if (playing.Instance.State == SoundState.Paused)
                {
                    playing.Instance.Resume();
                }
            }
        }
    }

    /// <summary>Whether sound effects play: the effects bus not muted.</summary>
    public bool Enabled
    {
        get => !Mixer[AudioMixer.Effects].Muted;
        set => Mixer[AudioMixer.Effects].Muted = !value;
    }

    /// <summary>Sound effects volume, 0 to 1: the effects bus's.</summary>
    public float Volume
    {
        get => Mixer[AudioMixer.Effects].Volume;
        set => Mixer[AudioMixer.Effects].Volume = value;
    }

    /// <summary>Whether music plays; turning it off silences the current track at once.</summary>
    public bool MusicEnabled
    {
        get => !Mixer[AudioMixer.Music].Muted;
        set
        {
            Mixer[AudioMixer.Music].Muted = !value;
            ApplyMusicVolume();
        }
    }

    /// <summary>Music volume, 0 to 1: the music bus's.</summary>
    public float MusicVolume
    {
        get => Mixer[AudioMixer.Music].Volume;
        set
        {
            Mixer[AudioMixer.Music].Volume = value;
            ApplyMusicVolume();
        }
    }

    /// <summary>The track playing (or fading in), if any.</summary>
    public string? CurrentMusic => _waiting?.Name ?? _music?.Name;

    /// <summary>A player with no sounds: every <see cref="Play"/> does nothing. For tests and games without audio.</summary>
    public static SoundPlayer Silent() => new([], isAvailable: false);

    /// <summary>Loads <c>&lt;directory&gt;/&lt;name&gt;.wav</c> for each name (16-bit PCM or float), relative to the game root.</summary>
    public static SoundPlayer Load(string directory, params string[] names)
    {
        var sounds = new Dictionary<string, SoundEffect>();
        try
        {
            foreach (var name in names)
            {
                using var stream = GameFiles.Open($"{directory}/{name}.wav");
                sounds[name] = SoundEffect.FromStream(stream);
            }

            return new SoundPlayer(sounds, isAvailable: true);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            foreach (var sound in sounds.Values)
            {
                sound.Dispose();
            }

            Console.Error.WriteLine($"Audio unavailable, continuing without sound: {e.Message}");
            return new SoundPlayer([], isAvailable: false);
        }
    }

    /// <summary>Adds an effect from samples (e.g. rendered by <see cref="Synth"/>); replaces one of the same name.</summary>
    public void Add(string name, short[] samples, int sampleRate = Synth.SampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!IsAvailable)
        {
            return;
        }

        if (_sounds.Remove(name, out var old))
        {
            old.Dispose();
        }

        _sounds[name] = new SoundEffect(Synth.ToPcm(samples), sampleRate, AudioChannels.Mono);
    }

    /// <summary>
    /// Adds a music track; <paramref name="render"/> makes its samples the first time it is played (or
    /// <see cref="PrepareMusic"/>d), away from the game's thread so a long song doesn't hold up a frame.
    /// </summary>
    public void AddMusic(string name, Func<short[]> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        _tracks[name] = render;
    }

    /// <summary>
    /// Puts a new <paramref name="render"/> in place of track <paramref name="name"/>'s, dropping what was rendered of
    /// the old one (stopping it at once if it was playing): a music maker's song after each change.
    /// </summary>
    public void ReplaceMusic(string name, Func<short[]> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        if (_music?.Name == name)
        {
            _music.Instance.Stop();
            _music.Instance.Dispose();
            _music = null;
        }

        if (_fading?.Name == name)
        {
            _fading.Instance.Stop();
            _fading.Instance.Dispose();
            _fading = null;
        }

        if (_waiting?.Name == name)
        {
            _waiting = null;
        }

        if (_rendered.Remove(name, out var old))
        {
            old.Dispose();
        }

        _rendering.Remove(name);
        _tracks[name] = render;
    }

    /// <summary>Whether track <paramref name="name"/> is playing now, rather than waiting to be rendered.</summary>
    public bool IsPlayingMusic(string name) => _music?.Name == name && _waiting is null;

    /// <summary>Whether an effect or a track of that name exists.</summary>
    public bool Has(string name) => _sounds.ContainsKey(name) || _tracks.ContainsKey(name);

    /// <summary>
    /// Plays effect <paramref name="name"/> on <paramref name="bus"/> at <paramref name="volume"/> times the bus's gain,
    /// <paramref name="pan"/> from -1 (left) to 1 (right) and <paramref name="pitch"/> from -1 (an octave down) to 1
    /// (an octave up). The bus's ducking rules start with it.
    /// </summary>
    public void Play(string name, float volume = 1f, float pan = 0, float pitch = 0, string bus = AudioMixer.Effects)
    {
        var gain = Mixer.GainOf(bus);
        if (_suspended || gain <= 0 || !_sounds.TryGetValue(name, out var sound))
        {
            return;
        }

        try
        {
            sound.Play(Math.Clamp(volume * gain, 0f, 1f), Math.Clamp(pitch, -1, 1), Math.Clamp(pan, -1, 1));
            Mixer.Played(bus, sound.Duration);
        }
        catch (InstancePlayLimitException)
        {
            // Too many sounds at once; dropping one is fine.
        }
    }

    /// <summary>
    /// Plays effect <paramref name="name"/> as heard from <paramref name="listener"/>: fainter the further
    /// <paramref name="source"/> is, silent from <paramref name="range"/> away, and panned to its side
    /// (<see cref="AudioMixer.Place"/>).
    /// </summary>
    public void PlayAt(string name, Vector2 source, Vector2 listener, float range, float volume = 1f, string bus = AudioMixer.Effects)
    {
        var (near, pan) = AudioMixer.Place(source, listener, range);
        if (near > 0)
        {
            Play(name, volume * near, pan, bus: bus);
        }
    }

    /// <summary>Starts rendering track <paramref name="name"/> in the background, so it is ready when it is played.</summary>
    public void PrepareMusic(string name)
    {
        if (IsAvailable && !_rendered.ContainsKey(name) && !_rendering.ContainsKey(name) && _tracks.TryGetValue(name, out var render))
        {
            _rendering[name] = Task.Run(render);
        }
    }

    /// <summary>
    /// Loops track <paramref name="name"/>, fading it in over <paramref name="fade"/> while the current one fades
    /// out. Asking for the track already playing changes nothing, so every screen can simply name its music. A
    /// track not rendered yet is rendered in the background while the current one plays on, and starts when ready
    /// (in <see cref="Update"/>).
    /// </summary>
    public void PlayMusic(string name, TimeSpan? fade = null)
    {
        if (!IsAvailable || CurrentMusic == name || !_tracks.ContainsKey(name))
        {
            return;
        }

        var time = fade ?? TimeSpan.FromSeconds(1);
        if (!_rendered.ContainsKey(name))
        {
            PrepareMusic(name);
            _waiting = (name, time);
            return;
        }

        _waiting = null;
        Begin(name, time);
    }

    private void Begin(string name, TimeSpan time)
    {
        var effect = _rendered[name];
        FadeOut(time);
        var instance = effect.CreateInstance();
        instance.IsLooped = true;
        _music = new Playing(name, instance, Level: 0, Target: 1, Speed: Speed(time));
        ApplyMusicVolume();
        if (!_suspended)
        {
            Start(_music);
        }
    }

    /// <summary>Fades the music out and stops it.</summary>
    public void StopMusic(TimeSpan? fade = null)
    {
        _waiting = null;
        FadeOut(fade ?? TimeSpan.FromSeconds(1));
        _music = null;
    }

    /// <summary>Moves fades on; call once per frame.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (_suspended)
        {
            return;
        }

        Mixer.Update(elapsed);
        Collect();
        var seconds = (float)elapsed.TotalSeconds;
        if (_music is { } music)
        {
            _music = music with { Level = Math.Min(music.Target, music.Level + music.Speed * seconds) };
            _retry -= elapsed;
            if (_retry <= TimeSpan.Zero && _music.Instance.State != SoundState.Playing)
            {
                _retry = RetryEvery;
                Start(_music);
            }
        }

        if (_fading is { } fading)
        {
            _fading = fading with { Level = Math.Max(0, fading.Level - fading.Speed * seconds) };
            if (_fading.Level <= 0)
            {
                _fading.Instance.Stop();
                _fading.Instance.Dispose();
                _fading = null;
            }
        }

        ApplyMusicVolume();
    }

    public void Dispose()
    {
        _music?.Instance.Dispose();
        _fading?.Instance.Dispose();
        foreach (var sound in _sounds.Values.Concat(_rendered.Values))
        {
            sound.Dispose();
        }
    }

    /// <summary>Takes in the tracks finished rendering, and starts the one waited for when it is ready.</summary>
    private void Collect()
    {
        foreach (var (name, task) in _rendering.Where(r => r.Value.IsCompleted).ToList())
        {
            _rendering.Remove(name);

            // A song that fails to render is a bug in its writing: let it surface here, on the game's thread.
            _rendered[name] = new SoundEffect(Synth.ToPcm(task.GetAwaiter().GetResult()), Synth.SampleRate, AudioChannels.Mono);
        }

        if (_waiting is { } waiting && _rendered.ContainsKey(waiting.Name))
        {
            _waiting = null;
            Begin(waiting.Name, waiting.Fade);
        }
    }

    private static float Speed(TimeSpan fade) => fade <= TimeSpan.Zero ? float.MaxValue : (float)(1 / fade.TotalSeconds);

    private static void Start(Playing playing)
    {
        try
        {
            playing.Instance.Play();
        }
        catch (Exception e) when (e is InstancePlayLimitException or InvalidOperationException)
        {
            // Not allowed yet (a browser waiting for the first touch); Update tries again.
        }
    }

    private void FadeOut(TimeSpan fade)
    {
        if (_fading is { } old)
        {
            old.Instance.Stop();
            old.Instance.Dispose();
        }

        _fading = _music is { } current ? current with { Speed = Speed(fade) } : null;
    }

    private void ApplyMusicVolume()
    {
        var volume = Mixer.GainOf(AudioMixer.Music);
        if (_music is { } music)
        {
            music.Instance.Volume = Math.Clamp(music.Level * volume, 0, 1);
        }

        if (_fading is { } fading)
        {
            fading.Instance.Volume = Math.Clamp(fading.Level * volume, 0, 1);
        }
    }

    private sealed record Playing(string Name, SoundEffectInstance Instance, float Level, float Target, float Speed);
}
