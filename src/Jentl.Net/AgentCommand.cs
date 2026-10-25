namespace Jentl.Net;

/// <summary>What to run for an agent: the program, its arguments (passed as they are, no shell) and where to run it.</summary>
public sealed record AgentCommand(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory = null)
{
    // Where macOS installers and Homebrew put programs; an app started from the Finder does not have them on its PATH.
    private static readonly string[] MacOsFolders = ["/usr/local/bin", "/opt/homebrew/bin"];

    /// <summary>
    /// The folders a program named without a folder is looked for in: the PATH's, then on macOS <c>/usr/local/bin</c>
    /// and <c>/opt/homebrew/bin</c>, which an app started from the Finder does not get on its PATH.
    /// </summary>
    public static IReadOnlyList<string> SearchFolders()
    {
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return OperatingSystem.IsMacOS() ? [.. path, .. MacOsFolders.Except(path)] : path;
    }

    /// <summary>
    /// The full path of <paramref name="program"/> in the first of <paramref name="folders"/> that has it as a file that
    /// may be run (on Windows, also with <c>.exe</c>, <c>.cmd</c> or <c>.bat</c> added), or null when none has it.
    /// </summary>
    public static string? Find(string program, IEnumerable<string> folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        ArgumentNullException.ThrowIfNull(folders);
        string[] names = OperatingSystem.IsWindows() && !Path.HasExtension(program)
            ? [program + ".exe", program + ".cmd", program + ".bat", program]
            : [program];
        foreach (var folder in folders)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                if (CanRun(path))
                {
                    return Path.GetFullPath(path);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the .NET program <paramref name="dll"/> with <paramref name="arguments"/>: through its apphost (the program
    /// next to it with its name, without <c>.dll</c>) when there is one, as that finds the .NET runtime where it is
    /// installed even when <c>dotnet</c> is not on the PATH; otherwise as <c>dotnet <paramref name="dll"/></c>.
    /// </summary>
    public static AgentCommand DotNet(string dll, IReadOnlyList<string>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dll);
        arguments ??= [];
        var appHost = Path.ChangeExtension(dll, OperatingSystem.IsWindows() ? ".exe" : null);
        return CanRun(appHost) ? new AgentCommand(appHost, arguments) : new AgentCommand("dotnet", [dll, .. arguments]);
    }

    private static bool CanRun(string path) =>
        File.Exists(path) && (OperatingSystem.IsWindows()
            || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);
}
