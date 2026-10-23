using System.Text.Json;
using Jentl.Rpg.Dialogue;
using Jentl.Rpg.Items;
using Jentl.Rpg.Quests;
using Jentl.Rpg.Scripting;

namespace Jentl.Rpg.Tests;

public class StateTests
{
    private readonly RpgContent _content = Fixtures.Content();

    [Fact]
    public void Inventory_FillsStacksFirst_AndSaysWhatDidNotFit()
    {
        var herb = _content.Items["herb"];
        var sword = _content.Items["sword"];
        var bag = new Inventory(capacity: 2);

        bag.Add(herb, 7).Should().Be(0);
        bag.Add(herb, 7).Should().Be(0, "the first stack fills to 10, the rest takes the second slot");
        bag.Stacks.Select(s => s.Count).Should().Equal(10, 4);
        bag.Add(sword).Should().Be(1, "both slots are taken");
        bag.Room(herb).Should().Be(6);
        bag.IsFull.Should().BeTrue();
    }

    [Fact]
    public void Inventory_RemovesAllOrNothing()
    {
        var bag = new Inventory();
        bag.Add(_content.Items["herb"], 12);
        var changes = 0;
        bag.Changed += () => changes++;

        bag.Remove("herb", 13).Should().BeFalse();
        bag.Count("herb").Should().Be(12);
        bag.Remove("herb", 11).Should().BeTrue();
        bag.Stacks.Should().ContainSingle().Which.Count.Should().Be(1);
        changes.Should().Be(1);
    }

    [Fact]
    public void Equipment_AddsUp_AndSwaps()
    {
        var equipment = new Equipment();

        equipment.Equip(_content.Items["sword"]).Should().BeNull();
        equipment.Equip(_content.Items["vest"]);
        equipment.Attack.Should().Be(3);
        equipment.Defense.Should().Be(1);
        equipment.Equip(_content.Items["sword"]).Should().Be(_content.Items["sword"]);
        equipment.Unequip("armor").Should().Be(_content.Items["vest"]);
        equipment.Defense.Should().Be(0);
    }

    [Theory]
    [InlineData("""{ "flag": "a" }""", true)]
    [InlineData("""{ "notFlag": "a" }""", false)]
    [InlineData("""{ "has": "herb", "count": 2 }""", true)]
    [InlineData("""{ "has": "herb", "count": 3 }""", false)]
    [InlineData("""{ "var": "gold", "atLeast": 5 }""", true)]
    [InlineData("""{ "var": "gold", "atMost": 4 }""", false)]
    [InlineData("""{ "quest": "rats", "is": "notStarted" }""", true)]
    [InlineData("""{ "any": [ { "flag": "b" }, { "flag": "a" } ] }""", true)]
    [InlineData("""[ { "flag": "a" }, { "flag": "b" } ]""", false)]
    [InlineData("""{ "not": { "flag": "b" } }""", true)]
    public void Conditions_TestTheState(string json, bool expected)
    {
        var state = new RpgState(_content.Items, _content.Quests);
        state.Flags.Add("a");
        state.AddVariable("gold", 5);
        state.Inventory.Add(_content.Items["herb"], 2);
        using var document = JsonDocument.Parse(json);

        Condition.Parse(document.RootElement, "test").Test(state).Should().Be(expected);
    }

    [Fact]
    public void Effects_ChangeTheState_AndTellThePlayer()
    {
        var state = new RpgState(_content.Items, _content.Quests);
        var said = new List<string>();
        state.Said += said.Add;
        CustomEffect? custom = null;
        state.CustomEffect = e => custom = e;
        using var document = JsonDocument.Parse("""
            [ { "give": "herb", "count": 3 }, { "take": "herb" }, { "setFlag": "x" }, { "addVar": "gold", "amount": 4 },
              { "if": { "flag": "x" }, "then": [ { "message": "yes" } ], "else": [ { "message": "no" } ] },
              { "shake": "screen", "strength": 2 } ]
            """);

        state.Run([.. document.RootElement.EnumerateArray().Select(e => Effect.Parse(e, "test"))]);

        state.Inventory.Count("herb").Should().Be(2);
        state.Flags.Should().Contain("x");
        state.Variable("gold").Should().Be(4);
        said.Should().Contain(["You receive 3 x Herb.", "yes"]).And.NotContain("no");
        custom!.Name.Should().Be("shake");
        custom.Arguments.GetProperty("strength").GetInt32().Should().Be(2);
    }

    [Fact]
    public void UnknownCondition_IsAContentError()
    {
        using var document = JsonDocument.Parse("""{ "flagg": "typo" }""");

        var act = () => Condition.Parse(document.RootElement, "dialogue 'x'");

        act.Should().Throw<ContentException>().WithMessage("dialogue 'x': unknown condition*");
    }

