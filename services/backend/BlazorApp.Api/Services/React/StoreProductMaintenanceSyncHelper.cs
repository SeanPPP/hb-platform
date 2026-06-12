using System;

namespace BlazorApp.Api.Services.React
{
    public static class StoreProductMaintenanceSyncHelper
    {
        public static decimal? CalculateSetPurchasePrice(
            decimal? mainPurchasePrice,
            decimal? mainRetailPrice,
            decimal? setRetailPrice
        )
        {
            if (
                !mainPurchasePrice.HasValue
                || !mainRetailPrice.HasValue
                || !setRetailPrice.HasValue
                || mainRetailPrice.Value <= 0
            )
            {
                return null;
            }

            return Math.Round(
                setRetailPrice.Value * mainPurchasePrice.Value / mainRetailPrice.Value,
                2,
                MidpointRounding.AwayFromZero
            );
        }

        public static string? NormalizeProductTypeLabel(int? productType)
        {
            return productType switch
            {
                0 => "普通",
                1 => "套装",
                2 => "多码",
                _ => null,
            };
        }
    }
}
