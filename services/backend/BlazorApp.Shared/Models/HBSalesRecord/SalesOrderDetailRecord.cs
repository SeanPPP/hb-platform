using SqlSugar;

namespace BlazorApp.Shared.Models.HBSalesRecord
{
    [SugarTable("B销售清单详情表副本")]
    public class SalesOrderDetailRecord
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int ID { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B分店代码 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B分店代码_ID { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? B结账日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public TimeSpan? B结账时间 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B销售单号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B产品编号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B分店商品编号 { get; set; }

        [SugarColumn(Length = 200, IsNullable = true)]
        public string? B商品名 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B规格 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B单位 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B单价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B折扣价 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B折扣率 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B类别 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B原价合计金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B合计金额 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B供应商ID { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B货号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B条形码 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B_POS { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? B结算状态 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B活动类型 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B满减活动代码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B满减合计数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? B活动开始日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? B活动结束日期 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B打折授权 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B满减数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B满减金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public byte[]? B商品图片 { get; set; }

        [SugarColumn(IsNullable = true)]
        public bool? B退换货 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B退货码 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B退货数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B换货数量 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B随机打印代码 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B折扣操作人 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? FGC_Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_CreateDate { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? FGC_LastModifier { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FGC_LastModifyDate { get; set; }

        [SugarColumn(IsNullable = true)]
        public byte[]? FGC_Rowversion { get; set; }

        [SugarColumn(IsNullable = true)]
        public string? FGC_UpdateHelp { get; set; }
    }
}
