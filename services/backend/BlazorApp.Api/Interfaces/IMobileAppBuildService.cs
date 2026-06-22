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

        Task<ApiResponse<MobileAppOtaUpdateDto>> UpsertOtaUpdateAsync(
            MobileAppOtaUpdateUpsertDto dto
        );

        Task<ApiResponse<PagedResult<MobileAppOtaUpdateDto>>> GetOtaUpdatesAsync(
            MobileAppOtaUpdateQueryDto query
        );

        Task<ApiResponse<MobileAppOtaRollbackCommandDto>> CreateOtaRollbackCommandAsync(
            string updateGroupId,
            MobileAppOtaRollbackCommandDto dto
        );
    }
}
