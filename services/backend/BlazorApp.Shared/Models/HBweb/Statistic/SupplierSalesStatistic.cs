using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("SupplierSalesStatistic")]
    public class SupplierSalesStatistic
    {
        [SugarColumn(IsPrimaryKey = true)]
        public DateTime Date { get; set; }

        [SugarColumn(IsPrimaryKey = true)]
        public string SupplierCode { get; set; } = string.Empty;

        [SugarColumn(Length = 200, IsNullable = false)]
        public string SupplierName { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public bool IsDomestic { get; set; }

        [SugarColumn(IsNullable = false)]
        public decimal TotalAmount { get; set; }

        [SugarColumn(IsNullable = false)]
        public int TotalQuantity { get; set; }

        [SugarColumn(IsNullable = false)]
        public int StoreCount { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? OrderCount { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
