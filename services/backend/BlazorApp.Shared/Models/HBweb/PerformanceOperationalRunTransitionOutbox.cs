using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceOperationalRunTransitionOutbox")]
public sealed class PerformanceOperationalRunTransitionOutbox : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 120, IsNullable = false)]
    public string ExternalRunId { get; set; } = string.Empty;

    [SugarColumn(Length = 40, IsNullable = false)]
    public string Category { get; set; } = string.Empty;

    [SugarColumn(Length = 160, IsNullable = false)]
    public string Operation { get; set; } = string.Empty;

    [SugarColumn(Length = 30, IsNullable = false)]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime OccurredAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public int Attempt { get; set; } = 1;

    [SugarColumn(IsNullable = true)]
    public int? Backlog { get; set; }

    [SugarColumn(IsNullable = false)]
    public int RetryCount { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime NextAttemptAtUtc { get; set; }

    [SugarColumn(Length = 160, IsNullable = true)]
    public string? LastErrorType { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeadLetteredAtUtc { get; set; }
}
