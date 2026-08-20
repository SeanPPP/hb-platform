using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 自动生成货号和条码的永久占用记录。
    /// 先占位、后创建商品，允许创建失败后留下序号空洞，但禁止任何后续创建重复使用。
    /// </summary>
    [SugarTable("ItemBarcodeReservation")]
    public sealed class ItemBarcodeReservation
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 120)]
        public string ReservationKey { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 20)]
        public string IdentifierType { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 100)]
        public string IdentifierValue { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
