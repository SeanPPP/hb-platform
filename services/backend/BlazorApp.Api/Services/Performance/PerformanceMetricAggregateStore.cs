using System.Text.Json;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

/// <summary>
/// 将需要确认写入的指标直接合并到五分钟桶。客户端/CI 批次只有在原始样本与桶处于同一事务后才会 ACK。
/// </summary>
public sealed class PerformanceMetricAggregateStore
{
    private readonly SemaphoreSlim[] _stripes = Enumerable
        .Range(0, 128)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();
    private readonly string _instanceId;

    public PerformanceMetricAggregateStore(IOptions<PerformanceMetricsOptions> options)
    {
        _instanceId = string.IsNullOrWhiteSpace(options.Value.InstanceId)
            ? $"{Environment.MachineName}-{Environment.ProcessId}"
            : options.Value.InstanceId.Trim();
    }

    public async Task<bool> PersistSampleAndAggregateAsync(
        ISqlSugarClient db,
        PerformanceMetricSample sample,
        PerformanceMetricEventV1Dto metricEvent,
        CancellationToken cancellationToken = default
    )
    {
        var record = new PerformanceMetricRecord(
            sample.MetricName,
            sample.ProjectCode,
            sample.Environment,
            sample.SourceType,
            sample.Value,
            sample.ObservedAtUtc,
            metricEvent.Dimensions
        );
        var stripe = StripeFor(record, _instanceId);
        await stripe.WaitAsync(cancellationToken);
        try
        {
            db.Ado.BeginTran();
            try
            {
                await db.Insertable(sample).ExecuteCommandAsync(cancellationToken);
                await UpsertCoreAsync(db, record, _instanceId, cancellationToken);
                db.Ado.CommitTran();
                return true;
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                db.Ado.RollbackTran();
                return false;
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
        }
        finally
        {
            stripe.Release();
        }
    }

    /// <summary>调用方已负责跨实例互斥及事务时，直接合并一个加权指标。</summary>
    public Task UpsertInCurrentTransactionAsync(
        ISqlSugarClient db,
        PerformanceMetricRecord record,
        string instanceId,
        CancellationToken cancellationToken = default
    ) => UpsertCoreAsync(db, record, instanceId, cancellationToken);

    private async Task UpsertCoreAsync(
        ISqlSugarClient db,
        PerformanceMetricRecord record,
        string instanceId,
        CancellationToken cancellationToken
    )
    {
        var normalized = PerformanceMetricDimensions.Normalize(
            record.MetricName,
            record.Dimensions
        );
        var observedAt = PerformanceUtc.Normalize(record.ObservedAtUtc);
        var windowStart = FloorToFiveMinutes(observedAt);
        var metricName = Normalize(record.MetricName, 120, "unknown");
        var projectCode = Normalize(record.ProjectCode, 80, "unknown");
        var environment = Normalize(record.Environment, 60, "unknown");
        var sourceType = Normalize(record.SourceType, 40, "unknown");
        instanceId = Normalize(instanceId, 120, "unknown");

        var existing = await db
            .Queryable<PerformanceMetricBucket>()
            .Where(item =>
                item.MetricName == metricName
                && item.ProjectCode == projectCode
                && item.Environment == environment
                && item.SourceType == sourceType
                && item.InstanceId == instanceId
                && item.DimensionsHash == normalized.Hash
                && item.WindowStartUtc == windowStart
                && item.BucketSizeMinutes == 5
            )
            .FirstAsync(cancellationToken);

        var histogram = PerformanceHistogram.Create();
        histogram.Record(record.Value, record.Weight);
        if (existing == null)
        {
            await db
                .Insertable(
                    new PerformanceMetricBucket
                    {
                        MetricName = metricName,
                        ProjectCode = projectCode,
                        Environment = environment,
                        SourceType = sourceType,
                        InstanceId = instanceId,
                        Selector = normalized.Selector,
                        DimensionsHash = normalized.Hash,
                        DimensionsJson = normalized.Json,
                        WindowStartUtc = windowStart,
                        BucketSizeMinutes = 5,
                        SampleCount = record.Weight,
                        SumValue = record.Value * record.Weight,
                        MinimumValue = record.Value,
                        MaximumValue = record.Value,
                        HistogramCountsJson = JsonSerializer.Serialize(histogram.Counts),
                        LastObservedAtUtc = observedAt,
                    }
                )
                .ExecuteCommandAsync(cancellationToken);
            return;
        }

        var merged = PerformanceHistogram.FromCounts(
            DeserializeCounts(existing.HistogramCountsJson),
            existing.MaximumValue
        );
        merged.Merge(histogram);
        existing.SampleCount += record.Weight;
        existing.SumValue += record.Value * record.Weight;
        existing.MinimumValue = Math.Min(existing.MinimumValue, record.Value);
        existing.MaximumValue = Math.Max(existing.MaximumValue, record.Value);
        existing.HistogramCountsJson = JsonSerializer.Serialize(merged.Counts);
        existing.LastObservedAtUtc = existing.LastObservedAtUtc > observedAt
            ? existing.LastObservedAtUtc
            : observedAt;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.Updateable(existing).ExecuteCommandAsync(cancellationToken);
    }

    private SemaphoreSlim StripeFor(PerformanceMetricRecord record, string instanceId)
    {
        var hash = HashCode.Combine(
            record.MetricName,
            record.ProjectCode,
            record.Environment,
            record.SourceType,
            instanceId,
            FloorToFiveMinutes(PerformanceUtc.Normalize(record.ObservedAtUtc))
        );
        return _stripes[(int)((uint)hash % (uint)_stripes.Length)];
    }

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
        new(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute / 5 * 5,
            0,
            DateTimeKind.Utc
        );

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (
                current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }
        return false;
    }
}
