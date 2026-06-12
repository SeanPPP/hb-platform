using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("DailySalesStatistic")]
    public class DailySalesStatistic
    {
        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal TotalAmount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int TotalQuantity { get; set; }

        [SugarColumn(IsNullable = false)]
        public int OrderCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int SkuCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int CustomerCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal AverageOrderValue { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