    [Fact]
    public void Quests_AdvanceStepByStep_AndCollectIsLive()
    {
        var state = new RpgState(_content.Items, _content.Quests);
        var herbs = state.Quests["herbs"];
        state.Inventory.Add(_content.Items["herb"]);

        state.StartQuest("herbs");
        herbs.Status.Should().Be(QuestStatus.Active);
        herbs.Progress(0).Should().Be(1);

        state.Give("herb");
        herbs.Step.Should().Be(1, "two herbs complete the first step");
        state.Notify(new TalkedTrigger("someone", "villager"));
        herbs.IsReady.Should().BeFalse();
        state.Notify(new TalkedTrigger("elder", "elder"));
        herbs.IsReady.Should().BeTrue();
        herbs.Status.Should().Be(QuestStatus.Active, "it waits to be handed in");

        state.CompleteQuest("herbs");
        herbs.Status.Should().Be(QuestStatus.Completed);
        state.Inventory.Count("potion").Should().Be(1, "the reward");
    }

    [Fact]
    public void KillObjectives_OnlyCountAfterTheQuestStarts_AndAutoCompleteRewards()
    {
        var state = new RpgState(_content.Items, _content.Quests);

        state.Notify(new KilledTrigger("rat"));
        state.StartQuest("rats");
        state.Quests["rats"].Progress(0).Should().Be(0);

        state.Notify(new KilledTrigger("rat"));
        state.Notify(new KilledTrigger("bat"));
        state.Quests["rats"].Progress(0).Should().Be(1);
        state.Notify(new KilledTrigger("rat"));

        state.Quests["rats"].Status.Should().Be(QuestStatus.Completed);
        state.Flags.Should().Contain("rats_done");
    }

    [Fact]
    public void Dialogue_RoutesByQuestState_AndFiltersChoices()
    {
        var state = new RpgState(_content.Items, _content.Quests);
        var elder = _content.Dialogue("elder");

        var talk = new DialogueRunner(elder, state);
        talk.Current!.Text.Should().Be("Hello.");
        talk.Current.Speaker.Should().Be("Elder", "the dialogue's speaker is the default");
        talk.Choices.Select(c => c.Text).Should().Equal("Any work?", "Bye.");

        talk.Choose(0);
        state.Quests["herbs"].Status.Should().Be(QuestStatus.Active);
        talk.Current!.Text.Should().Be("Bring two herbs.");
        talk.Continue();
        talk.Current!.Text.Should().Be("Off you go.");
        talk.Continue();
        talk.IsOver.Should().BeTrue();

        new DialogueRunner(elder, state).Current!.Text.Should().Be("Still waiting.");

        state.Give("herb", 2);
        state.Notify(new TalkedTrigger("elder", "elder"));
        var thanks = new DialogueRunner(elder, state);
        thanks.Current!.Text.Should().Be("Thank you!");
        state.Inventory.Count("herb").Should().Be(0);
        state.Quests["herbs"].Status.Should().Be(QuestStatus.Completed);
        new DialogueRunner(elder, state).Current!.Text.Should().Be("Good to see you.");
    }

    [Fact]
    public void Dialogue_ShowsChoicesOnlyWhenTheirConditionHolds()
    {
        var state = new RpgState(_content.Items, _content.Quests);
        state.Flags.Add("knows_secret");

        new DialogueRunner(_content.Dialogue("elder"), state).Choices.Select(c => c.Text).Should().Equal("Any work?", "Secret", "Bye.");
    }

    [Fact]
    public void Dialogue_ThatLoopsWithoutSpeaking_IsAContentError()
    {
        var loop = DialogueDefinition.FromJson("""
            { "dialogues": [ { "id": "loop", "nodes": { "start": { "goto": [ { "node": "start" } ] } } } ] }
            """)[0];

        var act = () => new DialogueRunner(loop, new RpgState(_content.Items, _content.Quests));

        act.Should().Throw<ContentException>();
    }

    [Fact]
    public void Validator_AcceptsGoodContent_AndFindsBrokenReferences()
    {
        ContentValidator.Validate(_content).Should().BeEmpty();

        var broken = RpgContent.FromJson(
            Fixtures.Items,
            """{ "actors": [ { "id": "player" }, { "id": "bat", "drops": [ { "item": "wing" } ], "brain": "flappy", "dialogue": "nope" } ] }""",
            """{ "quests": [ { "id": "q", "objectives": [ { "text": "x", "kill": "dragon" } ], "rewards": [ { "give": "crown" } ] } ] }""",
            """{ "dialogues": [ { "id": "d", "start": "missing", "nodes": { "a": { "text": "hi", "next": "b", "do": [ { "startQuest": "nope" } ] } } } ] }""");

        ContentValidator.Validate(broken).Should().HaveCount(8).And.Contain(p => p.Contains("'wing'", StringComparison.Ordinal))
            .And.Contain(p => p.Contains("'flappy'", StringComparison.Ordinal))
            .And.Contain(p => p.Contains("'dragon'", StringComparison.Ordinal))
            .And.Contain(p => p.Contains("'crown'", StringComparison.Ordinal))
            .And.Contain(p => p.Contains("'missing'", StringComparison.Ordinal))
            .And.Contain(p => p.Contains("'b'", StringComparison.Ordinal))
            .And.Contain(p => p.Contains("quest 'nope'", StringComparison.Ordinal));
    }
}
