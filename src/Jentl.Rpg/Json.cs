using System.Text.Json;

namespace Jentl.Rpg;

/// <summary>
/// Small helpers for reading content files. Content is read by hand from <see cref="JsonElement"/>s
/// rather than with reflection-based serialisation: that stays trim- and AOT-safe, and errors can say
/// exactly which entry of which file is wrong.
/// </summary>
internal static class Json
{
    public static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static JsonDocument Parse(string text, string what)
    {
        try
        {
            return JsonDocument.Parse(text, Options);
        }
        catch (JsonException e)
        {
            throw new ContentException($"{what}: {e.Message}", e);
        }
    }

    public static string String(this JsonElement element, string name, string where) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ContentException($"{where}: \"{name}\" must be a string.");

    public static string? OptionalString(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static int Int(this JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;

    public static double Double(this JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

    public static bool Bool(this JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    public static IEnumerable<JsonElement> Array(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
}

/// <summary>A content file (map, items, quests, dialogue) is malformed or refers to something that does not exist.</summary>
public sealed class ContentException : Exception
{
    public ContentException()
    {
    }

    public ContentException(string message)
        : base(message)
    {
    }

    public ContentException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
