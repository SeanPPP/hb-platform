using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

/// <summary>
/// WPF POS 写入的分期商品行表。
/// </summary>
[SugarTable("InstallmentOrderLine")]
public sealed class InstallmentOrderLine
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)]
    public string InstallmentLineGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 36)]
    public string InstallmentGuid { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? ReferenceCode { get; set; }

    [SugarColumn(Length = 255)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(Length = 50)]
    public string LookupCode { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = true)]
    public string? ItemNumber { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ActualAmount { get; set; }
}
