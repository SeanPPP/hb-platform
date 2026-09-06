using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("AustralianSupplierStoreSalesDetail")]
    public class AustralianSupplierStoreSalesDetail
    {
        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(IsPrimaryKey = true)]
        public string BranchCode { get; set; } = string.Empty;

        [SugarColumn(IsPrimaryKey = true)]
        public string SupplierCode { get; set; } = string.Empty;

        [SugarColumn(Length = 200, IsNullable = true)]
        public string? SupplierName { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal TotalAmount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int TotalQuantity { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? OrderCount { get; set; }

        // 成本来自商品分店日统计快照；没有成本的行不伪造为 0。
        [SugarColumn(IsNullable = true)]
        public decimal? TotalCost { get; set; }

        [SugarColumn(IsNullable = true)]
        public decimal? GrossProfit { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? StatisticRowCount { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? CostedRowCount { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? GrossProfitRowCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
