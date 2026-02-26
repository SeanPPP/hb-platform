using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IPricingStrategyReactService
    {
        Task<GridResponseDto<PricingStrategyListDto>> GetGridAsync(GridRequestDto request);
        Task<ApiResponse<PricingStrategyDetailDto>> GetByIdAsync(string id);
        Task<ApiResponse<PricingStrategyDetailDto>> CreateAsync(CreatePricingStrategyDto dto);
        Task<ApiResponse<PricingStrategyDetailDto>> UpdateAsync(
            string id,
            UpdatePricingStrategyDto dto
        );
        Task<ApiResponse<bool>> DeleteAsync(string id);
    }
}
