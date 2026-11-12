namespace Jentl.Script.Tests;

public class ClosureLoopTests
{
    private static string Run(string source)
    {
        var engine = new ScriptEngine();
        var output = new List<string>();
        engine.Define("say", call =>
        {
            output.Add(call[0].ToString());
            return ScriptValue.Nil;
        });
        engine.Run(source);
        return string.Join(" ", output);
    }

    [Fact]
    public void FunctionsMadeInAForLoop_EachKeepTheirOwnTimeRound()
    {
        Run("""
            let fns = []
            for i in [1, 2, 3] do
              let doubled = i * 2
              push(fns, fn() return i + doubled end)
            end
            for f in fns do say(f()) end
            """).Should().Be("3 6 9");
    }

    [Fact]
    public void FunctionsMadeInAWhileLoop_KeepTheirOwnLets_AndStillSeeTheFunctionsVariables()
    {
        Run("""
            let fns = []
            let n = 0
            while n < 3 do
              n = n + 1
              let mine = n
              push(fns, fn() return str(mine) + "/" + str(n) end)
              if n == 2 then continue end
            end
            for f in fns do say(f()) end
            """).Should().Be("1/3 2/3 3/3");
    }

    [Fact]
    public void BreakingOutOfAScopedLoop_LeavesTheFunctionsVariablesAsTheyWere()
    {
        Run("""
            let total = 0
            let fns = []
            for i in [1, 2, 3, 4] do
              push(fns, fn() return i end)
              total = total + i
              if i == 2 then break end
            end
            say(total)
            say(fns[1]())
            """).Should().Be("3 2");
    }
}
