using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("HourlySalesStatistic")]
    public class HourlySalesStatistic
    {
        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(IsPrimaryKey = true)]
        public int Hour { get; set; }

        [SugarColumn(IsPrimaryKey = true)]
        public string? BranchCode { get; set; }

        [SugarColumn(Length = 100, IsNullable = true)]
        public string? BranchName { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal TotalAmount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int TotalQuantity { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? OrderCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int CustomerCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal AverageOrderValue { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
