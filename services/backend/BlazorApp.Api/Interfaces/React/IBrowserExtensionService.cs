using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IBrowserExtensionService
{
    BrowserExtensionReleaseDto GetRelease();
    BrowserExtensionSupplierProfilesDto GetSupplierProfiles();

    Task<BrowserExtensionProductSummaryBatchDto> GetProductSummariesAsync(
        BrowserExtensionProductSummaryBatchRequestDto request
    );

    Task<BrowserExtensionPurchaseCyclesDto> GetPurchaseCyclesAsync(
        BrowserExtensionPurchaseCyclesRequestDto request
    );

    Task<BrowserExtensionStoreOptionsDto> GetEnabledStoresAsync(
        IReadOnlyCollection<string> relatedStoreCodes
    );

    Task<BrowserExtensionSupplierTopSalesDto> GetSupplierTopSalesAsync(
        BrowserExtensionSupplierTopSalesRequestDto request
    );
}
