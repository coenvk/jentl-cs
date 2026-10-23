using Jentl.Cards;
using Jentl.Core.Graphics;
using Jentl.Core.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Jentl.Core.Cards;

/// <summary>How a pile's cards are spread out on screen.</summary>
public enum Fan
{
    /// <summary>Squared up: only the top card shows.</summary>
    Stacked,

    /// <summary>Overlapping downwards, like solitaire columns.</summary>
    Down,

    /// <summary>Overlapping to the right, like a hand of cards.</summary>
    Right,
}

/// <summary>
/// The screen side of a card table: where each pile sits and how it fans out, which card is under
/// the pointer, dragging runs of cards between piles, and animation. Any card whose place changes
/// (a move, an undo, a drop) glides from where it was drawn to its new place, so games never
/// animate by hand. Call <see cref="Update"/> and <see cref="Draw"/> every frame.
/// </summary>
public sealed class TableView
{
    private static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(180);
    private const float DragThreshold = 10;

    private readonly List<PileSpot> _spots = [];
    private readonly Dictionary<Card, Rectangle> _drawnAt = [];
    private readonly Dictionary<Card, (Tween Tween, Rectangle Target)> _glides = [];
    private (Pile Pile, int Index)? _pressed;
    private (Pile Pile, int Index, Vector2 Grab)? _dragging;
    private (Pile Pile, int Index, TimeSpan Left)? _highlight;

    /// <summary>Card size in pixels; set by the game's layout each frame.</summary>
    public Point CardSize { get; set; } = new(100, 140);

    /// <summary>Which cards may be picked up; by default any face-up card (and the cards on top of it).</summary>
    public Func<Pile, int, bool> CanDrag { get; set; } = (pile, index) => pile[index].FaceUp;

    /// <summary>Optional label inside an empty pile's outline, e.g. "A" on foundations.</summary>
    public Func<Pile, string?> EmptyLabel { get; set; } = _ => null;

    public bool IsDragging => _dragging is not null;

    /// <summary>Forgets the layout; call before placing the piles for this frame.</summary>
    public void Clear() => _spots.Clear();

    /// <param name="room">For fanned piles: pixels available along the fan; the overlap tightens to fit. 0 means no limit.</param>
    public void Place(Pile pile, Point position, Fan fan = Fan.Stacked, int room = 0)
    {
        ArgumentNullException.ThrowIfNull(pile);
        _spots.Add(new PileSpot(pile, position, fan, room));
    }

    /// <summary>Where card <paramref name="index"/> of <paramref name="pile"/> belongs (ignoring animation and dragging).</summary>
    public Rectangle CardBounds(Pile pile, int index)
    {
        var spot = Spot(pile);
        var offset = Offsets(spot);
        return new Rectangle(spot.Position + offset[Math.Clamp(index, 0, offset.Length - 1)], CardSize);
    }

    /// <summary>The area a pile covers: its outline plus every fanned card.</summary>
    public Rectangle PileBounds(Pile pile)
    {
        var spot = Spot(pile);
        var bounds = new Rectangle(spot.Position, CardSize);
        return pile.IsEmpty ? bounds : Rectangle.Union(bounds, CardBounds(pile, pile.Count - 1));
    }

    /// <summary>The topmost card under <paramref name="point"/> (index -1 for an empty pile's outline), or null.</summary>
    public (Pile Pile, int Index)? HitTest(Vector2 point)
    {
        for (var s = _spots.Count - 1; s >= 0; s--)
        {
            var pile = _spots[s].Pile;
            for (var i = pile.Count - 1; i >= 0; i--)
            {
                if (CardBounds(pile, i).Contains(point))
                {
                    return (pile, i);
                }
            }

            if (pile.IsEmpty && new Rectangle(_spots[s].Position, CardSize).Contains(point))
            {
                return (pile, -1);
            }
        }

        return null;
    }

    /// <summary>Outlines a card (or an empty pile, index -1) for a while, e.g. to show a hint.</summary>
    public void Highlight(Pile pile, int index, TimeSpan duration) => _highlight = (pile, index, duration);

    /// <summary>
    /// Handles the pointer: a press on a draggable card followed by movement drags it (and the cards on
    /// top of it); releasing over another pile returns a <see cref="DragCards"/>. A tap returns a
    /// <see cref="ClickPile"/>. Returns null when nothing was done this frame.
    /// </summary>
    public CardInput? Update(InputState input, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (var (tween, _) in _glides.Values)
        {
            tween.Update(elapsed);
        }

        if (_highlight is { } highlight)
        {
            var left = highlight.Left - elapsed;
            _highlight = left > TimeSpan.Zero ? highlight with { Left = left } : null;
        }

        var cursor = input.Cursor;
        if (input.Gestures.Pressed)
        {
            _pressed = HitTest(cursor.Position);
        }

        if (_dragging is null && cursor.IsDown && _pressed is { Index: >= 0 } pressed
            && Vector2.Distance(cursor.Position, input.Gestures.PressedAt) > DragThreshold && CanDrag(pressed.Pile, pressed.Index))
        {
            var origin = CardBounds(pressed.Pile, pressed.Index);
            _dragging = (pressed.Pile, pressed.Index, input.Gestures.PressedAt - origin.Location.ToVector2());
        }

        if (_dragging is { } drag && !cursor.IsDown)
        {
            _dragging = null;
            _pressed = null;
            return DropTarget(drag, cursor.Position) is { } target ? new DragCards(drag.Pile, drag.Index, target) : null;
        }

        if (input.Tap is { } tap && _dragging is null && HitTest(tap) is { } hit)
        {
            _pressed = null;
            return new ClickPile(hit.Pile, hit.Index);
        }

        return null;
    }

