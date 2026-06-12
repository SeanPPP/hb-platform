using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Interfaces
{
    public interface IAutoPricingService
    {
        Task<PricingStrategy?> FindStrategyAsync(string? supplierCode, string? storeCode);

        Task<PricingStrategy?> FindStrategyForPriceAsync(decimal purchasePrice, string? supplierCode, string? storeCode);

        Task<List<PricingStrategy>> GetAllActiveStrategiesAsync();

        PricingStrategy? FindBestStrategyForPrice(
            decimal purchasePrice,
            List<PricingStrategy> supplierStrategies,
            List<PricingStrategy> storeStrategies,
            List<PricingStrategy> globalStrategies
        );

        decimal CalculateRate(decimal purchasePrice, PricingStrategy? strategy);

        decimal CalculateRetailPrice(decimal purchasePrice, PricingStrategy? strategy);

        Task<decimal> GetAutoRetailPriceAsync(decimal purchasePrice, string? supplierCode, string? storeCode);
    }
}
