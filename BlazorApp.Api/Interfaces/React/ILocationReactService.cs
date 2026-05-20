using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface ILocationReactService
    {
        Task<PagedListReactDto<LocationReactDto>> GetPagedListAsync(LocationReactFilterDto filter);
        Task<ApiResponse<LocationReactDto>> GetByIdAsync(string locationGuid);
        Task<ApiResponse<LocationReactDto>> CreateAsync(CreateLocationReactDto dto);
        Task<ApiResponse<LocationReactDto>> UpdateAsync(string locationGuid, UpdateLocationReactDto dto);
        Task<ApiResponse<bool>> DeleteAsync(string locationGuid);
        Task<List<LocationLookupItemDto>> LookupAsync(string keyword);
        Task<ApiResponse<LocationReactDto>> BindProductAsync(string locationGuid, string productCode);
        Task<ApiResponse<LocationReactDto>> UnbindProductAsync(string locationGuid, string productCode);
    }
}
