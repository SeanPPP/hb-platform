using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocalSuppliersReactService
    {
        Task<PagedResult<LocalSupplierDto>> GetSuppliersAsync(int pageIndex, int pageSize, string? keyword, int? status, string? sortBy, string? sortOrder);
        Task<List<LocalSupplierDto>> GetActiveSuppliersAsync();
        Task<ApiResponse<LocalSupplierSyncResultDto>> SyncFromDicAsync(DateTime? since, bool overwrite);
        Task<ApiResponse<LocalSupplierDto>> CreateAsync(CreateLocalSupplierDto dto);
        Task<ApiResponse<LocalSupplierDto>> UpdateAsync(string code, UpdateLocalSupplierDto dto);
        Task<ApiResponse<bool>> DeleteAsync(string code);
        Task<ApiResponse<bool>> ToggleStatusAsync(string code, int status);
        Task<ApiResponse<bool>> CheckCodeExistsAsync(string code);
    }
}