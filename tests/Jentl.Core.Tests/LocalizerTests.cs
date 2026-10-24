using System.Globalization;
using System.Text.RegularExpressions;
using Jentl.Core.Localization;

namespace Jentl.Core.Tests;

public partial class LocalizerTests
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

    /// <summary>
    /// The app's language files against its source: every text the code asks for (<c>T("...")</c>, and both forms
    /// of <c>N(count, "...", "...")</c>) has a translation, every translation is still used, and translations keep
    /// the same placeholders.
    /// </summary>
    [Fact]
    public void TheAppsTranslations_AreCompleteAndCurrent()
    {
        var root = RepositoryRoot();
        var app = Path.Combine(root, "src", "Sokoban.Desktop");
        var used = SourceTexts(app);
        used.Should().Contain(["Select level", "{0} move", "{0} moves"], "the scan finds the texts");

        // Looked up from data rather than written in the code: the level packs' names and title prefix.
        string[] indirect = ["Classic", "Ice", "Level"];
        foreach (var file in Directory.GetFiles(Path.Combine(app, "Assets", "lang"), "*.json"))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            var texts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!
                .Where(t => !t.Key.StartsWith("//", StringComparison.Ordinal))
                .ToDictionary(t => t.Key, t => t.Value);
            Localizer.Load(language, File.ReadAllText(file)).Count.Should().Be(texts.Count, $"{language} has no empty translations");

            used.Except(texts.Keys).Should().BeEmpty($"every text needs a translation in {language}.json");
            texts.Keys.Except(used).Except(indirect).Should().BeEmpty($"{language}.json should not keep texts the game no longer uses");
            foreach (var (english, translated) in texts)
            {
                Placeholders(translated).Should().BeEquivalentTo(Placeholders(english), $"\"{translated}\" ({language}) must keep the placeholders of \"{english}\"");
            }
        }
    }

    private static HashSet<string> SourceTexts(string folder)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var code = string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (Match match in TextCall().Matches(code))
            {
                texts.Add(match.Groups[1].Value);
            }

            foreach (Match match in PluralCall().Matches(code))
            {
                texts.Add(match.Groups[1].Value);
                texts.Add(match.Groups[2].Value);
            }
        }

        return texts;
    }

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)];

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sokoban.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root (with Sokoban.slnx) was not found.");
    }

    [GeneratedRegex(@"\bT\(\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex TextCall();

    [GeneratedRegex(@"\bN\(\s*[^,""]+?,\s*""((?:[^""\\]|\\.)*)"",\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex PluralCall();

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();
}
