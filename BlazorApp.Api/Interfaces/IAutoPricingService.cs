using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Interfaces
{
    public interface IAutoPricingService
    {
        Task<PricingStrategy?> FindStrategyAsync(string? supplierCode, string? storeCode);

        Task<PricingStrategy?> FindStrategyForPriceAsync(decimal purchasePrice, string? supplierCode, string? storeCode);

        decimal CalculateRate(decimal purchasePrice, PricingStrategy? strategy);

        decimal CalculateRetailPrice(decimal purchasePrice, PricingStrategy? strategy);

        Task<decimal> GetAutoRetailPriceAsync(decimal purchasePrice, string? supplierCode, string? storeCode);
    }
}
