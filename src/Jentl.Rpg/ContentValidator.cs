using Jentl.Rpg.Dialogue;
using Jentl.Rpg.Maps;
using Jentl.Rpg.Quests;
using Jentl.Rpg.Scripting;
using Jentl.Rpg.World;

namespace Jentl.Rpg;

/// <summary>
/// Finds broken references in content before a player does: dialogue nodes that do not exist, effects
/// giving unknown items, quests that count unknown monsters, map objects of unknown kinds. Meant for a
/// unit test over the game's content files.
/// </summary>
public static class ContentValidator
{
    public static IReadOnlyList<string> Validate(RpgContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var problems = new List<string>();

        foreach (var item in content.Items.All)
        {
            Check(content, $"item '{item.Id}'", item.Use.SelectMany(e => e.References()), problems);
        }

        foreach (var actor in content.Actors.All)
        {
            var where = $"actor '{actor.Id}'";
            problems.AddRange(actor.Drops.Where(d => !content.Items.Contains(d.Item)).Select(d => $"{where} drops unknown item '{d.Item}'."));
            if (actor.Dialogue is { } dialogue && !content.Dialogues.ContainsKey(dialogue))
            {
                problems.Add($"{where} has unknown dialogue '{dialogue}'.");
            }

            if (!Brains.IsKnown(actor.Brain))
            {
                problems.Add($"{where} has unknown brain '{actor.Brain}'.");
            }
        }

        foreach (var quest in content.Quests)
        {
            var where = $"quest '{quest.Id}'";
            Check(content, where, quest.Rewards.SelectMany(e => e.References()), problems);
            foreach (var objective in quest.Objectives)
            {
                switch (objective.Kind)
                {
                    case ObjectiveKind.Collect when !content.Items.Contains(objective.Target):
                        problems.Add($"{where} collects unknown item '{objective.Target}'.");
                        break;
                    case ObjectiveKind.Kill when !content.Actors.Contains(objective.Target):
                        problems.Add($"{where} counts unknown actor '{objective.Target}'.");
                        break;
                    case ObjectiveKind.Check:
                        Check(content, where, objective.Condition!.References(), problems);
                        break;
                }
            }
        }

        foreach (var dialogue in content.Dialogues.Values)
        {
            Validate(content, dialogue, problems);
        }

        return problems;
    }

    /// <summary>Checks a map's objects against the content: actor kinds, items, dialogues and brains.</summary>
    public static IReadOnlyList<string> Validate(RpgContent content, TileMap map, string id)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        var problems = new List<string>();
        foreach (var o in map.Objects)
        {
            var where = $"map '{id}', {o}";
            switch (o.Type)
            {
                case "actor":
                    var kind = o.Properties.GetString("kind") ?? o.Name;
                    if (!content.Actors.Contains(kind))
                    {
                        problems.Add($"{where}: unknown actor kind '{kind}'.");
                    }

                    if (o.Properties.GetString("dialogue") is { } dialogue && !content.Dialogues.ContainsKey(dialogue))
                    {
                        problems.Add($"{where}: unknown dialogue '{dialogue}'.");
                    }

                    if (o.Properties.GetString("brain") is { } brain && !Brains.IsKnown(brain))
                    {
                        problems.Add($"{where}: unknown brain '{brain}'.");
                    }

                    break;
                case "item" when !content.Items.Contains(o.Properties.GetString("item") ?? o.Name):
                    problems.Add($"{where}: unknown item '{o.Properties.GetString("item") ?? o.Name}'.");
                    break;
                case "portal" when o.Properties.GetString("area") is null:
                    problems.Add($"{where}: a portal needs an 'area' property.");
                    break;
            }
        }

        return problems;
    }

    private static void Validate(RpgContent content, DialogueDefinition dialogue, List<string> problems)
    {
        var where = $"dialogue '{dialogue.Id}'";
        void Node(string? id, string from)
        {
            if (id is not null && !dialogue.Nodes.ContainsKey(id))
            {
                problems.Add($"{where}: {from} leads to unknown node '{id}'.");
            }
        }

        Node(dialogue.Start, "the start");
        foreach (var node in dialogue.Nodes.Values)
        {
            var at = $"node '{node.Id}'";
            Node(node.Next, at);
            var references = node.Effects.SelectMany(e => e.References());
            foreach (var route in node.Routes)
            {
                Node(route.Node, at);
                references = references.Concat(route.If?.References() ?? []);
            }

            foreach (var choice in node.Choices)
            {
                Node(choice.Next, $"{at}, choice \"{choice.Text}\"");
                references = references.Concat(choice.Effects.SelectMany(e => e.References())).Concat(choice.If?.References() ?? []);
            }

            if (node.Text is null && node.Routes.Count == 0 && node.Next is null && node.Effects.Count == 0)
            {
                problems.Add($"{where}: {at} is empty.");
            }

            Check(content, $"{where}, {at}", references, problems);
        }
    }

    private static void Check(RpgContent content, string where, IEnumerable<ContentReference> references, List<string> problems)
    {
        foreach (var reference in references.Distinct())
        {
            var known = reference.Kind switch
            {
                ContentReference.Item => content.Items.Contains(reference.Id),
                ContentReference.Quest => content.Quests.Any(q => q.Id == reference.Id),
                _ => true,
            };
            if (!known)
            {
                problems.Add($"{where} refers to unknown {reference.Kind} '{reference.Id}'.");
            }
        }
    }
}
