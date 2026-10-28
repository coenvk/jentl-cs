using Microsoft.Xna.Framework;

namespace Jentl.Core.Audio;

/// <summary>A group of sounds with one volume and a mute switch: the effects, the music, voices, the UI's clicks.</summary>
public sealed class AudioBus(string name)
{
    private readonly List<(float Level, TimeSpan Left)> _ducks = [];
    private float _volume = 1;

    public string Name { get; } = name;

    /// <summary>The player's volume for the bus, 0 to 1.</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 1);
    }

    public bool Muted { get; set; }

    /// <summary>How far ducking has turned the bus down right now: 1 is not at all.</summary>
    public float Ducked { get; private set; } = 1;

    /// <summary>The bus's own gain: its volume, ducked, or 0 when muted.</summary>
    public float Gain => Muted ? 0 : Volume * Ducked;

    /// <summary>Holds the bus down at <paramref name="level"/> (of its volume) for <paramref name="time"/>; the lowest of several holds wins.</summary>
    public void Duck(float level, TimeSpan time) => _ducks.Add((Math.Clamp(level, 0, 1), time));

    /// <summary>Moves the ducking towards the lowest hold still on (or back up to 1), at most <paramref name="rate"/> a second.</summary>
    internal void Update(TimeSpan elapsed, float rate)
    {
        var target = 1f;
        for (var i = _ducks.Count - 1; i >= 0; i--)
        {
            var (level, left) = _ducks[i];
            left -= elapsed;
            if (left <= TimeSpan.Zero)
            {
                _ducks.RemoveAt(i);
                continue;
            }

            _ducks[i] = (level, left);
            target = Math.Min(target, level);
        }

        var step = rate * (float)elapsed.TotalSeconds;
        Ducked = Ducked < target ? Math.Min(target, Ducked + step) : Math.Max(target, Ducked - step);
    }
}

/// <summary>
/// Buses for the game's sound, under a <see cref="Master"/>: <see cref="Effects"/>, <see cref="Music"/>,
/// <see cref="Voice"/> and <see cref="Ui"/> to start with, and any other a game names. Players get a slider per bus; a
/// sound plays at its own volume times its bus's gain times the master's. Ducking rules turn one bus down while another
/// plays (the music under a voice line, so the words are heard), fading back up after.
/// </summary>
public sealed class AudioMixer
{
    public const string Effects = "effects";
    public const string Music = "music";
    public const string Voice = "voice";
    public const string Ui = "ui";

    private readonly Dictionary<string, AudioBus> _buses = [];
    private readonly List<(string When, string Duck, float Level)> _ducking = [];

    public AudioMixer()
    {
        foreach (var name in new[] { Effects, Music, Voice, Ui })
        {
            _buses[name] = new AudioBus(name);
        }
    }

    /// <summary>Over every bus: the game's overall volume and mute.</summary>
    public AudioBus Master { get; } = new("master");

    /// <summary>How fast ducking fades, in full volume a second (4: a quarter of a second from full to silent).</summary>
    public float DuckRate { get; set; } = 4;

    /// <summary>The bus of that name, made the first time it is asked for.</summary>
    public AudioBus this[string name]
    {
        get
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (!_buses.TryGetValue(name, out var bus))
            {
                bus = _buses[name] = new AudioBus(name);
            }

            return bus;
        }
    }

    public IEnumerable<AudioBus> Buses => _buses.Values;

    /// <summary>What a sound on <paramref name="bus"/> is multiplied by now: the bus's gain times the master's.</summary>
    public float GainOf(string bus) => Master.Gain * this[bus].Gain;

    /// <summary>While anything plays on <paramref name="when"/>, <paramref name="duck"/> is held down to <paramref name="level"/> of its volume.</summary>
    public void AddDucking(string when, string duck, float level = 0.35f) => _ducking.Add((when, duck, level));

    /// <summary>Tells the mixer a sound of <paramref name="duration"/> started on <paramref name="bus"/>, for the ducking rules.</summary>
    public void Played(string bus, TimeSpan duration)
    {
        foreach (var (when, duck, level) in _ducking)
        {
            if (when == bus)
            {
                this[duck].Duck(level, duration);
            }
        }
    }

    /// <summary>Moves the ducking fades on; <see cref="SoundPlayer.Update"/> calls it.</summary>
    public void Update(TimeSpan elapsed)
    {
        foreach (var bus in _buses.Values)
        {
            bus.Update(elapsed, DuckRate);
        }

        Master.Update(elapsed, DuckRate);
    }

    /// <summary>
    /// How a sound at <paramref name="source"/> is heard at <paramref name="listener"/> (the camera, the player): fainter
    /// with distance, silent from <paramref name="range"/> on, and panned to the side it is on, never fully, so a sound
    /// just off-centre is still heard in both ears.
    /// </summary>
    public static (float Volume, float Pan) Place(Vector2 source, Vector2 listener, float range)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range);
        var offset = source - listener;
        var near = Math.Clamp(1 - offset.Length() / range, 0, 1);
        return (near * near, Math.Clamp(offset.X / range * 2, -1, 1) * 0.8f);
    }
}
