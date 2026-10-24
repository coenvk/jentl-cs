using Jentl.Core.Content;
using Microsoft.Xna.Framework.Audio;

namespace Jentl.Core.Audio;

/// <summary>
/// Plays short named sound effects loaded from <c>.wav</c> files. Without a working audio
/// device every call is a no-op, so a missing sound card never crashes the game.
/// </summary>
public sealed class SoundPlayer : IDisposable
{
    private readonly Dictionary<string, SoundEffect> _sounds;

    private SoundPlayer(Dictionary<string, SoundEffect> sounds, bool isAvailable)
    {
        _sounds = sounds;
        IsAvailable = isAvailable;
    }

    /// <summary>False when audio could not be initialised; sounds are then silently skipped.</summary>
    public bool IsAvailable { get; }

    public bool Enabled { get; set; } = true;

    /// <summary>Master volume, 0 to 1, applied to every sound.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Loads <c>&lt;directory&gt;/&lt;name&gt;.wav</c> for each name (16-bit PCM or float), relative to the game root.</summary>
    /// <summary>A player with no sounds: every <see cref="Play"/> does nothing. For tests and games without audio.</summary>
    public static SoundPlayer Silent() => new([], isAvailable: false);

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

    public void Dispose()
    {
        foreach (var sound in _sounds.Values)
        {
            sound.Dispose();
        }
    }
}
