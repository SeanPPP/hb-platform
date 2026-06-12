using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("SeasonalCardCatalog")]
    public class SeasonalCardCatalog : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
        public string CatalogGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 100)]
        public string CatalogCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public SeasonalCardType CardType { get; set; }

        [SugarColumn(IsNullable = false)]
        public SeasonalCardPriceOptionType PriceOption { get; set; }

        [SugarColumn(IsNullable = false, Length = 20)]
        public string PriceLabel { get; set; } = string.Empty;

        [SugarColumn(IsNullable = true)]
        public decimal? FixedUnitPrice { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool AllowsCustomUnitPrice { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool IsEnabled { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public int SortOrder { get; set; }
    }
}
