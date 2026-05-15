using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreProductMaintenanceReactService
    {
        Task<ApiResponse<List<StoreProductLookupItemDto>>> LookupAsync(
            StoreProductLookupRequestDto request,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductDetailDto>> GetDetailAsync(
            string productCode,
            string? storeCode,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            UpdateStoreProductPriceDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );

        Task<ApiResponse<StoreProductMultiCodeDto>> UpdateMultiCodeAsync(
            string uuid,
            UpdateStoreProductMultiCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        );
    }
}
