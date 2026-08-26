using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceMetricService
{
    internal const int MaxSeriesPointCount = 10_000;
    internal const int MaxOverviewAggregateCount = 50_000;
    private static readonly TimeSpan MaxOverviewRange = TimeSpan.FromDays(31);
    private static readonly TimeSpan MaxSeriesRange = TimeSpan.FromDays(31);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        SemaphoreSlim
    > FreezeGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly ISqlSugarClient _db;
    private readonly PerformanceMetricBuffer _buffer;
    private readonly PerformanceMetricAggregateStore _aggregateStore;
    private readonly PerformanceMetricsOptions _options;
    private readonly ILogger<PerformanceMetricService> _logger;

    public PerformanceMetricService(
        SqlSugarContext context,
        PerformanceMetricBuffer buffer,
        PerformanceMetricAggregateStore aggregateStore,
        IOptions<PerformanceMetricsOptions> options,
        ILogger<PerformanceMetricService> logger
    )
        : this(context.Db, buffer, aggregateStore, options, logger) { }

    internal PerformanceMetricService(
        ISqlSugarClient db,
        PerformanceMetricBuffer buffer,
        PerformanceMetricAggregateStore aggregateStore,
        IOptions<PerformanceMetricsOptions> options,
        ILogger<PerformanceMetricService> logger
    )
    {
        _db = db;
        _buffer = buffer;
        _aggregateStore = aggregateStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<PerformanceMetricIngestResultDto>> IngestAsync(
        string projectCode,
        string sourceType,
        PerformanceMetricBatchV1Dto? request,
        DateTime utcNow,
        string? persistedSourceType = null
    )
    {
        var errors = PerformanceMetricBatchValidator.Validate(
            request,
            utcNow,
            sourceType,
            projectCode
        );
        if (errors.Count > 0)
        {
            return ApiResponse<PerformanceMetricIngestResultDto>.FailWithData(
                new PerformanceMetricIngestResultDto
                {
                    RejectedCount = request?.Events?.Count ?? 0,
                },
                "性能指标批次校验失败",
                "PERFORMANCE_METRIC_BATCH_INVALID",
                errors
            );
        }

        projectCode = Normalize(projectCode, 80, "unknown");
        sourceType = Normalize(sourceType, 40, "unknown");
        persistedSourceType = Normalize(persistedSourceType ?? sourceType, 40, "unknown");
        var events = request!.Events;
        var eventIds = events.Select(item => item.EventId).Distinct().ToList();
        var existingIds = eventIds.Count == 0
            ? []
            : await _db
                .Queryable<PerformanceMetricSample>()
                .Where(item => item.ProjectCode == projectCode && eventIds.Contains(item.EventId))
                .Select(item => item.EventId)
                .ToListAsync();
        var seen = existingIds.ToHashSet();
        var accepted = new List<(PerformanceMetricSample Sample, PerformanceMetricEventV1Dto Event)>();
        var duplicates = 0;

        foreach (var item in events)
        {
            if (!seen.Add(item.EventId))
            {
                duplicates++;
                continue;
            }

            var dimensions = PerformanceMetricDimensions.Normalize(item.Metric, item.Dimensions);
            var environment = item.Dimensions.TryGetValue("environment", out var eventEnvironment)
                ? NormalizeEnvironment(eventEnvironment, _options.DefaultEnvironment)
                : NormalizeEnvironment(_options.DefaultEnvironment, "Production");
            var observedAt = PerformanceUtc.Normalize(item.ObservedAt);
            accepted.Add(
                (
                    new PerformanceMetricSample
                    {
                        EventId = item.EventId,
                        ProjectCode = projectCode,
                        Environment = environment,
                        SourceType = persistedSourceType,
                        MetricName = item.Metric,
                        ObservedAtUtc = observedAt,
                        Value = item.Value,
                        Unit = item.Unit,
                        Selector = dimensions.Selector,
                        DimensionsHash = dimensions.Hash,
                        DimensionsJson = dimensions.Json,
                    },
                    item
                )
            );
        }

        var inserted = new List<(PerformanceMetricSample Sample, PerformanceMetricEventV1Dto Event)>();
        foreach (var item in accepted)
        {
            if (
                await _aggregateStore.PersistSampleAndAggregateAsync(
                    _db,
                    item.Sample,
                    item.Event
                )
            )
            {
                inserted.Add(item);
            }
            else
            {
                duplicates++;
            }
        }

        var policyEnvironment = inserted.FirstOrDefault().Sample?.Environment
            ?? accepted.FirstOrDefault().Sample?.Environment
            ?? NormalizeEnvironment(_options.DefaultEnvironment, "Production");
        var sampling = await GetClientSamplingPolicyAsync(
            policyEnvironment,
            events
        );
        return ApiResponse<PerformanceMetricIngestResultDto>.OK(
            new PerformanceMetricIngestResultDto
            {
                AcceptedCount = inserted.Count,
                DuplicateCount = duplicates,
                RejectedCount = 0,
                BaselineState = sampling.State,
                DefaultSampleRate = sampling.DefaultSampleRate,
                Policies = sampling.Policies,
            },
            "性能指标写入成功"
        );
    }

    public async Task<ApiResponse<PerformanceReleaseEventRequestDto>> RecordReleaseEventAsync(
        PerformanceReleaseEventRequestDto request,
        DateTime utcNow
    )
    {
        var error = ValidateReleaseEvent(request);
        if (error != null)
        {
            return ApiResponse<PerformanceReleaseEventRequestDto>.Error(
                error,
                "PERFORMANCE_RELEASE_EVENT_INVALID"
            );
        }

        var existing = await _db.Queryable<PerformanceReleaseEvent>().InSingleAsync(request.EventId);
        if (existing != null)
        {
            return await HandleExistingReleaseEventAsync(existing, request);
        }

        var releaseEvent = ToReleaseEvent(request);
        try
        {
            await _db.Insertable(releaseEvent).ExecuteCommandAsync();
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            existing = await _db
                .Queryable<PerformanceReleaseEvent>()
                .InSingleAsync(request.EventId);
            if (existing == null)
            {
                throw;
            }
            return await HandleExistingReleaseEventAsync(existing, request);
        }

        if (IsAcceptedProductionDeploy(releaseEvent))
        {
            await StartObservationCycleIfNeededAsync(
                releaseEvent.Environment,
                releaseEvent.CompletedAtUtc
            );
        }

        return ApiResponse<PerformanceReleaseEventRequestDto>.OK(
            ToReleaseEventRequest(releaseEvent),
            "发布事件写入成功"
        );
    }

    private async Task<ApiResponse<PerformanceReleaseEventRequestDto>> HandleExistingReleaseEventAsync(
        PerformanceReleaseEvent existing,
        PerformanceReleaseEventRequestDto request
    )
    {
        var candidate = ToReleaseEvent(request);
        if (!ReleaseEventEquals(existing, candidate))
        {
            return ApiResponse<PerformanceReleaseEventRequestDto>.Error(
                "同一 eventId 的发布事件载荷不一致",
                "PERFORMANCE_RELEASE_EVENT_CONFLICT",
                new { request.EventId }
            );
        }

        // 观察周期只能由数据库中已确认的事件驱动，冲突请求不得改变状态。
        if (IsAcceptedProductionDeploy(existing))
        {
            await StartObservationCycleIfNeededAsync(
                existing.Environment,
                existing.CompletedAtUtc
            );
        }
        return ApiResponse<PerformanceReleaseEventRequestDto>.OK(
            ToReleaseEventRequest(existing),
            "发布事件已存在"
        );
    }

    public async Task<PerformanceOverviewDto> GetOverviewAsync(
        PerformanceOverviewQueryDto query,
        DateTime utcNow
    )
    {
        await _buffer.FlushAsync(_db);
        var end = AsUtc(query.EndUtc ?? utcNow);
        var start = AsUtc(query.StartUtc ?? end.AddDays(-7));
        if (start >= end)
        {
            start = end.AddDays(-7);
        }
        if (end - start > MaxOverviewRange)
        {
            throw new PerformanceOverviewQueryException(
                "总览查询范围最多为 31 天，请缩小时间窗口",
                "PERFORMANCE_OVERVIEW_RANGE_TOO_LARGE"
            );
        }

        var environment = NormalizeEnvironment(query.Environment, "Production");
        // 公开项目键只提供低风险接收能力，不能影响正式看板或冻结阈值。
        var rows = await LoadAggregateRowsAsync(
            environment,
            start,
            end,
            grouping: MetricAggregateGrouping.MetricSelector,
            maxAggregateRows: MaxOverviewAggregateCount,
            limitExceeded: OverviewAggregateLimitExceeded,
            freezeEligibleOnly: true
        );
        var percentiles = rows
            .GroupBy(item => (item.MetricName, item.Selector))
            .Select(group => ToPercentile(group.Key.MetricName, group.Key.Selector, group))
            .OrderByDescending(item => item.P95 ?? 0)
            .ToList();
        await ApplyBaselineWarningsAsync(environment, start, end, percentiles);
        var releases = await _db
            .Queryable<PerformanceReleaseEvent>()
            .Where(item =>
                item.Environment == environment
                && item.CompletedAtUtc >= start
                && item.CompletedAtUtc < end
            )
            .ToListAsync();

        return new PerformanceOverviewDto
        {
            Environment = environment,
            StartUtc = start,
            EndUtc = end,
            GeneratedAtUtc = utcNow,
            Baseline = await GetBaselineStatusCoreAsync(environment),
            Api = percentiles.Where(item => item.Metric == PerformanceMetricNames.ApiRequestDuration).ToList(),
            Sql = percentiles.Where(item => item.Metric == PerformanceMetricNames.SqlCommandDuration).ToList(),
            HqAndJobs = percentiles.Where(item =>
                item.Metric is PerformanceMetricNames.HqSyncDuration
                    or PerformanceMetricNames.HqSyncSuccessRate
                    or PerformanceMetricNames.HqSyncFailureRate
                    or PerformanceMetricNames.HqSyncBacklog
                    or PerformanceMetricNames.BackgroundJobDuration
                    or PerformanceMetricNames.BackgroundJobSuccessRate
                    or PerformanceMetricNames.BackgroundJobFailureRate
            ).ToList(),
            WebAndPos = percentiles.Where(item =>
                item.Metric.StartsWith("web.", StringComparison.Ordinal)
                || item.Metric.StartsWith("pos.", StringComparison.Ordinal)
                || item.Metric == PerformanceMetricNames.SentryCrashFreeSession
            ).ToList(),
            Delivery = percentiles.Where(item => item.Metric == PerformanceMetricNames.CiRunDuration).ToList(),
            AcceptedDeployments = releases.Count(item => item.Action == "deploy" && item.Status == "accepted"),
            AcceptedRollbacks = releases.Count(item => item.Action == "rollback" && item.Status == "accepted"),
            ReleaseEvents = releases
                .OrderByDescending(item => item.CompletedAtUtc)
                .Take(50)
                .Select(item => new PerformanceReleaseEventDto
                {
                    Id = item.Id,
                    Action = item.Action,
                    Status = item.Status,
                    Environment = item.Environment,
                    Component = item.Component,
                    Commit = item.Commit,
                    Version = item.Version,
                    StartedAtUtc = PerformanceUtc.Normalize(item.StartedAtUtc),
                    CompletedAtUtc = PerformanceUtc.Normalize(item.CompletedAtUtc),
                    Source = item.Source,
                })
                .ToList(),
        };
    }

    private async Task ApplyBaselineWarningsAsync(
        string environment,
        DateTime start,
        DateTime end,
        IReadOnlyCollection<PerformancePercentileDto> percentiles
    )
    {
        var cycle = await _db
            .Queryable<PerformanceBaselineCycle>()
            .Where(item => item.Environment == environment && item.State == "frozen")
            .OrderBy(item => item.ObservationStartedAtUtc, OrderByType.Desc)
            .FirstAsync();
        if (cycle == null)
        {
            return;
        }
        var definitions = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .Where(item => item.CycleId == cycle.Id && item.CoverageState == "qualified")
            .ToListAsync();
        var byKey = definitions.ToDictionary(item => (item.MetricName, item.Selector));
        var latestCompleteWindow = FloorToFifteenMinutes(end).AddMinutes(-15);
        var warningStart = latestCompleteWindow.AddMinutes(-30);
        if (warningStart < start)
        {
            warningStart = start;
        }
        var warningEnd = latestCompleteWindow.AddMinutes(15);
        IReadOnlyCollection<MetricAggregateRow> warningRows = warningStart < warningEnd
            && percentiles.Any(item => IsHighFrequencyMetric(item.Metric))
            ? await LoadAggregateRowsAsync(
                environment,
                warningStart,
                warningEnd,
                grouping: MetricAggregateGrouping.SeriesPoint,
                maxAggregateRows: MaxOverviewAggregateCount,
                limitExceeded: OverviewAggregateLimitExceeded,
                freezeEligibleOnly: true
            )
            : [];
        foreach (var percentile in percentiles)
        {
            if (!byKey.TryGetValue((percentile.Metric, percentile.Selector), out var baseline))
            {
                continue;
            }
            percentile.BaselineP95 = baseline.P95;
            percentile.WarningThreshold = baseline.WarningThreshold;
            if (!baseline.WarningThreshold.HasValue)
            {
                continue;
            }

            if (IsHighFrequencyMetric(percentile.Metric))
            {
                var windowResults = warningRows
                    .Where(item =>
                        item.MetricName == percentile.Metric
                        && item.Selector == percentile.Selector
                        && item.BucketSizeMinutes <= 15
                        && item.WindowStartUtc >= latestCompleteWindow.AddMinutes(-30)
                        && item.WindowStartUtc < latestCompleteWindow.AddMinutes(15)
                    )
                    .GroupBy(item => FloorToFifteenMinutes(item.WindowStartUtc))
                    .ToDictionary(
                        group => group.Key,
                        group => ToPercentile(percentile.Metric, percentile.Selector, group).P95
                    );
                var breaches = 0;
                for (var index = 0; index < 3; index++)
                {
                    var window = latestCompleteWindow.AddMinutes(-15 * index);
                    if (
                        windowResults.TryGetValue(window, out var p95)
                        && p95.HasValue
                        && p95.Value > baseline.WarningThreshold.Value
                    )
                    {
                        breaches++;
                        continue;
                    }
                    break;
                }
                percentile.ConsecutiveBreaches = breaches;
                percentile.IsWarning = breaches >= 3;
                continue;
            }

            var observed = percentile.P95;
            percentile.ConsecutiveBreaches = observed.HasValue
                && IsThresholdBreached(
                    percentile.Metric,
                    observed.Value,
                    baseline.WarningThreshold.Value
                )
                ? 1
                : 0;
            percentile.IsWarning = percentile.ConsecutiveBreaches > 0;
        }
    }

    private static bool IsHighFrequencyMetric(string metric) =>
        metric is PerformanceMetricNames.ApiRequestDuration
            or PerformanceMetricNames.SqlCommandDuration
            or PerformanceMetricNames.WebTableReactCommit
            or PerformanceMetricNames.WebTableRenderToPaint
            or PerformanceMetricNames.PosColdStart
            or PerformanceMetricNames.PosScanToCart
            or PerformanceMetricNames.PosPaymentResponse;

    private static bool IsThresholdBreached(string metric, double observed, double threshold) =>
        metric == PerformanceMetricNames.SentryCrashFreeSession
            ? observed < threshold
            : observed > threshold;

    private static DateTime FloorToFifteenMinutes(DateTime value) =>
        new(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute / 15 * 15,
            0,
            DateTimeKind.Utc
        );

    public async Task<PerformanceSeriesDto> GetSeriesAsync(
        PerformanceOverviewQueryDto query,
        DateTime utcNow
    )
    {
        await _buffer.FlushAsync(_db);
        var end = AsUtc(query.EndUtc ?? utcNow);
        var start = AsUtc(query.StartUtc ?? end.AddDays(-7));
        if (start >= end)
        {
            throw new PerformanceSeriesQueryException(
                "序列查询开始时间必须早于结束时间",
                "PERFORMANCE_SERIES_INVALID_RANGE"
            );
        }
        if (end - start > MaxSeriesRange)
        {
            throw new PerformanceSeriesQueryException(
                "序列查询范围最多为 31 天，请缩小时间窗口",
                "PERFORMANCE_SERIES_RANGE_TOO_LARGE"
            );
        }
        var environment = NormalizeEnvironment(query.Environment, "Production");
        // bucket 按实例保存，同一最终序列点可能有多条源记录；上限必须在跨实例合并后判断。
        // 序列与总览使用同一可信来源口径，避免公开键样本制造伪趋势或伪告警。
        var rows = await LoadAggregateRowsAsync(
            environment,
            start,
            end,
            grouping: MetricAggregateGrouping.SeriesPoint,
            maxAggregateRows: MaxSeriesPointCount,
            limitExceeded: SeriesPointLimitExceeded,
            freezeEligibleOnly: true
        );
        var points = rows
            .Select(row =>
            {
                var percentile = ToPercentile(
                    row.MetricName,
                    row.Selector,
                    [row]
                );
                return new PerformanceSeriesPointDto
                {
                    Metric = percentile.Metric,
                    Selector = percentile.Selector,
                    SampleCount = percentile.SampleCount,
                    P50 = percentile.P50,
                    P95 = percentile.P95,
                    P99 = percentile.P99,
                    Average = percentile.Average,
                    Maximum = percentile.Maximum,
                    LastObservedAtUtc = percentile.LastObservedAtUtc,
                    CoverageState = percentile.CoverageState,
                    WindowStartUtc = row.WindowStartUtc,
                    BucketSizeMinutes = row.BucketSizeMinutes,
                };
            })
            .OrderBy(item => item.WindowStartUtc)
            .ThenBy(item => item.Metric, StringComparer.Ordinal)
            .ThenBy(item => item.Selector, StringComparer.Ordinal)
            .ToList();

        return new PerformanceSeriesDto
        {
            Environment = environment,
            StartUtc = start,
            EndUtc = end,
            Points = points,
        };
    }

    public async Task<List<PerformanceSlowSqlDto>> GetSlowSqlAsync(
        PerformanceSlowSqlQueryDto query,
        DateTime utcNow
    )
    {
        await _buffer.FlushAsync(_db);
        var end = AsUtc(query.EndUtc ?? utcNow);
        // slow-sql 的 1h/24h/7d 选择器是明确口径，不受看板全局时间范围覆盖。
        var start = end.Subtract(SlowSqlWindow(query.Window));
        var environment = NormalizeEnvironment(query.Environment, "Production");
        var buckets = await LoadAggregateRowsAsync(
            environment,
            start,
            end,
            PerformanceMetricNames.SqlCommandDuration,
            grouping: MetricAggregateGrouping.MetricProjectSelector
        );

        var result = buckets
            .GroupBy(item => (item.ProjectCode, item.Selector))
            .Select(group =>
            {
                var percentile = ToPercentile(
                    PerformanceMetricNames.SqlCommandDuration,
                    group.Key.Selector,
                    group
                );
                var dimensions = PerformanceMetricDimensions.Parse(group.First().DimensionsJson);
                return new PerformanceSlowSqlDto
                {
                    DatabaseContext = dimensions.GetValueOrDefault("databaseContext", group.Key.ProjectCode),
                    Fingerprint = dimensions.GetValueOrDefault("sqlFingerprint", group.Key.Selector),
                    Template = dimensions.GetValueOrDefault("sqlTemplate", string.Empty),
                    ExecutionCount = percentile.SampleCount,
                    TotalDurationMs = group.Sum(item => item.SumValue),
                    AverageDurationMs = percentile.Average ?? 0,
                    P95DurationMs = percentile.P95,
                    MaximumDurationMs = percentile.Maximum ?? 0,
                    LastObservedAtUtc = percentile.LastObservedAtUtc ?? start,
                };
            })
            .ToList();
        return (query.SortBy.Trim().ToLowerInvariant() switch
        {
            "p95" => result.OrderByDescending(item => item.P95DurationMs ?? 0),
            "max" => result.OrderByDescending(item => item.MaximumDurationMs),
            _ => result.OrderByDescending(item => item.TotalDurationMs),
        })
            .Take(20)
            .ToList();
    }

    public async Task<List<PerformanceOperationalRunDto>> GetRunsAsync(
        PerformanceOverviewQueryDto query,
        DateTime utcNow
    )
    {
        var end = AsUtc(query.EndUtc ?? utcNow);
        var start = AsUtc(query.StartUtc ?? end.AddDays(-7));
        var environment = NormalizeEnvironment(query.Environment, "Production");
        return (await _db
                .Queryable<PerformanceOperationalRun>()
                .Where(item =>
                    item.Environment == environment
                    && item.QueuedAtUtc >= start
                    && item.QueuedAtUtc < end
                )
                .OrderBy(item => item.QueuedAtUtc, OrderByType.Desc)
                .Take(200)
                .ToListAsync())
            .Select(item => new PerformanceOperationalRunDto
            {
                Id = item.Id,
                Category = item.Category,
                Operation = item.Operation,
                Status = item.Status,
                Attempt = item.Attempt,
                Backlog = item.Backlog,
                QueuedAtUtc = PerformanceUtc.Normalize(item.QueuedAtUtc),
                StartedAtUtc = PerformanceUtc.Normalize(item.StartedAtUtc),
                CompletedAtUtc = PerformanceUtc.Normalize(item.CompletedAtUtc),
                DurationMs = item.DurationMs,
            })
            .ToList();
    }

    public Task<PerformanceBaselineStatusDto> GetBaselineStatusAsync(string environment) =>
        GetBaselineStatusCoreAsync(NormalizeEnvironment(environment, "Production"));

    public async Task<PerformanceBaselineDto> GetBaselineAsync(string environment)
    {
        environment = NormalizeEnvironment(environment, "Production");
        var cycle = await _db
            .Queryable<PerformanceBaselineCycle>()
            .Where(item => item.Environment == environment)
            .OrderBy(item => item.ObservationStartedAtUtc, OrderByType.Desc)
            .FirstAsync();
        if (cycle == null)
        {
            return new PerformanceBaselineDto();
        }
        var definitions = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .Where(item => item.CycleId == cycle.Id)
            .OrderBy(item => item.MetricName)
            .OrderBy(item => item.Selector)
            .ToListAsync();
        return new PerformanceBaselineDto
        {
            Status = await GetBaselineStatusCoreAsync(environment),
            Definitions = definitions.Select(item => new PerformanceBaselineDefinitionDto
            {
                Metric = item.MetricName,
                Selector = item.Selector,
                SampleCount = item.SampleCount,
                P50 = item.P50,
                P95 = item.P95,
                P99 = item.P99,
                WarningThreshold = item.WarningThreshold,
                CoverageState = item.CoverageState,
                GatePolicy = item.GatePolicy,
            }).ToList(),
        };
    }

    public async Task<ApiResponse<PerformanceBaselineStatusDto>> FreezeBaselineAsync(
        string environment,
        string actor,
        DateTime utcNow
    )
    {
        environment = NormalizeEnvironment(environment, "Production");
        await _buffer.FlushAsync(_db);
        var freezeGate = FreezeGates.GetOrAdd(environment, static _ => new SemaphoreSlim(1, 1));
        await freezeGate.WaitAsync();
        ISqlSugarClient? databaseLock = null;
        try
        {
            // 先取得跨实例会话锁，再开启 Snapshot，避免等待锁期间持有旧快照并在写回时冲突。
            databaseLock = await AcquireFreezeDatabaseLockAsync(environment);
            await _db.Ado.BeginTranAsync(
                ResolveConsistentReadIsolationLevel(_db.CurrentConnectionConfig.DbType)
            );
            var committed = false;
            try
            {
                if (_db.CurrentConnectionConfig.DbType == DbType.Sqlite)
                {
                    await AcquireSqliteFreezeWriteLockAsync(environment);
                }
                var cycle = await _db
                    .Queryable<PerformanceBaselineCycle>()
                    .Where(item =>
                        item.Environment == environment
                        && (item.State == "observing" || item.State == "frozen")
                    )
                    .OrderBy(item => item.ObservationStartedAtUtc, OrderByType.Desc)
                    .FirstAsync();
                if (cycle == null)
                {
                    _db.Ado.RollbackTran();
                    return ApiResponse<PerformanceBaselineStatusDto>.Error(
                        "没有可冻结的观察周期",
                        "PERFORMANCE_BASELINE_NOT_OBSERVING"
                    );
                }
                var wasFrozen = cycle.State == "frozen";
                if (!wasFrozen && utcNow < cycle.ObservationEndsAtUtc)
                {
                    _db.Ado.RollbackTran();
                    return ApiResponse<PerformanceBaselineStatusDto>.Error(
                        "观察周期尚未满 14 天",
                        "PERFORMANCE_BASELINE_WINDOW_INCOMPLETE"
                    );
                }

                // 首次候选严格使用固定 14 天窗口；候选生成后，只有尚未合格或新出现的 selector 继续累积。
                var aggregateEndUtc = wasFrozen || cycle.CandidateGeneratedAtUtc.HasValue
                    ? utcNow
                    : cycle.ObservationEndsAtUtc;
                var rows = await LoadAggregateRowsAsync(
                    environment,
                    cycle.ObservationStartedAtUtc,
                    aggregateEndUtc,
                    freezeEligibleOnly: true
                );
                var candidates = rows
                    .GroupBy(item => (item.MetricName, item.Selector))
                    .Select(group =>
                    {
                        var metric = ToPercentile(
                            group.Key.MetricName,
                            group.Key.Selector,
                            group
                        );
                        var qualified = metric.SampleCount >= RequiredSampleCount(metric.Metric);
                        return new PerformanceBaselineDefinition
                        {
                            CycleId = cycle.Id,
                            MetricName = metric.Metric,
                            Selector = metric.Selector,
                            SampleCount = metric.SampleCount,
                            P50 = metric.P50,
                            P95 = metric.P95,
                            P99 = metric.P99,
                            WarningThreshold = qualified
                                && metric.P95.HasValue
                                && !IsDisplayOnlyMetric(metric.Metric)
                                ? WarningThreshold(metric.Metric, metric.P95.Value)
                                : null,
                            CoverageState = qualified ? "qualified" : "insufficient",
                            GatePolicy = IsDisplayOnlyMetric(metric.Metric)
                                ? "display_only"
                                : metric.Metric is PerformanceMetricNames.WebFirstScreenBytes
                                    or PerformanceMetricNames.WebLargestInitialChunkBytes
                                    ? "web_bundle_hard"
                                    : "runtime_warning",
                        };
                    })
                    .ToList();
                // 构建体积是确定性 CI 样本，硬门禁必须使用原始值计算精确分位数，不能接受宽桶上界误差。
                candidates.RemoveAll(item => IsWebBundleMetric(item.MetricName));
                candidates.AddRange(
                    await LoadExactWebBundleDefinitionsAsync(
                        cycle.Id,
                        environment,
                        cycle.ObservationStartedAtUtc,
                        aggregateEndUtc
                    )
                );

                var existingDefinitions = await _db
                    .Queryable<PerformanceBaselineDefinition>()
                    .Where(item => item.CycleId == cycle.Id)
                    .ToListAsync();
                var existingBySelector = existingDefinitions.ToDictionary(item =>
                    (item.MetricName, item.Selector)
                );
                var mergedBySelector = existingDefinitions.ToDictionary(
                    item => (item.MetricName, item.Selector),
                    item => item
                );
                var newlyQualifiedCount = 0;
                foreach (var candidate in candidates)
                {
                    var key = (candidate.MetricName, candidate.Selector);
                    if (
                        existingBySelector.TryGetValue(key, out var existing)
                        && existing.CoverageState == "qualified"
                    )
                    {
                        // 已冻结值必须保持不变，后续慢样本不能悄悄改写正式基线。
                        continue;
                    }

                    if (candidate.CoverageState == "qualified")
                    {
                        newlyQualifiedCount++;
                    }
                    mergedBySelector[key] = candidate;
                }
                var definitions = mergedBySelector.Values.ToList();
                var hasQualifiedDefinition = definitions.Any(item =>
                    item.CoverageState == "qualified"
                );

                await _db
                    .Deleteable<PerformanceBaselineDefinition>()
                    .Where(item => item.CycleId == cycle.Id)
                    .ExecuteCommandAsync();
                if (definitions.Count > 0)
                {
                    await _db.Insertable(definitions).ExecuteCommandAsync();
                }
                cycle.CandidateGeneratedAtUtc ??= utcNow;
                if (!wasFrozen && hasQualifiedDefinition)
                {
                    cycle.State = "frozen";
                    cycle.FrozenAtUtc = utcNow;
                    cycle.FrozenBy = Normalize(actor, 120, "System");
                }
                cycle.UpdatedAt = utcNow;
                await _db.Updateable(cycle).ExecuteCommandAsync();
                _db.Ado.CommitTran();
                committed = true;

                if (!hasQualifiedDefinition)
                {
                    return ApiResponse<PerformanceBaselineStatusDto>.Error(
                        "没有达到最低样本量的指标，数据不足项继续观察",
                        "PERFORMANCE_BASELINE_INSUFFICIENT"
                    );
                }

                return ApiResponse<PerformanceBaselineStatusDto>.OK(
                    await GetBaselineStatusCoreAsync(environment),
                    wasFrozen
                        ? newlyQualifiedCount > 0
                            ? $"已补充冻结 {newlyQualifiedCount} 个原数据不足指标"
                            : "数据不足指标候选已更新，既有冻结值保持不变"
                        : "性能基线已冻结"
                );
            }
            catch
            {
                if (!committed)
                {
                    _db.Ado.RollbackTran();
                }
                throw;
            }
        }
        finally
        {
            await ReleaseFreezeDatabaseLockAsync(databaseLock, environment);
            freezeGate.Release();
        }
    }

    private async Task<ISqlSugarClient?> AcquireFreezeDatabaseLockAsync(string environment)
    {
        if (_db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return null;
        }

        var config = _db.CurrentConnectionConfig;
        var lockClient = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = config.ConnectionString,
                DbType = DbType.SqlServer,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        SqlPerformanceAttachmentService.Attach(
            lockClient,
            "PerformanceMetricService.FreezeLock"
        );
        try
        {
            var lockResult = await lockClient.Ado.SqlQuerySingleAsync<int>(
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = @LockResource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 60000;
                SELECT @Result;
                """,
                new SugarParameter(
                    "@LockResource",
                    $"PerformanceBaseline_Freeze_{environment}"
                )
            );
            if (lockResult < 0)
            {
                throw new InvalidOperationException("无法获取性能基线冻结锁");
            }
            return lockClient;
        }
        catch
        {
            lockClient.Dispose();
            throw;
        }
    }

    private async Task ReleaseFreezeDatabaseLockAsync(
        ISqlSugarClient? lockClient,
        string environment
    )
    {
        if (lockClient == null)
        {
            return;
        }

        try
        {
            await lockClient.Ado.ExecuteCommandAsync(
                """
                EXEC sys.sp_releaseapplock
                    @Resource = @LockResource,
                    @LockOwner = N'Session';
                """,
                new SugarParameter(
                    "@LockResource",
                    $"PerformanceBaseline_Freeze_{environment}"
                )
            );
        }
        catch (Exception ex)
        {
            // 关闭专用连接也会释放会话锁；释放失败不能覆盖冻结本身的结果。
            _logger.LogWarning(ex, "释放性能基线冻结数据库锁失败: {Environment}", environment);
        }
        finally
        {
            lockClient.Dispose();
        }
    }

    private async Task AcquireSqliteFreezeWriteLockAsync(string environment)
    {
        // SQLite 默认延迟事务；先执行无值变更的 UPDATE 获取写锁，避免另一进程并发读旧候选。
        await _db.Ado.ExecuteCommandAsync(
            "UPDATE PerformanceBaselineCycle SET UpdatedAt = UpdatedAt WHERE Environment = @Environment",
            new SugarParameter("@Environment", environment)
        );
    }

    private async Task StartObservationCycleIfNeededAsync(string environment, DateTime utcNow)
    {
        var exists = await _db
            .Queryable<PerformanceBaselineCycle>()
            .AnyAsync(item => item.Environment == environment);
        if (exists)
        {
            return;
        }

        try
        {
            await _db
                .Insertable(
                    new PerformanceBaselineCycle
                    {
                        Environment = environment,
                        State = "observing",
                        ObservationStartedAtUtc = utcNow,
                        ObservationEndsAtUtc = utcNow.AddDays(14),
                    }
                )
                .ExecuteCommandAsync();
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // 多实例同时收到首个生产验收事件时，由唯一索引保证只创建一个周期。
            return;
        }
        _logger.LogInformation(
            "性能基线观察周期已开始: {Environment}, Start={Start}, End={End}",
            environment,
            utcNow,
            utcNow.AddDays(14)
        );
    }

    private async Task<ClientSamplingPolicy> GetClientSamplingPolicyAsync(
        string environment,
        IReadOnlyCollection<PerformanceMetricEventV1Dto> events
    )
    {
        var cycle = await _db
            .Queryable<PerformanceBaselineCycle>()
            .Where(item => item.Environment == environment)
            .OrderBy(item => item.ObservationStartedAtUtc, OrderByType.Desc)
            .FirstAsync();
        if (cycle == null || cycle.State != "frozen")
        {
            return new ClientSamplingPolicy(cycle?.State ?? "not_started", 1, []);
        }

        var selectors = events
            .Select(item =>
                (
                    item.Metric,
                    PerformanceMetricDimensions.Normalize(item.Metric, item.Dimensions).Selector
                )
            )
            .Distinct()
            .ToHashSet();
        var definitions = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .Where(item => item.CycleId == cycle.Id && item.CoverageState == "qualified")
            .ToListAsync();
        var policies = definitions
            .Where(item => selectors.Contains((item.MetricName, item.Selector)))
            .Select(item => new PerformanceClientSamplingPolicyDto
            {
                Metric = item.MetricName,
                Selector = item.Selector,
                SampleRate = 0.2,
                SlowThreshold = item.WarningThreshold,
            })
            .ToList();
        // 未达到冻结样本量或冻结后新出现的 selector 继续全量观察；仅精确命中合格定义时降为 20%。
        return new ClientSamplingPolicy("frozen", 1, policies);
    }

    private async Task<PerformanceBaselineStatusDto> GetBaselineStatusCoreAsync(string environment)
    {
        var cycle = await _db
            .Queryable<PerformanceBaselineCycle>()
            .Where(item => item.Environment == environment)
            .OrderBy(item => item.ObservationStartedAtUtc, OrderByType.Desc)
            .FirstAsync();
        if (cycle == null)
        {
            return new PerformanceBaselineStatusDto();
        }

        var definitions = await _db
            .Queryable<PerformanceBaselineDefinition>()
            .Where(item => item.CycleId == cycle.Id)
            .ToListAsync();
        return new PerformanceBaselineStatusDto
        {
            State = cycle.State,
            ObservationStartedAtUtc = PerformanceUtc.Normalize(cycle.ObservationStartedAtUtc),
            ObservationEndsAtUtc = PerformanceUtc.Normalize(cycle.ObservationEndsAtUtc),
            FrozenAtUtc = PerformanceUtc.Normalize(cycle.FrozenAtUtc),
            QualifiedMetricCount = definitions.Count(item => item.CoverageState == "qualified"),
            InsufficientMetricCount = definitions.Count(item => item.CoverageState == "insufficient"),
        };
    }

    private static PerformancePercentileDto ToPercentile(
        string metricName,
        string selector,
        IEnumerable<MetricAggregateRow> buckets
    )
    {
        var rows = buckets.ToList();
        var histogram = PerformanceHistogram.Create();
        foreach (var row in rows)
        {
            IReadOnlyList<long> counts;
            try
            {
                counts = JsonSerializer.Deserialize<long[]>(row.HistogramCountsJson) ?? [];
            }
            catch (JsonException)
            {
                counts = [];
            }
            histogram.Merge(PerformanceHistogram.FromCounts(counts, row.MaximumValue));
        }

        var sampleCount = rows.Sum(item => item.SampleCount);
        double? average = sampleCount == 0
            ? null
            : rows.Sum(item => item.SumValue) / sampleCount;
        var isRatioAverage = metricName is PerformanceMetricNames.HqSyncFailureRate
            or PerformanceMetricNames.HqSyncSuccessRate
            or PerformanceMetricNames.BackgroundJobFailureRate
            or PerformanceMetricNames.BackgroundJobSuccessRate
            or PerformanceMetricNames.SentryCrashFreeSession;
        return new PerformancePercentileDto
        {
            Metric = metricName,
            Selector = selector,
            SampleCount = sampleCount,
            P50 = isRatioAverage ? average : histogram.EstimatePercentile(0.50),
            P95 = isRatioAverage ? average : histogram.EstimatePercentile(0.95),
            P99 = isRatioAverage ? average : histogram.EstimatePercentile(0.99),
            Average = average,
            Maximum = sampleCount == 0 ? null : rows.Max(item => item.MaximumValue),
            LastObservedAtUtc = rows.Count == 0 ? null : rows.Max(item => item.LastObservedAtUtc),
            CoverageState = sampleCount >= RequiredSampleCount(metricName) ? "qualified" : "insufficient",
        };
    }

    private Task<List<MetricAggregateRow>> LoadAggregateRowsAsync(
        string environment,
        DateTime start,
        DateTime end,
        string? metricName = null,
        MetricAggregateGrouping grouping = MetricAggregateGrouping.MetricSelector,
        int? maxAggregateRows = null,
        Func<Exception>? limitExceeded = null,
        bool freezeEligibleOnly = false
    ) =>
        ExecuteConsistentReadAsync(
            () =>
                LoadAggregateRowsCoreAsync(
                    environment,
                    start,
                    end,
                    metricName,
                    grouping,
                    maxAggregateRows,
                    limitExceeded,
                    freezeEligibleOnly
                )
        );

    private async Task<List<MetricAggregateRow>> LoadAggregateRowsCoreAsync(
        string environment,
        DateTime start,
        DateTime end,
        string? metricName,
        MetricAggregateGrouping grouping,
        int? maxAggregateRows,
        Func<Exception>? limitExceeded,
        bool freezeEligibleOnly
    )
    {
        var aggregates = new Dictionary<MetricAggregateKey, MetricAggregateAccumulator>();
        var bucketQuery = _db
            .Queryable<PerformanceMetricBucket>()
            .With(SqlWith.Null)
            .Where(item =>
                item.Environment == environment
                && item.WindowStartUtc >= start
                && item.WindowStartUtc < end
            );
        if (!string.IsNullOrWhiteSpace(metricName))
        {
            bucketQuery = bucketQuery.Where(item => item.MetricName == metricName);
        }
        if (freezeEligibleOnly)
        {
            // 公开项目键只证明可写，不证明来源可信；历史 client 与显式 public 桶均不得进入正式基线。
            bucketQuery = bucketQuery.Where(item =>
                item.SourceType != "client" && item.SourceType != "client-public"
            );
        }
        // 单条 forward-only reader 在读取时立即合并；不会把全部实体物化，也没有 offset 分页的并发漂移。
        await bucketQuery.ForEachDataReaderAsync(item =>
        {
            MergeAggregateRow(
                aggregates,
                new MetricAggregateRow(
                    item.MetricName,
                    item.ProjectCode,
                    item.Selector,
                    item.DimensionsJson,
                    PerformanceUtc.Normalize(item.WindowStartUtc),
                    item.BucketSizeMinutes,
                    item.SampleCount,
                    item.SumValue,
                    item.MaximumValue,
                    item.HistogramCountsJson,
                    PerformanceUtc.Normalize(item.LastObservedAtUtc)
                ),
                grouping,
                maxAggregateRows,
                limitExceeded
            );
        });

        var dailyStart = start.TimeOfDay == TimeSpan.Zero ? start.Date : start.Date.AddDays(1);
        var dailyEnd = end.Date;
        if (dailyStart < dailyEnd)
        {
            var dailyQuery = _db
                .Queryable<PerformanceMetricDailyAggregate>()
                .With(SqlWith.Null)
                .Where(item =>
                    item.Environment == environment
                    && item.DayUtc >= dailyStart
                    && item.DayUtc < dailyEnd
                );
            if (freezeEligibleOnly)
            {
                dailyQuery = dailyQuery.Where(item =>
                    item.SourceType != "client" && item.SourceType != "client-public"
                );
            }
            if (!string.IsNullOrWhiteSpace(metricName))
            {
                dailyQuery = dailyQuery.Where(item => item.MetricName == metricName);
            }
            await dailyQuery.ForEachDataReaderAsync(item =>
            {
                MergeAggregateRow(
                    aggregates,
                    new MetricAggregateRow(
                        item.MetricName,
                        item.ProjectCode,
                        item.Selector,
                        item.DimensionsJson,
                        PerformanceUtc.Normalize(item.DayUtc),
                        24 * 60,
                        item.SampleCount,
                        item.SumValue,
                        item.MaximumValue,
                        item.HistogramCountsJson,
                        PerformanceUtc.Normalize(item.LastObservedAtUtc)
                    ),
                    grouping,
                    maxAggregateRows,
                    limitExceeded
                );
            });
        }

        if (metricName == null || IsOperationalRunMetric(metricName))
        {
            await LoadOperationalRunRowsAsync(
                aggregates,
                environment,
                start,
                end,
                metricName,
                grouping,
                maxAggregateRows,
                limitExceeded
            );
        }
        return aggregates.Values.Select(item => item.ToRow()).ToList();
    }

    private async Task LoadOperationalRunRowsAsync(
        Dictionary<MetricAggregateKey, MetricAggregateAccumulator> aggregates,
        string environment,
        DateTime start,
        DateTime end,
        string? metricName,
        MetricAggregateGrouping grouping,
        int? maxAggregateRows,
        Func<Exception>? limitExceeded
    )
    {
        var runQuery = _db
            .Queryable<PerformanceOperationalRun>()
            .With(SqlWith.Null)
            .Where(item =>
                item.Environment == environment
                && item.CompletedAtUtc >= start
                && item.CompletedAtUtc < end
                && item.DurationMs != null
                && (item.Status == "success" || item.Status == "failure")
            );
        await runQuery.ForEachDataReaderAsync(run =>
        {
            var completedAt = PerformanceUtc.Normalize(run.CompletedAtUtc!.Value);
            var isHq = string.Equals(run.Category, "hq", StringComparison.Ordinal);
            var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["taskType"] = run.Category,
                ["operation"] = run.Operation,
                ["outcome"] = run.Status,
            };
            AddOperationalMetric(
                aggregates,
                metricName,
                isHq
                    ? PerformanceMetricNames.HqSyncDuration
                    : PerformanceMetricNames.BackgroundJobDuration,
                run.DurationMs!.Value,
                completedAt,
                dimensions,
                grouping,
                maxAggregateRows,
                limitExceeded
            );
            AddOperationalMetric(
                aggregates,
                metricName,
                isHq
                    ? PerformanceMetricNames.HqSyncSuccessRate
                    : PerformanceMetricNames.BackgroundJobSuccessRate,
                run.Status == "success" ? 1 : 0,
                completedAt,
                dimensions,
                grouping,
                maxAggregateRows,
                limitExceeded
            );
            AddOperationalMetric(
                aggregates,
                metricName,
                isHq
                    ? PerformanceMetricNames.HqSyncFailureRate
                    : PerformanceMetricNames.BackgroundJobFailureRate,
                run.Status == "failure" ? 1 : 0,
                completedAt,
                dimensions,
                grouping,
                maxAggregateRows,
                limitExceeded
            );
        });
    }

    internal static System.Data.IsolationLevel ResolveConsistentReadIsolationLevel(DbType dbType) =>
        dbType == DbType.SqlServer
            ? System.Data.IsolationLevel.Snapshot
            : System.Data.IsolationLevel.Serializable;

    private async Task<T> ExecuteConsistentReadAsync<T>(Func<Task<T>> read)
    {
        if (_db.Ado.Transaction != null)
        {
            return await read();
        }

        var transactionStarted = false;
        try
        {
            await _db.Ado.BeginTranAsync(
                ResolveConsistentReadIsolationLevel(_db.CurrentConnectionConfig.DbType)
            );
            transactionStarted = true;
            var result = await read();
            await _db.Ado.CommitTranAsync();
            transactionStarted = false;
            return result;
        }
        catch
        {
            if (transactionStarted && _db.Ado.Transaction != null)
            {
                await _db.Ado.RollbackTranAsync();
            }
            throw;
        }
    }

    private static PerformanceSeriesQueryException SeriesPointLimitExceeded() =>
        new(
            $"序列查询结果超过 {MaxSeriesPointCount} 个点，请缩小时间窗口",
            "PERFORMANCE_SERIES_POINT_LIMIT_EXCEEDED"
        );

    private static PerformanceOverviewQueryException OverviewAggregateLimitExceeded() =>
        new(
            $"总览查询结果超过 {MaxOverviewAggregateCount} 个聚合项，请缩小时间窗口",
            "PERFORMANCE_OVERVIEW_RESULT_LIMIT_EXCEEDED"
        );

    private void AddOperationalMetric(
        Dictionary<MetricAggregateKey, MetricAggregateAccumulator> aggregates,
        string? requestedMetric,
        string metricName,
        double value,
        DateTime observedAt,
        IReadOnlyDictionary<string, string> dimensions,
        MetricAggregateGrouping grouping,
        int? maxAggregateRows,
        Func<Exception>? limitExceeded
    )
    {
        if (requestedMetric != null && requestedMetric != metricName)
        {
            return;
        }
        var normalized = PerformanceMetricDimensions.Normalize(metricName, dimensions);
        var histogram = PerformanceHistogram.Create();
        histogram.Record(value);
        MergeAggregateRow(
            aggregates,
            new MetricAggregateRow(
                metricName,
                _options.BackendProjectCode,
                normalized.Selector,
                normalized.Json,
                FloorToFiveMinutes(observedAt),
                5,
                1,
                value,
                value,
                JsonSerializer.Serialize(histogram.Counts),
                observedAt
            ),
            grouping,
            maxAggregateRows,
            limitExceeded
        );
    }

    private static void MergeAggregateRow(
        Dictionary<MetricAggregateKey, MetricAggregateAccumulator> aggregates,
        MetricAggregateRow row,
        MetricAggregateGrouping grouping,
        int? maxAggregateRows,
        Func<Exception>? limitExceeded
    )
    {
        var key = grouping switch
        {
            MetricAggregateGrouping.MetricSelector => new MetricAggregateKey(
                row.MetricName,
                string.Empty,
                row.Selector,
                default,
                0
            ),
            MetricAggregateGrouping.MetricProjectSelector => new MetricAggregateKey(
                row.MetricName,
                row.ProjectCode,
                row.Selector,
                default,
                0
            ),
            MetricAggregateGrouping.SeriesPoint => new MetricAggregateKey(
                row.MetricName,
                string.Empty,
                row.Selector,
                row.WindowStartUtc,
                row.BucketSizeMinutes
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping)),
        };
        if (aggregates.TryGetValue(key, out var existing))
        {
            existing.Merge(row);
            return;
        }
        if (maxAggregateRows.HasValue && aggregates.Count >= maxAggregateRows.Value)
        {
            throw limitExceeded?.Invoke()
                ?? new InvalidOperationException("性能聚合结果超过安全上限");
        }
        aggregates[key] = new MetricAggregateAccumulator(row);
    }

    private enum MetricAggregateGrouping
    {
        MetricSelector,
        MetricProjectSelector,
        SeriesPoint,
    }

    private readonly record struct MetricAggregateKey(
        string MetricName,
        string ProjectCode,
        string Selector,
        DateTime WindowStartUtc,
        int BucketSizeMinutes
    );

    private sealed class MetricAggregateAccumulator
    {
        private readonly long[] _histogramCounts = new long[PerformanceHistogram.Boundaries.Length];

        public MetricAggregateAccumulator(MetricAggregateRow row)
        {
            MetricName = row.MetricName;
            ProjectCode = row.ProjectCode;
            Selector = row.Selector;
            DimensionsJson = row.DimensionsJson;
            WindowStartUtc = row.WindowStartUtc;
            BucketSizeMinutes = row.BucketSizeMinutes;
            Merge(row);
        }

        private string MetricName { get; }
        private string ProjectCode { get; }
        private string Selector { get; }
        private string DimensionsJson { get; }
        private DateTime WindowStartUtc { get; set; }
        private int BucketSizeMinutes { get; }
        private long SampleCount { get; set; }
        private double SumValue { get; set; }
        private double MaximumValue { get; set; }
        private DateTime LastObservedAtUtc { get; set; }

        public void Merge(MetricAggregateRow row)
        {
            SampleCount = checked(SampleCount + row.SampleCount);
            SumValue += row.SumValue;
            MaximumValue = Math.Max(MaximumValue, row.MaximumValue);
            if (row.WindowStartUtc < WindowStartUtc)
            {
                WindowStartUtc = row.WindowStartUtc;
            }
            if (row.LastObservedAtUtc > LastObservedAtUtc)
            {
                LastObservedAtUtc = row.LastObservedAtUtc;
            }

            IReadOnlyList<long> counts;
            try
            {
                counts = JsonSerializer.Deserialize<long[]>(row.HistogramCountsJson) ?? [];
            }
            catch (JsonException)
            {
                counts = [];
            }
            for (var index = 0; index < Math.Min(counts.Count, _histogramCounts.Length); index++)
            {
                _histogramCounts[index] = checked(
                    _histogramCounts[index] + Math.Max(0, counts[index])
                );
            }
        }

        public MetricAggregateRow ToRow() =>
            new(
                MetricName,
                ProjectCode,
                Selector,
                DimensionsJson,
                WindowStartUtc,
                BucketSizeMinutes,
                SampleCount,
                SumValue,
                MaximumValue,
                JsonSerializer.Serialize(_histogramCounts),
                LastObservedAtUtc
            );
    }

    private sealed record MetricAggregateRow(
        string MetricName,
        string ProjectCode,
        string Selector,
        string DimensionsJson,
        DateTime WindowStartUtc,
        int BucketSizeMinutes,
        long SampleCount,
        double SumValue,
        double MaximumValue,
        string HistogramCountsJson,
        DateTime LastObservedAtUtc
    );

    private static int RequiredSampleCount(string metricName) => metricName switch
    {
        PerformanceMetricNames.ApiRequestDuration => 100,
        PerformanceMetricNames.SqlCommandDuration => 100,
        PerformanceMetricNames.HqSyncDuration => 5,
        PerformanceMetricNames.HqSyncSuccessRate => 5,
        PerformanceMetricNames.HqSyncFailureRate => 5,
        PerformanceMetricNames.HqSyncBacklog => 5,
        PerformanceMetricNames.BackgroundJobDuration => 5,
        PerformanceMetricNames.BackgroundJobSuccessRate => 5,
        PerformanceMetricNames.BackgroundJobFailureRate => 5,
        PerformanceMetricNames.SentryCrashFreeSession => 100,
        PerformanceMetricNames.CiRunDuration => 10,
        PerformanceMetricNames.WebFirstScreenBytes => 30,
        PerformanceMetricNames.WebLargestInitialChunkBytes => 30,
        _ when metricName.StartsWith("web.", StringComparison.Ordinal) => 30,
        _ when metricName.StartsWith("pos.", StringComparison.Ordinal) => 30,
        _ => 30,
    };

    private async Task<List<PerformanceBaselineDefinition>> LoadExactWebBundleDefinitionsAsync(
        Guid cycleId,
        string environment,
        DateTime startUtc,
        DateTime endUtc
    )
    {
        var samples = await _db
            .Queryable<PerformanceMetricSample>()
            .With(SqlWith.Null)
            .Where(item =>
                item.Environment == environment
                && item.SourceType == "ci"
                && item.ObservedAtUtc >= startUtc
                && item.ObservedAtUtc < endUtc
                && (
                    item.MetricName == PerformanceMetricNames.WebFirstScreenBytes
                    || item.MetricName == PerformanceMetricNames.WebLargestInitialChunkBytes
                )
            )
            .ToListAsync();

        return samples
            .GroupBy(item => (item.MetricName, item.Selector))
            .Select(group =>
            {
                var values = group.Select(item => item.Value).Order().ToArray();
                var qualified = values.Length >= RequiredSampleCount(group.Key.MetricName);
                return new PerformanceBaselineDefinition
                {
                    CycleId = cycleId,
                    MetricName = group.Key.MetricName,
                    Selector = group.Key.Selector,
                    SampleCount = values.LongLength,
                    P50 = ExactPercentile(values, 0.50),
                    P95 = ExactPercentile(values, 0.95),
                    P99 = ExactPercentile(values, 0.99),
                    WarningThreshold = null,
                    CoverageState = qualified ? "qualified" : "insufficient",
                    GatePolicy = "web_bundle_hard",
                };
            })
            .ToList();
    }

    private static double? ExactPercentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }
        var index = Math.Clamp(
            (int)Math.Ceiling(sortedValues.Count * percentile) - 1,
            0,
            sortedValues.Count - 1
        );
        return sortedValues[index];
    }

    private static bool IsWebBundleMetric(string metricName) =>
        metricName is PerformanceMetricNames.WebFirstScreenBytes
            or PerformanceMetricNames.WebLargestInitialChunkBytes;

    private static double WarningThreshold(string metricName, double p95) => metricName switch
    {
        PerformanceMetricNames.HqSyncBacklog => PerformanceBaselineThreshold.BacklogWarning(p95),
        PerformanceMetricNames.HqSyncFailureRate or PerformanceMetricNames.BackgroundJobFailureRate =>
            PerformanceBaselineThreshold.FailureRateWarning(p95),
        PerformanceMetricNames.SentryCrashFreeSession =>
            1 - PerformanceBaselineThreshold.CrashRateWarning(Math.Max(0, 1 - p95)),
        _ => PerformanceBaselineThreshold.LatencyWarning(p95),
    };

    private static bool IsDisplayOnlyMetric(string metricName) =>
        metricName is PerformanceMetricNames.HqSyncSuccessRate
            or PerformanceMetricNames.BackgroundJobSuccessRate;

    private static bool IsOperationalRunMetric(string metricName) =>
        metricName is PerformanceMetricNames.HqSyncDuration
            or PerformanceMetricNames.HqSyncSuccessRate
            or PerformanceMetricNames.HqSyncFailureRate
            or PerformanceMetricNames.BackgroundJobDuration
            or PerformanceMetricNames.BackgroundJobSuccessRate
            or PerformanceMetricNames.BackgroundJobFailureRate;

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

    private static string? ValidateReleaseEvent(PerformanceReleaseEventRequestDto request)
    {
        if (request.EventId == Guid.Empty)
            return "eventId 不能为空";
        if (request.Action is not ("deploy" or "rollback"))
            return "action 仅支持 deploy 或 rollback";
        if (request.Status is not ("accepted" or "failed"))
            return "status 仅支持 accepted 或 failed";
        if (string.IsNullOrWhiteSpace(request.Environment) || string.IsNullOrWhiteSpace(request.Component))
            return "environment 和 component 不能为空";
        if (string.IsNullOrWhiteSpace(request.Commit) || string.IsNullOrWhiteSpace(request.Source))
            return "commit 和 source 不能为空";
        if (AsUtc(request.CompletedAtUtc) < AsUtc(request.StartedAtUtc))
            return "completedAtUtc 不能早于 startedAtUtc";
        return null;
    }

    private static bool IsAcceptedDeploy(PerformanceReleaseEventRequestDto request) =>
        string.Equals(request.Action, "deploy", StringComparison.Ordinal)
        && string.Equals(request.Status, "accepted", StringComparison.Ordinal);

    private static bool IsAcceptedProductionDeploy(PerformanceReleaseEventRequestDto request) =>
        IsAcceptedDeploy(request)
        && string.Equals(
            NormalizeEnvironment(request.Environment, "unknown"),
            "Production",
            StringComparison.Ordinal
        );

    private static bool IsAcceptedProductionDeploy(PerformanceReleaseEvent releaseEvent) =>
        string.Equals(releaseEvent.Action, "deploy", StringComparison.Ordinal)
        && string.Equals(releaseEvent.Status, "accepted", StringComparison.Ordinal)
        && string.Equals(releaseEvent.Environment, "Production", StringComparison.Ordinal);

    private static PerformanceReleaseEvent ToReleaseEvent(
        PerformanceReleaseEventRequestDto request
    ) =>
        new()
        {
            Id = request.EventId,
            Action = request.Action,
            Status = request.Status,
            Environment = NormalizeEnvironment(request.Environment, "unknown"),
            Component = Normalize(request.Component, 120, "unknown"),
            Commit = Normalize(request.Commit, 80, "unknown"),
            Version = NormalizeOptional(request.Version, 80),
            StartedAtUtc = AsUtc(request.StartedAtUtc),
            CompletedAtUtc = AsUtc(request.CompletedAtUtc),
            Source = Normalize(request.Source, 120, "unknown"),
        };

    private static PerformanceReleaseEventRequestDto ToReleaseEventRequest(
        PerformanceReleaseEvent releaseEvent
    ) =>
        new()
        {
            EventId = releaseEvent.Id,
            Action = releaseEvent.Action,
            Status = releaseEvent.Status,
            Environment = releaseEvent.Environment,
            Component = releaseEvent.Component,
            Commit = releaseEvent.Commit,
            Version = releaseEvent.Version,
            StartedAtUtc = releaseEvent.StartedAtUtc,
            CompletedAtUtc = releaseEvent.CompletedAtUtc,
            Source = releaseEvent.Source,
        };

    private static bool ReleaseEventEquals(
        PerformanceReleaseEvent left,
        PerformanceReleaseEvent right
    ) =>
        left.Id == right.Id
        && string.Equals(left.Action, right.Action, StringComparison.Ordinal)
        && string.Equals(left.Status, right.Status, StringComparison.Ordinal)
        && string.Equals(left.Environment, right.Environment, StringComparison.Ordinal)
        && string.Equals(left.Component, right.Component, StringComparison.Ordinal)
        && string.Equals(left.Commit, right.Commit, StringComparison.Ordinal)
        && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
        && AsUtc(left.StartedAtUtc) == AsUtc(right.StartedAtUtc)
        && AsUtc(left.CompletedAtUtc) == AsUtc(right.CompletedAtUtc)
        && string.Equals(left.Source, right.Source, StringComparison.Ordinal);

    private static DateTime AsUtc(DateTime value) => PerformanceUtc.Normalize(value);

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string NormalizeEnvironment(string? value, string fallback)
    {
        var normalized = Normalize(value, 60, fallback);
        if (string.Equals(normalized, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "Production";
        }
        if (string.Equals(normalized, "pullrequest", StringComparison.OrdinalIgnoreCase))
        {
            return "PullRequest";
        }
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static TimeSpan SlowSqlWindow(string? window) => window?.Trim().ToLowerInvariant() switch
    {
        "1h" => TimeSpan.FromHours(1),
        "7d" => TimeSpan.FromDays(7),
        _ => TimeSpan.FromHours(24),
    };

    private static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (
                message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }
        return false;
    }

    private sealed record ClientSamplingPolicy(
        string State,
        double DefaultSampleRate,
        List<PerformanceClientSamplingPolicyDto> Policies
    );
}
