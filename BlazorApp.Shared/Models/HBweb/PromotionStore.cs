using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("PromotionStore")]
    public class PromotionStore : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        public string PromotionId { get; set; } = string.Empty;

        [SugarColumn(Length = 50)]
        public string StoreCode { get; set; } = string.Empty;
    }
}
