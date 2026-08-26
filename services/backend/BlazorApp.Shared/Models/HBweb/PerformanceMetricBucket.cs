using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

[SugarTable("PerformanceMetricBucket")]
public sealed class PerformanceMetricBucket : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 120, IsNullable = false)]
    public string MetricName { get; set; } = string.Empty;

    [SugarColumn(Length = 80, IsNullable = false)]
    public string ProjectCode { get; set; } = string.Empty;

    [SugarColumn(Length = 60, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 40, IsNullable = false)]
    public string SourceType { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string InstanceId { get; set; } = string.Empty;

    [SugarColumn(Length = 500, IsNullable = false)]
    public string Selector { get; set; } = "all";

    [SugarColumn(Length = 64, IsNullable = false)]
    public string DimensionsHash { get; set; } = string.Empty;

    [SugarColumn(ColumnDataType = "nvarchar(2000)", IsNullable = false)]
    public string DimensionsJson { get; set; } = "{}";

    [SugarColumn(IsNullable = false)]
    public DateTime WindowStartUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public int BucketSizeMinutes { get; set; } = 5;

    [SugarColumn(IsNullable = false)]
    public long SampleCount { get; set; }

    [SugarColumn(IsNullable = false)]
    public double SumValue { get; set; }

    [SugarColumn(IsNullable = false)]
    public double MinimumValue { get; set; }

    [SugarColumn(IsNullable = false)]
    public double MaximumValue { get; set; }

    [SugarColumn(ColumnDataType = "nvarchar(1000)", IsNullable = false)]
    public string HistogramCountsJson { get; set; } = "[]";

    [SugarColumn(IsNullable = false)]
    public DateTime LastObservedAtUtc { get; set; }
}
