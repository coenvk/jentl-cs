using System.Globalization;

namespace Jentl.Rpg.Maps;

/// <summary>Custom properties from a map editor (Tiled): string values with typed readers.</summary>
public sealed class Properties
{
    private readonly Dictionary<string, string> _values;

    public Properties(IReadOnlyDictionary<string, string>? values = null) =>
        _values = new Dictionary<string, string>(values ?? new Dictionary<string, string>(), StringComparer.Ordinal);

    public static Properties Empty { get; } = new();

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool Has(string name) => _values.ContainsKey(name);

    public string? GetString(string name) => _values.GetValueOrDefault(name);

    public string GetString(string name, string fallback) => _values.GetValueOrDefault(name) ?? fallback;

    public bool GetBool(string name, bool fallback = false) =>
        _values.TryGetValue(name, out var value) ? bool.TryParse(value, out var parsed) ? parsed : value == "1" : fallback;

    public int GetInt(string name, int fallback = 0) =>
        _values.TryGetValue(name, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public float GetFloat(string name, float fallback = 0) =>
        _values.TryGetValue(name, out var value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public override string ToString() => string.Join(", ", _values.Select(p => $"{p.Key}={p.Value}"));
}
