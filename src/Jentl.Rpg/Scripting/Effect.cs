using System.Text.Json;

namespace Jentl.Rpg.Scripting;

/// <summary>
/// A change to the game state, written in content files as a small JSON object named after what it does:
/// <c>{"setFlag": "x"}</c>, <c>{"clearFlag": "x"}</c>, <c>{"addVar": "gold", "amount": 5}</c>,
/// <c>{"give": "potion", "count": 2}</c>, <c>{"take": "glowcap", "count": 3}</c>, <c>{"startQuest": "q"}</c>,
/// <c>{"completeQuest": "q"}</c>, <c>{"heal": 10}</c>, <c>{"message": "text"}</c> and
/// <c>{"if": condition, "then": [...], "else": [...]}</c>. Any other name becomes a <see cref="CustomEffect"/>
/// that the game handles through <see cref="RpgState.CustomEffect"/>.
/// </summary>
public abstract record Effect
{
    public abstract void Apply(RpgState state);

    public virtual IEnumerable<ContentReference> References() => [];

    public static Effect Parse(JsonElement element, string where)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ContentException($"{where}: an effect must be an object.");
        }

        if (element.TryGetProperty("if", out var test))
        {
            return new IfEffect(Condition.Parse(test, where), List(element, "then", where), List(element, "else", where));
        }

        if (element.EnumerateObject().FirstOrDefault() is not { Name: not null } first)
        {
            throw new ContentException($"{where}: an effect needs a name, such as \"give\".");
        }

        var count = element.Int("count", 1);
        return first.Name switch
        {
            "setFlag" => new SetFlagEffect(Text(first, where), Set: true),
            "clearFlag" => new SetFlagEffect(Text(first, where), Set: false),
            "addVar" => new AddVariableEffect(Text(first, where), element.Int("amount", 1)),
            "give" => new GiveEffect(Text(first, where), count),
            "take" => new TakeEffect(Text(first, where), count),
            "startQuest" => new StartQuestEffect(Text(first, where)),
            "completeQuest" => new CompleteQuestEffect(Text(first, where)),
            "heal" => first.Value.ValueKind == JsonValueKind.Number
                ? new HealEffect(first.Value.GetInt32())
                : throw new ContentException($"{where}: \"heal\" takes a number."),
            "message" => new MessageEffect(Text(first, where)),
            _ => new CustomEffect(first.Name, element.Clone()),
        };
    }

    /// <summary>Reads an optional list of effects ("do"); empty when absent.</summary>
    public static IReadOnlyList<Effect> List(JsonElement element, string name, string where) =>
        [.. element.Array(name).Select(e => Parse(e, where))];

    private static string Text(JsonProperty property, string where) =>
        property.Value.ValueKind == JsonValueKind.String
            ? property.Value.GetString()!
            : throw new ContentException($"{where}: \"{property.Name}\" takes a string.");
}

public sealed record SetFlagEffect(string Flag, bool Set) : Effect
{
    public override void Apply(RpgState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Set)
        {
            state.Flags.Add(Flag);
        }
        else
        {
            state.Flags.Remove(Flag);
        }
    }
}

public sealed record AddVariableEffect(string Variable, int Amount) : Effect
{
    public override void Apply(RpgState state) => state.AddVariable(Variable, Amount);
}

public sealed record GiveEffect(string Item, int Count) : Effect
{
    public override void Apply(RpgState state) => state.Give(Item, Count);

    public override IEnumerable<ContentReference> References() => [new(ContentReference.Item, Item)];
}

public sealed record TakeEffect(string Item, int Count) : Effect
{
    public override void Apply(RpgState state) => state.Take(Item, Count);

    public override IEnumerable<ContentReference> References() => [new(ContentReference.Item, Item)];
}

public sealed record StartQuestEffect(string Quest) : Effect
{
    public override void Apply(RpgState state) => state.StartQuest(Quest);

    public override IEnumerable<ContentReference> References() => [new(ContentReference.Quest, Quest)];
}

public sealed record CompleteQuestEffect(string Quest) : Effect
{
    public override void Apply(RpgState state) => state.CompleteQuest(Quest);

    public override IEnumerable<ContentReference> References() => [new(ContentReference.Quest, Quest)];
}

public sealed record HealEffect(int Amount) : Effect
{
    public override void Apply(RpgState state) => state.Heal(Amount);
}

public sealed record MessageEffect(string Text) : Effect
{
    public override void Apply(RpgState state) => state.Say(Text);
}

public sealed record IfEffect(Condition Test, IReadOnlyList<Effect> Then, IReadOnlyList<Effect> Else) : Effect
{
    public override void Apply(RpgState state)
    {
        foreach (var effect in Test.Test(state) ? Then : Else)
        {
            effect.Apply(state);
        }
    }

    public override IEnumerable<ContentReference> References() =>
        Test.References().Concat(Then.SelectMany(e => e.References())).Concat(Else.SelectMany(e => e.References()));
}

/// <summary>A game-specific effect; <see cref="Arguments"/> is the whole JSON object it was written as.</summary>
public sealed record CustomEffect(string Name, JsonElement Arguments) : Effect
{
    public override void Apply(RpgState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CustomEffect is not { } handler)
        {
            throw new InvalidOperationException($"No handler for the custom effect '{Name}'; set RpgState.CustomEffect.");
        }

        handler(this);
    }
}
