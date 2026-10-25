namespace Jentl.Cards.Tests;

public class ChipsTests
{
    [Fact]
    public void Break_UsesTheFewestChips_LargestFirst() =>
        Chips.Break(1_637).Should().Equal(new ChipCount(1000, 1), new ChipCount(500, 1), new ChipCount(100, 1), new ChipCount(25, 1), new ChipCount(5, 2), new ChipCount(1, 2));

    [Fact]
    public void Break_OfNothing_IsEmpty() => Chips.Break(0).Should().BeEmpty();

    [Fact]
    public void Break_OfANegativeAmount_Throws() => ((Action)(() => Chips.Break(-1))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Stacks_SplitTallStacks() =>
        Chips.Stacks([new ChipCount(100, 23), new ChipCount(5, 2)], maxPerStack: 10).Should().Equal(new ChipStack(100, 10), new ChipStack(100, 10), new ChipStack(100, 3), new ChipStack(5, 2));
}
