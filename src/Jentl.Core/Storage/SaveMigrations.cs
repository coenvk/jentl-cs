using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jentl.Core.Storage;

/// <summary>
/// Brings a JSON save written by an older version of a game up to date, one version at a time. A save carries its
/// version in <see cref="Property"/> (a save without one is version 0); each migration changes the JSON of one
/// version into the next, so a save from any older version passes through every step after it. <see cref="Stamp"/>
/// writes the current version into a save, and <see cref="Upgrade(string)"/> runs the steps before the game parses it.
/// </summary>
public sealed class SaveMigrations
{
    private readonly Dictionary<int, Action<JsonObject>> _steps = [];

    /// <param name="current">The version the game writes now.</param>
    /// <param name="property">The top-level property that holds the version.</param>
    public SaveMigrations(int current, string property = "version")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentException.ThrowIfNullOrEmpty(property);
        Current = current;
        Property = property;
    }

    public int Current { get; }

    public string Property { get; }

    /// <summary>Adds the step from version <paramref name="from"/> to the next, which changes the save in place.</summary>
    public SaveMigrations Add(int from, Action<JsonObject> migrate)
    {
        ArgumentNullException.ThrowIfNull(migrate);
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, Current);
        if (!_steps.TryAdd(from, migrate))
        {
            throw new ArgumentException($"There is already a step from version {from}.", nameof(from));
        }

        return this;
    }

    /// <summary>The version a save was written with: its <see cref="Property"/>, or 0 without one.</summary>
    /// <exception cref="InvalidDataException">The version is not a whole number.</exception>
    public int VersionOf(JsonObject save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save[Property] is not { } node)
        {
            return 0;
        }

        return node is JsonValue value && value.TryGetValue<int>(out var version) && version >= 0
            ? version
            : throw new InvalidDataException($"The save's {Property} is not a version: {node.ToJsonString()}.");
    }

    /// <summary>The save as the current version wants it, as JSON text.</summary>
    /// <exception cref="InvalidDataException">
    /// The text is not a JSON object, the save is from a newer version than <see cref="Current"/>, a step is missing,
    /// or a step failed.
    /// </exception>
    public string Upgrade(string save)
    {
        ArgumentNullException.ThrowIfNull(save);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(save) as JsonObject ?? throw new InvalidDataException("A save must be a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The save is not valid JSON: {e.Message}", e);
        }

        return VersionOf(root) == Current ? save : Upgrade(root).ToJsonString();
    }

    /// <summary>Upgrades <paramref name="save"/> in place, stamping each version it reaches, and returns it.</summary>
    /// <exception cref="InvalidDataException">As <see cref="Upgrade(string)"/>.</exception>
    public JsonObject Upgrade(JsonObject save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var version = VersionOf(save);
        if (version > Current)
        {
            throw new InvalidDataException($"The save is from version {version}, newer than this game's {Current}.");
        }

        for (; version < Current; version++)
        {
            if (!_steps.TryGetValue(version, out var step))
            {
                throw new InvalidDataException($"A save from version {version} cannot be read: there is no step to {version + 1}.");
            }

            try
            {
                step(save);
            }
            catch (Exception e) when (e is not InvalidDataException and (InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException or NullReferenceException))
            {
                throw new InvalidDataException($"Upgrading the save from version {version}: {e.Message}", e);
            }

            save[Property] = version + 1;
        }

        return save;
    }

    /// <summary>The JSON object <paramref name="save"/> with <see cref="Property"/> set to <see cref="Current"/>.</summary>
    public string Stamp(string save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var root = JsonNode.Parse(save) as JsonObject ?? throw new ArgumentException("A save must be a JSON object.", nameof(save));
        root[Property] = Current;
        return root.ToJsonString();
    }
}