    public void Draw(SpriteBatch batch, CardRenderer cards, Vector2 cursor)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(cards);

        // Outlines, then resting cards, then cards in flight (so they pass over others), then the dragged run.
        foreach (var spot in _spots)
        {
            cards.DrawSlot(batch, new Rectangle(spot.Position, CardSize), spot.Pile.IsEmpty ? EmptyLabel(spot.Pile) : null);
        }

        var flying = new List<(PlacedCard Card, Rectangle At)>();
        foreach (var spot in _spots)
        {
            var pile = spot.Pile;
            for (var i = 0; i < pile.Count; i++)
            {
                if (_dragging is { } drag && drag.Pile == pile && i >= drag.Index)
                {
                    continue;
                }

                var at = Place(pile[i].Card, CardBounds(pile, i), out var gliding);
                if (gliding)
                {
                    flying.Add((pile[i], at));
                }
                else
                {
                    cards.Draw(batch, pile[i], at);
                }
            }
        }

        foreach (var (card, at) in flying)
        {
            cards.Draw(batch, card, at);
        }

        if (_dragging is { } dragging)
        {
            var origin = (cursor - dragging.Grab).ToPoint();
            var first = CardBounds(dragging.Pile, dragging.Index).Location;
            for (var i = dragging.Index; i < dragging.Pile.Count; i++)
            {
                var at = CardBounds(dragging.Pile, i);
                at.Location = origin + (at.Location - first);
                _drawnAt[dragging.Pile[i].Card] = at;
                cards.Draw(batch, dragging.Pile[i], at);
            }
        }

        if (_highlight is { } highlight)
        {
            var bounds = highlight.Index >= 0 ? CardBounds(highlight.Pile, highlight.Index) : new Rectangle(Spot(highlight.Pile).Position, CardSize);
            Outline(batch, bounds, new Color(255, 199, 44), Math.Max(3, CardSize.X / 25));
        }
    }

    /// <summary>Where to draw a card this frame; starts a glide when its target moved since the last frame.</summary>
    private Rectangle Place(Card card, Rectangle target, out bool gliding)
    {
        if (_drawnAt.TryGetValue(card, out var last) && (!_glides.TryGetValue(card, out var glide) || glide.Target != target) && last != target)
        {
            var from = glide.Tween is { IsDone: false } ? Rect(glide.Tween.Value) : last;
            _glides[card] = (new Tween(new Vector2(from.X, from.Y), new Vector2(target.X, target.Y), GlideDuration, Easing.CubicOut), target);
        }

        var at = target;
        gliding = false;
        if (_glides.TryGetValue(card, out var current) && current.Target == target && !current.Tween.IsDone)
        {
            at = Rect(current.Tween.Value);
            gliding = true;
        }

        _drawnAt[card] = target;
        return at;

        Rectangle Rect(Vector2 location) => new((int)location.X, (int)location.Y, CardSize.X, CardSize.Y);
    }

    /// <summary>The pile under the pointer, or failing that the one the dragged card overlaps most.</summary>
    private Pile? DropTarget((Pile Pile, int Index, Vector2 Grab) drag, Vector2 cursor)
    {
        var dragged = new Rectangle((cursor - drag.Grab).ToPoint(), CardSize);
        Pile? best = null;
        var bestArea = 0;
        foreach (var spot in _spots)
        {
            if (spot.Pile == drag.Pile)
            {
                continue;
            }

            var bounds = PileBounds(spot.Pile);
            if (bounds.Contains(cursor))
            {
                return spot.Pile;
            }

            var overlap = Rectangle.Intersect(bounds, dragged);
            if (overlap.Width * overlap.Height > bestArea)
            {
                bestArea = overlap.Width * overlap.Height;
                best = spot.Pile;
            }
        }

        return best;
    }

    private Point[] Offsets(PileSpot spot)
    {
        var pile = spot.Pile;
        var offsets = new Point[Math.Max(1, pile.Count)];
        if (spot.Fan == Fan.Stacked || pile.Count == 0)
        {
            return offsets;
        }

        // Face-down cards overlap more than face-up ones; squeeze both if the pile would not fit.
        var along = spot.Fan == Fan.Down ? CardSize.Y : CardSize.X;
        float down = along * 0.12f, up = along * (spot.Fan == Fan.Down ? 0.27f : 0.22f);
        var length = Enumerable.Range(0, pile.Count - 1).Sum(i => pile[i].FaceUp ? up : down);
        var scale = spot.Room > 0 && length > spot.Room - along ? Math.Max(0.2f, (spot.Room - along) / length) : 1f;

        var position = 0f;
        for (var i = 0; i < pile.Count; i++)
        {
            offsets[i] = spot.Fan == Fan.Down ? new Point(0, (int)position) : new Point((int)position, 0);
            position += (pile[i].FaceUp ? up : down) * scale;
        }

        return offsets;
    }

    private PileSpot Spot(Pile pile) =>
        _spots.FirstOrDefault(s => s.Pile == pile) ?? throw new InvalidOperationException($"{pile.Name} has not been placed.");

    private static void Outline(SpriteBatch batch, Rectangle r, Color color, int width)
    {
        batch.FillRectangle(new Rectangle(r.X, r.Y, r.Width, width), color);
        batch.FillRectangle(new Rectangle(r.X, r.Bottom - width, r.Width, width), color);
        batch.FillRectangle(new Rectangle(r.X, r.Y, width, r.Height), color);
        batch.FillRectangle(new Rectangle(r.Right - width, r.Y, width, r.Height), color);
    }

    private sealed record PileSpot(Pile Pile, Point Position, Fan Fan, int Room);
}
