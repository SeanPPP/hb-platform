using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreMultiCodePricesReactService
    {
        Task<GridResponseDto<StoreMultiCodePriceListDto>> GetGridDataAsync(GridRequestDto request);
        Task<ApiResponse<BatchResultDtoMC>> BatchUpsertAsync(
            List<StoreMultiCodePriceUpsertItemDto> items,
            string updatedBy
        );
        Task<ApiResponse<bool>> BatchUpdateSpecialFlagAsync(
            List<string> productCodes,
            bool isSpecial,
            string updatedBy
        );
        Task<ApiResponse<List<StoreMultiCodePriceListDto>>> GetListByUuidsAsync(List<string> uuids);

        Task<ApiResponse<BatchResultDtoMC>> UpsertForActiveStoresAsync(
            List<StoreMultiCodePriceUpsertForActiveStoresItemDto> items,
            string updatedBy
        );
    }
}
