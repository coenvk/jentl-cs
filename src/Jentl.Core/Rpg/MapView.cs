using Jentl.Core.Graphics;
using Jentl.Puzzle;
using Jentl.Rpg.Maps;
using Jentl.Rpg.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Rpg;

/// <summary>
/// Screen side of the RPG kit: draws an <see cref="Area"/>'s tile layers, items and actors around a
/// camera that follows the player, with fog of war in dark areas (unseen cells black, remembered ones
/// dimmed). Steps glide from cell to cell, hits flash, and damage numbers float up.
/// </summary>
/// <remarks>
/// Pixel art is scaled by whole numbers and drawn with point sampling, so tiles stay crisp; the view
/// restarts the sprite batch for that and restores the engine's usual state afterwards.
/// </remarks>
public sealed class MapView
{
    private static readonly TimeSpan StepTime = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan PopupTime = TimeSpan.FromMilliseconds(800);
    private static readonly Color Remembered = new(95, 100, 125);
    private static readonly RasterizerState Clipped = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    private readonly Func<Tileset, Texture2D> _textures;
    private readonly ITextDrawer _text;
    private readonly Dictionary<int, (Tween Tween, Vector2 Target)> _moves = [];
    private readonly Dictionary<int, TimeSpan> _flashes = [];
    private readonly List<FloatingText> _popups = [];
    private Vector2 _origin;

