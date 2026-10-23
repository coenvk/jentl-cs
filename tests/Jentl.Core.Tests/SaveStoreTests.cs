using Jentl.Core.Storage;

namespace Jentl.Core.Tests;

public sealed class SaveStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jentl-tests", Guid.NewGuid().ToString("N"));
    private readonly SaveStore _store;

    public SaveStoreTests() => _store = new SaveStore(Path.Combine(_directory, "app"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ReadText_MissingFile_ReturnsNull() =>
        _store.ReadText("save.json").Should().BeNull();

    [Fact]
    public void WriteText_CreatesDirectoryAndRoundTrips()
    {
        _store.WriteText("save.json", "one");
        _store.WriteText("save.json", "two");

        _store.ReadText("save.json").Should().Be("two");
        Directory.GetFiles(_store.Directory).Should().ContainSingle("the temporary file is replaced, not left behind");
    }

    [Fact]
    public void Quarantine_MovesFileAside()
    {
        _store.WriteText("save.json", "garbage");

        var moved = _store.Quarantine("save.json", new DateTimeOffset(2026, 10, 23, 9, 30, 0, TimeSpan.Zero));

        moved.Should().EndWith("save.json.corrupt-20261023-093000");
        File.ReadAllText(moved!).Should().Be("garbage");
        _store.ReadText("save.json").Should().BeNull();
    }

    [Fact]
    public void Quarantine_MissingFile_ReturnsNull() =>
        _store.Quarantine("save.json", DateTimeOffset.UnixEpoch).Should().BeNull();

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("sub/save.json")]
    public void PathOf_RejectsPaths(string name)
    {
        var act = () => _store.PathOf(name);

        act.Should().Throw<ArgumentException>();
    }
}
