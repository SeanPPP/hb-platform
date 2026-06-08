using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 商品分店每日销售统计，用于热销商品、分店参与数和毛利率查询。
    /// </summary>
    [SugarTable("ProductStoreDailySalesStatistic")]
    public class ProductStoreDailySalesStatistic
    {
        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string BranchCode { get; set; } = string.Empty;

        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string SupplierCode { get; set; } = string.Empty;

        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string ProductCode { get; set; } = string.Empty;

        [SugarColumn(Length = 255, IsNullable = true)]
        public string? ProductName { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? Barcode { get; set; }

        [SugarColumn(IsNullable = false)]
        public int TotalQuantity { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal TotalAmount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int OrderCount { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? UnitCostSnapshot { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? TotalCost { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? GrossProfit { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? GrossMarginRate { get; set; }

        [SugarColumn(Length = 50, IsNullable = false)]
        public string CostSource { get; set; } = "Missing";

        [SugarColumn(IsNullable = true)]
        public DateTime? LastSourceUploadTime { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
