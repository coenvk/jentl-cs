namespace Jentl.Script.Tests;

public class HostTests
{
    private enum Mood
    {
        Calm,
        Angry,
    }

    private sealed class Player
    {
        public int Health { get; set; } = 10;

        public string Name { get; init; } = "Ash";

        public Mood Mood { get; set; }

        public float Speed = 1;

        public List<string> Bag { get; } = ["rope", "lamp"];

        public int Heal(int amount, bool overheal = false)
        {
            Health = overheal ? Health + amount : Math.Min(20, Health + amount);
            return Health;
        }

        public void Shout() => throw new InvalidOperationException($"{Name} is too loud");
    }

    private readonly ScriptEngine _engine = new(seed: 1);
    private readonly Player _player = new();

    public HostTests() => _engine.Expose("player", _player);

    [Fact]
    public void Scripts_ReadAndChangeTheGamesObjects_ByNameInAnyCase()
    {
        _engine.Run("""
            player.health -= 3
            player.Speed = 2.5
            player.mood = "angry"
            """);

        _player.Health.Should().Be(7);
        _player.Speed.Should().Be(2.5f);
        _player.Mood.Should().Be(Mood.Angry);
        _engine.Evaluate("player.name + \" is \" + player.mood").AsText.Should().Be("Ash is Angry");
    }

    [Fact]
    public void Scripts_CallTheGamesMethods_WithOptionalParameters()
    {
        _engine.Evaluate("player.heal(15)").AsNumber.Should().Be(20);
        _engine.Evaluate("player.heal(5, true)").AsNumber.Should().Be(25);
    }

    [Fact]
    public void Scripts_GoThroughTheGamesCollections()
    {
        _engine.Evaluate("len(player.bag) == 0").AsLogical.Should().BeFalse();
        var script = _engine.Run("let items = \"\"\nfor item in player.bag do items += item + \" \" end");

        script["items"].AsText.Should().Be("rope lamp ");
    }

    [Fact]
    public void Scripts_IndexTheGamesListsAndDictionaries()
    {
        var stats = new Dictionary<string, int> { ["str"] = 3 };
        _engine.Expose("stats", stats);

        _engine.Run("player.bag[0] = \"torch\"\nstats.str += player.bag[-1] == \"lamp\" and 2 or 0\nstats[\"dex\"] = 1");

        _player.Bag.Should().Equal("torch", "lamp");
        stats.Should().Equal(new Dictionary<string, int> { ["str"] = 5, ["dex"] = 1 });
    }

    [Theory]
    [InlineData("player.name = \"Bo\"", "Name of Player can't be changed")]
    [InlineData("player.health = \"lots\"", "Health needs a number, got text \"lots\"")]
    [InlineData("player.mood = \"sleepy\"", "Mood needs one of Calm, Angry, got text \"sleepy\"")]
    [InlineData("player.wings = 2", "Player has no member 'wings'")]
    [InlineData("player.heal()", "Heal of Player doesn't take 0 arguments")]
    [InlineData("player.shout()", "Shout: Ash is too loud")]
    public void Mistakes_WithTheGamesObjects_AreExplained(string source, string reason)
    {
        var act = () => _engine.Run(source);

        act.Should().Throw<ScriptException>().Which.Reason.Should().Be(reason);
    }

    [Fact]
    public void EngineGlobals_AreSharedWithTheGame()
    {
        _engine["gold"] = 10;
        _engine.Run("gold += 5");

        _engine["gold"].AsNumber.Should().Be(15);
    }

    [Fact]
    public void Define_GivesScriptsTheGamesFunctions()
    {
        var log = new List<string>();
        _engine.Define("log", call => log.Add($"{call.Text(0)}x{call.Whole(1, 1)}"));
        _engine.Define("double", call => call.Number(0) * 2);

        _engine.Run("log(\"a\")\nlog(\"b\", double(1.5))");

        log.Should().Equal("ax1", "bx3");
    }

    [Fact]
    public void Script_CanBeReloaded_KeepingItsGlobals()
    {
        var script = _engine.Run("let visits = 0\nfn greet() visits += 1\n return \"hi\" end", "npc");
        script.Call("greet");
        script["visits"] = 4;

        script.Reload("fn greet()\n visits += 1\n return \"hello again\"\nend");

        script.Call("greet").AsText.Should().Be("hello again");
        script["visits"].AsNumber.Should().Be(5);
    }

    [Fact]
    public void ScriptValue_ComesFromCSharpValues()
    {
        ScriptValue.From(3).AsNumber.Should().Be(3);
        ScriptValue.From(null).IsNil.Should().BeTrue();
        ScriptValue.From(Mood.Calm).AsText.Should().Be("Calm");
        ScriptValue.From(_player).Raw.Should().BeSameAs(_player);
        ScriptValue.From(_player).Should().Be(ScriptValue.From(_player));
    }
}
