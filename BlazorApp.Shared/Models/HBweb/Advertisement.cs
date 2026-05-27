using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    [SugarTable("Advertisement")]
    public class Advertisement : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string Id { get; set; } = string.Empty;

        [SugarColumn(Length = 200)]
        public string Title { get; set; } = string.Empty;

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? Description { get; set; }

        [SugarColumn(Length = 20)]
        public string MediaType { get; set; } = string.Empty;

        [SugarColumn(Length = 1000)]
        public string MediaUrl { get; set; } = string.Empty;

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? ThumbnailUrl { get; set; }

        [SugarColumn(Length = 500)]
        public string ObjectKey { get; set; } = string.Empty;

        [SugarColumn(Length = 255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [SugarColumn(Length = 100)]
        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public DateTime EffectiveStart { get; set; }

        public DateTime EffectiveEnd { get; set; }

        public bool IsEnabled { get; set; } = true;

        public int SortOrder { get; set; }

        [SugarColumn(IsIgnore = true)]
        [Navigate(NavigateType.OneToMany, nameof(AdvertisementStore.AdvertisementId))]
        public List<AdvertisementStore> Stores { get; set; } = new();
    }
}
