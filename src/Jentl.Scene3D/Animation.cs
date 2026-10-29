using System.Numerics;

namespace Jentl.Scene3D;

/// <summary>What a channel of an animation moves.</summary>
public enum AnimationPath
{
    Translation,
    Rotation,
    Scale,
}

/// <summary>How a channel goes from one key to the next.</summary>
public enum Interpolation
{
    /// <summary>Evenly (rotations along the shortest arc).</summary>
    Linear,

    /// <summary>Holds each key until the next.</summary>
    Step,

    /// <summary>Along a smooth curve through the keys, by tangents stored with them (three values per key: in, value, out).</summary>
    CubicSpline,
}

/// <summary>
/// One property of one node over time: keys at <see cref="Times"/> (seconds, rising) with values (a position or scale
/// in XYZ, a rotation as a quaternion in XYZW). <see cref="Target"/> is an index into the nodes an
/// <see cref="AnimationPlayer"/> drives.
/// </summary>
public sealed class AnimationChannel
{
    public AnimationChannel(int target, AnimationPath path, IReadOnlyList<float> times, IReadOnlyList<Vector4> values, Interpolation interpolation = Interpolation.Linear)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(target);
        if (times.Count == 0)
        {
            throw new ArgumentException("A channel needs at least one key.", nameof(times));
        }

        if (values.Count != times.Count * (interpolation == Interpolation.CubicSpline ? 3 : 1))
        {
            throw new ArgumentException($"{times.Count} keys need {times.Count * (interpolation == Interpolation.CubicSpline ? 3 : 1)} values, not {values.Count}.", nameof(values));
        }

        for (var i = 1; i < times.Count; i++)
        {
            if (times[i] < times[i - 1])
            {
                throw new ArgumentException("Key times must rise.", nameof(times));
            }
        }

        (Target, Path, Times, Values, Interpolation) = (target, path, times, values, interpolation);
    }

    public int Target { get; }

    public AnimationPath Path { get; }

    public IReadOnlyList<float> Times { get; }

    public IReadOnlyList<Vector4> Values { get; }

    public Interpolation Interpolation { get; }

    public float Duration => Times[^1];

    /// <summary>A channel moving a node's position through <paramref name="keys"/>.</summary>
    public static AnimationChannel Move(int target, params (float Time, Vector3 Position)[] keys) =>
        new(target, AnimationPath.Translation, [.. keys.Select(k => k.Time)], [.. keys.Select(k => new Vector4(k.Position, 0))]);

    /// <summary>A channel turning a node through <paramref name="keys"/>.</summary>
    public static AnimationChannel Turn(int target, params (float Time, Quaternion Rotation)[] keys) =>
        new(target, AnimationPath.Rotation, [.. keys.Select(k => k.Time)], [.. keys.Select(k => new Vector4(k.Rotation.X, k.Rotation.Y, k.Rotation.Z, k.Rotation.W))]);

    /// <summary>A channel scaling a node through <paramref name="keys"/>.</summary>
    public static AnimationChannel Resize(int target, params (float Time, Vector3 Scale)[] keys) =>
        new(target, AnimationPath.Scale, [.. keys.Select(k => k.Time)], [.. keys.Select(k => new Vector4(k.Scale, 0))]);

    /// <summary>The value at <paramref name="time"/> seconds; before the first key the first, after the last the last.</summary>
    public Vector4 Sample(float time)
    {
        var cubic = Interpolation == Interpolation.CubicSpline;
        Vector4 ValueAt(int key) => Values[cubic ? (key * 3) + 1 : key];
        if (time <= Times[0])
        {
            return ValueAt(0);
        }

        if (time >= Times[^1])
        {
            return ValueAt(Times.Count - 1);
        }

        // The last key at or before the time.
        var (low, high) = (0, Times.Count - 1);
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            (low, high) = Times[middle] <= time ? (middle, high) : (low, middle);
        }

        var span = Times[high] - Times[low];
        var t = span > 0 ? (time - Times[low]) / span : 0;
        var rotation = Path == AnimationPath.Rotation;
        switch (Interpolation)
        {
            case Interpolation.Step:
                return ValueAt(low);
            case Interpolation.CubicSpline:
            {
                // Hermite: the tangents are per second, so they scale with the span between the keys.
                var (t2, t3) = (t * t, t * t * t);
                var value = (((2 * t3) - (3 * t2) + 1) * Values[(low * 3) + 1])
                    + ((t3 - (2 * t2) + t) * span * Values[(low * 3) + 2])
                    + (((-2 * t3) + (3 * t2)) * Values[(high * 3) + 1])
                    + ((t3 - t2) * span * Values[high * 3]);
                return rotation ? Vector4.Normalize(value) : value;
            }

            default:
                if (rotation)
                {
                    var (a, b) = (ValueAt(low), ValueAt(high));
                    var q = Quaternion.Slerp(new Quaternion(a.X, a.Y, a.Z, a.W), new Quaternion(b.X, b.Y, b.Z, b.W), t);
                    return new Vector4(q.X, q.Y, q.Z, q.W);
                }

                return Vector4.Lerp(ValueAt(low), ValueAt(high), t);
        }
    }

    internal void Apply(Node node, float time)
    {
        var value = Sample(time);
        switch (Path)
        {
            case AnimationPath.Translation:
                node.Position = new Vector3(value.X, value.Y, value.Z);
                break;
            case AnimationPath.Rotation:
                node.Rotation = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W));
                break;
            default:
                node.Scale = new Vector3(value.X, value.Y, value.Z);
                break;
        }
    }
}

