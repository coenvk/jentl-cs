namespace Jentl.Collect;

/// <summary>A Glicko-2 rating: the strength estimate, how unsure we are of it, and how erratic the player's results are.</summary>
/// <param name="Rating">The strength on the familiar scale, where 1500 is average.</param>
/// <param name="Deviation">The uncertainty of <paramref name="Rating"/>; the true strength is likely within two deviations of it.</param>
/// <param name="Volatility">The expected swing of the player's strength between rating periods.</param>
public readonly record struct GlickoRating(double Rating, double Deviation, double Volatility)
{
    /// <summary>The rating of a player nobody has seen play yet: 1500, maximally unsure, volatility 0.06.</summary>
    public static GlickoRating New => new(1500, 350, 0.06);
}

/// <summary>One game result against an opponent, seen from the player being rated.</summary>
/// <param name="Opponent">The opponent's rating when the game was played.</param>
/// <param name="Score">1 for a win, 0.5 for a draw, 0 for a loss.</param>
public readonly record struct GlickoResult(GlickoRating Opponent, double Score);

/// <summary>Glicko-2 rating updates, following Mark Glickman's paper "Example of the Glicko-2 system".</summary>
public static class Glicko2
{
    private const double Scale = 173.7178;
    private const double MaxDeviation = 350;
    private const double Epsilon = 0.000001;

    /// <summary>
    /// Rates a player after one rating period. Without results only the deviation grows, as the paper says;
    /// the deviation never exceeds 350, the value of a new player.
    /// </summary>
    public static GlickoRating Update(GlickoRating player, IReadOnlyList<GlickoResult> results, double tau = 0.5)
    {
        var mu = (player.Rating - 1500) / Scale;
        var phi = player.Deviation / Scale;
        var sigma = player.Volatility;

        if (results.Count == 0)
        {
            var widened = Math.Sqrt((phi * phi) + (sigma * sigma)) * Scale;
            return player with { Deviation = Math.Min(widened, MaxDeviation) };
        }

        var variance = 0.0;
        var improvement = 0.0;
        foreach (var result in results)
        {
            var muJ = (result.Opponent.Rating - 1500) / Scale;
            var g = G(result.Opponent.Deviation / Scale);
            var e = 1 / (1 + Math.Exp(-g * (mu - muJ)));
            variance += g * g * e * (1 - e);
            improvement += g * (result.Score - e);
        }

        variance = 1 / variance;
        var delta = variance * improvement;

        var newSigma = NewVolatility(phi, sigma, variance, delta, tau);
        var phiStar = Math.Sqrt((phi * phi) + (newSigma * newSigma));
        var newPhi = 1 / Math.Sqrt((1 / (phiStar * phiStar)) + (1 / variance));
        var newMu = mu + (newPhi * newPhi * improvement);

        return new GlickoRating(
            (newMu * Scale) + 1500,
            Math.Min(newPhi * Scale, MaxDeviation),
            newSigma);
    }

    private static double G(double phi) => 1 / Math.Sqrt(1 + (3 * phi * phi / (Math.PI * Math.PI)));

    // Step 5 of the paper: find the new volatility with the Illinois (regula falsi) iteration.
    private static double NewVolatility(double phi, double sigma, double variance, double delta, double tau)
    {
        var a = Math.Log(sigma * sigma);
        var phiSquared = phi * phi;
        var deltaSquared = delta * delta;

        double F(double x)
        {
            var ex = Math.Exp(x);
            var d = phiSquared + variance + ex;
            return (ex * (deltaSquared - phiSquared - variance - ex) / (2 * d * d)) - ((x - a) / (tau * tau));
        }

        var lower = a;
        double upper;
        if (deltaSquared > phiSquared + variance)
        {
            upper = Math.Log(deltaSquared - phiSquared - variance);
        }
        else
        {
            var k = 1;
            while (F(a - (k * tau)) < 0)
            {
                k++;
            }

            upper = a - (k * tau);
        }

        var fLower = F(lower);
        var fUpper = F(upper);
        while (Math.Abs(upper - lower) > Epsilon)
        {
            var c = lower + ((lower - upper) * fLower / (fUpper - fLower));
            var fC = F(c);
            if (fC * fUpper <= 0)
            {
                lower = upper;
                fLower = fUpper;
            }
            else
            {
                fLower /= 2;
            }

            upper = c;
            fUpper = fC;
        }

        return Math.Exp(lower / 2);
    }
}
