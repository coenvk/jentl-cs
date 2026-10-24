using System.Globalization;
using Jentl.Core.Localization;

namespace Jentl.Core.Tests;

public class LocalizerTests
{
    private const string Dutch = """
        {
          "// note": "for translators",
          "Play": "Spelen",
          "{0} wins": "{0} wint",
          "{0} move": "{0} stap",
          "{0} moves": "{0} stappen",
          "Score {0:N0}": "Score {0:N0}",
          "Empty": ""
        }
        """;

    private readonly Localizer _nl = Localizer.Load("nl", Dutch);

    [Fact]
    public void Translates_AndFallsBackToTheEnglish()
    {
        _nl["Play"].Should().Be("Spelen");
        _nl["Quit"].Should().Be("Quit");
        _nl.Missing.Should().Equal("Quit");
    }

    [Fact]
    public void FormatsArguments_TheLanguagesWay()
    {
        _nl.Format("{0} wins", "Ruby").Should().Be("Ruby wint");
        _nl.Format("Score {0:N0}", 1234567).Should().Be("Score 1.234.567");
        Localizer.English.Format("Score {0:N0}", 1234567).Should().Be("Score 1,234,567");
    }

    [Theory]
    [InlineData(1, "1 stap")]
    [InlineData(0, "0 stappen")]
    [InlineData(7, "7 stappen")]
    public void Plurals_PickTheRightForm(int count, string expected) =>
        _nl.Plural(count, "{0} move", "{0} moves").Should().Be(expected);

    [Fact]
    public void Plurals_TakeFurtherArguments()
    {
        Localizer.English.Plural(2, "{1} draws a card", "{1} draws {0} cards", "Otto").Should().Be("Otto draws 2 cards");
    }

    [Fact]
    public void CommentsAndEmptyTranslations_AreSkipped()
    {
        _nl.Count.Should().Be(5);
        _nl["Empty"].Should().Be("Empty");
    }

    [Fact]
    public void English_NeverReportsMissingText()
    {
        _ = Localizer.English["Anything"];

        Localizer.English.Missing.Should().BeEmpty();
    }

    [Theory]
    [InlineData("nl-NL", "nl")]
    [InlineData("nl-BE", "nl")]
    [InlineData("de-DE", "en")]
    [InlineData("en-GB", "en")]
    public void Pick_FindsTheClosestLanguage(string culture, string expected) =>
        Localizer.Pick(CultureInfo.GetCultureInfo(culture), ["en", "nl"]).Should().Be(expected);

    [Fact]
    public void ABrokenFile_IsAnError()
    {
        var act = () => Localizer.Load("nl", "[1, 2]");

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void TheCheck_FindsTheTextsInTheCode()
    {
        var folder = Directory.CreateTempSubdirectory("jentl-tr-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "Menu.cs"), """
                var a = T("Play");
                var b = T("{0} wins", name);
                // T("Commented out")
                var c = N(count, "{0} move", "{0} moves");
                """);

            TranslationCheck.SourceTexts(folder).Should().BeEquivalentTo(["Play", "{0} wins", "{0} move", "{0} moves"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheCheck_ReportsMissingUnusedEmptyAndBrokenTranslations()
    {
        var folder = Directory.CreateTempSubdirectory("jentl-tr-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "nl.json"), """
                {
                  "// note": "skipped",
                  "Play": "Spelen",
                  "{0} wins": "wint",
                  "Old": "Oud",
                  "Classic": "Klassiek",
                  "Quit": ""
                }
                """);
            HashSet<string> used = ["Play", "{0} wins", "Quit", "Options"];

            TranslationCheck.Problems(used, folder, indirect: ["Classic"]).Should().BeEquivalentTo(
            [
                "nl: no translation of \"Options\"",
                "nl: \"Old\" is no longer used",
                "nl: the translation of \"Quit\" is empty",
                "nl: \"wint\" doesn't keep the placeholders of \"{0} wins\"",
            ]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
