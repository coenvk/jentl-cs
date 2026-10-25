namespace Jentl.Collect;

/// <summary>Mean and spread of a series of numbers, kept up to date one value at a time (Welford's method).</summary>
public sealed class RunningStats
{
    private double _sumOfSquares;

    /// <summary>How many values were added.</summary>
    public int Count { get; private set; }

    /// <summary>The average of the values; 0 before any.</summary>
    public double Mean { get; private set; }

    /// <summary>The sample standard deviation; 0 below two values.</summary>
    public double StandardDeviation => Count < 2 ? 0 : Math.Sqrt(_sumOfSquares / (Count - 1));

    /// <summary>Half the width of the 95% confidence interval of the mean: 1.96 × deviation / √count; 0 below two values.</summary>
    public double Margin95 => Count < 2 ? 0 : 1.96 * StandardDeviation / Math.Sqrt(Count);

    public void Add(double value)
    {
        Count++;
        var delta = value - Mean;
        Mean += delta / Count;
        _sumOfSquares += delta * (value - Mean);
    }
}
