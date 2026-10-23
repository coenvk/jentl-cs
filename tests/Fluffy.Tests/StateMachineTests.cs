namespace Fluffy.Tests;

public class StateMachineTests
{
    private enum Door
    {
        Open,
        Closed,
        Locked,
    }

    private enum Act
    {
        Push,
        Pull,
        Lock,
        Unlock,
        Knock,
    }

    private enum Mode
    {
        Off,
        On,
        Idle,
        Busy,
        Error,
    }

    private enum Signal
    {
        Power,
        Work,
        Done,
        Fail,
        Reset,
    }

    [Fact]
    public void Fire_FollowsTransitions_AndRunsExitThenEntryActions()
    {
        var log = new List<string>();
        var door = new StateMachine<Door, Act>(Door.Open);
        door.Configure(Door.Open).Permit(Act.Push, Door.Closed).OnExit(_ => log.Add("exit open"));
        door.Configure(Door.Closed).Permit(Act.Pull, Door.Open).Permit(Act.Lock, Door.Locked).OnEnter(t => log.Add($"enter closed from {t.Source}"));
        door.Configure(Door.Locked).Permit(Act.Unlock, Door.Closed);

        door.Fire(Act.Push);

        door.State.Should().Be(Door.Closed);
        log.Should().Equal("exit open", "enter closed from Open");
        door.CanFire(Act.Lock).Should().BeTrue();
        door.CanFire(Act.Unlock).Should().BeFalse();
        door.PermittedTriggers.Should().BeEquivalentTo([Act.Pull, Act.Lock]);
    }

    [Fact]
    public void UnhandledTriggers_Throw_OrGoToTheHandler()
    {
        var door = new StateMachine<Door, Act>(Door.Open);
        door.Configure(Door.Open).Permit(Act.Push, Door.Closed);

        var act = () => door.Fire(Act.Unlock);

        act.Should().Throw<UnhandledTriggerException>().WithMessage("No transition for Unlock in state Open.");
        door.TryFire(Act.Unlock).Should().BeFalse();

        var unhandled = new List<(Door, Act)>();
        door.OnUnhandled = (state, trigger) => unhandled.Add((state, trigger));
        door.Fire(Act.Knock);
        unhandled.Should().Equal((Door.Open, Act.Knock));
    }

    [Fact]
    public void Guards_PickTheFirstHandlerThatAllows_AndExplainWhenNoneDo()
    {
        var hasKey = false;
        var door = new StateMachine<Door, Act>(Door.Locked);
        door.Configure(Door.Locked).PermitIf(Act.Unlock, Door.Closed, () => hasKey, "has key");

        var act = () => door.Fire(Act.Unlock);

        act.Should().Throw<UnhandledTriggerException>().WithMessage("*guards not met: has key*");
        hasKey = true;
        door.Fire(Act.Unlock);
        door.State.Should().Be(Door.Closed);
    }

    [Fact]
    public void Ignore_InternalTransitions_AndReentry()
    {
        var log = new List<string>();
        var knocks = 0;
        var door = new StateMachine<Door, Act>(Door.Closed);
        door.Configure(Door.Closed)
            .Ignore(Act.Pull)
            .InternalTransition(Act.Knock, _ => knocks++)
            .Permit(Act.Push, Door.Closed)
            .OnEnter(_ => log.Add("enter"))
            .OnExit(_ => log.Add("exit"));
        door.Start();
        log.Clear();

        door.Fire(Act.Pull);
        door.Fire(Act.Knock);
        door.Fire(Act.Knock);
        knocks.Should().Be(2);
        log.Should().BeEmpty("ignored and internal triggers neither leave nor enter");

        Transition<Door, Act>? last = null;
        door.Transitioned += t => last = t;
        door.Fire(Act.Push);
        log.Should().Equal("exit", "enter");
        last!.Value.IsReentry.Should().BeTrue();
    }