/// <summary>A named animation: channels that move nodes together (a walk, a door opening). Its length is its last key.</summary>
public sealed class AnimationClip
{
    public AnimationClip(string name, IReadOnlyList<AnimationChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        Name = name;
        Channels = channels;
        Duration = channels.Count == 0 ? 0 : channels.Max(channel => channel.Duration);
    }

    public string Name { get; }

    public IReadOnlyList<AnimationChannel> Channels { get; }

    /// <summary>In seconds.</summary>
    public float Duration { get; }

    /// <summary>Poses <paramref name="nodes"/> as the clip has them at <paramref name="time"/> seconds.</summary>
    public void Apply(IReadOnlyList<Node> nodes, float time)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var channel in Channels)
        {
            if (channel.Target < nodes.Count)
            {
                channel.Apply(nodes[channel.Target], time);
            }
        }
    }

    public override string ToString() => $"{Name} ({Duration:0.##} s)";
}

/// <summary>
/// Plays clips on a set of nodes (a model's, by index, as <see cref="GltfModel.Instantiate"/> gives them): once or
/// round and round, faster or slower, and cross-fading from the clip before over a time so a walk turns into a run
/// without a jump. Call <see cref="Update"/> once a frame.
/// </summary>
public sealed class AnimationPlayer(IReadOnlyList<Node> nodes)
{
    private AnimationClip? _previous;
    private float _previousTime;
    private float _fade;
    private float _fadeLength;

    public IReadOnlyList<Node> Nodes { get; } = nodes ?? throw new ArgumentNullException(nameof(nodes));

    /// <summary>The clip playing, or null.</summary>
    public AnimationClip? Clip { get; private set; }

    /// <summary>How far into <see cref="Clip"/> it is, in seconds.</summary>
    public float Time { get; set; }

    public bool Loop { get; set; }

    /// <summary>How fast time goes for the clip: 1 as made, 2 twice as fast.</summary>
    public float Speed { get; set; } = 1;

    /// <summary>Whether a clip that doesn't loop has reached its end.</summary>
    public bool IsFinished => Clip is not null && !Loop && Time >= Clip.Duration;

    /// <summary>
    /// Starts <paramref name="clip"/> from the beginning, fading over from the pose of the clip before for
    /// <paramref name="fade"/>. Playing the clip already playing does nothing unless <paramref name="restart"/>.
    /// </summary>
    public void Play(AnimationClip clip, bool loop = true, TimeSpan fade = default, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip == Clip && !restart)
        {
            Loop = loop;
            return;
        }

        (_previous, _previousTime) = fade > TimeSpan.Zero ? (Clip, Time) : (null, 0);
        (_fade, _fadeLength) = (0, (float)fade.TotalSeconds);
        (Clip, Time, Loop) = (clip, 0, loop);
        Clip.Apply(Nodes, 0);
    }

    /// <summary>Stops; the nodes keep their pose.</summary>
    public void Stop()
    {
        Clip = null;
        _previous = null;
    }

    public void Update(TimeSpan elapsed)
    {
        if (Clip is null)
        {
            return;
        }

        var step = (float)elapsed.TotalSeconds * Speed;
        Time = Advance(Clip, Time + step, Loop);
        if (_previous is null)
        {
            Clip.Apply(Nodes, Time);
            return;
        }

        _previousTime = Advance(_previous, _previousTime + step, loop: true);
        _fade += (float)elapsed.TotalSeconds;
        var weight = Math.Clamp(_fade / _fadeLength, 0, 1);
        _previous.Apply(Nodes, _previousTime);
        var targets = _previous.Channels.Concat(Clip.Channels).Select(channel => channel.Target).Where(i => i < Nodes.Count).Distinct().ToList();
        var from = targets.Select(i => (Nodes[i].Position, Nodes[i].Rotation, Nodes[i].Scale)).ToList();
        Clip.Apply(Nodes, Time);
        for (var k = 0; k < targets.Count; k++)
        {
            var node = Nodes[targets[k]];
            node.Position = Vector3.Lerp(from[k].Position, node.Position, weight);
            node.Rotation = Quaternion.Slerp(from[k].Rotation, node.Rotation, weight);
            node.Scale = Vector3.Lerp(from[k].Scale, node.Scale, weight);
        }

        if (weight >= 1)
        {
            _previous = null;
        }
    }

    private static float Advance(AnimationClip clip, float time, bool loop) =>
        clip.Duration <= 0 ? 0
        : loop ? time % clip.Duration
        : Math.Min(time, clip.Duration);
}
