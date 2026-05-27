namespace BlazorApp.Shared.DTOs
{
    public enum SeasonalCardType
    {
        Christmas = 1,
        ValentinesDay = 2,
        MothersDay = 3,
        Easter = 4,
        FathersDay = 5,
    }

    public enum SeasonalCardPriceOptionType
    {
        FixedOneDollar = 1,
        FixedTwoDollars = 2,
        FixedThreeDollars = 3,
        Other = 4,
    }

    public class SeasonalCardCatalogDto
    {
        public string CatalogGuid { get; set; } = string.Empty;
        public string CatalogCode { get; set; } = string.Empty;
        public SeasonalCardType CardType { get; set; }
        public string CardTypeName { get; set; } = string.Empty;
        public SeasonalCardPriceOptionType PriceOption { get; set; }
        public string PriceOptionName { get; set; } = string.Empty;
        public string PriceLabel { get; set; } = string.Empty;
        public decimal? FixedUnitPrice { get; set; }
        public bool AllowsCustomUnitPrice { get; set; }
        public bool IsEnabled { get; set; }
        public int SortOrder { get; set; }
    }

    public class CreateSeasonalCardRemainingSubmissionDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string CatalogGuid { get; set; } = string.Empty;
        public int SeasonYear { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal? CustomUnitPrice { get; set; }
        public string? Remark { get; set; }
    }

    public class SeasonalCardRemainingSubmissionQueryDto
    {
        public string? StoreCode { get; set; }
        public SeasonalCardType? CardType { get; set; }
        public int? SeasonYear { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class SeasonalCardRemainingSubmissionDto
    {
        public string SubmissionGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string? StoreName { get; set; }
        public string CatalogGuid { get; set; } = string.Empty;
        public string CatalogCode { get; set; } = string.Empty;
        public SeasonalCardType CardType { get; set; }
        public string CardTypeName { get; set; } = string.Empty;
        public SeasonalCardPriceOptionType PriceOption { get; set; }
        public string PriceOptionName { get; set; } = string.Empty;
        public string PriceLabel { get; set; } = string.Empty;
        public int SeasonYear { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Remark { get; set; }
        public string SubmittedByUserGuid { get; set; } = string.Empty;
        public string SubmittedByName { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
    }
}
