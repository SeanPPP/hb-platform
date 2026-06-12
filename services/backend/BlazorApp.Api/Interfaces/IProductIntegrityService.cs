using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IProductIntegrityService
    {
        Task<ApiResponse<ProductIntegrityCheckResultDto>> CheckIntegrityAsync(List<string>? storeCodes = null);
        Task<ApiResponse<ProductIntegrityFixResultDto>> FixIntegrityAsync(ProductIntegrityFixRequestDto request);
    }
}
