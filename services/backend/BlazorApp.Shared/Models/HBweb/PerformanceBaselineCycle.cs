using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceBaselineCycle")]
public sealed class PerformanceBaselineCycle : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 60, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 30, IsNullable = false)]
    public string State { get; set; } = "observing";

    [SugarColumn(IsNullable = false)]
    public DateTime ObservationStartedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime ObservationEndsAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CandidateGeneratedAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? FrozenAtUtc { get; set; }

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? FrozenBy { get; set; }
}
