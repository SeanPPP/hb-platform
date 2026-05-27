using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    [SugarTable("SeasonalCardRemainingSubmission")]
    public class SeasonalCardRemainingSubmission : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
        public string SubmissionGuid { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false, Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 50)]
        public string CatalogGuid { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 100)]
        public string CatalogCode { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public SeasonalCardType CardType { get; set; }

        [SugarColumn(IsNullable = false)]
        public SeasonalCardPriceOptionType PriceOption { get; set; }

        [SugarColumn(IsNullable = false, Length = 20)]
        public string PriceLabel { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public decimal UnitPrice { get; set; }

        [SugarColumn(IsNullable = false)]
        public int SeasonYear { get; set; }

        [SugarColumn(IsNullable = false)]
        public int RemainingQuantity { get; set; }

        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Remark { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime SubmittedAt { get; set; }

        [SugarColumn(IsNullable = false, Length = 50)]
        public string SubmittedByUserGuid { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false, Length = 100)]
        public string SubmittedByName { get; set; } = string.Empty;
    }
}
