using Jentl.Core.Content;
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
    private Playing? _music;
    private Playing? _fading;
    private TimeSpan _retry;
    private float _musicVolume = 0.6f;
    private bool _musicEnabled = true;

    private SoundPlayer(Dictionary<string, SoundEffect> sounds, bool isAvailable)
    {
        _sounds = sounds;
        IsAvailable = isAvailable;
    }

    /// <summary>False when audio could not be initialised; sounds are then silently skipped.</summary>
    public bool IsAvailable { get; }

    /// <summary>Whether sound effects play.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sound effects volume, 0 to 1.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Whether music plays; turning it off silences the current track at once.</summary>
    public bool MusicEnabled
    {
        get => _musicEnabled;
        set
        {
            _musicEnabled = value;
            ApplyMusicVolume();
        }
    }

    /// <summary>Music volume, 0 to 1.</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0, 1);
            ApplyMusicVolume();
        }
    }

    /// <summary>The track playing (or fading in), if any.</summary>
    public string? CurrentMusic => _music?.Name;

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

    /// <summary>Adds a music track; <paramref name="render"/> makes its samples the first time it is played.</summary>
    public void AddMusic(string name, Func<short[]> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        _tracks[name] = render;
    }

    /// <summary>Whether an effect or a track of that name exists.</summary>
    public bool Has(string name) => _sounds.ContainsKey(name) || _tracks.ContainsKey(name);

    public void Play(string name, float volume = 1f)
    {
        if (!Enabled || !_sounds.TryGetValue(name, out var sound))
        {
            return;
        }

        try
        {
            sound.Play(Math.Clamp(volume * Volume, 0f, 1f), 0f, 0f);
        }
        catch (InstancePlayLimitException)
        {
            // Too many sounds at once; dropping one is fine.
        }
    }

    /// <summary>
    /// Loops track <paramref name="name"/>, fading it in over <paramref name="fade"/> while the current one fades
    /// out. Asking for the track already playing changes nothing, so every screen can simply name its music.
    /// </summary>
    public void PlayMusic(string name, TimeSpan? fade = null)
    {
        if (!IsAvailable || _music?.Name == name || !_tracks.TryGetValue(name, out var render))
        {
            return;
        }

        var time = fade ?? TimeSpan.FromSeconds(1);
        if (!_rendered.TryGetValue(name, out var effect))
        {
            effect = new SoundEffect(Synth.ToPcm(render()), Synth.SampleRate, AudioChannels.Mono);
            _rendered[name] = effect;
        }

        FadeOut(time);
        var instance = effect.CreateInstance();
        instance.IsLooped = true;
        _music = new Playing(name, instance, Level: 0, Target: 1, Speed: Speed(time));
        ApplyMusicVolume();
        Start(_music);
    }

    /// <summary>Fades the music out and stops it.</summary>
    public void StopMusic(TimeSpan? fade = null)
    {
        FadeOut(fade ?? TimeSpan.FromSeconds(1));
        _music = null;
    }

    /// <summary>Moves fades on; call once per frame.</summary>
    public void Update(TimeSpan elapsed)
    {
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
        var volume = _musicEnabled ? _musicVolume : 0;
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
