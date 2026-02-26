using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

[SugarTable("sales_return_record"), Tenant("HBPOSM")]
public class SalesReturnRecord
{
    [SugarColumn(IsPrimaryKey = true)]
    public string ReturnDetailGuid { get; set; } = Guid.NewGuid().ToString(); // 退货记录guid

    [SugarColumn(IsNullable = true)]
    public string ReturnOrderGuid { get; set; } = string.Empty; // 退货订单编号

    [SugarColumn(IsNullable = true)]
    public string OriginalOrderGuid { get; set; } = string.Empty; // 原始订单编号

    [SugarColumn(IsNullable = true)]
    public string OriginalOrderDetailGuid { get; set; } = string.Empty; // 原始订单明细编号

    [SugarColumn(IsNullable = true)]
    public string? ProductCode { get; set; } = string.Empty; // 商品编号

    [SugarColumn(IsNullable = true)]
    public string? ReferenceGUID { get; set; } = string.Empty; // 商品reference

    [SugarColumn(IsNullable = true)]
    public decimal? ReturnQuantity { get; set; } = 0; // 退货数量

    [SugarColumn(IsNullable = true)]
    public decimal? ReturnAmount { get; set; } = 0; // 退货金额

    [SugarColumn(IsNullable = true)]
    public string? StaffCode { get; set; } = string.Empty; // 操作员工编码

    //审计字段
    [SugarColumn(IsNullable = true)]
    public string? CreatedBy { get; set; } = string.Empty; // 创建人

    [SugarColumn(IsNullable = true)]
    public DateTime? CreatedTime { get; set; } = DateTime.Now; // 创建时间

    [SugarColumn(IsNullable = true)]
    public string? UpdatedBy { get; set; } = string.Empty; // 修改人

    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedTime { get; set; } = DateTime.Now; // 修改时间

    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToOne, nameof(OriginalOrderGuid))]
    public SalesOrder? SalesOrder { get; set; } // 订单明细

    [SugarColumn(IsIgnore = true)]
    [Navigate(NavigateType.OneToOne, nameof(OriginalOrderDetailGuid))]
    public SalesOrderDetail? SalesOrderDetail { get; set; } // 订单明细
}

//
//返回退货记录模型
public class RetuenRecordModel
{
    public SalesOrder SalesOrder { get; set; } = new SalesOrder();
    public List<SalesReturnRecord> SalesReturnRecords { get; set; } = new List<SalesReturnRecord>();
}
