using System.Text.Json;
using Jentl.Rpg.Scripting;

namespace Jentl.Rpg.Quests;

public enum ObjectiveKind
{
    /// <summary>Hold <see cref="ObjectiveDefinition.Count"/> of an item (checked live, so dropping it undoes the step).</summary>
    Collect,

    /// <summary>Defeat that many actors of a kind after the step begins.</summary>
    Kill,

    /// <summary>Talk to an actor, by its tag (map object name) or kind.</summary>
    Talk,

    /// <summary>Enter an area.</summary>
    Reach,

    /// <summary>A <see cref="Condition"/> holds (a flag, a variable...), checked live.</summary>
    Check,
}

/// <summary>One step of a quest. A quest's steps are done in order.</summary>
public sealed record ObjectiveDefinition(string Text, ObjectiveKind Kind, string Target, int Count, Condition? Condition = null);

/// <summary>
/// A quest from the quest file:
/// <code>
/// { "quests": [ { "id": "glowcaps", "title": "Glowcaps", "description": "...",
///   "objectives": [ { "text": "Gather three glowcaps", "collect": "glowcap", "count": 3 },
///                   { "text": "Bring them to Wren", "talk": "herbalist" } ],
///   "rewards": [ { "give": "potion", "count": 2 } ], "autoComplete": false } ] }
/// </code>
/// Objectives take one of collect, kill, talk, reach (an area id) or check (a condition). A quest with
/// <c>autoComplete</c> finishes as soon as its last step is done; otherwise content completes it, usually
/// from a dialogue (<c>{"completeQuest": "glowcaps"}</c>), so the reward is handed over in person.
/// </summary>
public sealed record QuestDefinition(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<ObjectiveDefinition> Objectives,
    IReadOnlyList<Effect> Rewards,
    bool AutoComplete)
{
    public static IReadOnlyList<QuestDefinition> FromJson(string json)
    {
        using var document = Json.Parse(json, "quests");
        return [.. document.RootElement.Array("quests").Select(Read)];
    }

    private static QuestDefinition Read(JsonElement e, int index)
    {
        var id = e.String("id", $"quests[{index}]");
        var where = $"quest '{id}'";
        var objectives = e.Array("objectives").Select(o => ReadObjective(o, where)).ToArray();
        return new QuestDefinition(
            id,
            e.OptionalString("title") ?? id,
            e.OptionalString("description") ?? "",
            objectives,
            Effect.List(e, "rewards", where),
            e.Bool("autoComplete", false));
    }

    private static ObjectiveDefinition ReadObjective(JsonElement e, string where)
    {
        var text = e.String("text", where);
        var count = Math.Max(1, e.Int("count", 1));
        foreach (var kind in new[] { ObjectiveKind.Collect, ObjectiveKind.Kill, ObjectiveKind.Talk, ObjectiveKind.Reach })
        {
            if (e.OptionalString(kind.ToString().ToLowerInvariant()) is { } target)
            {
                return new ObjectiveDefinition(text, kind, target, count);
            }
        }

        return e.TryGetProperty("check", out var check)
            ? new ObjectiveDefinition(text, ObjectiveKind.Check, "", 1, Condition.Parse(check, where))
            : throw new ContentException($"{where}: the objective \"{text}\" needs collect, kill, talk, reach or check.");
    }
}

public enum QuestStatus
{
    NotStarted,
    Active,
    Completed,
}

/// <summary>A quest's progress in the current game.</summary>
public sealed class Quest
{
    private readonly int[] _progress;

    internal Quest(QuestDefinition definition)
    {
        Definition = definition;
        _progress = new int[definition.Objectives.Count];
    }

    public QuestDefinition Definition { get; }

    public QuestStatus Status { get; internal set; }

    /// <summary>The step being worked on; equal to the number of objectives once all are done.</summary>
    public int Step { get; internal set; }

    public bool IsReady => Step >= Definition.Objectives.Count;

    public ObjectiveDefinition? CurrentObjective => IsReady ? null : Definition.Objectives[Step];

