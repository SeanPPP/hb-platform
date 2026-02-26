using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("StoreSalesStatistic")]
    public class StoreSalesStatistic
    {
        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(IsPrimaryKey = true)]
        public string BranchCode { get; set; } = string.Empty;

        [SugarColumn(Length = 100, IsNullable = false)]
        public string BranchName { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public decimal TotalAmount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int TotalQuantity { get; set; }

        [SugarColumn(IsNullable = false)]
        public int OrderCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int CustomerCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal AverageOrderValue { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
