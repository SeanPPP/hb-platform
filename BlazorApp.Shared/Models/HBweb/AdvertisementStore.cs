using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("AdvertisementStore")]
    public class AdvertisementStore : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        [SugarColumn(Length = 50)]
        public string AdvertisementId { get; set; } = string.Empty;

        [SugarColumn(Length = 50)]
        public string StoreCode { get; set; } = string.Empty;
    }
}
