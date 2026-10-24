namespace Jentl.Collect;

/// <summary>
/// A seed for <see cref="Random"/> from a piece of text (an event's id, a track's name) that is the same on every
/// platform and in every run, unlike <see cref="string.GetHashCode()"/>. FNV-1a over the UTF-16 code units, top bit
/// cleared.
/// </summary>
public static class StableSeed
{
    public static int Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash & 0x7fffffff);
    }
}
