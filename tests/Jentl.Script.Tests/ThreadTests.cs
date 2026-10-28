namespace Jentl.Script.Tests;

public class ThreadTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(0.25);

    private readonly ScriptEngine _engine = new(seed: 1);
    private readonly List<string> _said = [];

    public ThreadTests() => _engine.Define("say", call => _said.Add(call.Text(0)));

    [Fact]
    public void AThread_WaitsWithoutStoppingTheGame_AndCarriesOnWhereItWas()
    {
        var script = _engine.Load("""
            say("hello")
            wait(0.5)
            say("still here")
            wait()
            say("one frame later")
            return "done"
            """, "cutscene");
        var thread = script.Start();

        _said.Should().BeEmpty("a thread starts in the next update");
        _engine.Update(Frame);
        _said.Should().Equal(["hello"]);
        _engine.Update(Frame);
        _said.Should().Equal(["hello"], "half a second hasn't passed yet");
        _engine.Update(Frame);
        _said.Should().Equal(["hello", "still here"]);
        _engine.Update(Frame);

        _said.Should().Equal("hello", "still here", "one frame later");
        thread.Status.Should().Be(ScriptThreadStatus.Done);
        thread.Result.AsText.Should().Be("done");
        _engine.Threads.Should().BeEmpty();
    }

    [Fact]
    public void Waiting_WorksInsideFunctionsAndLoops()
    {
        var script = _engine.Run("""
            fn countdown(n)
              for i in range(n, 0, -1) do
                say(str(i))
                wait(0.25)
              end
              return "go"
            end
            """, "count");
        var thread = script.Start("countdown", 3);

        for (var i = 0; i < 4; i++)
        {
            _engine.Update(Frame);
        }

        _said.Should().Equal("3", "2", "1");
        thread.Result.AsText.Should().Be("go");
    }

    [Fact]
    public void TheGame_CanMakeAScriptWaitForAnAnswer()
    {
        ScriptPromise? choosing = null;
        _engine.Define("ask", call =>
        {
            choosing = call.Suspend();
            return ScriptValue.Nil;
        });
        var script = _engine.Load("let answer = ask(\"Help the elder?\")\nsay(\"you chose \" + answer)", "talk");
        script.Start();

        _engine.Update(Frame);
        _engine.Update(Frame);
        _said.Should().BeEmpty();
        choosing!.Resolve("yes");
        _engine.Update(Frame);

        _said.Should().Equal("you chose yes");
    }

    [Fact]
    public void WaitUntil_ChecksEveryFrame()
    {
        _engine["door_open"] = false;
        var script = _engine.Load("waitUntil(fn() return door_open end)\nsay(\"through\")", "door");
        script.Start();

        _engine.Update(Frame);
        _engine.Update(Frame);
        _said.Should().BeEmpty();
        _engine["door_open"] = true;
        _engine.Update(Frame);

        _said.Should().Equal("through");
    }

    [Fact]
    public void Spawn_RunsThreadsSideBySide_AndStopEndsOne()
    {
        var script = _engine.Load("""
            fn tick(name)
              while true do
                say(name)
                wait(0.25)
              end
            end
            let a = spawn(tick, "a")
            let b = spawn(tick, "b")
            wait(0.5)
            stop(a)
            wait(0.25)
            say("a done: {a.done}, b done: {b.done}")
            """, "spawn");
        script.Start();

        for (var i = 0; i < 5; i++)
        {
            _engine.Update(Frame);
        }

        // Spawned threads start in the next update; the stop comes before "a" runs again, and "b" carries on after.
        _said.Should().Equal("a", "b", "b", "a done: true, b done: false", "b", "b");
    }

    [Fact]
    public void Stop_EndsAWaitingThread()
    {
        var thread = _engine.Load("wait(1)\nsay(\"never\")", "x").Start();
        _engine.Update(Frame);

        thread.Stop();
        _engine.Update(TimeSpan.FromSeconds(2));

        _said.Should().BeEmpty();
        thread.Status.Should().Be(ScriptThreadStatus.Stopped);
        _engine.Threads.Should().BeEmpty();
    }

    [Fact]
    public void AGameFunction_StartedAsAThread_CanWaitToo()
    {
        var native = new NativeFunction("pause", call =>
        {
            call.Wait(TimeSpan.FromSeconds(0.25));
            return "paused";
        });
        var thread = _engine.Start(native);

        _engine.Update(Frame);
        thread.IsFinished.Should().BeFalse();
        _engine.Update(Frame);

        thread.Result.AsText.Should().Be("paused");
    }
}
