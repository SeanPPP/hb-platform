using BlazorApp.Shared.DTOs;

namespace BlazorApp.Shared.Constants
{
    public sealed record SeasonalCardCatalogSeedDefinition(
        string CatalogCode,
        SeasonalCardType CardType,
        SeasonalCardPriceOptionType PriceOption,
        string PriceLabel,
        decimal? FixedUnitPrice,
        bool AllowsCustomUnitPrice,
        int SortOrder
    );

    public static class SeasonalCardCatalogSeedData
    {
        public static IReadOnlyList<SeasonalCardCatalogSeedDefinition> Catalogs { get; } =
            BuildCatalogs();

        public static string GetCardTypeName(SeasonalCardType cardType) =>
            cardType switch
            {
                SeasonalCardType.Christmas => "圣诞节",
                SeasonalCardType.ValentinesDay => "情人节",
                SeasonalCardType.MothersDay => "母亲节",
                SeasonalCardType.Easter => "复活节",
                SeasonalCardType.FathersDay => "父亲节",
                _ => cardType.ToString(),
            };

        public static string GetPriceOptionName(SeasonalCardPriceOptionType priceOption) =>
            priceOption switch
            {
                SeasonalCardPriceOptionType.FixedOneDollar => "$1",
                SeasonalCardPriceOptionType.FixedTwoDollars => "$2",
                SeasonalCardPriceOptionType.FixedThreeDollars => "$3",
                SeasonalCardPriceOptionType.Other => "其他",
                _ => priceOption.ToString(),
            };

        private static IReadOnlyList<SeasonalCardCatalogSeedDefinition> BuildCatalogs()
        {
            var cardTypes = new[]
            {
                SeasonalCardType.Christmas,
                SeasonalCardType.ValentinesDay,
                SeasonalCardType.MothersDay,
                SeasonalCardType.Easter,
                SeasonalCardType.FathersDay,
            };

            var definitions = new List<SeasonalCardCatalogSeedDefinition>();
            for (var typeIndex = 0; typeIndex < cardTypes.Length; typeIndex++)
            {
                var cardType = cardTypes[typeIndex];
                var prefix = cardType.ToString();
                var baseOrder = typeIndex * 10;

                definitions.Add(
                    new SeasonalCardCatalogSeedDefinition(
                        $"{prefix}-Fixed-1",
                        cardType,
                        SeasonalCardPriceOptionType.FixedOneDollar,
                        "$1",
                        1m,
                        false,
                        baseOrder + 1
                    )
                );
                definitions.Add(
                    new SeasonalCardCatalogSeedDefinition(
                        $"{prefix}-Fixed-2",
                        cardType,
                        SeasonalCardPriceOptionType.FixedTwoDollars,
                        "$2",
                        2m,
                        false,
                        baseOrder + 2
                    )
                );
                definitions.Add(
                    new SeasonalCardCatalogSeedDefinition(
                        $"{prefix}-Fixed-3",
                        cardType,
                        SeasonalCardPriceOptionType.FixedThreeDollars,
                        "$3",
                        3m,
                        false,
                        baseOrder + 3
                    )
                );
                definitions.Add(
                    new SeasonalCardCatalogSeedDefinition(
                        $"{prefix}-Other",
                        cardType,
                        SeasonalCardPriceOptionType.Other,
                        "其他",
                        null,
                        true,
                        baseOrder + 4
                    )
                );
            }

            return definitions;
        }
    }
}
