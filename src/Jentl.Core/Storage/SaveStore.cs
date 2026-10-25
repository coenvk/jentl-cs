using System.Globalization;

namespace Jentl.Core.Storage;

/// <summary>
/// Reads and writes small save files in one directory. Writes go to a temporary file that
/// then replaces the original, so a crash mid-write never leaves a half-written save.
/// </summary>
public sealed class SaveStore : ISaveStore
{
    public SaveStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>
    /// A store in the per-user application data folder, e.g. <c>~/Library/Application Support/&lt;app&gt;</c>
    /// on macOS or <c>%LOCALAPPDATA%\&lt;app&gt;</c> on Windows.
    /// </summary>
    public static SaveStore ForApp(string appName) =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            appName));

    public string PathOf(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Path.GetFileName(name) != name)
        {
            throw new ArgumentException($"'{name}' must be a plain file name.", nameof(name));
        }

        return Path.Combine(Directory, name);
    }

    public string? ReadText(string name)
    {
        var path = PathOf(name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void WriteText(string name, string contents)
    {
        var path = PathOf(name);
        System.IO.Directory.CreateDirectory(Directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string name) => File.Delete(PathOf(name));

    /// <summary>
    /// Moves the named folders over from <paramref name="from"/>'s directory (another app's, when a game moved out of
    /// it), each only when <paramref name="from"/> has it and this store has none yet: nothing is merged or overwritten.
    /// </summary>
    /// <returns>The names of the folders that were moved.</returns>
    public IReadOnlyList<string> MoveFolders(SaveStore from, params IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(from);
        List<string> moved = [];
        foreach (var name in names)
        {
            var source = Path.Combine(from.Directory, name);
            var target = Path.Combine(Directory, name);
            if (System.IO.Directory.Exists(source) && !System.IO.Directory.Exists(target))
            {
                System.IO.Directory.CreateDirectory(Directory);
                System.IO.Directory.Move(source, target);
                moved.Add(name);
            }
        }

        return moved;
    }

    public string? Quarantine(string name, DateTimeOffset now)
    {
        var path = PathOf(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var target = $"{path}.corrupt-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        File.Move(path, target, overwrite: true);
        return target;
    }
}
