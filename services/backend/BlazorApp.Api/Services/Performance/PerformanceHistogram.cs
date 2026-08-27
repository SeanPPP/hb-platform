namespace BlazorApp.Api.Services.Performance;

/// <summary>
/// 可跨实例合并的固定耗时桶。分位数返回对应桶上界，避免先算实例分位数再平均。
/// </summary>
public sealed class PerformanceHistogram
{
    public static readonly double[] Boundaries =
    [
        // 首次发布即固定低计数桶语义，避免 backlog 0-5 被延迟桶上界 10 放大。
        0,
        1,
        2,
        3,
        5,
        10,
        25,
        50,
        100,
        250,
        500,
        750,
        1000,
        1500,
        1750,
        2000,
        2500,
        3000,
        5000,
        10000,
        30000,
        60000,
        120000,
        300000,
        600000,
        1000000,
        1500000,
        2000000,
        3000000,
        5000000,
        10000000,
        30000000,
        60000000,
        double.PositiveInfinity,
    ];

    private readonly long[] _counts;

    private PerformanceHistogram(long[] counts, double maximum)
    {
        _counts = counts;
        Maximum = maximum;
    }

    public long Count => _counts.Sum();

    public double Maximum { get; private set; }

    public IReadOnlyList<long> Counts => _counts;

    public static PerformanceHistogram Create() => new(new long[Boundaries.Length], 0);

    public static PerformanceHistogram FromCounts(IReadOnlyList<long>? counts, double maximum)
    {
        var normalized = new long[Boundaries.Length];
        if (counts != null)
        {
            for (var index = 0; index < Math.Min(counts.Count, normalized.Length); index++)
            {
                normalized[index] = Math.Max(0, counts[index]);
            }
        }

        return new PerformanceHistogram(normalized, Math.Max(0, maximum));
    }

    public void Record(double value, long weight = 1)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "指标值必须是非负有限数值");
        }
        if (weight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "指标权重必须为正整数");
        }

        var bucket = Array.FindIndex(Boundaries, boundary => value <= boundary);
        _counts[bucket < 0 ? _counts.Length - 1 : bucket] += weight;
        Maximum = Math.Max(Maximum, value);
    }

    public void Merge(PerformanceHistogram other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (var index = 0; index < _counts.Length; index++)
        {
            _counts[index] += other._counts[index];
        }

        Maximum = Math.Max(Maximum, other.Maximum);
    }

    public double? EstimatePercentile(double percentile)
    {
        if (Count == 0)
        {
            return null;
        }

        if (percentile <= 0 || percentile > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        var rank = (long)Math.Ceiling(Count * percentile);
        long cumulative = 0;
        for (var index = 0; index < _counts.Length; index++)
        {
            cumulative += _counts[index];
            if (cumulative >= rank)
            {
                return double.IsPositiveInfinity(Boundaries[index]) ? Maximum : Boundaries[index];
            }
        }

        return Maximum;
    }
}
