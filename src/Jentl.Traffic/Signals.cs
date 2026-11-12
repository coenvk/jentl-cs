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
/// <summary>
/// The lights of one junction: phases taking turns, each green, then amber, then all red for a moment. On a fixed
/// plan every phase gets <see cref="Green"/> in turn. An <see cref="Actuated"/> plan reacts to traffic: a phase stays
/// green while cars come (at least <see cref="MinGreen"/>, at most <see cref="Green"/>), gives up early when its
/// approaches are empty and another phase has cars waiting, skips phases nobody waits for, and rests on green when
/// no one else is waiting.
/// </summary>
public sealed class SignalPlan
{
    private readonly List<SignalGroup> _groups = [];
    private float _time;
    private int _phase;
    private float _inStage;
    private SignalState _stage = SignalState.Green;

    internal SignalPlan(int phases, float green, float amber, float allRed)
    {
        Phases = phases;
        Green = green;
        Amber = amber;
        AllRed = allRed;
    }

    public int Phases { get; }

    /// <summary>Seconds of green per phase; for an actuated plan, the most a phase gets while others wait.</summary>
    public float Green { get; set; }

    public float Amber { get; set; }

    public float AllRed { get; set; }

    /// <summary>Whether the lights follow the traffic rather than a fixed cycle.</summary>
    public bool Actuated { get; set; }

    /// <summary>The least green an actuated phase gets once it has it.</summary>
    public float MinGreen { get; set; } = 6;

    public IReadOnlyList<SignalGroup> Groups => _groups;

    /// <summary>The phase now green or amber, or clearing after.</summary>
    public int Phase => Actuated ? _phase : (int)(_time / Cycle) % Phases;

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

    /// <summary>Moves the lights on; <paramref name="waiting"/> says whether a phase has traffic coming (for an actuated plan).</summary>
    internal void Advance(float seconds, Func<int, bool>? waiting = null)
    {
        _time += seconds;
        if (!Actuated)
        {
            var inPhase = _time % Cycle;
            var state = inPhase < Green ? SignalState.Green : inPhase < Green + Amber ? SignalState.Amber : SignalState.Red;
            Show(state);
            return;
        }

        _inStage += seconds;
        bool Waits(int phase) => waiting?.Invoke(phase) ?? true;
        switch (_stage)
        {
            case SignalState.Green:
                var others = Enumerable.Range(1, Phases - 1).Any(i => Waits((_phase + i) % Phases));
                if (others && _inStage >= MinGreen && (_inStage >= Green || !Waits(_phase)))
                {
                    (_stage, _inStage) = (SignalState.Amber, 0);
                }

                break;
            case SignalState.Amber when _inStage >= Amber:
                (_stage, _inStage) = (SignalState.Red, 0);
                break;
            case SignalState.Red when _inStage >= AllRed:
                // The next phase with traffic; round to this one again if no one else waits.
                var next = Enumerable.Range(1, Phases).Select(i => (_phase + i) % Phases).FirstOrDefault(Waits, (_phase + 1) % Phases);
                (_phase, _stage, _inStage) = (next, SignalState.Green, 0);
                break;
        }

        Show(_stage);
    }

    private void Show(SignalState state)
    {
        foreach (var group in _groups)
        {
            group.State = group.Phase == Phase ? state : SignalState.Red;
        }
    }
}
