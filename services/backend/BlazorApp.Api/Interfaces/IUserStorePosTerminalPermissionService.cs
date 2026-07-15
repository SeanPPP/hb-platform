using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces;

public interface IUserStorePosTerminalPermissionService
{
    Task<ApiResponse<UserStorePosTerminalPermissionsResponse>> GetAsync(
        string userGuid,
        string storeGuid
    );

    Task<ApiResponse<UserStorePosTerminalPermissionsResponse>> UpdateAsync(
        string userGuid,
        string storeGuid,
        UpdateUserStorePosTerminalPermissionsRequest request
    );

    Task<ApiResponse<UserStorePosTerminalPermissionsResponse>> DeleteAsync(
        string userGuid,
        string storeGuid
    );
}
