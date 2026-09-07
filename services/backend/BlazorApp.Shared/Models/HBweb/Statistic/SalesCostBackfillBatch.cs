using SqlSugar;

namespace BlazorApp.Shared.Models;

[SugarTable("SalesCostBackfillBatch")]
public sealed class SalesCostBackfillBatch
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [SugarColumn(Length = 40)] public string RuleVersion { get; set; } = "cost-gap-v1";
    [SugarColumn(Length = 32)] public string Status { get; set; } = "Previewing";
    [SugarColumn(Length = 100)] public string RequestedBy { get; set; } = "";
    [SugarColumn(Length = 100, IsNullable = true)] public string? AppliedBy { get; set; }
    [SugarColumn(Length = 100, IsNullable = true)] public string? RolledBackBy { get; set; }
    [SugarColumn(Length = 100, IsNullable = true)] public string? PreviewRetriedBy { get; set; }
    public bool Automatic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    [SugarColumn(Length = 1000, IsNullable = true)] public string? Error { get; set; }
}

[SugarTable("SalesCostBackfillDay")]
public sealed class SalesCostBackfillDay
{
    [SugarColumn(IsPrimaryKey = true)] public Guid BatchId { get; set; }
    [SugarColumn(IsPrimaryKey = true)] public DateTime Date { get; set; }
    [SugarColumn(Length = 32)] public string Status { get; set; } = "Pending";
    [SugarColumn(Length = 64, IsNullable = true)] public string? SourceHash { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? SnapshotVersion { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)] public string? FailedOperation { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? BeforePublicationJson { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? AfterPublicationJson { get; set; }
    public int CandidateCount { get; set; }
    public int UnresolvedCount { get; set; }
    public int AppliedCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    [SugarColumn(Length = 1000, IsNullable = true)] public string? Error { get; set; }
}

[SugarTable("SalesCostBackfillItem")]
public sealed class SalesCostBackfillItem
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public DateTime Date { get; set; }
    [SugarColumn(Length = 50)] public string BranchCode { get; set; } = "";
    [SugarColumn(Length = 50)] public string SupplierCode { get; set; } = "";
    [SugarColumn(Length = 50)] public string ProductCode { get; set; } = "";
    [SugarColumn(Length = 32)] public string Status { get; set; } = "Candidate";
    [SugarColumn(Length = 200)] public string Reason { get; set; } = "";
    [SugarColumn(Length = 200, IsNullable = true)] public string? ConflictReason { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)")] public string BeforeJson { get; set; } = "";
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? ProposedJson { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)] public string? AfterJson { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)")] public string EvidenceJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}
