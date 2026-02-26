using System;
using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

 /// <summary>
    /// 销售订单明细表
    /// </summary>
    [SugarTable("sales_order_detail"), Tenant("HBPOSM")]
    public class SalesOrderDetail
    {
       
        [SugarColumn(IsPrimaryKey = true)]
        public string OrderDetailGuid { get; set; } = Guid.NewGuid().ToString();  // 订单明细guid

        [SugarColumn(IsNullable = true)]
        public string OrderGuid { get; set; } = string.Empty;  // 订单编号

        [SugarColumn( IsNullable = true)] 
        public string ProductCode { get; set; } = string.Empty;  // 商品编号

        [SugarColumn( IsNullable = true)] 
        public string ReferenceGUID { get; set; } = string.Empty;  // 参考guid

        [SugarColumn( IsNullable = true)] 
        public string SupplierCode { get; set; } = string.Empty;  // 供应商编号

        [SugarColumn(Length = 255, IsNullable = true)] 
        public string? ProductName { get; set; } = string.Empty;  // 商品名称

        [SugarColumn(IsNullable = true)]
        public string? Barcode { get; set; } = string.Empty;  // 条码

        [SugarColumn(IsNullable = true)]
        public decimal? Price { get; set; } = 0;  // 单价

        [SugarColumn(IsNullable = true)]
        public decimal? DiscountRate { get; set; }  // 折扣率

        [SugarColumn(IsNullable = true)]
        public int? Quantity { get; set; } = 0;  // 数量

        [SugarColumn(IsNullable = true)]
        public decimal? Subtotal { get; set; } = 0;   // 小计金额（未折扣）

        [SugarColumn(IsNullable = true)]
        public decimal? DiscountAmount { get; set; } = 0;  // 折扣金额

        [SugarColumn(IsNullable = true)]
        public decimal? ActualAmount { get; set; } = 0;  // 实际金额（已折扣）
        [SugarColumn(IsNullable = true)]
        public string? CreatedBy { get; set; } = string.Empty;  // 创建人

        [SugarColumn(IsNullable = true)]
        public DateTime? CreatedTime { get; set; }  // 创建时间

        [SugarColumn(IsNullable = true)]
        public string? UpdatedBy { get; set; } = string.Empty;  // 更新人

        [SugarColumn(IsNullable = true)]
        public DateTime? UpdatedTime { get; set; }  // 更新时间

        [SugarColumn(IsNullable = true)]
        public DateTime? LastUploadTime { get; set; }  // 上传时间

        [SugarColumn(Length = 200, IsNullable = true)]
        public string? Remark { get; set; } = string.Empty;  // 备注

        [Navigate(NavigateType.OneToOne, nameof(OrderGuid))]
        public SalesOrder? Order { get; set; }  // 订单主表
    }
