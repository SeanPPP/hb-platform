using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreManagerProductReactService
    {
        Task<ApiResponse<List<StoreDto>>> GetAuthorizedStoresAsync(string userGuid);

        Task<StoreManagerPagedListDto<StoreManagerProductListItemDto>> GetProductPagedListAsync(
            StoreManagerProductFilterDto filter);

        Task<ApiResponse<StoreManagerProductDetailDto>> GetProductDetailAsync(
            string productCode,
            List<string> authorizedStoreCodes);

        Task<ApiResponse<StoreManagerStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            StoreManagerUpdatePriceDto dto,
            string updatedBy);

        Task<ApiResponse<BatchOperationReactResult>> BatchUpdateStorePricesAsync(
            List<StoreManagerUpdatePriceDto> items,
            string updatedBy);

        Task<ApiResponse<StoreManagerMultiCodePriceDto>> UpdateMultiCodePriceAsync(
            string uuid,
            StoreManagerUpdateMultiCodePriceDto dto,
            string updatedBy);

        Task<ApiResponse<BatchOperationReactResult>> BatchUpdateMultiCodePricesAsync(
            List<StoreManagerUpdateMultiCodePriceDto> items,
            string updatedBy);
    }
}
