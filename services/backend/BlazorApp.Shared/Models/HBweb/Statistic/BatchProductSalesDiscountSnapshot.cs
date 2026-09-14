using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>按已授权查询范围保存每日分店折扣聚合；任务与结果在同一行原子发布。</summary>
[SugarTable("BatchProductSalesDiscountSnapshot")]
public sealed class BatchProductSalesDiscountSnapshot
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = "";
    [SugarColumn(Length = 64)]
    public string SourceVersion { get; set; } = "";
    [SugarColumn(Length = 50)]
    public string ProductCode { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)")]
    public string StoreCodesJson { get; set; } = "[]";
    [SugarColumn(Length = 20)]
    public string Status { get; set; } = "Queued";
    public int Attempts { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    [SugarColumn(Length = 32, IsNullable = true)]
    public string? LeaseToken { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? LeaseUntilUtc { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAtUtc { get; set; }
    [SugarColumn(ColumnDataType = "nvarchar(max)", IsNullable = true)]
    public string? PayloadJson { get; set; }
}
