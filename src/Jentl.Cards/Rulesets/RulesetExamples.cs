namespace Jentl.Cards.Rulesets;

/// <summary>
/// Rulesets that come with Jentl, to play as they are or to copy and change: the solitaires <c>klondike</c>,
/// <c>freecell</c>, <c>spider</c>, <c>golf</c>, <c>tripeaks</c> (overlapping cards), <c>yukon</c> and <c>fortythieves</c>, and for several players
/// <c>crazyeights</c> (the standard deck), <c>uno</c> (Uno's) and <c>maumau</c> (a 32-card deck of its own, with
/// a script for its stacking sevens).
/// </summary>
public static class RulesetExamples
{
    private const string Prefix = "Jentl.Cards.Rulesets.";

    /// <summary>The examples' names, as <see cref="Load"/> takes them.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. typeof(RulesetExamples).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..^".json".Length])
        .Order(StringComparer.Ordinal)];

    /// <summary>An example's JSON, to show or start a new ruleset from.</summary>
    public static string Text(string name)
    {
        using var stream = typeof(RulesetExamples).Assembly.GetManifestResourceStream($"{Prefix}{name}.json")
            ?? throw new ArgumentException($"There is no example ruleset '{name}' (there are {string.Join(", ", Names)}).", nameof(name));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static CardRuleset Load(string name) => CardRuleset.Parse(Text(name));
}
