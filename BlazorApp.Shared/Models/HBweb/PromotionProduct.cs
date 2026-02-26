using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("PromotionProduct")]
    public class PromotionProduct : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        public string PromotionId { get; set; } = string.Empty;

        [SugarColumn(Length = 50)]
        public string ProductCode { get; set; } = string.Empty;

        public int UnitWeight { get; set; } = 1;
    }
}