    [Fact]
    public void PermitDynamic_ChoosesTheDestinationWhenFired()
    {
        var target = Door.Locked;
        var door = new StateMachine<Door, Act>(Door.Open);
        door.Configure(Door.Open).PermitDynamic(Act.Push, () => target);

        door.Fire(Act.Push);

        door.State.Should().Be(Door.Locked);
    }

    [Fact]
    public void Start_EntersTheInitialState_Once_AndFireStartsByItself()
    {
        var entered = 0;
        var door = new StateMachine<Door, Act>(Door.Open);
        door.Configure(Door.Open).OnEnter(t => entered += t.IsStart ? 1 : 100).Permit(Act.Push, Door.Closed);

        door.IsStarted.Should().BeFalse();
        door.Fire(Act.Push);
        door.Start();

        entered.Should().Be(1);
        door.IsStarted.Should().BeTrue();
    }

    [Fact]
    public void Substates_InheritTriggers_AndBordersRunActionsOnlyWhenCrossed()
    {
        var (machine, log) = Appliance();
        machine.Fire(Signal.Power);

        machine.State.Should().Be(Mode.Idle, "On's initial transition goes to Idle");
        machine.IsIn(Mode.On).Should().BeTrue();
        machine.ActiveStates.Should().Equal(Mode.Idle, Mode.On);
        log.Should().Equal("exit Off", "enter On", "enter Idle");

        log.Clear();
        machine.Fire(Signal.Work);
        log.Should().Equal("exit Idle", "enter Busy"); // moving inside On does not leave it

        log.Clear();
        machine.Fire(Signal.Power);
        machine.State.Should().Be(Mode.Off, "Busy inherits On's Power");
        log.Should().Equal("exit Busy", "exit On", "enter Off");
    }

    [Fact]
    public void ASubstatesOwnHandler_ComesBeforeItsSuperstates()
    {
        var (machine, _) = Appliance();
        machine.Fire(Signal.Power);
        machine.Fire(Signal.Work);

        machine.Fire(Signal.Fail);
        machine.State.Should().Be(Mode.Error);
        machine.IsIn(Mode.On).Should().BeFalse();

        machine.Fire(Signal.Reset);
        machine.State.Should().Be(Mode.Off);
    }

    [Fact]
    public void MovingToASuperstate_LeavesTheSubstate_AndFollowsTheInitialTransitionAgain()
    {
        var (machine, log) = Appliance();
        machine.Fire(Signal.Power);
        machine.Fire(Signal.Work);
        log.Clear();

        machine.Fire(Signal.Reset);

        machine.State.Should().Be(Mode.Idle);
        log.Should().Equal("exit Busy", "enter Idle");
    }

    [Fact]
    public void Timers_MoveAfterTheirDelay_CountingTimeInSubstates()
    {
        var (machine, _) = Appliance();
        machine.Fire(Signal.Power);
        machine.Fire(Signal.Work);

        machine.Update(TimeSpan.FromSeconds(2));
        machine.State.Should().Be(Mode.Busy);
        machine.TimeIn(Mode.Busy).Should().Be(TimeSpan.FromSeconds(2));

        machine.Update(TimeSpan.FromSeconds(1.5));
        machine.State.Should().Be(Mode.Idle, "Busy finishes by itself after three seconds");

        machine.Update(TimeSpan.FromSeconds(6.5));
        machine.State.Should().Be(Mode.Off, "On switches off after ten seconds, wherever inside it the machine is");
    }

    [Fact]
    public void ASuperstateTimer_ThatStaysInside_FiresOncePerDelay()
    {
        var naps = 0;
        var machine = new StateMachine<Mode, Signal>(Mode.On);
        machine.Configure(Mode.On).InitialTransition(Mode.Idle).After(TimeSpan.FromSeconds(1), Mode.Idle);
        machine.Configure(Mode.Idle).SubstateOf(Mode.On).OnEnter(_ => naps++);
        machine.Start();
        naps = 0;

        machine.Update(TimeSpan.FromSeconds(1));
        machine.Update(TimeSpan.FromSeconds(0.5));

        naps.Should().Be(1);
    }

