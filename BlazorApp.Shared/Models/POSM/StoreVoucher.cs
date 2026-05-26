using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

[SugarTable("StoreVoucher"), Tenant("HBPOSM")]
public class StoreVoucher
{
    [SugarColumn(IsPrimaryKey = true)]
    public int ID { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? StoreCode { get; set; }

    [SugarColumn(IsNullable = false)]
    public string? VoucherCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public int? VoucherType { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? CustomerCode { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? DiscountRate { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? Amount { get; set; }

    [SugarColumn(IsNullable = true)]
    public decimal? RemainingAmount { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? Status { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CreateTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdateTime { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? CreateUser { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? UpdateUser { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? Remark { get; set; }

    [SugarColumn(IsNullable = true)]
    public bool? IsDelete { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ExpiredDate { get; set; }
}
