using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 专用国内商品服务接口（与原有 IDomesticProductService 解耦）
    /// 仅包含 React 控制器所需的方法
    /// </summary>
    public interface IDomesticProductReactService
    {
        Task<GridResponseDto<DomesticProductDto>> GetGridDataAsync(GridRequestDto request);
        Task<ApiResponse<List<DomesticProductDto>>> BatchCreateDomesticProductsAsync(BatchCreateDomesticProductDto dto);
        Task<ApiResponse<object>> BatchValidateProductsAsync(BatchCreateDomesticProductDto dto);
        Task<ApiResponse<List<BatchProductDetectionResultDto>>> BatchDetectProductsAsync(BatchProductDetectionDto dto);
        Task<ApiResponse<BatchProductOperationResultDto>> BatchCreateAndUpdateProductsAsync(BatchProductOperationDto dto);
        Task<ApiResponse<DomesticProductDto>> UpdateDomesticProductAsync(string productCode, UpdateDomesticProductDto dto);
        Task<ApiResponse<BatchProductOperationResultDto>> BatchUpdateDomesticProductsAsync(BatchUpdateDomesticProductsDto dto);
        Task<ApiResponse<bool>> BatchDeleteAsync(List<string> productCodes);
        Task<ApiResponse<List<DomesticSetProductDto>>> GetSetItemsAsync(string productCode);
        Task<ApiResponse<bool>> UpdateSetItemsAsync(string productCode, List<SetItemUpdateDto> items);
        Task<ApiResponse<BatchCreateSetProductsResultDto>> BatchCreateSetProductsAsync(BatchCreateSetProductsDto dto);
        Task<ApiResponse<SyncResult>> SyncSelectedToHBSalesAsync(List<string> productCodes, bool includeImage = false);
    }
}