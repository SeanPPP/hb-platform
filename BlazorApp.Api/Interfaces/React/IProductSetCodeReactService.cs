using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IProductSetCodeReactService
    {
        Task<GridResponseDto<ProductSetCodeGridDto>> GetGridDataAsync(GridRequestDto request);
        Task<ApiResponse<bool>> BatchUpdateStatusAsync(
            List<string> ids,
            bool isActive,
            string updatedBy
        );
        Task<ApiResponse<bool>> BatchUpdatePricesAsync(
            List<BatchUpdatePricesItemDto> items,
            string updatedBy
        );
        Task<ApiResponse<bool>> BatchDeleteAsync(List<string> ids, string updatedBy);
        Task<ApiResponse<bool>> BatchUpdateBarcodesAsync(
            List<BatchUpdateBarcodesItemDto> items,
            string updatedBy
        );
        Task<ApiResponse<List<string>>> BatchCreateAsync(
            List<CreateSetCodeItemDto> items,
            string updatedBy
        );
    }
}
