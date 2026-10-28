namespace Jentl.Script.Tests;

public class ErrorTests
{
    private readonly ScriptEngine _engine = new(seed: 1);

    private ScriptException Fails(string source)
    {
        var act = () => _engine.Load(source, "level").Run();
        return act.Should().Throw<ScriptException>().Which;
    }

    [Theory]
    [InlineData("let x = ", "level:1: expected a value, got the end of the script")]
    [InlineData("if x then\n  print(1)\n", "level:3: expected 'end' to close 'if', got the end of the script")]
    [InlineData("while true\n print(1) end", "level:2: expected 'do' after the condition of 'while', got 'print'")]
    [InlineData("x == 1", "level:1: this does nothing on its own; a statement is a call, an assignment, 'let', 'fn', 'if', 'while', 'for' or 'return'")]
    [InlineData("f() = 2", "level:1: can't assign to this; '=' needs a name, a field or an index on its left")]
    [InlineData("let s = \"open", "level:1: text without its closing quote")]
    [InlineData("let a = 1 $ 2", "level:1: unexpected '$'")]
    [InlineData("fn f()\n let a = 1\n let a = 2\nend", "level:3: 'a' is already declared here")]
    [InlineData("break", "level:1: 'break' outside a loop")]
    [InlineData("print(\"{1 +}\")", "level:1: expected a value, got the end of the script")]
    public void Mistakes_AreFoundBeforeRunning_WithTheirLine(string source, string message)
    {
        var error = Fails(source);

        error.Should().BeOfType<ScriptSyntaxException>();
        error.Message.Should().Be(message);
    }

    [Fact]
    public void SyntaxErrors_SayTheColumn()
    {
        var error = (ScriptSyntaxException)Fails("let ok = 1\nlet bad = ]");

        (error.Line, error.Column).Should().Be((2, 11));
    }

    [Theory]
    [InlineData("return nothing", "unknown name 'nothing'")]
    [InlineData("missing = 1", "unknown name 'missing' (use 'let missing = ...' to make a new variable)")]
    [InlineData("return 1 + nil", "can't add number 1 and nil")]
    [InlineData("return {} < 1", "can't compare map with number 1")]
    [InlineData("return 1 / 0", "division by zero")]
    [InlineData("return [1, 2][2]", "index 2 is outside the list (it has 2)")]
    [InlineData("return nil.x", "can't read field 'x' of nil")]
    [InlineData("let n = 3\nn()", "can't call number 3")]
    [InlineData("return upper(3)", "upper: argument 1 should be text, got number 3")]
    [InlineData("return clamp(1, 2)", "clamp: argument 3 (a number) is missing")]
    [InlineData("error(\"the bridge is out\")", "the bridge is out")]
    [InlineData("for x in 5 do end", "can't go through number 5 with 'for'; use a list, a map, text or range(...)")]
    public void RunningMistakes_SayWhatWentWrong(string source, string reason) =>
        Fails(source).Reason.Should().Be(reason);

    [Fact]
    public void RunningMistakes_SayWhereAndThroughWhichCalls()
    {
        var error = Fails("fn inner()\n  return 1 + nil\nend\nfn outer()\n  return inner()\nend\nouter()");

        error.Message.Should().Be("level:2: can't add number 1 and nil");
        error.Trace.Should().Equal("level:2 in inner", "level:5 in outer", "level:7 in (script)");
    }

    [Fact]
    public void EndlessLoops_AreStopped()
    {
        _engine.InstructionLimit = 10_000;

        Fails("while true do end").Reason.Should().StartWith("ran more than 10000 steps");
    }

    [Fact]
    public void EndlessRecursion_IsStopped() =>
        Fails("fn f() return f() end\nf()").Reason.Should().StartWith("calls went more than 200 deep");

    [Fact]
    public void Waiting_IsOnlyForThreads() =>
        Fails("wait(1)").Reason.Should().StartWith("can't wait here");

    [Fact]
    public void AFailedThread_IsReported_OrThrownWithoutAHandler()
    {
        var script = _engine.Load("wait(0)\nlet x = nil + 1", "cutscene");
        script.Start();
        _engine.Update(TimeSpan.Zero);

        var act = () => _engine.Update(TimeSpan.FromSeconds(0.1));

        act.Should().Throw<ScriptException>().WithMessage("cutscene:2: can't add nil and number 1");

        var failed = new List<ScriptThread>();
        _engine.Failed += failed.Add;
        script.Start();
        _engine.Update(TimeSpan.Zero);
        _engine.Update(TimeSpan.FromSeconds(0.1));
        failed.Should().ContainSingle().Which.Error!.Line.Should().Be(2);
        _engine.Threads.Should().BeEmpty();
    }
}
