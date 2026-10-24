using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Tests;

public class PostProcessingTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public void Glow_FadesFromTheCentreToTheEdge(float distance, float expected) =>
        Gradients.Glow(distance).Should().BeApproximately(expected, 0.001f);

    [Fact]
    public void Glow_FallsOffSmoothly()
    {
        var values = Enumerable.Range(0, 11).Select(i => Gradients.Glow(i / 10f)).ToList();

        values.Should().BeInDescendingOrder();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.4f, 1)]
    [InlineData(1, 0.6f)]
    public void Vignette_LeavesTheMiddle_AndDarkensTheCorners(float distance, float expected) =>
        Gradients.Vignette(distance, strength: 0.4f).Should().BeApproximately(expected, 0.001f);

    [Fact]
    public void AFlash_FadesOut()
    {
        var flash = new ScreenFlash();
        flash.IsActive.Should().BeFalse();

        flash.Start(new Color(255, 0, 0, 255), TimeSpan.FromMilliseconds(200));
        flash.Amount.Should().Be(1);
        flash.Update(TimeSpan.FromMilliseconds(50));
        flash.Amount.Should().BeApproximately(0.75f, 0.001f);
        flash.Update(TimeSpan.FromMilliseconds(500));

        flash.IsActive.Should().BeFalse();
        flash.Amount.Should().Be(0);
    }

    [Fact]
    public void AFlashColoursAlpha_ScalesItsStrength()
    {
        var flash = new ScreenFlash();

        flash.Start(new Color(255, 0, 0, 128), TimeSpan.FromSeconds(1));

        flash.Amount.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void TheProcessor_IsActiveOnlyWhenSomethingWouldDraw()
    {
        using var post = new PostProcessor();
        post.IsActive.Should().BeFalse("no effects means the frame goes straight to the screen");

        var vignette = new Vignette { Enabled = false };
        post.Effects.Add(vignette);
        post.IsActive.Should().BeFalse();

        vignette.Enabled = true;
        post.IsActive.Should().BeTrue();
    }

    [Fact]
    public void AFlash_WakesTheProcessor_UntilItFades()
    {
        using var post = new PostProcessor();

        post.Flash(Color.White, TimeSpan.FromMilliseconds(100));
        post.IsActive.Should().BeTrue();
        post.Update(TimeSpan.FromMilliseconds(150));

        post.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Pixelate_OfOne_DoesNothing()
    {
        var pixelate = new Pixelate { Size = 1 };

        pixelate.IsActive.Should().BeFalse();
    }

    [Fact]
    public void BlendModes_CombineAsNamed()
    {
        BlendModes.Multiply.ColorSourceBlend.Should().Be(Blend.DestinationColor);
        BlendModes.Multiply.ColorDestinationBlend.Should().Be(Blend.Zero);
        BlendModes.Add.ColorSourceBlend.Should().Be(Blend.One);
        BlendModes.Add.ColorDestinationBlend.Should().Be(Blend.One);
        BlendModes.Subtract.ColorBlendFunction.Should().Be(BlendFunction.ReverseSubtract);
    }
}
