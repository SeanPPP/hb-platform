using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>独立折扣日统计的持久进度；完成空日也须留记录，避免把尚未回填误当作零销量。</summary>
[SugarTable("BatchProductSalesDiscountRefreshState")]
public sealed class BatchProductSalesDiscountRefreshState
{
    [SugarColumn(IsPrimaryKey = true)]
    public DateTime Date { get; set; }
    [SugarColumn(Length = 20)]
    public string Status { get; set; } = "Queued";
    public int RuleVersion { get; set; } = 1;
    [SugarColumn(Length = 64)]
    public string StatisticsVersion { get; set; } = "";
    [SugarColumn(Length = 64)]
    public string SourceVersion { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? LeaseToken { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? LeaseUntilUtc { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAtUtc { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? LastCheckedAtUtc { get; set; }
    [SugarColumn(Length = 2000, IsNullable = true)]
    public string? LastError { get; set; }
    public int SnapshotCount { get; set; }
    public bool ReconcileRequested { get; set; }
}
