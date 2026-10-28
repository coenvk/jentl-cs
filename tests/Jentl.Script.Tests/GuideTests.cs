namespace Jentl.Script.Tests;

public class GuideTests
{
    private const string Cutscene = """
        # A cutscene. Comments start with #.
        let elder = actors.elder
        
        fn walk(who, x)
          while who.x < x do
            who.x += 2
            wait()                          # a frame
          end
        end
        
        say(elder, "You came back, {player.name}.")
        walk(elder, 120)
        if has("amulet") then
          say(elder, "And you found it!")
          gold += 50
        elif chance(0.5) then
          say(elder, "Still nothing?")
        else
          let answer = ask("Keep looking?", ["Yes", "No"])
          if answer == "No" then return end
        end
        for i, line in ["Off you go.", "Mind the bridge."] do
          say(elder, line)
          wait(0.5)
        end
        """;

    private sealed class Actor
    {
        public float X { get; set; }
    }

    private sealed class Hero
    {
        public string Name { get; } = "Ash";
    }

    [Theory]
    [InlineData(true, "Ash: You came back, Ash.|Ash: And you found it!|Ash: Off you go.|Ash: Mind the bridge.", 60)]
    [InlineData(false, "Ash: You came back, Ash.|ask Keep looking?", 10)]
    public void TheGuidesCutscene_Runs(bool hasAmulet, string expected, int gold)
    {
        var engine = new ScriptEngine(seed: 2);
        var elder = new Actor();
        var said = new List<string>();
        engine.Expose("player", new Hero());
        engine.Expose("actors", new Dictionary<string, Actor> { ["elder"] = elder }.AsReadOnly());
        engine["gold"] = 10;
        engine.Define("has", call => hasAmulet && call.Text(0) == "amulet");
        engine.Define("chance", call => false);
        engine.Define("say", call =>
        {
            said.Add($"Ash: {call.Text(1)}");
            call.Wait(TimeSpan.FromSeconds(0.1));
        });
        engine.Define("ask", call =>
        {
            said.Add($"ask {call.Text(0)}");
            call.Suspend().Resolve("No");
            return ScriptValue.Nil;
        });
        var thread = engine.Load(Cutscene, "cutscene").Start();

        for (var frame = 0; frame < 200 && !thread.IsFinished; frame++)
        {
            engine.Update(TimeSpan.FromSeconds(1 / 60.0));
        }

        thread.Status.Should().Be(ScriptThreadStatus.Done);
        string.Join("|", said).Should().Be(expected);
        elder.X.Should().Be(120);
        engine["gold"].AsNumber.Should().Be(gold);
    }
}
