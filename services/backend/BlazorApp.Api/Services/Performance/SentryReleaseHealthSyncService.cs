using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed class SentryReleaseHealthSyncService : BackgroundService
{
    private const string SourceType = "sentry-release-health";
    private const string AggregateInstanceId = "global-sentry-release-health";
    private static readonly TimeSpan ReleaseHealthWindow = TimeSpan.FromHours(1);

    private readonly SentryReleaseHealthClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PerformanceMetricAggregateStore _aggregateStore;
    private readonly PerformanceCollectorCoordinator _coordinator;
    private readonly SentryReleaseHealthOptions _options;
    private readonly ILogger<SentryReleaseHealthSyncService> _logger;
    private readonly TimeProvider _timeProvider;

    public SentryReleaseHealthSyncService(
        SentryReleaseHealthClient client,
        IServiceScopeFactory scopeFactory,
        PerformanceMetricAggregateStore aggregateStore,
        PerformanceCollectorCoordinator coordinator,
        IOptions<SentryReleaseHealthOptions> options,
        ILogger<SentryReleaseHealthSyncService> logger,
        TimeProvider? timeProvider = null
    )
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _aggregateStore = aggregateStore;
        _coordinator = coordinator;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }
        if (!_client.IsConfigured)
        {
            _logger.LogWarning("Sentry Release Health 配置不完整或不安全，后台同步已禁用");
            return;
        }

        await SyncOnceAsync(_timeProvider.GetUtcNow(), stoppingToken);
        var interval = TimeSpan.FromMinutes(
            Math.Clamp(_options.SyncIntervalMinutes, 5, 24 * 60)
        );
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncOnceAsync(_timeProvider.GetUtcNow(), stoppingToken);
        }
    }

    internal async Task SyncOnceAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SqlSugarContext>().Db;
            await SyncOnceAsync(
                db,
                _client,
                _aggregateStore,
                _coordinator,
                _options,
                utcNow,
                _timeProvider,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Sentry Release Health 同步失败，类型 {ExceptionType}",
                ex.GetType().Name
            );
        }
    }

    internal static async Task<int> SyncOnceAsync(
        ISqlSugarClient db,
        SentryReleaseHealthClient client,
        PerformanceMetricAggregateStore aggregateStore,
        PerformanceCollectorCoordinator coordinator,
        SentryReleaseHealthOptions options,
        DateTimeOffset utcNow,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        // Sentry sessions API 的最小 interval 为 1h，并会按 interval 舍入边界；调度频率不能改变数据窗口。
        var interval = ReleaseHealthWindow;
        var latestCompleteEnd = FloorToInterval(utcNow.UtcDateTime, interval);
        var lookbackHours = Math.Clamp(options.LookbackHours, 1, 168);
        var initialCursor = FloorToInterval(
            latestCompleteEnd.AddHours(-lookbackHours),
            interval
        );
        var metricEnvironment = NormalizeMetricEnvironment(options.MetricEnvironment);
        var collectorKey = $"sentry-release-health:{NormalizeKey(options.Environment)}:{metricEnvironment}";
        var leaseDuration = TimeSpan.FromSeconds(
            Math.Clamp(options.HttpTimeoutSeconds * 4 + 60, 120, 900)
        );
        var lease = await coordinator.TryAcquireAsync(
            db,
            collectorKey,
            utcNow.UtcDateTime,
            leaseDuration,
            initialCursor,
            cancellationToken
        );
        if (lease == null)
        {
            return 0;
        }

        DateTime LeaseUtcNow() => PerformanceUtc.Normalize(
            timeProvider?.GetUtcNow().UtcDateTime ?? utcNow.UtcDateTime
        );

        // 每轮都重读配置的滚动窗口，吸收迟到的离线 session；游标仅记录最近成功边界。
        var cursor = initialCursor;
        if (cursor >= latestCompleteEnd)
        {
            await coordinator.ReleaseAsync(db, lease, LeaseUtcNow(), cancellationToken);
            return 0;
        }

        var maximumWindows = Math.Clamp(lookbackHours + 1, 1, 512);
        var processed = 0;
        while (cursor < latestCompleteEnd && processed < maximumWindows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var windowEnd = cursor.Add(interval);
            if (windowEnd > latestCompleteEnd)
            {
                windowEnd = latestCompleteEnd;
            }
            var result = await client.FetchWindowAsync(
                new DateTimeOffset(cursor, TimeSpan.Zero),
                new DateTimeOffset(windowEnd, TimeSpan.Zero),
                cancellationToken,
                token => coordinator.RenewAsync(db, lease, LeaseUtcNow(), token)
            );
            if (!result.Complete)
            {
                await coordinator.ReleaseAsync(
                    db,
                    lease with { CursorUtc = cursor },
                    LeaseUtcNow(),
                    cancellationToken
                );
                return processed;
            }

            var release = windowEnd >= latestCompleteEnd || processed + 1 >= maximumWindows;
            var committed = await coordinator.CommitAsync(
                db,
                lease,
                LeaseUtcNow(),
                windowEnd,
                release,
                async token =>
                {
                    // Sentry 返回的是该小时的完整快照。先清除同一来源/窗口，再写入当前结果，
                    // 确保 project/release/dist/window 重放是替换而不是累加。
                    await db
                        .Deleteable<BlazorApp.Shared.Models.HBweb.PerformanceMetricBucket>()
                        .Where(item =>
                            item.MetricName == PerformanceMetricNames.SentryCrashFreeSession
                            && item.Environment == metricEnvironment
                            && item.SourceType == SourceType
                            && item.InstanceId == AggregateInstanceId
                            && item.WindowStartUtc == windowEnd
                            && item.BucketSizeMinutes == 5
                        )
                        .ExecuteCommandAsync(token);
                    foreach (var snapshot in result.Snapshots)
                    {
                        await aggregateStore.UpsertInCurrentTransactionAsync(
                            db,
                            new PerformanceMetricRecord(
                                PerformanceMetricNames.SentryCrashFreeSession,
                                snapshot.Project,
                                metricEnvironment,
                                SourceType,
                                snapshot.CrashFreeSessionRatio,
                                windowEnd,
                                new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["environment"] = metricEnvironment,
                                    ["release"] = snapshot.Release,
                                    ["dist"] = snapshot.Dist,
                                    ["project"] = snapshot.Project,
                                },
                                snapshot.SessionCount
                            ),
                            AggregateInstanceId,
                            token
                        );
                    }
                },
                cancellationToken
            );
            if (!committed)
            {
                return processed;
            }

            processed++;
            cursor = windowEnd;
            lease = lease with { CursorUtc = cursor };
            if (release)
            {
                break;
            }
        }
        return processed;
    }

    private static DateTime FloorToInterval(DateTime value, TimeSpan interval)
    {
        value = PerformanceUtc.Normalize(value);
        var elapsedTicks = value.Ticks - DateTime.UnixEpoch.Ticks;
        var flooredTicks = elapsedTicks / interval.Ticks * interval.Ticks;
        return new DateTime(DateTime.UnixEpoch.Ticks + flooredTicks, DateTimeKind.Utc);
    }

    private static string NormalizeMetricEnvironment(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Production";
        }
        if (string.Equals(normalized, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "Production";
        }
        return normalized.Length <= 60 ? normalized : normalized[..60];
    }

    private static string NormalizeKey(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "unknown";
        }
        return normalized.Length <= 60 ? normalized : normalized[..60];
    }
}
