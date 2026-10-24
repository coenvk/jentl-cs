using System.Globalization;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Jentl.Core.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Jentl.Core.Diagnostics;

/// <summary>
/// A panel of what the game is doing, toggled with <see cref="ToggleKey"/> (F3): frame rate and a frame-time
/// graph, memory, the scene stack, particles and timers, the top scene's own <see cref="Scene.DebugInfo"/>,
/// watched values and recent log lines. While it shows, scenes also draw their <see cref="Scene.DrawDebug"/>
/// shapes (hit areas, paths). The scene manager owns one; the game draws it above everything.
/// </summary>
public sealed class DebugOverlay
{
    private const int MaxLog = 8;
    private static readonly TimeSpan LogLife = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Smooth = TimeSpan.FromSeconds(1.0 / 60);
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(1.0 / 30);
    private static readonly Color Panel = new(10, 12, 20, 210);
    private static readonly Color Heading = new(255, 199, 44);

    private readonly List<(string Name, Func<object?> Value)> _watches = [];
    private readonly List<(string Text, TimeSpan Age)> _log = [];

    public bool IsVisible { get; set; }

    public Keys ToggleKey { get; set; } = Keys.F3;

    /// <summary>Draws the panel's text; without one only the frame graph shows.</summary>
    public ITextDrawer? Text { get; set; }

    /// <summary>Text size in pixels.</summary>
    public float TextSize { get; set; } = 15;

    public FrameStats Stats { get; } = new();

    /// <summary>Shows <paramref name="name"/>: <paramref name="value"/> on the panel, read each time it draws; a second watch of the same name replaces the first.</summary>
    public void Watch(string name, Func<object?> value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Unwatch(name);
        _watches.Add((name, value));
    }

    public void Unwatch(string name) => _watches.RemoveAll(w => w.Name == name);

    /// <summary>Adds a line to the panel's log; it fades after a few seconds, and only the latest few are kept.</summary>
    public void Log(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _log.Add((message, TimeSpan.Zero));
        if (_log.Count > MaxLog)
        {
            _log.RemoveAt(0);
        }
    }

    /// <summary>Toggles on <see cref="ToggleKey"/> and ages the log. The game calls this every frame.</summary>
    public void Update(TimeSpan elapsed, InputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.WasPressed(ToggleKey))
        {
            IsVisible = !IsVisible;
        }

        for (var i = 0; i < _log.Count; i++)
        {
            _log[i] = (_log[i].Text, _log[i].Age + elapsed);
        }

        _log.RemoveAll(l => l.Age >= LogLife);
    }

    /// <summary>The panel's text: the engine's own lines, then <paramref name="extra"/> from the game, the top scene's lines, watches and the log.</summary>
    public IReadOnlyList<string> Lines(SceneManager scenes, IEnumerable<string>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            string.Format(
                c,
                "{0:0} fps  {1:0.0} ms (worst {2:0.0})  update {3:0.00} ms  draw {4:0.00} ms",
                Stats.FramesPerSecond,
                Stats.AverageFrame.TotalMilliseconds,
                Stats.WorstFrame.TotalMilliseconds,
                Stats.AverageUpdate.TotalMilliseconds,
                Stats.AverageDraw.TotalMilliseconds),
            string.Format(
                c,
                "Memory {0:0.0} MB  GC {1}/{2}/{3}",
                GC.GetTotalMemory(forceFullCollection: false) / 1048576.0,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2)),
        };
        lines.AddRange(extra ?? []);
        lines.Add("Scenes: " + (scenes.Stack.Count == 0 ? "none" : string.Join(" > ", scenes.Stack.Select(s => s.GetType().Name))));
        lines.Add(string.Format(
            c,
            "Particles {0}  Timers {1} scene, {2} global",
            scenes.Effects.Count,
            scenes.Top?.Timers.Count ?? 0,
            scenes.Timers.Count));

        if (scenes.Top is { } top && top.DebugInfo.ToList() is { Count: > 0 } info)
        {
            lines.Add("");
            lines.Add("# " + top.GetType().Name);
            lines.AddRange(info);
        }

        if (_watches.Count > 0)
        {
            lines.Add("");
            lines.Add("# Watches");
            lines.AddRange(_watches.Select(w => $"{w.Name}: {Describe(w.Value)}"));
        }

        if (_log.Count > 0)
        {
            lines.Add("");
            lines.Add("# Log");
            lines.AddRange(_log.Select(l => l.Text));
        }

        return lines;
    }

    /// <summary>Draws the panel in the top-left of <paramref name="window"/>, if visible.</summary>
    public void Draw(SpriteBatch batch, Rectangle window, SceneManager scenes, IEnumerable<string>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!IsVisible)
        {
            return;
        }

        var lines = Text is null ? [] : Lines(scenes, extra);
        var size = TextSize;
        var lineHeight = size * 1.25f;
        var graph = new Point(Stats.Capacity * 2, (int)(size * 3.5f));
        var width = Math.Max(graph.X, lines.Count == 0 ? 0 : (int)lines.Max(l => Text!.Measure(l, size).X));
        var pad = (int)(size * 0.6f);
        var panel = new Rectangle(window.X + 8, window.Y + 8, width + 2 * pad, graph.Y + (int)(lines.Count * lineHeight) + 3 * pad);
        batch.FillRectangle(panel, Panel);

        DrawGraph(batch, new Rectangle(panel.X + pad, panel.Y + pad, graph.X, graph.Y));

        var y = panel.Y + 2 * pad + graph.Y;
        foreach (var line in lines)
        {
            var heading = line.StartsWith("# ", StringComparison.Ordinal);
            Text!.Draw(batch, heading ? line[2..] : line, new Vector2(panel.X + pad, y), size, heading ? Heading : Color.White);
            y += (int)lineHeight;
        }
    }

    private static string Describe(Func<object?> value)
    {
        try
        {
            return Convert.ToString(value(), CultureInfo.InvariantCulture) ?? "null";
        }
#pragma warning disable CA1031 // A broken watch shows its error instead of taking the game down.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return $"<{e.GetType().Name}>";
        }
    }

    /// <summary>One bar per frame, scaled to 50 ms, with marks at 60 and 30 frames a second.</summary>
    private void DrawGraph(SpriteBatch batch, Rectangle area)
    {
        const double Top = 50;
        batch.FillRectangle(area, new Color(255, 255, 255, 18));
        var x = area.Right - Stats.Count * 2;
        foreach (var frame in Stats.Frames)
        {
            var ms = frame.TotalMilliseconds;
            var height = (int)Math.Ceiling(Math.Min(ms, Top) / Top * area.Height);
            var color = frame <= Smooth * 1.05 ? new Color(110, 204, 120) : frame <= Slow * 1.05 ? new Color(255, 199, 44) : new Color(232, 72, 85);
            batch.FillRectangle(new Rectangle(x, area.Bottom - height, 2, height), color);
            x += 2;
        }

        foreach (var mark in new[] { Smooth, Slow })
        {
            var y = area.Bottom - (int)(mark.TotalMilliseconds / Top * area.Height);
            batch.FillRectangle(new Rectangle(area.X, y, area.Width, 1), Color.White * 0.35f);
        }
    }
}
