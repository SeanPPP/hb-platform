using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceBaselineDefinition")]
public sealed class PerformanceBaselineDefinition : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(IsNullable = false)]
    public Guid CycleId { get; set; }

    [SugarColumn(Length = 120, IsNullable = false)]
    public string MetricName { get; set; } = string.Empty;

    [SugarColumn(Length = 500, IsNullable = false)]
    public string Selector { get; set; } = "all";

    [SugarColumn(IsNullable = false)]
    public long SampleCount { get; set; }

    [SugarColumn(IsNullable = true)]
    public double? P50 { get; set; }

    [SugarColumn(IsNullable = true)]
    public double? P95 { get; set; }

    [SugarColumn(IsNullable = true)]
    public double? P99 { get; set; }

    [SugarColumn(IsNullable = true)]
    public double? WarningThreshold { get; set; }

    [SugarColumn(Length = 30, IsNullable = false)]
    public string CoverageState { get; set; } = "insufficient";

    [SugarColumn(Length = 40, IsNullable = false)]
    public string GatePolicy { get; set; } = "runtime_warning";
}
