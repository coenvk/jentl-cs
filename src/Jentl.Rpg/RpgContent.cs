using Jentl.Rpg.Dialogue;
using Jentl.Rpg.Items;
using Jentl.Rpg.Quests;
using Jentl.Rpg.World;

namespace Jentl.Rpg;

/// <summary>Everything a game defines in content files: items, actors, quests and dialogues.</summary>
public sealed class RpgContent
{
    private readonly Dictionary<string, DialogueDefinition> _dialogues = new(StringComparer.Ordinal);

    public RpgContent(ItemCatalog items, ActorCatalog actors, IReadOnlyList<QuestDefinition> quests, IEnumerable<DialogueDefinition> dialogues)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(dialogues);
        Items = items;
        Actors = actors;
        Quests = quests;
        foreach (var dialogue in dialogues)
        {
            if (!_dialogues.TryAdd(dialogue.Id, dialogue))
            {
                throw new ContentException($"The dialogue '{dialogue.Id}' is defined twice.");
            }
        }
    }

    public ItemCatalog Items { get; }

    public ActorCatalog Actors { get; }

    public IReadOnlyList<QuestDefinition> Quests { get; }

    public IReadOnlyDictionary<string, DialogueDefinition> Dialogues => _dialogues;

    /// <summary>Reads the content files' text; any number of dialogue files.</summary>
    public static RpgContent FromJson(string items, string actors, string quests, params string[] dialogues)
    {
        ArgumentNullException.ThrowIfNull(dialogues);
        return new RpgContent(
            ItemCatalog.FromJson(items),
            new ActorCatalog(ActorDefinition.FromJson(actors)),
            QuestDefinition.FromJson(quests),
            dialogues.SelectMany(DialogueDefinition.FromJson));
    }

    public DialogueDefinition Dialogue(string id) =>
        _dialogues.TryGetValue(id, out var dialogue) ? dialogue : throw new ContentException($"There is no dialogue '{id}'.");
}
