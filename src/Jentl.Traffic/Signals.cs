namespace Jentl.Traffic;

/// <summary>What a light shows.</summary>
public enum SignalState
{
    Red,
    Amber,
    Green,
}

/// <summary>The lights for one or more lanes that turn green together.</summary>
public sealed class SignalGroup
{
    internal SignalGroup(int phase) => Phase = phase;

    /// <summary>Which phase of its plan it is green in.</summary>
    public int Phase { get; }

    public SignalState State { get; internal set; } = SignalState.Red;
}

/// <summary>
/// A junction's lights on a fixed cycle: each phase in turn shows green for <see cref="Green"/> seconds, then amber
/// for <see cref="Amber"/>, then all red for <see cref="AllRed"/> while the junction clears.
/// </summary>
public sealed class SignalPlan
{
    private readonly List<SignalGroup> _groups = [];
    private float _time;

    internal SignalPlan(int phases, float green, float amber, float allRed)
    {
        Phases = phases;
        Green = green;
        Amber = amber;
        AllRed = allRed;
    }

    public int Phases { get; }

    public float Green { get; set; }

    public float Amber { get; set; }

    public float AllRed { get; set; }

    public IReadOnlyList<SignalGroup> Groups => _groups;

    /// <summary>The phase now green or amber, or clearing after.</summary>
    public int Phase => (int)(_time / Cycle) % Phases;

    private float Cycle => Green + Amber + AllRed;

    internal SignalGroup Group(int phase)
    {
        var group = _groups.FirstOrDefault(g => g.Phase == phase);
        if (group is null)
        {
            group = new SignalGroup(phase);
            _groups.Add(group);
        }

        return group;
    }

    internal void Advance(float seconds)
    {
        _time += seconds;
        var inPhase = _time % Cycle;
        var state = inPhase < Green ? SignalState.Green : inPhase < Green + Amber ? SignalState.Amber : SignalState.Red;
        foreach (var group in _groups)
        {
            group.State = group.Phase == Phase ? state : SignalState.Red;
        }
    }
}
