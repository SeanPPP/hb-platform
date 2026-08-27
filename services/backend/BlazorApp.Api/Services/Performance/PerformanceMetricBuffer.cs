using System.Collections.Concurrent;
using System.Text.Json;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceMetricBuffer : IPerformanceMetricRecorder
{
    private readonly ConcurrentDictionary<MetricBufferKey, MetricAccumulator> _metrics = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly ILogger<PerformanceMetricBuffer> _logger;
    private readonly string _instanceId;
    private readonly bool _enabled;

    public PerformanceMetricBuffer(
        IOptions<PerformanceMetricsOptions> options,
        ILogger<PerformanceMetricBuffer> logger
    )
    {
        _logger = logger;
        _enabled = options.Value.Enabled;
        _instanceId = string.IsNullOrWhiteSpace(options.Value.InstanceId)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : options.Value.InstanceId.Trim();
    }

    public int BufferedSeriesCount => _metrics.Count;

    public void Record(PerformanceMetricRecord metric)
    {
        if (
            !_enabled
            || !double.IsFinite(metric.Value)
            || metric.Value < 0
            || metric.Weight <= 0
            || string.IsNullOrWhiteSpace(metric.MetricName)
        )
        {
            return;
        }

        var observedAt = PerformanceUtc.Normalize(metric.ObservedAtUtc);
        var normalized = PerformanceMetricDimensions.Normalize(
            metric.MetricName,
            metric.Dimensions
        );
        var windowStart = FloorToFiveMinutes(observedAt);
        var key = new MetricBufferKey(
            metric.MetricName,
            Normalize(metric.ProjectCode, 80, "unknown"),
            Normalize(metric.Environment, 60, "unknown"),
            Normalize(metric.SourceType, 40, "unknown"),
            _instanceId,
            normalized.Selector,
            normalized.Hash,
            normalized.Json,
            windowStart
        );
        while (true)
        {
            var accumulator = _metrics.GetOrAdd(key, static _ => new MetricAccumulator());
            if (accumulator.TryRecord(metric.Value, observedAt, metric.Weight))
            {
                return;
            }

            // Flush 已将旧累加器关闭；重试会写入字典中的新实例，避免迟到记录落到游离对象。
        }
    }

    public async Task FlushAsync(ISqlSugarClient db, CancellationToken cancellationToken = default)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            foreach (var entry in _metrics.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_metrics.TryRemove(entry.Key, out var accumulator))
                {
                    continue;
                }

                var snapshot = accumulator.CloseAndSnapshot();
                try
                {
                    var existing = await db
                        .Queryable<PerformanceMetricBucket>()
                        .Where(item =>
                            item.MetricName == entry.Key.MetricName
                            && item.ProjectCode == entry.Key.ProjectCode
                            && item.Environment == entry.Key.Environment
                            && item.SourceType == entry.Key.SourceType
                            && item.InstanceId == entry.Key.InstanceId
                            && item.DimensionsHash == entry.Key.DimensionsHash
                            && item.WindowStartUtc == entry.Key.WindowStartUtc
                            && item.BucketSizeMinutes == 5
                        )
                        .FirstAsync(cancellationToken);

                    if (existing == null)
                    {
                        await db
                            .Insertable(ToEntity(entry.Key, snapshot))
                            .ExecuteCommandAsync(cancellationToken);
                        continue;
                    }

                    var histogram = PerformanceHistogram.FromCounts(
                        DeserializeCounts(existing.HistogramCountsJson),
                        existing.MaximumValue
                    );
                    histogram.Merge(snapshot.Histogram);
                    existing.SampleCount += snapshot.Count;
                    existing.SumValue += snapshot.Sum;
                    existing.MinimumValue = Math.Min(existing.MinimumValue, snapshot.Minimum);
                    existing.MaximumValue = Math.Max(existing.MaximumValue, snapshot.Maximum);
                    existing.HistogramCountsJson = JsonSerializer.Serialize(histogram.Counts);
                    existing.LastObservedAtUtc = existing.LastObservedAtUtc > snapshot.LastObservedAtUtc
                        ? existing.LastObservedAtUtc
                        : snapshot.LastObservedAtUtc;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.Updateable(existing).ExecuteCommandAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "性能指标桶持久化失败: {Metric}/{Selector}",
                        entry.Key.MetricName,
                        entry.Key.Selector
                    );
                    while (
                        !_metrics
                            .GetOrAdd(entry.Key, static _ => new MetricAccumulator())
                            .TryMerge(snapshot)
                    )
                    {
                        // 仅当目标累加器同时被关闭时重试；同一实例的 Flush 由 _flushGate 串行化。
                    }
                }
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private static PerformanceMetricBucket ToEntity(
        MetricBufferKey key,
        MetricAccumulatorSnapshot snapshot
    ) =>
        new()
        {
            MetricName = key.MetricName,
            ProjectCode = key.ProjectCode,
            Environment = key.Environment,
            SourceType = key.SourceType,
            InstanceId = key.InstanceId,
            Selector = key.Selector,
            DimensionsHash = key.DimensionsHash,
            DimensionsJson = key.DimensionsJson,
            WindowStartUtc = key.WindowStartUtc,
            BucketSizeMinutes = 5,
            SampleCount = snapshot.Count,
            SumValue = snapshot.Sum,
            MinimumValue = snapshot.Minimum,
            MaximumValue = snapshot.Maximum,
            HistogramCountsJson = JsonSerializer.Serialize(snapshot.Histogram.Counts),
            LastObservedAtUtc = snapshot.LastObservedAtUtc,
        };

    private static IReadOnlyList<long> DeserializeCounts(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<long[]>(json ?? "[]") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateTime FloorToFiveMinutes(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute / 5 * 5, 0, DateTimeKind.Utc);

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed record MetricBufferKey(
        string MetricName,
        string ProjectCode,
        string Environment,
        string SourceType,
        string InstanceId,
        string Selector,
        string DimensionsHash,
        string DimensionsJson,
        DateTime WindowStartUtc
    );

    internal sealed class MetricAccumulator
    {
        private readonly object _sync = new();
        private readonly PerformanceHistogram _histogram = PerformanceHistogram.Create();
        private bool _closed;
        private long _count;
        private double _sum;
        private double _minimum = double.PositiveInfinity;
        private double _maximum;
        private DateTime _lastObservedAtUtc;

        public bool TryRecord(double value, DateTime observedAtUtc, long weight)
        {
            lock (_sync)
            {
                if (_closed)
                {
                    return false;
                }

                _histogram.Record(value, weight);
                _count += weight;
                _sum += value * weight;
                _minimum = Math.Min(_minimum, value);
                _maximum = Math.Max(_maximum, value);
                if (observedAtUtc > _lastObservedAtUtc)
                {
                    _lastObservedAtUtc = observedAtUtc;
                }

                return true;
            }
        }

        public bool TryMerge(MetricAccumulatorSnapshot snapshot)
        {
            lock (_sync)
            {
                if (_closed)
                {
                    return false;
                }

                _histogram.Merge(snapshot.Histogram);
                _count += snapshot.Count;
                _sum += snapshot.Sum;
                _minimum = Math.Min(_minimum, snapshot.Minimum);
                _maximum = Math.Max(_maximum, snapshot.Maximum);
                if (snapshot.LastObservedAtUtc > _lastObservedAtUtc)
                {
                    _lastObservedAtUtc = snapshot.LastObservedAtUtc;
                }

                return true;
            }
        }

        public MetricAccumulatorSnapshot CloseAndSnapshot()
        {
            lock (_sync)
            {
                _closed = true;
                return new MetricAccumulatorSnapshot(
                    _count,
                    _sum,
                    double.IsPositiveInfinity(_minimum) ? 0 : _minimum,
                    _maximum,
                    _lastObservedAtUtc,
                    PerformanceHistogram.FromCounts(_histogram.Counts, _histogram.Maximum)
                );
            }
        }
    }

    internal sealed record MetricAccumulatorSnapshot(
        long Count,
        double Sum,
        double Minimum,
        double Maximum,
        DateTime LastObservedAtUtc,
        PerformanceHistogram Histogram
    );
}
