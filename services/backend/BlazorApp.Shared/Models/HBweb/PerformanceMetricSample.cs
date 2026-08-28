using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceMetricSample")]
public sealed class PerformanceMetricSample : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(IsNullable = false)]
    public Guid EventId { get; set; }

    [SugarColumn(Length = 80, IsNullable = false)]
    public string ProjectCode { get; set; } = string.Empty;

    [SugarColumn(Length = 60, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 40, IsNullable = false)]
    public string SourceType { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string MetricName { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime ObservedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public double Value { get; set; }

    [SugarColumn(Length = 20, IsNullable = false)]
    public string Unit { get; set; } = string.Empty;

    [SugarColumn(Length = 500, IsNullable = false)]
    public string Selector { get; set; } = "all";

    [SugarColumn(Length = 64, IsNullable = false)]
    public string DimensionsHash { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(2000)", IsNullable = false)]
    public string DimensionsJson { get; set; } = "{}";
}
