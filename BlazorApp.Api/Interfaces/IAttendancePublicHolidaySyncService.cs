using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    public interface IAttendancePublicHolidaySyncService
    {
        Task<ApiResponse<SyncAttendanceStoreHolidayResultDto>> SyncStoreAsync(
            SyncAttendanceStoreHolidayDto request,
            CancellationToken cancellationToken = default
        );

        Task<SyncAttendanceStoreHolidayResultDto> SyncAllActiveStoresAsync(
            int daysAhead = 30,
            CancellationToken cancellationToken = default
        );
    }
}