    /// <summary>How far along objective <paramref name="index"/> is, out of its count.</summary>
    public int Progress(int index) => index < Step ? Definition.Objectives[index].Count : _progress[index];

    internal void SetProgress(int index, int value) => _progress[index] = Math.Min(value, Definition.Objectives[index].Count);

    internal void Reset()
    {
        Status = QuestStatus.NotStarted;
        Step = 0;
        Array.Clear(_progress);
    }
}

/// <summary>Something that happened in the world that quest steps may be waiting for.</summary>
public abstract record QuestTrigger;

public sealed record KilledTrigger(string Kind) : QuestTrigger;

public sealed record TalkedTrigger(string Tag, string Kind) : QuestTrigger;

public sealed record ReachedTrigger(string Area) : QuestTrigger;

/// <summary>Every quest of the game and where the player is with each.</summary>
public sealed class QuestLog
{
    private readonly Dictionary<string, Quest> _quests = new(StringComparer.Ordinal);
    private readonly List<Quest> _order = [];

    public QuestLog(IEnumerable<QuestDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var definition in definitions)
        {
            var quest = new Quest(definition);
            if (!_quests.TryAdd(definition.Id, quest))
            {
                throw new ContentException($"The quest '{definition.Id}' is defined twice.");
            }

            _order.Add(quest);
        }
    }

    public Quest this[string id] =>
        _quests.TryGetValue(id, out var quest) ? quest : throw new ContentException($"There is no quest '{id}'.");

    public bool Contains(string id) => _quests.ContainsKey(id);

    public IEnumerable<Quest> All => _order;

    /// <summary>Started quests in definition order, active ones before completed ones.</summary>
    public IEnumerable<Quest> Started =>
        _order.Where(q => q.Status == QuestStatus.Active).Concat(_order.Where(q => q.Status == QuestStatus.Completed));

    internal bool Start(string id)
    {
        var quest = this[id];
        if (quest.Status != QuestStatus.NotStarted)
        {
            return false;
        }

        quest.Status = QuestStatus.Active;
        return true;
    }

    internal bool Complete(string id)
    {
        var quest = this[id];
        if (quest.Status == QuestStatus.Completed)
        {
            return false;
        }

        quest.Status = QuestStatus.Completed;
        quest.Step = quest.Definition.Objectives.Count;
        return true;
    }

    /// <summary>
    /// Advances quests waiting on <paramref name="trigger"/>, or (with null) re-checks the live objectives.
    /// Returns the quests whose step changed.
    /// </summary>
    internal IReadOnlyList<Quest> Update(RpgState state, QuestTrigger? trigger)
    {
        var changed = new List<Quest>();
        foreach (var quest in _order.Where(q => q.Status == QuestStatus.Active))
        {
            var before = quest.Step;

            // Several steps can complete at once, e.g. when the items were already in the bag.
            while (quest.CurrentObjective is { } objective)
            {
                var progress = Measure(state, objective, quest.Progress(quest.Step), trigger);
                quest.SetProgress(quest.Step, progress);
                if (progress < objective.Count)
                {
                    break;
                }

                quest.Step++;
                trigger = null; // one event completes at most one step
            }

            if (quest.Step != before)
            {
                changed.Add(quest);
            }
        }

        return changed;
    }

    private static int Measure(RpgState state, ObjectiveDefinition objective, int progress, QuestTrigger? trigger) => objective.Kind switch
    {
        ObjectiveKind.Collect => state.Inventory.Count(objective.Target),
        ObjectiveKind.Check => objective.Condition!.Test(state) ? 1 : 0,
        ObjectiveKind.Kill when trigger is KilledTrigger killed && killed.Kind == objective.Target => progress + 1,
        ObjectiveKind.Talk when trigger is TalkedTrigger talked && (talked.Tag == objective.Target || talked.Kind == objective.Target) => progress + 1,
        ObjectiveKind.Reach when trigger is ReachedTrigger reached && reached.Area == objective.Target => progress + 1,
        _ => progress,
    };
}
