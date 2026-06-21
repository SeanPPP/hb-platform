using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IMobileAppBuildService
    {
        Task<ApiResponse<MobileAppBuildWebhookResultDto>> HandleEasWebhookAsync(string json);

        Task<ApiResponse<MobileAppBuildDto?>> GetLatestAsync(string profile);

        Task<ApiResponse<PagedResult<MobileAppBuildDto>>> GetHistoryAsync(
            MobileAppBuildQueryDto query
        );
    }
}
