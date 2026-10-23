using Jentl.Rpg.Items;
using Jentl.Rpg.Quests;
using Jentl.Rpg.Scripting;
using Jentl.Rpg.World;

namespace Jentl.Rpg;

/// <summary>
/// The player's side of the game that content scripts read and change: inventory, equipment, quests,
/// story flags and counters. Conditions test it and effects change it; every change worth telling the
/// player is reported through <see cref="Said"/>.
/// </summary>
public sealed class RpgState
{
    private readonly Dictionary<string, int> _variables = new(StringComparer.Ordinal);

    public RpgState(ItemCatalog items, IEnumerable<QuestDefinition> quests, int inventoryCapacity = 20)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
        Quests = new QuestLog(quests);
        Inventory = new Inventory(inventoryCapacity);
    }

    /// <summary>A message for the player, such as "You receive a healing potion." or "New quest: ...".</summary>
    public event Action<string>? Said;

    public ItemCatalog Items { get; }

    public Inventory Inventory { get; }

    public Equipment Equipment { get; } = new();

    public QuestLog Quests { get; }

    /// <summary>Story flags: set once something has happened ("met_elder").</summary>
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Variables => _variables;

    /// <summary>The player character, for effects such as healing; set by the world.</summary>
    public Actor? Player { get; set; }

    /// <summary>Runs game-specific effects from content (any effect name the kit does not know).</summary>
    public Action<CustomEffect>? CustomEffect { get; set; }

    public int Variable(string name) => _variables.GetValueOrDefault(name);

    public void AddVariable(string name, int amount) => _variables[name] = Variable(name) + amount;

    public void Say(string message) => Said?.Invoke(message);

    public void Run(IEnumerable<Effect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        foreach (var effect in effects)
        {
            effect.Apply(this);
        }
    }

    /// <summary>Puts items in the bag; whatever does not fit is lost, with a message. Returns how many fitted.</summary>
    public int Give(string id, int count = 1)
    {
        var item = Items[id];
        var left = Inventory.Add(item, count);
        var given = count - left;
        if (given > 0)
        {
            Say($"You receive {Describe(item, given)}.");
        }

        if (left > 0)
        {
            Say($"Your bag is full: {Describe(item, left)} left behind.");
        }

        Refresh();
        return given;
    }

    public bool Take(string id, int count = 1)
    {
        if (!Inventory.Remove(id, count))
        {
            return false;
        }

        Say($"You hand over {Describe(Items[id], count)}.");
        Refresh();
        return true;
    }

    public void StartQuest(string id)
    {
        if (Quests.Start(id))
        {
            var quest = Quests[id];
            Say($"New quest: {quest.Definition.Title}");
            Report(Quests.Update(this, trigger: null));
        }
    }

    public void CompleteQuest(string id)
    {
        if (Quests.Complete(id))
        {
            var quest = Quests[id];
            Say($"Quest complete: {quest.Definition.Title}");
            Run(quest.Definition.Rewards);
        }
    }

    public void Heal(int amount)
    {
        if (Player is { } player)
        {
            var healed = player.Heal(amount);
            Say(healed > 0 ? $"You recover {healed} health." : "You are already at full health.");
        }
    }

    /// <summary>Lets quests react to something that happened (an actor killed, someone talked to, an area entered).</summary>
    public void Notify(QuestTrigger trigger) => Report(Quests.Update(this, trigger));

    /// <summary>Re-checks quest steps that depend on the state itself (items held, flags).</summary>
    public void Refresh() => Report(Quests.Update(this, trigger: null));

    /// <summary>The item's name, with the count when there is more than one: "Glowcap" or "3 x Glowcap".</summary>
    public static string Describe(ItemDefinition item, int count)
    {
        ArgumentNullException.ThrowIfNull(item);
        return count == 1 ? item.Name : $"{count} x {item.Name}";
    }

    private void Report(IReadOnlyList<Quest> changed)
    {
        foreach (var quest in changed)
        {
            if (quest.IsReady && quest.Definition.AutoComplete)
            {
                CompleteQuest(quest.Definition.Id);
            }
            else
            {
                Say($"{quest.Definition.Title}: {quest.CurrentObjective?.Text ?? "done"}");
            }
        }
    }
}