    /// <param name="textures">The texture of a tileset's image.</param>
    /// <param name="text">Draws the floating damage numbers.</param>
    public MapView(Func<Tileset, Texture2D> textures, ITextDrawer text)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(text);
        _textures = textures;
        _text = text;
    }

    /// <summary>At least this many cells fit across and down; the tile scale is the largest whole number that allows it.</summary>
    public Point MinimumCells { get; set; } = new(15, 11);

    /// <summary>The smallest whole-number scale, even if fewer cells than <see cref="MinimumCells"/> then fit (phones).</summary>
    public int MinimumScale { get; set; } = 2;

    /// <summary>A cell's size on screen as of the last <see cref="Draw"/>.</summary>
    public int TileSize { get; private set; }

    public bool IsAnimating => _moves.Values.Any(m => !m.Tween.IsDone);

    /// <summary>Glides an actor (or anything with an id) from one cell to the next.</summary>
    public void Animate(int id, Cell from, Cell to)
    {
        var start = _moves.TryGetValue(id, out var moving) && !moving.Tween.IsDone ? moving.Tween.Value : new Vector2(from.X, from.Y);
        var target = new Vector2(to.X, to.Y);
        _moves[id] = (new Tween(start, target, StepTime, Easing.QuadInOut), target);
    }

    /// <summary>Tints an actor red for a moment, as when it is hit.</summary>
    public void Flash(int id) => _flashes[id] = FlashTime;

    /// <summary>Floats <paramref name="text"/> up from <paramref name="cell"/>, e.g. damage dealt.</summary>
    public void FloatText(Cell cell, string text, Color color) => _popups.Add(new FloatingText(new Vector2(cell.X, cell.Y), text, color));

    /// <summary>Plays the animations for a turn's events.</summary>
    public void Show(IEnumerable<WorldEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            switch (e)
            {
                case ActorMoved moved:
                    Animate(moved.Actor.Id, moved.From, moved.To);
                    break;
                case ActorAttacked attacked:
                    Flash(attacked.Target.Id);
                    FloatText(attacked.Target.Position, attacked.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture), new Color(255, 96, 80));
                    break;
                case AreaEntered:
                    Stop();
                    break;
            }
        }
    }

    /// <summary>Drops every animation, e.g. when changing areas.</summary>
    public void Stop()
    {
        _moves.Clear();
        _flashes.Clear();
        _popups.Clear();
    }

    public void Update(TimeSpan elapsed)
    {
        foreach (var (tween, _) in _moves.Values)
        {
            tween.Update(elapsed);
        }

        foreach (var id in _flashes.Keys.ToArray())
        {
            var left = _flashes[id] - elapsed;
            if (left > TimeSpan.Zero)
            {
                _flashes[id] = left;
            }
            else
            {
                _flashes.Remove(id);
            }
        }

        foreach (var popup in _popups)
        {
            popup.Age += elapsed;
        }

        _popups.RemoveAll(p => p.Age >= PopupTime);
    }

    /// <summary>Where an actor is drawn: gliding towards its cell, or on it.</summary>
    public Vector2 PositionOf(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var at = new Vector2(actor.Position.X, actor.Position.Y);
        return _moves.TryGetValue(actor.Id, out var moving) && moving.Target == at && !moving.Tween.IsDone ? moving.Tween.Value : at;
    }

    /// <summary>The cell under a screen point as of the last draw.</summary>
    public Cell? CellAt(Vector2 screen) =>
        TileSize == 0 ? null : new Cell((int)MathF.Floor((screen.X - _origin.X) / TileSize), (int)MathF.Floor((screen.Y - _origin.Y) / TileSize));

    /// <summary>Draws <paramref name="area"/> into <paramref name="bounds"/>, centred on <paramref name="focus"/>.</summary>
    public void Draw(SpriteBatch batch, Rectangle bounds, Area area, Actor focus)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(area);
        var map = area.Map;
        var scale = Math.Max(MinimumScale, Math.Min(bounds.Width / (map.TileWidth * MinimumCells.X), bounds.Height / (map.TileHeight * MinimumCells.Y)));
        TileSize = map.TileWidth * scale;
        _origin = Origin(bounds, map, PositionOf(focus));

        var device = batch.GraphicsDevice;
        var scissor = device.ScissorRectangle;
        batch.End();
        device.ScissorRectangle = Rectangle.Intersect(bounds, device.Viewport.Bounds);
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.PointClamp, rasterizerState: Clipped);

        var first = CellAt(bounds.Location.ToVector2())!.Value;
        var last = CellAt(new Vector2(bounds.Right, bounds.Bottom))!.Value;
        for (var y = Math.Max(0, first.Y); y <= Math.Min(map.Height - 1, last.Y); y++)
        {
            for (var x = Math.Max(0, first.X); x <= Math.Min(map.Width - 1, last.X); x++)
            {
                var cell = new Cell(x, y);
                if (!area.IsExplored(cell))
                {
                    continue;
                }

                var tint = area.IsVisible(cell) ? Color.White : Remembered;
                foreach (var layer in map.Layers)
                {
                    if (layer.Visible && layer[cell] != 0)
                    {
                        DrawTile(batch, map, layer[cell], Screen(new Vector2(x, y)), tint);
                    }
                }
            }
        }

        foreach (var item in area.Items.Where(i => area.IsVisible(i.Cell)))
        {
            DrawSprite(batch, map, item.Item.Sprite, Screen(new Vector2(item.Cell.X, item.Cell.Y)), Color.White);
        }

        foreach (var actor in area.Actors.Where(a => a.IsAlive && (a == focus || area.IsVisible(a.Position))).OrderBy(a => a.Position.Y))
        {
            var at = Screen(PositionOf(actor));
            DrawSprite(batch, map, actor.Sprite, at, _flashes.ContainsKey(actor.Id) ? new Color(255, 110, 100) : Color.White);
            if (actor.Faction == Faction.Hostile && actor.Hp < actor.MaxHp)
            {
                var bar = new Rectangle(at.X + TileSize / 8, at.Y - TileSize / 10, TileSize * 3 / 4, Math.Max(2, TileSize / 12));
                batch.FillRectangle(bar, new Color(20, 20, 30));
                batch.FillRectangle(bar with { Width = bar.Width * actor.Hp / actor.MaxHp }, new Color(220, 70, 60));
            }
        }

        foreach (var popup in _popups)
        {
            var t = (float)(popup.Age / PopupTime);
            var size = TileSize * 0.45f;
            var width = _text.Measure(popup.Text, size).X;
            var position = new Vector2(
                _origin.X + (popup.Cell.X + 0.5f) * TileSize - width / 2,
                _origin.Y + (popup.Cell.Y - 0.2f - t * 0.6f) * TileSize);
            _text.Draw(batch, popup.Text, position, size, popup.Color * (1 - t * t));
        }

        batch.End();
        device.ScissorRectangle = scissor;
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
    }

    /// <summary>Draws the tile named <paramref name="name"/> (a sprite, an item icon) at a screen rectangle; skipped if the map has none.</summary>
    public void DrawSprite(SpriteBatch batch, TileMap map, string name, Rectangle at, Color tint)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var set in map.Tilesets)
        {
            if (set.Tileset.TryFind(name, out var id))
            {
                DrawTile(batch, map, set.FirstGid + id, at, tint);
                return;
            }
        }
    }

    private void DrawTile(SpriteBatch batch, TileMap map, int gid, Rectangle at, Color tint)
    {
        if (map.TilesetFor(gid) is not { } set)
        {
            return;
        }

        var tileset = set.Tileset;
        var (column, row) = tileset.Location(gid - set.FirstGid);
        var source = new Rectangle(column * tileset.TileWidth, row * tileset.TileHeight, tileset.TileWidth, tileset.TileHeight);
        batch.Draw(_textures(tileset), at, source, tint);
    }

    private Rectangle Screen(Vector2 grid) =>
        new((int)MathF.Round(_origin.X + grid.X * TileSize), (int)MathF.Round(_origin.Y + grid.Y * TileSize), TileSize, TileSize);

    /// <summary>
    /// Centres the camera on <paramref name="focus"/> but keeps the map's edge at the edge of the view; a
    /// map smaller than the view is centred instead.
    /// </summary>
    private Vector2 Origin(Rectangle bounds, TileMap map, Vector2 focus)
    {
        float Axis(int start, int length, int cells, float at)
        {
            var size = cells * TileSize;
            if (size <= length)
            {
                return start + (length - size) / 2f;
            }

            var offset = length / 2f - (at + 0.5f) * TileSize;
            return start + Math.Clamp(offset, length - size, 0);
        }

        return new Vector2(
            MathF.Round(Axis(bounds.X, bounds.Width, map.Width, focus.X)),
            MathF.Round(Axis(bounds.Y, bounds.Height, map.Height, focus.Y)));
    }

    private sealed class FloatingText(Vector2 cell, string text, Color color)
    {
        public Vector2 Cell { get; } = cell;

        public string Text { get; } = text;

        public Color Color { get; } = color;

        public TimeSpan Age { get; set; }
    }
}
