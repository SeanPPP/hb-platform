using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IEmployeeProfileService
    {
        Task<ApiResponse<PagedResult<EmployeeProfileListItemDto>>> GetAdminListAsync(
            EmployeeProfileQueryDto query
        );

        Task<ApiResponse<EmployeeProfileDetailDto>> GetAdminDetailAsync(string userGuid);

        Task<ApiResponse<EmployeeProfileDetailDto>> UpsertAdminAsync(
            string userGuid,
            EmployeeProfileUpsertDto dto
        );

        Task<ApiResponse<EmployeeProfileDetailDto>> GetSelfAsync();

        Task<ApiResponse<EmployeeProfileDetailDto>> UpsertSelfAsync(EmployeeProfileUpsertDto dto);
    }
}
