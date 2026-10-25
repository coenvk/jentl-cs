namespace Jentl.Multiplayer.Tests;

public class DecisionTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task ALegalAnswerInTime_IsTaken() =>
        (await Decisions.DecideAsync(_ => Task.FromResult<string?>("call"), OneSecond, m => m == "call", () => "fold")).Should().Be(new Decided<string>("call", Fault.None));

    [Fact]
    public async Task AnIllegalAnswer_IsReplaced() =>
        (await Decisions.DecideAsync(_ => Task.FromResult<string?>("raise"), OneSecond, m => m == "call", () => "fold")).Should().Be(new Decided<string>("fold", Fault.Illegal));

    [Fact]
    public async Task NoAnswerInTime_IsIllegal() =>
        (await Decisions.DecideAsync(_ => Task.FromResult<string?>(null), OneSecond, _ => true, () => "fold")).Should().Be(new Decided<string>("fold", Fault.Illegal));

    [Fact]
    public async Task ALateAnswer_IsReplaced_AndTheAskIsCancelled()
    {
        var cancelled = false;
        var d = await Decisions.DecideAsync(async c => { try { await Task.Delay(1000, c); } catch (OperationCanceledException) { cancelled = true; } return "call"; }, TimeSpan.FromMilliseconds(50), _ => true, () => "fold");
        d.Should().Be(new Decided<string>("fold", Fault.Late));
        await Task.Delay(100);
        cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task AThrowingAgent_IsGone() =>
        (await Decisions.DecideAsync<string>(_ => throw new IOException(), OneSecond, _ => true, () => "fold")).Fault.Should().Be(Fault.Gone);

    [Fact]
    public async Task AnAgentFailingInsideTheTask_IsGone() =>
        (await Decisions.DecideAsync<string>(async _ => { await Task.Yield(); throw new IOException(); }, OneSecond, _ => true, () => "fold")).Should().Be(new Decided<string>("fold", Fault.Gone));

    [Fact]
    public async Task AnAgentKnownToBeGone_IsNotAsked()
    {
        var asked = false;
        var d = await Decisions.DecideAsync(_ => { asked = true; return Task.FromResult<string?>("call"); }, OneSecond, _ => true, () => "fold", () => true);
        d.Should().Be(new Decided<string>("fold", Fault.Gone));
        asked.Should().BeFalse();
    }

    [Fact]
    public async Task AFailureAfterTheLimit_IsObserved()
    {
        var d = await Decisions.DecideAsync<string>(async _ => { await Task.Delay(100, CancellationToken.None); throw new IOException(); }, TimeSpan.FromMilliseconds(20), _ => true, () => "fold");
        d.Should().Be(new Decided<string>("fold", Fault.Late));

        var unobserved = new List<Exception>();
        void Record(object? s, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            await Task.Delay(300);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }

        unobserved.Should().BeEmpty();
    }

    [Fact]
    public void HeadsUp_SwapsSeats() => Seatings.Duplicate(2).Should().BeEquivalentTo(new int[][] { [0, 1], [1, 0] }, o => o.WithStrictOrdering());

    [Fact]
    public void Ring_PutsEveryBotInEverySeatOnce()
    {
        var runs = Seatings.Duplicate(4);
        runs.Should().HaveCount(4);
        Enumerable.Range(0, 4).Should().OnlyContain(seat => runs.Select(r => r[seat]).Distinct().Count() == 4);
    }
}
