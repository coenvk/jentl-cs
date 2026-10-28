namespace Jentl.Core.Input;

/// <summary>
/// One shake of the gamepad: the low-frequency (heavy) and high-frequency (light) motors from 0 to 1, for
/// <see cref="Duration"/>, dying away to nothing when <see cref="Fade"/> is on.
/// </summary>
public readonly record struct RumbleEffect(float Low, float High, TimeSpan Duration, bool Fade = true)
{
    /// <summary>A light tick: a card placed, a menu choice.</summary>
    public static RumbleEffect Tap { get; } = new(0, 0.25f, TimeSpan.FromMilliseconds(60));

    /// <summary>A soft knock: a crate pushed into place, landing.</summary>
    public static RumbleEffect Bump { get; } = new(0.35f, 0.2f, TimeSpan.FromMilliseconds(120));

    /// <summary>A hit taken or dealt.</summary>
    public static RumbleEffect Hit { get; } = new(0.7f, 0.5f, TimeSpan.FromMilliseconds(220));

    /// <summary>Something big: a crash, an explosion, a win.</summary>
    public static RumbleEffect Crash { get; } = new(1, 0.8f, TimeSpan.FromMilliseconds(500));
}

/// <summary>
/// Gamepad vibration. <see cref="Play(RumbleEffect)"/> starts an effect; effects that overlap add up to the strongest
/// of each motor. <see cref="Update"/> works out the motors' speeds every frame and sends them only when they change.
/// Players can turn it off (<see cref="Enabled"/>) or down (<see cref="Strength"/>): some find it uncomfortable or
/// cannot hold a shaking pad. <c>JentlGame.Rumble</c> drives the connected controller, and stops it while the window
/// is in the background.
/// </summary>
public sealed class Rumble(Action<float, float> setMotors)
{
    private readonly Action<float, float> _setMotors = setMotors ?? throw new ArgumentNullException(nameof(setMotors));
    private readonly List<(RumbleEffect Effect, TimeSpan Left)> _playing = [];
    private (float Low, float High) _sent;
    private float _strength = 1;

    /// <summary>Whether the pad vibrates at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How strongly, from 0 to 1: every effect's motors are scaled by it.</summary>
    public float Strength
    {
        get => _strength;
        set => _strength = Math.Clamp(value, 0, 1);
    }

    /// <summary>The motors' speeds sent last, low then high.</summary>
    public (float Low, float High) Motors => _sent;

    /// <summary>Whether any effect is still going.</summary>
    public bool IsPlaying => _playing.Count > 0;

    public void Play(RumbleEffect effect)
    {
        if (effect.Duration > TimeSpan.Zero)
        {
            _playing.Add((effect, effect.Duration));
        }
    }

    public void Play(float low, float high, TimeSpan duration, bool fade = true) => Play(new RumbleEffect(low, high, duration, fade));

    /// <summary>Ends every effect; the motors stop on the next <see cref="Update"/>.</summary>
    public void Stop() => _playing.Clear();

    /// <summary>
    /// Advances the effects by <paramref name="elapsed"/> and sets the motors. While <paramref name="quiet"/> (the game
    /// in the background, paused) the effects run on but the motors stay off.
    /// </summary>
    public void Update(TimeSpan elapsed, bool quiet = false)
    {
        float low = 0, high = 0;
        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            var (effect, left) = _playing[i];
            left -= elapsed;
            if (left <= TimeSpan.Zero)
            {
                _playing.RemoveAt(i);
                continue;
            }

            _playing[i] = (effect, left);
            var share = effect.Fade ? (float)(left / effect.Duration) : 1;
            low = Math.Max(low, effect.Low * share);
            high = Math.Max(high, effect.High * share);
        }

        var scale = Enabled && !quiet ? Strength : 0;
        var motors = (Math.Clamp(low * scale, 0, 1), Math.Clamp(high * scale, 0, 1));
        if (motors != _sent)
        {
            _sent = motors;
            _setMotors(motors.Item1, motors.Item2);
        }
    }
}
