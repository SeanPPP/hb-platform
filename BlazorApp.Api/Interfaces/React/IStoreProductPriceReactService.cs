using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreProductPriceReactService
    {
        Task<GridResponseDto<StoreProductPriceListDto>> GetGridDataAsync(StoreProductPriceQueryDto query);

        Task<ApiResponse<object>> BatchUpdateStoreRetailPricesAsync(
            BatchUpdateStoreRetailPriceDto dto,
            string updatedBy
        );

        Task<ApiResponse<object>> SyncToOtherStoresAsync(
            SyncToOtherStoresDto dto,
            string updatedBy
        );

        Task<ApiResponse<CopyStoreDataResultDto>> CopyStoreDataAsync(
            CopyStoreDataDto dto,
            string updatedBy
        );

        IAsyncEnumerable<CopyProgressDto> CopyStoreDataWithProgressAsync(
            CopyStoreDataDto dto,
            string updatedBy,
            CancellationToken cancellationToken = default
        );
    }
}
