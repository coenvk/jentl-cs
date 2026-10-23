using System.Text.Json;
using Jentl.Rpg.Scripting;

namespace Jentl.Rpg.Dialogue;

/// <summary>An answer the player can pick. Hidden unless <see cref="If"/> holds; runs <see cref="Effects"/> when picked.</summary>
public sealed record DialogueChoice(string Text, string? Next, Condition? If, IReadOnlyList<Effect> Effects);

/// <summary>Jumps to <see cref="Node"/> when <see cref="If"/> holds (or always, without a condition).</summary>
public sealed record DialogueRoute(Condition? If, string Node);

/// <summary>
/// One step of a conversation: a line spoken, then either choices, a next node, or the end. A node with
/// <see cref="Routes"/> says nothing itself and jumps to the first route whose condition holds; that is
/// how a conversation opens differently depending on quests and flags. <see cref="Effects"/> run when the
/// node is reached.
/// </summary>
public sealed record DialogueNode(
    string Id,
    string? Speaker,
    string? Text,
    IReadOnlyList<DialogueChoice> Choices,
    string? Next,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<DialogueRoute> Routes);

/// <summary>
/// A conversation, from a dialogue file:
/// <code>
/// { "dialogues": [ { "id": "elder", "start": "start", "nodes": {
///   "start": { "goto": [ { "if": { "quest": "amulet", "is": "completed" }, "node": "thanks" }, { "node": "hello" } ] },
///   "hello": { "speaker": "Elder Maren", "text": "Welcome, traveller.",
///              "choices": [ { "text": "Can I help?", "next": "ask", "do": [ { "startQuest": "amulet" } ] },
///                           { "text": "Goodbye." } ] },
///   ... } } ] }
/// </code>
/// A node's <c>speaker</c> defaults to the dialogue's. A choice without <c>next</c> ends the conversation;
/// so does a node without choices or <c>next</c>.
/// </summary>
public sealed record DialogueDefinition(string Id, string Start, IReadOnlyDictionary<string, DialogueNode> Nodes)
{
    public static IReadOnlyList<DialogueDefinition> FromJson(string json)
    {
        using var document = Json.Parse(json, "dialogues");
        return [.. document.RootElement.Array("dialogues").Select(Read)];
    }

    private static DialogueDefinition Read(JsonElement e, int index)
    {
        var id = e.String("id", $"dialogues[{index}]");
        var speaker = e.OptionalString("speaker");
        if (!e.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Object)
        {
            throw new ContentException($"dialogue '{id}': \"nodes\" must be an object of nodes by id.");
        }

        var read = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
        foreach (var property in nodes.EnumerateObject())
        {
            read[property.Name] = ReadNode(property.Name, property.Value, speaker, $"dialogue '{id}', node '{property.Name}'");
        }

        return new DialogueDefinition(id, e.OptionalString("start") ?? "start", read);
    }

    private static DialogueNode ReadNode(string id, JsonElement e, string? speaker, string where) => new(
        id,
        e.OptionalString("speaker") ?? speaker,
        e.OptionalString("text"),
        [.. e.Array("choices").Select(c => new DialogueChoice(
            c.String("text", where),
            c.OptionalString("next"),
            Condition.Optional(c, "if", where),
            Effect.List(c, "do", where)))],
        e.OptionalString("next"),
        Effect.List(e, "do", where),
        [.. e.Array("goto").Select(r => new DialogueRoute(Condition.Optional(r, "if", where), r.String("node", where)))]);
}

/// <summary>
/// Plays a conversation against the game state: shows a node's line and the choices whose conditions
/// hold, runs effects, and follows the player's picks until the conversation ends.
/// </summary>
public sealed class DialogueRunner
{
    // A conversation that keeps routing without ever saying anything is a content bug, not a game.
    private const int MaxRoutes = 64;

    private readonly RpgState _state;

    public DialogueRunner(DialogueDefinition dialogue, RpgState state)
    {
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(state);
        Dialogue = dialogue;
        _state = state;
        Enter(dialogue.Start);
    }

    public DialogueDefinition Dialogue { get; }

    /// <summary>The node being shown, or null once the conversation is over.</summary>
    public DialogueNode? Current { get; private set; }

    public bool IsOver => Current is null;

    /// <summary>The choices on offer now; empty when the player just continues.</summary>
    public IReadOnlyList<DialogueChoice> Choices { get; private set; } = [];

    /// <summary>Moves on from a node without choices.</summary>
    public void Continue()
    {
        if (Current is { } node && Choices.Count == 0)
        {
            Enter(node.Next);
        }
    }

    public void Choose(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Choices.Count);
        var choice = Choices[index];
        _state.Run(choice.Effects);
        Enter(choice.Next);
    }

    private void Enter(string? id)
    {
        for (var hops = 0; id is not null; hops++)
        {
            if (hops > MaxRoutes)
            {
                throw new ContentException($"dialogue '{Dialogue.Id}' keeps routing without saying anything (at node '{id}').");
            }

            if (!Dialogue.Nodes.TryGetValue(id, out var node))
            {
                throw new ContentException($"dialogue '{Dialogue.Id}' has no node '{id}'.");
            }

            _state.Run(node.Effects);
            if (node.Routes.Count > 0)
            {
                id = node.Routes.FirstOrDefault(r => r.If?.Test(_state) ?? true)?.Node;
                continue;
            }

            if (node.Text is null)
            {
                // A node that only does things passes straight on.
                id = node.Next;
                continue;
            }

            Current = node;
            Choices = [.. node.Choices.Where(c => c.If?.Test(_state) ?? true)];
            return;
        }

        Current = null;
        Choices = [];
    }
}
