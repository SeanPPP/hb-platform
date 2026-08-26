using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceOperationalRun")]
public sealed class PerformanceOperationalRun : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? ExternalRunId { get; set; }

    [SugarColumn(Length = 40, IsNullable = false)]
    public string Category { get; set; } = string.Empty;

    [SugarColumn(Length = 160, IsNullable = false)]
    public string Operation { get; set; } = string.Empty;

    [SugarColumn(Length = 30, IsNullable = false)]
    public string Status { get; set; } = "queued";

    [SugarColumn(IsNullable = false)]
    public int Attempt { get; set; } = 1;

    [SugarColumn(IsNullable = true)]
    public int? Backlog { get; set; }

    [SugarColumn(Length = 60, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 80, IsNullable = false)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(Length = 160, IsNullable = true)]
    public string? OwnerInstanceId { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LeaseExpiresAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastHeartbeatAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastTransitionAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime QueuedAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DurationMs { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(2000)", IsNullable = true)]
    public string? MetadataJson { get; set; }
}
