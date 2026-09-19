using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>
/// 仓库商品的建议折扣（与 Product 一对一）。
/// 独立成表而不是给 Product 加列：Product 被全后端及多个共用 Shared 的工具查询，
/// 加列会让任何先于迁移运行的进程因缺列而整体失败。
/// </summary>
[SugarTable("ProductSuggestedDiscount")]
public sealed class ProductSuggestedDiscount
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>
    /// 减免比例（0~1，0.2 表示减 20%），语义与 StoreRetailPrice.DiscountRate 一致。
    /// null 表示"未设置，不与分店折扣比较"；0 表示"明确无折扣"。
    /// </summary>
    [SugarColumn(IsNullable = true, DecimalDigits = 4)]
    public decimal? SuggestedDiscountRate { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [SugarColumn(IsNullable = true, Length = 255)]
    public string? UpdatedBy { get; set; }
}
