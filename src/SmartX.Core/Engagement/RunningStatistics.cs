namespace SmartX.Core.Engagement;

public sealed class RunningStatistics
{
    private double _weight;
    private double _mean;
    private double _m2;
    private long _observations;

    // it forgets slowly on purpose, a probe that was recalibrated last month should not be judged against last month
    public double Decay { get; init; } = 0.995d;

    public double Count => _weight;

    public long Observations => _observations;

    public double Mean => _mean;

    public double Variance => _weight > 1d ? Math.Max(_m2 / (_weight - 1d), 0d) : 0d;

    public double StandardDeviation => Math.Sqrt(Variance);

    public double Min { get; private set; } = double.PositiveInfinity;

    public double Max { get; private set; } = double.NegativeInfinity;

    // keeps a running middle and spread, so a new reading can be scored without walking the whole history again
    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        _weight = (_weight * Decay) + 1d;
        _observations++;

        var delta = value - _mean;
        _mean += delta / _weight;
        _m2 = (_m2 * Decay) + (delta * (value - _mean));

        if (value < Min) Min = value;
        if (value > Max) Max = value;
    }

    public double ZScore(double value)
    {
        if (_weight < 8d) return 0d;

        var sd = StandardDeviation;

        if (sd < 1e-9d)
        {
            return Math.Abs(value - _mean) < 1e-9d ? 0d : 6d;
        }

        return (value - _mean) / sd;
    }

    public void Reset()
    {
        _weight = 0d;
        _observations = 0;
        _mean = 0d;
        _m2 = 0d;
        Min = double.PositiveInfinity;
        Max = double.NegativeInfinity;
    }
}
