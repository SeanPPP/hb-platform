using SqlSugar;

namespace BlazorApp.Shared.Models;

[SugarTable("HourlySalesBackfillBatch")]
public sealed class HourlySalesBackfillBatch
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [SugarColumn(Length = 40)] public string RuleVersion { get; set; } = "";
    [SugarColumn(Length = 32)] public string Status { get; set; } = "Previewing";
    [SugarColumn(Length = 100)] public string RequestedBy { get; set; } = "";
    [SugarColumn(Length = 100, IsNullable = true)] public string? AppliedBy { get; set; }
    [SugarColumn(Length = 100, IsNullable = true)] public string? RolledBackBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    [SugarColumn(Length = 1000, IsNullable = true)] public string? Error { get; set; }
}

[SugarTable("HourlySalesBackfillDay")]
public sealed class HourlySalesBackfillDay
{
    [SugarColumn(IsPrimaryKey = true)] public Guid BatchId { get; set; }
    [SugarColumn(IsPrimaryKey = true)] public DateTime Date { get; set; }
    [SugarColumn(Length = 32)] public string Status { get; set; } = "Pending";
    [SugarColumn(Length = 64, IsNullable = true)] public string? SourceHash { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? BeforeHash { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? AfterHash { get; set; }
    [SugarColumn(IsNullable = true)] public string? BeforeJson { get; set; }
    [SugarColumn(IsNullable = true)] public string? CandidateJson { get; set; }
    [SugarColumn(IsNullable = true)] public string? SourceStatusJson { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal CandidateAmount { get; set; }
    public int ExpectedOrderCount { get; set; }
    public int CandidateOrderCount { get; set; }
    public int RowCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    [SugarColumn(Length = 1000, IsNullable = true)] public string? Error { get; set; }
}

/// <summary>
/// 一次回填批次发布的不可变小时版本。当前版本由 HourlySalesBackfillDay 的 Applied 状态指向，
/// 因此旧统计写入器可以继续更新 HourlySalesStatistic，而不会覆盖已发布历史。
/// </summary>
[SugarTable("HourlySalesBackfillPublishedRow")]
public sealed class HourlySalesBackfillPublishedRow
{
    [SugarColumn(IsPrimaryKey = true)] public Guid BatchId { get; set; }
    [SugarColumn(IsPrimaryKey = true)] public DateTime Date { get; set; }
    [SugarColumn(IsPrimaryKey = true)] public int Hour { get; set; }
    [SugarColumn(IsPrimaryKey = true, Length = 100)] public string BranchCode { get; set; } = "";
    [SugarColumn(Length = 100, IsNullable = true)] public string? BranchName { get; set; }
    public decimal TotalAmount { get; set; }
    public int TotalQuantity { get; set; }
    public int OrderCount { get; set; }
    public int CustomerCount { get; set; }
    public decimal AverageOrderValue { get; set; }
    public DateTime PublishedAtUtc { get; set; }
}
