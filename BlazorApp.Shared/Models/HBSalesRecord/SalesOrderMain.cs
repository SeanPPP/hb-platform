using SqlSugar;

namespace BlazorApp.Shared.Models.HBSalesRecord
{
    [SugarTable("B销售清单主表副本")]
    public class SalesOrderMain
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int ID { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B分店代码 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B销售单号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B单据类型 { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? B结账日期 { get; set; }

        [SugarColumn(IsNullable = true)]
        public TimeSpan? B结账时间 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B收银员 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B工号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B收银机号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? B商品数量 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B合计金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B实收金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B优惠金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B合计金额舍入 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B结算方式一 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B结算方式一金额 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B结算方式二 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B结算方式二金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? B找零金额 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? B结算状态 { get; set; }

        [SugarColumn(IsNullable = true)]
        public bool? B收银结算是否 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B下线结算单号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? B打印次数 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B会员编号 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B分期付款人 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B分期付款人证件号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B分期付款人电话 { get; set; }

        [SugarColumn(IsNullable = true)]
        public bool? B是否停止实时上传 { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? B银行卡号 { get; set; }

        [SugarColumn(Length = 50, IsNullable = true)]
        public string? B原销售单号 { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? B金额为零数 { get; set; }

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
