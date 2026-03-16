using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IStoreProductPriceReactService
    {
        Task<GridResponseDto<StoreProductPriceListDto>> GetGridDataAsync(StoreProductPriceQueryDto query);
    }
}
