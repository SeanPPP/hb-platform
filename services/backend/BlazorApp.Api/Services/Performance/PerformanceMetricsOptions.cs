namespace BlazorApp.Api.Services.Performance;

public sealed class PerformanceMetricsOptions
{
    public bool Enabled { get; set; } = true;

    public string DefaultEnvironment { get; set; } = "Production";

    public string BackendProjectCode { get; set; } = "hb-backend";

    public string? InstanceId { get; set; }

    public int FlushIntervalSeconds { get; set; } = 30;

    public int RawSampleRetentionDays { get; set; } = 30;

    public int BucketRetentionDays { get; set; } = 90;

    public int AggregateRetentionMonths { get; set; } = 13;

    public int ClientRequestsPerMinute { get; set; } = 30;

    public int ProjectRequestsPerMinute { get; set; } = 300;

    public int ClientEventsPerMinute { get; set; } = 2_000;

    public int ProjectEventsPerMinute { get; set; } = 40_000;

    public int ClientBytesPerMinute { get; set; } = 4 * 1024 * 1024;

    public int ProjectBytesPerMinute { get; set; } = 32 * 1024 * 1024;

    public int OperationalRunLeaseSeconds { get; set; } = 120;

    public int OperationalRunHeartbeatSeconds { get; set; } = 30;
}
