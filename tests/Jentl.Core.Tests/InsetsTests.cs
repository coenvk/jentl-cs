using Microsoft.Xna.Framework;

namespace Jentl.Core.Tests;

public class InsetsTests
{
    [Fact]
    public void Insets_LeaveRoomAtEachEdge() =>
        new Insets(10, 60, 20, 90).Inside(new Rectangle(0, 0, 1080, 2400)).Should().Be(new Rectangle(10, 60, 1050, 2250));

    [Fact]
    public void None_LeavesTheWindowAsItIs() =>
        Insets.None.Inside(new Rectangle(5, 5, 100, 50)).Should().Be(new Rectangle(5, 5, 100, 50));

    [Fact]
    public void InsetsWiderThanTheWindow_LeaveNothing_NotLessThanNothing() =>
        new Insets(80, 0, 80, 0).Inside(new Rectangle(0, 0, 100, 100)).Width.Should().Be(0);
}
