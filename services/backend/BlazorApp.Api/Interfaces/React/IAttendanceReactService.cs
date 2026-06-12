using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IAttendanceReactService
    {
        Task<ApiResponse<List<AttendanceScheduleDto>>> GetSchedulesAsync(AttendanceScheduleQueryDto query);
        Task<ApiResponse<List<AttendanceScheduleDto>>> GetWeekSchedulesAsync(AttendanceScheduleQueryDto query);
        Task<ApiResponse<AttendanceScheduleDto>> CreateScheduleAsync(CreateAttendanceScheduleDto request);
        Task<ApiResponse<AttendanceScheduleDto>> UpdateScheduleAsync(string scheduleGuid, UpdateAttendanceScheduleDto request);
        Task<ApiResponse<int>> PublishWeekAsync(PublishAttendanceWeekDto request);
        Task<ApiResponse<bool>> DeleteScheduleAsync(string scheduleGuid);
        Task<ApiResponse<AttendanceTodayDto>> GetMyTodayAsync(DateTime? workDate = null, string? storeCode = null);
        Task<ApiResponse<List<AttendanceScheduleDto>>> GetMyWeekAsync(DateTime? weekStartDate, string? storeCode = null);
        Task<ApiResponse<List<AttendanceAvailabilityDto>>> GetMyAvailabilityAsync(DateTime? weekStartDate, string? storeCode = null);
        Task<ApiResponse<List<AttendanceAvailabilityDto>>> CreateMyAvailabilityAsync(CreateAttendanceAvailabilityDto request);
        Task<ApiResponse<AttendanceAvailabilityDto>> UpdateMyAvailabilityAsync(string availabilityGuid, UpdateAttendanceAvailabilityDto request);
        Task<ApiResponse<bool>> CancelMyAvailabilityAsync(string availabilityGuid);
        Task<ApiResponse<AttendancePunchDto>> PunchAsync(AttendancePunchRequestDto request);
        Task<ApiResponse<List<AttendanceLeaveRequestDto>>> GetMyLeaveRequestsAsync();
        Task<ApiResponse<AttendanceLeaveRequestDto>> CreateMyLeaveRequestAsync(CreateAttendanceLeaveRequestDto request);
        Task<ApiResponse<bool>> CancelMyLeaveRequestAsync(string leaveGuid);
        Task<ApiResponse<AttendanceLeaveRequestDto>> CreateManagedLeaveRequestAsync(CreateManagedAttendanceLeaveRequestDto request);
        Task<ApiResponse<DirectUploadSignature>> GetLeaveAttachmentUploadSignatureAsync(DirectUploadRequest request);
        Task<ApiResponse<List<AttendanceAvailabilityDto>>> GetAvailabilityAsync(AttendanceAvailabilityQueryDto query);
        Task<ApiResponse<List<AttendancePunchDto>>> GetPunchesAsync(AttendancePunchQueryDto query);
        Task<ApiResponse<List<AttendanceApprovalDto>>> GetApprovalsAsync(AttendanceApprovalQueryDto query);
        Task<ApiResponse<List<AttendanceApprovalDto>>> GetPendingApprovalsAsync(AttendanceApprovalQueryDto query);
        Task<ApiResponse<AttendanceApprovalDto>> ApproveAsync(string approvalGuid, ReviewAttendanceApprovalDto request);
        Task<ApiResponse<AttendanceApprovalDto>> RejectAsync(string approvalGuid, ReviewAttendanceApprovalDto request);
        Task<ApiResponse<List<AttendanceStoreHolidayDto>>> GetHolidaysAsync(AttendanceStoreHolidayQueryDto query);
        Task<ApiResponse<AttendanceStoreHolidayDto>> CreateHolidayAsync(CreateAttendanceStoreHolidayDto request);
        Task<ApiResponse<BatchUpsertAttendanceStoreHolidayResultDto>> BatchUpsertHolidaysAsync(BatchUpsertAttendanceStoreHolidayDto request);
        Task<ApiResponse<AttendanceStoreHolidayDto>> UpdateHolidayAsync(string holidayGuid, UpdateAttendanceStoreHolidayDto request);
        Task<ApiResponse<bool>> DeleteHolidayAsync(string holidayGuid);
        Task<ApiResponse<AttendanceSettingsDto>> GetSettingsAsync();
        Task<ApiResponse<AttendanceSettingsDto>> UpdateSettingsAsync(UpdateAttendanceSettingsDto request);
    }
}