    [Fact]
    public void TriggersFiredFromActions_WaitForTheCurrentTransitionToFinish()
    {
        var seen = new List<string>();
        var door = new StateMachine<Door, Act>(Door.Open);
        door.Configure(Door.Open).Permit(Act.Push, Door.Closed);
        door.Configure(Door.Closed)
            .OnEnter(_ =>
            {
                door.Fire(Act.Lock);
                seen.Add($"still {door.State}");
            })
            .Permit(Act.Lock, Door.Locked);

        door.Fire(Act.Push);

        seen.Should().Equal("still Closed");
        door.State.Should().Be(Door.Locked);
    }

    [Fact]
    public void Configuration_RejectsLoopsInTheHierarchy()
    {
        var machine = new StateMachine<Mode, Signal>(Mode.Off);
        machine.Configure(Mode.Idle).SubstateOf(Mode.On);

        var act = () => machine.Configure(Mode.On).SubstateOf(Mode.Idle);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_FindsUnreachableStates_AndBadInitialTransitions()
    {
        var (machine, _) = Appliance();
        machine.Validate().Should().BeEmpty();

        machine.Configure(Mode.Error).InitialTransition(Mode.Idle);
        var lonely = new StateMachine<Door, Act>(Door.Open);
        lonely.Configure(Door.Open).Permit(Act.Push, Door.Closed);
        lonely.Configure(Door.Locked).Permit(Act.Unlock, Door.Closed);

        machine.Validate().Should().ContainSingle().Which.Should().Contain("not one of its substates");
        lonely.Validate().Should().Equal("Locked can never be reached.");
    }

    [Fact]
    public void Diagrams_ShowStatesNestingAndLabelledTransitions()
    {
        var (machine, _) = Appliance();

        var mermaid = machine.ToMermaid();
        mermaid.Should().StartWith("stateDiagram-v2\n    [*] --> Off\n")
            .And.Contain("state On {\n        [*] --> Idle\n        Idle\n        Busy\n    }")
            .And.Contain("Busy --> Idle : after 3s")
            .And.Contain("Busy --> Error : Fail [can fail]");

        var dot = machine.ToDot();
        dot.Should().Contain("subgraph cluster_On {").And.Contain("start -> Off;").And.Contain("Off -> On [label=\"Power\"];");
    }

    /// <summary>
    /// Off ⇄ On (Power); On holds Idle and Busy (Work, Done, or three seconds), switches off after ten seconds,
    /// and Reset returns to On; Busy can Fail into Error, which Reset clears.
    /// </summary>
    private static (StateMachine<Mode, Signal> Machine, List<string> Log) Appliance()
    {
        var log = new List<string>();
        var machine = new StateMachine<Mode, Signal>(Mode.Off);
        foreach (var mode in Enum.GetValues<Mode>())
        {
            machine.Configure(mode).OnEnter(_ => log.Add($"enter {mode}")).OnExit(_ => log.Add($"exit {mode}"));
        }

        machine.Configure(Mode.Off).Permit(Signal.Power, Mode.On);
        machine.Configure(Mode.On)
            .InitialTransition(Mode.Idle)
            .Permit(Signal.Power, Mode.Off)
            .Permit(Signal.Reset, Mode.On)
            .After(TimeSpan.FromSeconds(10), Mode.Off);
        machine.Configure(Mode.Idle).SubstateOf(Mode.On).Permit(Signal.Work, Mode.Busy);
        machine.Configure(Mode.Busy)
            .SubstateOf(Mode.On)
            .Permit(Signal.Done, Mode.Idle)
            .PermitIf(Signal.Fail, Mode.Error, () => true, "can fail")
            .After(TimeSpan.FromSeconds(3), Mode.Idle);
        machine.Configure(Mode.Error).Permit(Signal.Reset, Mode.Off);
        machine.Start();
        log.Clear();
        return (machine, log);
    }
}
