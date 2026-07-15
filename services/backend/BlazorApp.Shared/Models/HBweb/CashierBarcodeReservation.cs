using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("CashierBarcodeReservations")]
    public sealed class CashierBarcodeReservation
    {
        [SugarColumn(IsPrimaryKey = true, Length = 50)]
        public string Barcode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public DateTime CreatedAt { get; set; }

        [SugarColumn(IsNullable = true, Length = 20)]
        public string? OwnerType { get; set; }

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? OwnerId { get; set; }
    }
}
