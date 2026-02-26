using System;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IPromotionReactService
    {
        Task<GridResponseDto<PromotionListDto>> GetGridAsync(GridRequestDto request);
        Task<ApiResponse<PromotionDetailDto>> GetByIdAsync(string id);
        Task<ApiResponse<PromotionDetailDto>> CreateAsync(CreatePromotionDto dto);
        Task<ApiResponse<PromotionDetailDto>> UpdateAsync(string id, UpdatePromotionDto dto);
        Task<ApiResponse<bool>> DeleteAsync(string id);
        Task<ApiResponse<bool>> EnableAsync(string id, bool enable);
        Task<ApiResponse<PromotionEvaluateResponse>> EvaluateAsync(PromotionEvaluateRequest req);
        Task<ApiResponse<List<PromotionListDto>>> GetValidByStoreAsync(
            string storeCode,
            DateTime? asOf = null
        );
        Task<ApiResponse<List<PromotionListDto>>> GetValidByProductAndStoreAsync(
            string productCode,
            string storeCode,
            DateTime? asOf = null
        );
    }
}
