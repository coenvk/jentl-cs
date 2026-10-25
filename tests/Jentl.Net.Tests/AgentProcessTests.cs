namespace Jentl.Net.Tests;

public sealed class AgentProcessTests
{
    // Generous: a test that only needs an answer must not fail on a slow start (dotnet start-up).
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(5);

    // The test agent is copied next to the tests by the project file.
    private static AgentProcess Start(string mode) => AgentProcess.Start(new AgentCommand(
        "dotnet", [Path.Combine(AppContext.BaseDirectory, "TestAgent", "Jentl.Net.TestAgent.dll"), mode]));

    [Fact]
    public void AnAgentProcess_IsAnAgent()
    {
        using var a = Start("echo");
        a.Should().BeAssignableTo<IAgent>().Which.IsAway.Should().BeFalse();
    }

    [Fact]
    public async Task Ask_GetsTheAnswer()
    {
        using var a = Start("echo");
        (await a.AskAsync("hi", OneSecond)).Should().Be("hi");
    }

    [Fact]
    public async Task ALateAnswer_IsNeverTakenForTheNextQuestion()
    {
        using var a = Start("slow:300");
        (await a.AskAsync("warm up", OneSecond)).Should().Be("warm up"); // the process is up before the timing starts
        (await a.AskAsync("first", TimeSpan.FromMilliseconds(50))).Should().BeNull();
        a.Problem.Should().Be("timed out");
        await Task.Delay(400); // "first" arrives late
        (await a.AskAsync("second", OneSecond)).Should().Be("second");
        a.Problem.Should().BeNull();
    }

    [Fact]
    public async Task ACrash_EndsInAProblem_WithItsErrorOutput()
    {
        using var a = Start("crash");
        (await a.AskAsync("x", OneSecond)).Should().BeNull();
        (a.HasExited, a.Problem).Should().Be((true, "exited with code 3"));
        a.ErrorOutput.Should().Contain("boom");
    }

    [Fact]
    public void ACommandThatCannotStart_IsAProblem_NotAnException()
    {
        using var a = AgentProcess.Start(new AgentCommand("no-such-program-anywhere", []));
        a.HasExited.Should().BeTrue();
        a.Problem.Should().StartWith("could not start");
    }

    [Fact]
    public async Task ASilentAgent_TimesOut_AndCanBeStopped()
    {
        using var a = Start("silent");
        (await a.AskAsync("x", TimeSpan.FromMilliseconds(100))).Should().BeNull();
        a.Stop();
        a.HasExited.Should().BeTrue();
    }

    [Fact]
    public async Task Ask_ThrowsAwayWhatTheAgentSaidBefore()
    {
        using var a = Start("chatty");
        (await a.AskAsync("one", OneSecond)).Should().Be("hello world");
        await Task.Delay(100); // the rest of the first answer is already waiting
        (await a.AskAsync("two", OneSecond)).Should().Be("hello world");
        (await a.ReadAsync(OneSecond)).Should().Be("two");
    }

    [Fact]
    public async Task StoppingACrashedAgent_KeepsItsProblem()
    {
        using var a = Start("crash");
        (await a.AskAsync("x", OneSecond)).Should().BeNull();
        a.Stop();
        a.Problem.Should().Be("exited with code 3");
    }

    [Fact]
    public async Task AfterDispose_ItStillAnswers_WithoutThrowing()
    {
        var a = Start("crash");
        (await a.AskAsync("x", OneSecond)).Should().BeNull();
        a.Dispose();
        (a.HasExited, a.Problem).Should().Be((true, "exited with code 3"));
        a.Invoking(x => x.Send("late")).Should().NotThrow();
    }

    [Fact]
    public async Task ADeafAgent_NeverBlocksTheSender_AndSimplyDoesNotAnswer()
    {
        using var a = Start("deaf");
        var line = new string('x', 1024);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 2000; i++)
        {
            a.Send(line);
        }

        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        (await a.AskAsync("x", TimeSpan.FromMilliseconds(200))).Should().BeNull();
    }

    [Fact]
    public async Task ANegativeLimit_IsZero()
    {
        using var a = Start("silent");
        (await a.ReadAsync(TimeSpan.FromSeconds(-1))).Should().BeNull();
    }

    [Fact]
    public async Task AFloodingAgent_IsHeldToAFewShortLines_AndStillAnswersAfterwards()
    {
        using var a = Start("flood");
        a.Send("go");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!a.ErrorOutput.Contains("flooded") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        a.ErrorOutput.Should().Contain("flooded");
        var (count, longest) = (0, 0);
        while (await a.ReadAsync(TimeSpan.FromMilliseconds(300)) is { } line)
        {
            (count, longest) = (count + 1, Math.Max(longest, line.Length));
        }

        count.Should().BeLessThanOrEqualTo(AgentProcess.MaxLines, "only the last lines are kept");
        longest.Should().Be(AgentProcess.MaxLineLength, "a longer line is cut");
        (await a.AskAsync("hi", OneSecond)).Should().Be("hi");
    }
}
