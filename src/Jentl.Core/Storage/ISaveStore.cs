namespace Jentl.Core.Storage;

/// <summary>Small named save files: on disk for desktop, in browser storage for the web.</summary>
public interface ISaveStore
{
    /// <summary>Returns the file's contents, or null if it does not exist.</summary>
    string? ReadText(string name);

    void WriteText(string name, string contents);

    /// <summary>Removes the file, if there is one.</summary>
    void Delete(string name);

    /// <summary>Moves an unreadable file aside (keeping it for inspection) and returns where it went.</summary>
    string? Quarantine(string name, DateTimeOffset now);
}
