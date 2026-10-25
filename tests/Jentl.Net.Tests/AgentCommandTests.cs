namespace Jentl.Net.Tests;

public sealed class AgentCommandTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("agents").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Find_TakesTheFirstFolderThatHasTheProgram()
    {
        var first = Directory.CreateDirectory(Path.Combine(_folder, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_folder, "second")).FullName;
        var program = Program(second, "my-bot");
        Program(Directory.CreateDirectory(Path.Combine(_folder, "third")).FullName, "my-bot");

        AgentCommand.Find("my-bot", [first, Path.Combine(_folder, "nowhere"), second, Path.Combine(_folder, "third")]).Should().Be(program);
        AgentCommand.Find("other-bot", [first, second]).Should().BeNull();
    }

    [Fact]
    public void Find_SkipsAFileThatCannotBeRun()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_folder, "notes");
        File.WriteAllText(file, "");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AgentCommand.Find("notes", [_folder]).Should().BeNull();
    }

    [Fact]
    public void TheSearchFolders_AreThePath_AndOnMacOsTheUsualInstallFolders()
    {
        var folders = AgentCommand.SearchFolders();
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        folders.Take(path.Length).Should().Equal(path);
        if (OperatingSystem.IsMacOS())
        {
            folders.Should().Contain(["/usr/local/bin", "/opt/homebrew/bin"]);
        }
    }

    [Fact]
    public void ADotNetProgram_RunsThroughItsAppHost_WhenItHasOne()
    {
        var dll = Path.Combine(_folder, "Host.dll");
        File.WriteAllText(dll, "");
        AgentCommand.DotNet(dll, ["bot.dll"]).Should().BeEquivalentTo(new AgentCommand("dotnet", [dll, "bot.dll"]));

        var appHost = Program(_folder, "Host");
        AgentCommand.DotNet(dll, ["bot.dll"]).Should().BeEquivalentTo(new AgentCommand(appHost, ["bot.dll"]));
    }

    // An empty file that may be run: an .exe on Windows, the execute bit elsewhere.
    private static string Program(string folder, string name)
    {
        var path = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
