using System.Text.Json.Nodes;
using Jentl.Core.Storage;

namespace Jentl.Core.Tests;

public class SaveMigrationsTests
{
    // Version 1 renamed "gold" to "coins"; version 2 turned "level" from a number into an object.
    private static SaveMigrations Migrations() => new SaveMigrations(2)
        .Add(0, save =>
        {
            save["coins"] = save["gold"]?.DeepClone();
            save.Remove("gold");
        })
        .Add(1, save => save["level"] = new JsonObject { ["number"] = save["level"]!.DeepClone() });

    [Fact]
    public void Upgrade_RunsEveryStepAfterTheSavesVersion()
    {
        var upgraded = JsonNode.Parse(Migrations().Upgrade("""{"gold":12,"level":3}"""))!;

        upgraded["version"]!.GetValue<int>().Should().Be(2);
        upgraded["coins"]!.GetValue<int>().Should().Be(12);
        upgraded["gold"].Should().BeNull();
        upgraded["level"]!["number"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void Upgrade_StartsAtTheSavesVersion()
    {
        var upgraded = JsonNode.Parse(Migrations().Upgrade("""{"version":1,"coins":5,"level":7}"""))!;

        upgraded["coins"]!.GetValue<int>().Should().Be(5);
        upgraded["level"]!["number"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void Upgrade_ACurrentSave_ComesBackUntouched()
    {
        const string save = """{ "version": 2, "coins": 5 }""";

        Migrations().Upgrade(save).Should().BeSameAs(save);
    }

    [Theory]
    [InlineData("""{"version":3}""", "newer")]
    [InlineData("""{"version":"two"}""", "not a version")]
    [InlineData("""{"version":-1}""", "not a version")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "JSON object")]
    public void Upgrade_SavesItCannotRead_ThrowInvalidData(string save, string reason) =>
        FluentActions.Invoking(() => Migrations().Upgrade(save))
            .Should().Throw<InvalidDataException>().WithMessage($"*{reason}*");

    [Fact]
    public void Upgrade_AMissingStep_ThrowsInvalidData() =>
        FluentActions.Invoking(() => new SaveMigrations(2).Add(1, _ => { }).Upgrade("{}"))
            .Should().Throw<InvalidDataException>().WithMessage("*version 0*");

    [Fact]
    public void Upgrade_AFailingStep_ThrowsInvalidData() =>
        FluentActions.Invoking(() => Migrations().Upgrade("""{"version":1}"""))
            .Should().Throw<InvalidDataException>().WithMessage("*from version 1*");

    [Fact]
    public void Stamp_WritesTheCurrentVersion_UnderItsOwnName()
    {
        var migrations = new SaveMigrations(4, "game");

        var stamped = JsonNode.Parse(migrations.Stamp("""{"version":1,"x":2}"""))!;

        stamped["game"]!.GetValue<int>().Should().Be(4);
        stamped["version"]!.GetValue<int>().Should().Be(1, "another version, such as the RPG kit's own, is left alone");
    }

    [Fact]
    public void Add_RefusesStepsBeyondTheCurrentVersion_AndTwoFromOneVersion()
    {
        FluentActions.Invoking(() => new SaveMigrations(1).Add(1, _ => { })).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new SaveMigrations(2).Add(0, _ => { }).Add(0, _ => { })).Should().Throw<ArgumentException>();
    }
}
