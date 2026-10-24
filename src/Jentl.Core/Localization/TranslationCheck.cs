using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jentl.Core.Localization;

/// <summary>
/// Checks a game's language files against its source code, for a test: every text the code asks for
/// (<c>T("...")</c>, and both forms of <c>N(count, "...", "...")</c>) has a translation, no translation lingers that
/// the code no longer uses, and every translation keeps the placeholders (<c>{0}</c>, <c>{1:N0}</c>) of its text.
/// </summary>
public static partial class TranslationCheck
{
    /// <summary>The texts the C# files under <paramref name="sourceFolder"/> ask to translate (comments and build output skipped).</summary>
    public static IReadOnlySet<string> SourceTexts(string sourceFolder)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal) && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal)))
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

    /// <summary>
    /// What's wrong with each language file (<c>&lt;language&gt;.json</c>) in <paramref name="languageFolder"/>, given the
    /// texts the game uses: missing, unused (other than <paramref name="indirect"/>, texts looked up from data), empty,
    /// or with different placeholders. Empty when all is well.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlySet<string> used, string languageFolder, IEnumerable<string>? indirect = null)
    {
        ArgumentNullException.ThrowIfNull(used);
        var extra = indirect?.ToHashSet(StringComparer.Ordinal) ?? [];
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(languageFolder, "*.json").Order(StringComparer.Ordinal))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            Dictionary<string, string> texts;
            try
            {
                texts = JsonSerializer.Deserialize(File.ReadAllText(file), LocalizationJson.Default.DictionaryStringString) ?? [];
            }
            catch (JsonException e)
            {
                problems.Add($"{language}: not a JSON object of texts ({e.Message})");
                continue;
            }

            var translations = texts.Where(t => !t.Key.StartsWith("//", StringComparison.Ordinal)).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
            problems.AddRange(used.Where(t => !translations.ContainsKey(t)).Order(StringComparer.Ordinal).Select(t => $"{language}: no translation of \"{t}\""));
            problems.AddRange(translations.Keys.Where(t => !used.Contains(t) && !extra.Contains(t)).Order(StringComparer.Ordinal).Select(t => $"{language}: \"{t}\" is no longer used"));
            foreach (var (english, translated) in translations.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                if (translated.Length == 0)
                {
                    problems.Add($"{language}: the translation of \"{english}\" is empty");
                }
                else if (!Placeholders(english).SequenceEqual(Placeholders(translated)))
                {
                    problems.Add($"{language}: \"{translated}\" doesn't keep the placeholders of \"{english}\"");
                }
            }
        }

        return problems;
    }

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\bT\(\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex TextCall();

    [GeneratedRegex(@"\bN\(\s*[^,""]+?,\s*""((?:[^""\\]|\\.)*)"",\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex PluralCall();

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();
}
