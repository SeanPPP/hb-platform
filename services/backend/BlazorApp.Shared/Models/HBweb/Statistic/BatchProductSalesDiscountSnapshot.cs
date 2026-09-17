using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>折扣聚合结果。格式 1 保留旧查询快照；格式 2 为商品每日全部分店，供任意授权范围复用。</summary>
[SugarTable("BatchProductSalesDiscountSnapshot")]
public sealed class BatchProductSalesDiscountSnapshot
{
    [SugarColumn(IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = "";
    /// <summary>格式 2 的 StartDate 与 EndDate 为同一天，查询不得消费旧格式组合快照。</summary>
    public int SnapshotFormat { get; set; } = 1;
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
