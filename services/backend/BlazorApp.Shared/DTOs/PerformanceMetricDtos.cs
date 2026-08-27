using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

public static class PerformanceMetricNames
{
    public const string ApiRequestDuration = "api.request.duration";
    public const string SqlCommandDuration = "sql.command.duration";
    public const string HqSyncDuration = "hq.sync.duration";
    public const string HqSyncSuccessRate = "hq.sync.success_rate";
    public const string HqSyncFailureRate = "hq.sync.failure_rate";
    public const string HqSyncBacklog = "hq.sync.backlog";
    public const string BackgroundJobDuration = "background.job.duration";
    public const string BackgroundJobSuccessRate = "background.job.success_rate";
    public const string BackgroundJobFailureRate = "background.job.failure_rate";
    public const string WebFirstScreenBytes = "web.first_screen.bytes";
    public const string WebLargestInitialChunkBytes = "web.largest_initial_chunk.bytes";
    public const string WebTableReactCommit = "web.table.react_commit.duration";
    public const string WebTableRenderToPaint = "web.table.render_to_paint.duration";
    public const string PosColdStart = "pos.cold_start.duration";
    public const string PosScanToCart = "pos.scan_to_cart.duration";
    public const string PosPaymentResponse = "pos.payment_response.duration";
    public const string SentryCrashFreeSession = "sentry.crash_free_session.ratio";
    public const string CiRunDuration = "ci.run.duration";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ApiRequestDuration,
        SqlCommandDuration,
        HqSyncDuration,
        HqSyncSuccessRate,
        HqSyncFailureRate,
        HqSyncBacklog,
        BackgroundJobDuration,
        BackgroundJobSuccessRate,
        BackgroundJobFailureRate,
        WebFirstScreenBytes,
        WebLargestInitialChunkBytes,
        WebTableReactCommit,
        WebTableRenderToPaint,
        PosColdStart,
        PosScanToCart,
        PosPaymentResponse,
        SentryCrashFreeSession,
        CiRunDuration,
    };
}

public static class PerformanceMetricUnits
{
    public const string Milliseconds = "ms";
    public const string Bytes = "bytes";
    public const string Count = "count";
    public const string Ratio = "ratio";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Milliseconds,
        Bytes,
        Count,
        Ratio,
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PerformanceMetricBatchV1Dto
{
    public int SchemaVersion { get; set; } = 1;

    public List<PerformanceMetricEventV1Dto> Events { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PerformanceMetricEventV1Dto
{
    public Guid EventId { get; set; }

    public string Metric { get; set; } = string.Empty;

    public DateTime ObservedAt { get; set; }

    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;

    public Dictionary<string, string> Dimensions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PerformanceMetricIngestResultDto
{
    public int AcceptedCount { get; set; }

    public int DuplicateCount { get; set; }

    public int RejectedCount { get; set; }

    public string BaselineState { get; set; } = "not_started";

    public double DefaultSampleRate { get; set; } = 1;

    public List<PerformanceClientSamplingPolicyDto> Policies { get; set; } = new();
}

public sealed class PerformanceClientSamplingPolicyDto
{
    public string Metric { get; set; } = string.Empty;

    public string Selector { get; set; } = "all";

    public double SampleRate { get; set; } = 1;

    public double? SlowThreshold { get; set; }
}

public class PerformanceOverviewQueryDto
{
    public string Environment { get; set; } = "Production";

    public DateTime? StartUtc { get; set; }

    public DateTime? EndUtc { get; set; }
}

public sealed class PerformanceSlowSqlQueryDto : PerformanceOverviewQueryDto
{
    public string Window { get; set; } = "24h";

    public string SortBy { get; set; } = "total";
}

public class PerformancePercentileDto
{
    public string Metric { get; set; } = string.Empty;

    public string Selector { get; set; } = string.Empty;

    public long SampleCount { get; set; }

    public double? P50 { get; set; }

    public double? P95 { get; set; }

    public double? P99 { get; set; }

    public double? Average { get; set; }

    public double? Maximum { get; set; }

    public DateTime? LastObservedAtUtc { get; set; }

    public string CoverageState { get; set; } = "insufficient";

    public double? BaselineP95 { get; set; }

    public double? WarningThreshold { get; set; }

    public bool IsWarning { get; set; }

    public int ConsecutiveBreaches { get; set; }
}

public sealed class PerformanceOverviewDto
{
    public string Environment { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    public PerformanceBaselineStatusDto Baseline { get; set; } = new();

    public List<PerformancePercentileDto> Api { get; set; } = new();

    public List<PerformancePercentileDto> Sql { get; set; } = new();

    public List<PerformancePercentileDto> HqAndJobs { get; set; } = new();

    public List<PerformancePercentileDto> WebAndPos { get; set; } = new();

    public List<PerformancePercentileDto> Delivery { get; set; } = new();

    public int AcceptedDeployments { get; set; }

    public int AcceptedRollbacks { get; set; }

    public List<PerformanceReleaseEventDto> ReleaseEvents { get; set; } = new();
}

public sealed class PerformanceReleaseEventDto
{
    public Guid Id { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public string Commit { get; set; } = string.Empty;

    public string? Version { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime CompletedAtUtc { get; set; }

    public string Source { get; set; } = string.Empty;
}

public sealed class PerformanceSeriesPointDto : PerformancePercentileDto
{
    public DateTime WindowStartUtc { get; set; }

    public int BucketSizeMinutes { get; set; }
}

public sealed class PerformanceSeriesDto
{
    public string Environment { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public List<PerformanceSeriesPointDto> Points { get; set; } = new();
}

public sealed class PerformanceSlowSqlDto
{
    public string DatabaseContext { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public string Template { get; set; } = string.Empty;

    public long ExecutionCount { get; set; }

    public double TotalDurationMs { get; set; }

    public double AverageDurationMs { get; set; }

    public double? P95DurationMs { get; set; }

    public double MaximumDurationMs { get; set; }

    public DateTime LastObservedAtUtc { get; set; }
}

public sealed class PerformanceOperationalRunDto
{
    public Guid Id { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public int? Backlog { get; set; }

    public DateTime QueuedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public long? DurationMs { get; set; }
}

public sealed class PerformanceBaselineStatusDto
{
    public string State { get; set; } = "not_started";

    public DateTime? ObservationStartedAtUtc { get; set; }

    public DateTime? ObservationEndsAtUtc { get; set; }

    public DateTime? FrozenAtUtc { get; set; }

    public int QualifiedMetricCount { get; set; }

    public int InsufficientMetricCount { get; set; }
}

public sealed class PerformanceBaselineDefinitionDto
{
    public string Metric { get; set; } = string.Empty;

    public string Selector { get; set; } = "all";

    public long SampleCount { get; set; }

    public double? P50 { get; set; }

    public double? P95 { get; set; }

    public double? P99 { get; set; }

    public double? WarningThreshold { get; set; }

    public string CoverageState { get; set; } = "insufficient";

    public string GatePolicy { get; set; } = "runtime_warning";
}

public sealed class PerformanceBaselineDto
{
    public PerformanceBaselineStatusDto Status { get; set; } = new();

    public List<PerformanceBaselineDefinitionDto> Definitions { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PerformanceBaselineFreezeRequestDto
{
    public string Environment { get; set; } = "Production";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PerformanceReleaseEventRequestDto
{
    public Guid EventId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public string Commit { get; set; } = string.Empty;

    public string? Version { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime CompletedAtUtc { get; set; }

    public string Source { get; set; } = string.Empty;
}
