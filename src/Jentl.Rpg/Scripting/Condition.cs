using System.Text.Json;
using Jentl.Rpg.Quests;

namespace Jentl.Rpg.Scripting;

/// <summary>What a piece of content refers to by id, for <see cref="ContentValidator"/>.</summary>
public readonly record struct ContentReference(string Kind, string Id)
{
    public const string Item = "item";
    public const string Quest = "quest";
}

/// <summary>
/// A test against the game state, written in content files as a small JSON object:
/// <c>{"flag": "met_elder"}</c>, <c>{"notFlag": "x"}</c>, <c>{"has": "glowcap", "count": 3}</c>,
/// <c>{"quest": "amulet", "is": "active"}</c> (notStarted, active, ready, completed),
/// <c>{"var": "gold", "atLeast": 10}</c>, <c>{"not": ...}</c>, <c>{"any": [...]}</c> or <c>{"all": [...]}</c>.
/// A JSON array means all of its conditions.
/// </summary>
public abstract record Condition
{
    public abstract bool Test(RpgState state);

    public virtual IEnumerable<ContentReference> References() => [];

    public static Condition Parse(JsonElement element, string where)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return new AllCondition([.. element.EnumerateArray().Select(e => Parse(e, where))]);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ContentException($"{where}: a condition must be an object or an array.");
        }

        if (element.OptionalString("flag") is { } flag)
        {
            return new FlagCondition(flag);
        }

        if (element.OptionalString("notFlag") is { } notFlag)
        {
            return new NotCondition(new FlagCondition(notFlag));
        }

        if (element.OptionalString("has") is { } item)
        {
            return new HasCondition(item, element.Int("count", 1));
        }

        if (element.OptionalString("quest") is { } quest)
        {
            var state = element.OptionalString("is") ?? "active";
            return Enum.TryParse<QuestCheck>(state, ignoreCase: true, out var check)
                ? new QuestCondition(quest, check)
                : throw new ContentException($"{where}: unknown quest state '{state}' (use notStarted, active, ready or completed).");
        }

        if (element.OptionalString("var") is { } variable)
        {
            return new VariableCondition(variable, element.Int("atLeast", int.MinValue), element.Int("atMost", int.MaxValue));
        }

        if (element.TryGetProperty("not", out var inner))
        {
            return new NotCondition(Parse(inner, where));
        }

        if (element.TryGetProperty("any", out var any))
        {
            return new AnyCondition([.. any.EnumerateArray().Select(e => Parse(e, where))]);
        }

        if (element.TryGetProperty("all", out var all))
        {
            return Parse(all, where);
        }

        throw new ContentException($"{where}: unknown condition {element.GetRawText()}.");
    }

    /// <summary>Reads an optional condition property ("if"); null when absent.</summary>
    internal static Condition? Optional(JsonElement element, string name, string where) =>
        element.TryGetProperty(name, out var value) ? Parse(value, where) : null;
}

public enum QuestCheck
{
    NotStarted,
    Active,

    /// <summary>Active with every objective done, waiting to be handed in.</summary>
    Ready,
    Completed,
}

public sealed record FlagCondition(string Flag) : Condition
{
    public override bool Test(RpgState state) => state.Flags.Contains(Flag);
}

public sealed record HasCondition(string Item, int Count) : Condition
{
    public override bool Test(RpgState state) => state.Inventory.Count(Item) >= Count;

    public override IEnumerable<ContentReference> References() => [new(ContentReference.Item, Item)];
}

public sealed record QuestCondition(string Quest, QuestCheck Check) : Condition
{
    public override bool Test(RpgState state)
    {
        var quest = state.Quests[Quest];
        return Check switch
        {
            QuestCheck.NotStarted => quest.Status == QuestStatus.NotStarted,
            QuestCheck.Active => quest.Status == QuestStatus.Active,
            QuestCheck.Ready => quest.Status == QuestStatus.Active && quest.IsReady,
            _ => quest.Status == QuestStatus.Completed,
        };
    }

    public override IEnumerable<ContentReference> References() => [new(ContentReference.Quest, Quest)];
}

public sealed record VariableCondition(string Variable, int AtLeast, int AtMost) : Condition
{
    public override bool Test(RpgState state) => state.Variable(Variable) is var value && value >= AtLeast && value <= AtMost;
}

public sealed record NotCondition(Condition Inner) : Condition
{
    public override bool Test(RpgState state) => !Inner.Test(state);

    public override IEnumerable<ContentReference> References() => Inner.References();
}

public sealed record AllCondition(IReadOnlyList<Condition> Conditions) : Condition
{
    public override bool Test(RpgState state) => Conditions.All(c => c.Test(state));

    public override IEnumerable<ContentReference> References() => Conditions.SelectMany(c => c.References());
}

public sealed record AnyCondition(IReadOnlyList<Condition> Conditions) : Condition
{
    public override bool Test(RpgState state) => Conditions.Any(c => c.Test(state));

    public override IEnumerable<ContentReference> References() => Conditions.SelectMany(c => c.References());
}
