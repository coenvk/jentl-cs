using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Jentl.Core.Localization;

/// <summary>
/// Translates the game's text, gettext style: the English text is the key, so code reads naturally
/// (<c>T("Select level")</c>) and anything not yet translated simply shows in English. A language is a JSON object
/// from English to translated text. Arguments go in <c>{0}</c>, <c>{1}</c>... as for <see cref="string.Format(IFormatProvider, string, object[])"/>,
/// formatted the language's way (<c>1.234</c> in Dutch, <c>1,234</c> in English).
/// </summary>
/// <example>
/// <code>
/// var nl = Localizer.Load("nl", File.ReadAllText("nl.json"));   // {"Play": "Spelen", "{0} wins": "{0} wint"}
/// nl["Play"];                                       // "Spelen"
/// nl.Format("{0} wins", "Ruby");                    // "Ruby wint"
/// nl.Plural(3, "{0} move", "{0} moves");            // "3 zetten" (via the translation of "{0} moves")
/// </code>
/// </example>
public sealed class Localizer
{
    private readonly IReadOnlyDictionary<string, string> _texts;
    private readonly ConcurrentDictionary<string, bool> _missing = new(StringComparer.Ordinal);

    public Localizer(string language, IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(language);
        Language = language;
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        Culture = CultureFor(language);
    }

    /// <summary>The game's own text: nothing to translate.</summary>
    public static Localizer English { get; } = new("en", new Dictionary<string, string>());

    /// <summary>A language code: "en", "nl".</summary>
    public string Language { get; }

    /// <summary>How numbers and dates are written in this language.</summary>
    public CultureInfo Culture { get; }

    /// <summary>How many texts this language translates.</summary>
    public int Count => _texts.Count;

    /// <summary>Texts asked for that this language doesn't translate (always none for English), for spotting gaps.</summary>
    public IReadOnlyCollection<string> Missing => [.. _missing.Keys];

    /// <summary>The translation of <paramref name="text"/>, or the text itself if there is none.</summary>
    public string this[string text]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(text);
            if (_texts.TryGetValue(text, out var translated))
            {
                return translated;
            }

            if (Language != "en")
            {
                _missing.TryAdd(text, true);
            }

            return text;
        }
    }

    /// <summary>Translates <paramref name="text"/>, then fills in <paramref name="args"/>.</summary>
    public string Format(string text, params object?[] args) => string.Format(Culture, this[text], args);

    /// <summary>
    /// The singular or plural text for <paramref name="count"/> (as <c>{0}</c>), translated, with any further
    /// <paramref name="args"/> as <c>{1}</c>, <c>{2}</c>...: <c>Plural(n, "{0} move", "{0} moves")</c>.
    /// </summary>
    public string Plural(int count, string one, string other, params object?[] args) =>
        string.Format(Culture, this[IsOne(count) ? one : other], [count, .. args]);

    /// <summary>
    /// Reads a language file: a JSON object of English text to translation. Keys starting with <c>//</c> are
    /// comments for translators; empty translations count as missing.
    /// </summary>
    /// <exception cref="JsonException">The file isn't such an object.</exception>
    public static Localizer Load(string language, string json)
    {
        var texts = JsonSerializer.Deserialize(json, LocalizationJson.Default.DictionaryStringString)
            ?? throw new JsonException("A language file must be a JSON object.");
        return new Localizer(language, texts.Where(t => !t.Key.StartsWith("//", StringComparison.Ordinal) && t.Value.Length > 0)
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal));
    }

    /// <summary>The best of <paramref name="available"/> for someone who reads <paramref name="culture"/> ("nl-BE" → "nl"), else English.</summary>
    public static string Pick(CultureInfo culture, IEnumerable<string> available)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var languages = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (languages.Contains(c.Name))
            {
                return c.Name.ToLowerInvariant();
            }

            if (languages.Contains(c.TwoLetterISOLanguageName))
            {
                return c.TwoLetterISOLanguageName;
            }
        }

        return "en";
    }

    // Which counts take the singular. True for English, Dutch, German and the other one/other languages;
    // French also uses it for 0. Languages with more forms would need more than two texts.
    private bool IsOne(int count) => Language == "fr" ? count is 0 or 1 : count == 1;

    private static CultureInfo CultureFor(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocalizationJson : System.Text.Json.Serialization.JsonSerializerContext;
