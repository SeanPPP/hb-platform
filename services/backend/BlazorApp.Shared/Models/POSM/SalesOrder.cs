using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

/// <summary>
/// 销售订单主表
/// </summary>
[SugarTable("sales_order"), Tenant("HBPOSM")]
public class SalesOrder
{
    [SugarColumn(IsPrimaryKey = true)]
    public string? OrderGuid { get; set; } = string.Empty; // 订单编号

    [SugarColumn(IsIgnore = true)]
    public string ShortOrderNo =>
        OrderGuid?.Substring(Math.Max(0, OrderGuid.Length - 8)).ToUpper() ?? string.Empty; // 简短单号

    public DateTime? OrderTime { get; set; } // 下单时间

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? BranchCode { get; set; } = string.Empty; // 分店编号

    [SugarColumn(Length = 20, IsNullable = true)]
    public string? DeviceCode { get; set; } = string.Empty; // 设备编号

    [SugarColumn(IsNullable = true)]
    public decimal? TotalAmount { get; set; } = 0; // 订单总金额

    [SugarColumn(IsNullable = true)]
    public decimal? DiscountAmount { get; set; } = 0; // 折扣金额

    [SugarColumn(IsNullable = true)]
    public decimal? ActualAmount { get; set; } = 0; // 实付金额(顾客支付金额 现金有可能有找零)

    [SugarColumn(IsNullable = true)]
    public int? ItemCount { get; set; } = 0; // 商品数量

    [SugarColumn(IsNullable = true)]
    public string? CashierId { get; set; } = string.Empty; // 收银员ID

    [SugarColumn(IsNullable = true)]
    public string? CashierName { get; set; } = string.Empty; // 收银员名字

    [SugarColumn(IsNullable = true)]
    public int? Status { get; set; } = 0; // 订单状态：0-待支付，1-已支付，2-已取消，3-已退款， 4-分期付

    [SugarColumn(IsNullable = true)]
    public DateTime? LastUploadTime { get; set; } // 最后上传时间

    [SugarColumn(IsNullable = true)]
    public string? Remark { get; set; } = string.Empty; // 备注

    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToOne, nameof(Remark), nameof(CustomerInfo.CustomerCode))]
    public CustomerInfo? LayBuyCustomer { get; set; } // 分期付款顾客

    [SugarColumn(IsNullable = true)]
    public string? CreatedBy { get; set; } = string.Empty; // 创建人

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedTime { get; set; } // 创建时间

    [SugarColumn(IsNullable = true)]
    public string? UpdatedBy { get; set; } = string.Empty; // 修改人

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedTime { get; set; } // 修改时间

    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToMany, nameof(SalesOrderDetail.OrderGuid))]
    public List<SalesOrderDetail>? OrderDetails { get; set; } // 订单明细

    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToMany, nameof(PaymentDetail.OrderGuid))]
    public List<PaymentDetail>? PaymentDetails { get; set; } // 支付明细

    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToMany, nameof(BankTransaction.OrderGuid))]
    public List<BankTransaction>? BankTransactions { get; set; } // 银行交易
}
