using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Tests;

// Accessibility and the palette are static: these tests set and restore them, so they must not run beside others that read them.
[Collection(nameof(StaticSettings))]
public class AccessibilityTests
{
    private enum Move
    {
        Run,
        Jump,
    }

    private readonly InputState _input = new();

    [Fact]
    public void ReduceMotion_StopsTheCameraShaking()
    {
        var camera = new Camera2D { Viewport = new Rectangle(0, 0, 100, 100) };
        try
        {
            Accessibility.ReduceMotion = true;
            camera.Shake(10, TimeSpan.FromSeconds(1));
            camera.Update(TimeSpan.FromMilliseconds(16));

            camera.ShakeOffset.Should().Be(Vector2.Zero);
            Accessibility.Motion(5).Should().Be(0);
        }
        finally
        {
            Accessibility.ReduceMotion = false;
        }

        camera.Shake(10, TimeSpan.FromSeconds(1));
        camera.Update(TimeSpan.FromMilliseconds(16));
        camera.ShakeOffset.Should().NotBe(Vector2.Zero);
        Accessibility.Motion(5).Should().Be(5);
    }

    [Fact]
    public void Contrast_FollowsWcag()
    {
        PaletteColors.Contrast(Color.Black, Color.White).Should().BeApproximately(21, 0.01);
        PaletteColors.Contrast(Color.Red, Color.Red).Should().Be(1);
        PaletteColors.Contrast(new Color(118, 118, 118), Color.White).Should().BeApproximately(4.54, 0.01);
    }

    [Fact]
    public void HighContrast_PushesTextAndSurfacesApart()
    {
        var colors = Palette.Capture();
        var high = colors.WithHighContrast();

        PaletteColors.Contrast(high.Text, high.Background).Should().BeGreaterThan(PaletteColors.Contrast(colors.Text, colors.Background)).And.BeGreaterThan(7);
        PaletteColors.Contrast(high.Muted, high.Panel).Should().BeGreaterThan(PaletteColors.Contrast(colors.Muted, colors.Panel)).And.BeGreaterThan(4.5);
        PaletteColors.Contrast(high.Ink, high.Accent).Should().BeGreaterThan(7);
    }

    [Fact]
    public void HighContrast_OnALightPalette_UsesBlackText()
    {
        var light = Palette.Capture() with { Background = new Color(240, 240, 235), Panel = Color.White, Text = new Color(60, 60, 60) };

        light.WithHighContrast().Text.Should().Be(Color.Black);
    }

    [Fact]
    public void Capture_AndApply_RoundTrip_AndColorBlindSafeChangesOnlySuccess()
    {
        var original = Palette.Capture();
        try
        {
            var safe = original.WithColorBlindSafe();
            safe.Apply();

            Palette.Solved.Should().NotBe(original.Solved);
            (safe with { Solved = original.Solved }).Should().Be(original);
            Palette.Capture().Should().Be(safe);
        }
        finally
        {
            original.Apply();
        }
    }

    [Fact]
    public void AToggle_StaysDownUntilPressedAgain()
    {
        var map = new ActionMap<Move>(new Dictionary<Move, Keys[]> { [Move.Run] = [Keys.LeftShift] });
        map.SetToggle(Move.Run, true);

        Frame(Keys.LeftShift);
        map.IsDown(_input, Move.Run).Should().BeTrue();
        map.IsDown(_input, Move.Run).Should().BeTrue("asking twice in a frame does not flip it back");
        Frame();
        map.IsDown(_input, Move.Run).Should().BeTrue("released, it stays on");
        Frame(Keys.LeftShift);
        map.IsDown(_input, Move.Run).Should().BeFalse();

        Frame();
        map.IsDown(_input, Move.Run).Should().BeFalse();
        Frame(Keys.LeftShift);
        map.IsDown(_input, Move.Run).Should().BeTrue();
        map.ReleaseToggles();
        Frame();
        map.IsDown(_input, Move.Run).Should().BeFalse();
    }

    [Fact]
    public void Toggles_AreSaved_Loaded_AndReset()
    {
        var map = new ActionMap<Move>(new Dictionary<Move, Keys[]> { [Move.Run] = [Keys.LeftShift] });
        map.SetToggle(Move.Run, true);
        map.IsChanged.Should().BeTrue();

        var loaded = new ActionMap<Move>(new Dictionary<Move, Keys[]> { [Move.Run] = [Keys.LeftShift] });
        loaded.Load(map.Save());
        loaded.IsToggle(Move.Run).Should().BeTrue();
        loaded.Copy().IsToggle(Move.Run).Should().BeTrue();

        loaded.Reset();
        loaded.IsToggle(Move.Run).Should().BeFalse();
    }

    private void Frame(params Keys[] keys) => _input.Update(new KeyboardState(keys));
}

[CollectionDefinition(nameof(StaticSettings), DisableParallelization = true)]
public sealed class StaticSettings;
