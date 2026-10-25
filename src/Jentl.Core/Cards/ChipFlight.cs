using Jentl.Core.Graphics;
using Microsoft.Xna.Framework;

namespace Jentl.Core.Cards;

/// <summary>A pile of chips worth <see cref="Amount"/> travelling from one spot to another, easing in and out.</summary>
public sealed class ChipFlight(int amount, Vector2 from, Vector2 to, TimeSpan duration)
{
    private readonly Tween _tween = new(from, to, duration, Easing.QuadInOut);

    public int Amount { get; } = amount;

    public Vector2 Position => _tween.Value;

    public bool IsDone => _tween.IsDone;

    public void Update(TimeSpan elapsed) => _tween.Update(elapsed);
}
