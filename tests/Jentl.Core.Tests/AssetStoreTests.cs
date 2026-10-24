using Jentl.Core.Content;

namespace Jentl.Core.Tests;

/// <summary>The asset store's text side; textures need a graphics device, so the game checks those on screen.</summary>
public sealed class AssetStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("jentl-store-").FullName;
    private readonly AssetStore _store = new(null!);

    public AssetStoreTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Assets"));
        Write("Assets/data.txt", "first");
        _store.Watch(_folder);
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void ReadsFromTheWatchedFolder()
    {
        _store.WatchedFolder.Should().Be(Path.GetFullPath(_folder));
        _store.ReadText("Assets/data.txt").Should().Be("first");
    }

    [Fact]
    public void LoadsOnce_AndAgainAfterAChange()
    {
        var loads = 0;
        string Load() => _store.Get("Assets/data.txt", text =>
        {
            loads++;
            return text.ToUpperInvariant();
        });

        Load().Should().Be("FIRST");
        Load().Should().Be("FIRST");
        loads.Should().Be(1);

        Write("Assets/data.txt", "second");
        _store.Reload("Assets/data.txt");

        Load().Should().Be("SECOND");
        loads.Should().Be(2);
    }

    [Fact]
    public void Reloading_TellsTheGame()
    {
        var changed = new List<string>();
        _store.Changed += changed.Add;
        _ = _store.Get("Assets/data.txt", text => text);

        _store.Reload("Assets/data.txt");
        _store.Reload("Assets/lang/nl.json");
        _store.Reload("Art/notes.txt");

        changed.Should().Equal(["Assets/data.txt", "Assets/lang/nl.json"], "assets the game reads itself are announced too; files outside Assets aren't");
    }

    [Fact]
    public void Update_WithNothingChanged_DoesNothing()
    {
        var changed = 0;
        _store.Changed += _ => changed++;

        _store.Update();

        changed.Should().Be(0);
    }

    private void Write(string path, string text) => File.WriteAllText(Path.Combine(_folder, path), text);
}
