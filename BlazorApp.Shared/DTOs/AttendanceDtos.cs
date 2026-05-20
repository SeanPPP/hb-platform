namespace BlazorApp.Shared.DTOs
{
    public class AttendanceScheduleQueryDto
    {
        public string? StoreCode { get; set; }
        public string? UserGuid { get; set; }
        public DateTime? WeekStartDate { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class CreateAttendanceScheduleDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public DateTime WorkDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string? Status { get; set; }
        public string? Remark { get; set; }
    }

    public class PublishAttendanceWeekDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public DateTime WeekStartDate { get; set; }
    }

    public class UpdateAttendanceScheduleDto
    {
        public DateTime WorkDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string? Status { get; set; }
        public string? Remark { get; set; }
    }

    public class AttendanceScheduleDto
    {
        public string ScheduleGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public bool IsMine { get; set; }
        public DateTime WorkDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Remark { get; set; }
    }

    public class AttendanceAvailabilityQueryDto
    {
        public string? StoreCode { get; set; }
        public string? UserGuid { get; set; }
        public DateTime? WeekStartDate { get; set; }
    }

    public class AttendanceAvailabilitySegmentDto
    {
        public DateTime AvailableDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string? Remark { get; set; }
    }

    public class CreateAttendanceAvailabilityDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public DateTime WeekStartDate { get; set; }
        public List<AttendanceAvailabilitySegmentDto> Segments { get; set; } = new();
    }

    public class UpdateAttendanceAvailabilityDto
    {
        public DateTime AvailableDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string? Remark { get; set; }
    }

    public class AttendanceAvailabilityDto
    {
        public string AvailabilityGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public DateTime WeekStartDate { get; set; }
        public DateTime AvailableDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Remark { get; set; }
    }

    public class AttendancePunchRequestDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string PunchType { get; set; } = "ClockIn";
        public string? StoreTimeZone { get; set; }
        public DateTime? PunchTimeUtc { get; set; }
        public string? DeviceId { get; set; }
        public string? Remark { get; set; }
    }

    public class AttendancePunchQueryDto
    {
        public string? StoreCode { get; set; }
        public string? UserGuid { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? Status { get; set; }
    }

    public class AttendancePunchDto
    {
        public string PunchGuid { get; set; } = string.Empty;
        public string? ScheduleGuid { get; set; }
        public string StoreCode { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public DateTime WorkDate { get; set; }
        public string StoreTimeZone { get; set; } = string.Empty;
        public string PunchType { get; set; } = string.Empty;
        public DateTime PunchTimeUtc { get; set; }
        public DateTime PunchTimeLocal { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string? Remark { get; set; }
    }

    public class AttendanceTodayDto
    {
        public DateTime WorkDate { get; set; }
        public List<AttendanceScheduleDto> Schedules { get; set; } = new();
        public List<AttendancePunchDto> Punches { get; set; } = new();
        public List<AttendanceStoreHolidayDto> Holidays { get; set; } = new();
    }

    public class AttendanceApprovalQueryDto
    {
        public string? StoreCode { get; set; }
        public string? SourceType { get; set; }
        public string? ReviewStatus { get; set; }
    }

    public class ReviewAttendanceApprovalDto
    {
        public string? ReviewRemark { get; set; }
    }

    public class AttendanceApprovalDto
    {
        public string ApprovalGuid { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string SourceGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string ApplicantUserGuid { get; set; } = string.Empty;
        public string? ReviewerUserGuid { get; set; }
        public string ReviewStatus { get; set; } = string.Empty;
        public string? ReviewRemark { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AttendanceStoreHolidayQueryDto
    {
        public string? StoreCode { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class CreateAttendanceStoreHolidayDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public DateTime HolidayDate { get; set; }
        public string HolidayName { get; set; } = string.Empty;
        public string BusinessStatus { get; set; } = "Open";
        public TimeSpan? OpenTime { get; set; }
        public TimeSpan? CloseTime { get; set; }
        public bool IsPaidHoliday { get; set; }
        public string? Remark { get; set; }
    }

    public class UpdateAttendanceStoreHolidayDto : CreateAttendanceStoreHolidayDto { }

    public class AttendanceStoreHolidayDto
    {
        public string HolidayGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public DateTime HolidayDate { get; set; }
        public string HolidayName { get; set; } = string.Empty;
        public string BusinessStatus { get; set; } = string.Empty;
        public TimeSpan? OpenTime { get; set; }
        public TimeSpan? CloseTime { get; set; }
        public bool IsPaidHoliday { get; set; }
        public string? Remark { get; set; }
    }

    public class CreateAttendanceLeaveRequestDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string LeaveType { get; set; } = "AnnualLeave";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public string? Reason { get; set; }
        public string? AttachmentUrl { get; set; }
    }

    public class AttendanceLeaveRequestDto
    {
        public string LeaveGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public string? Reason { get; set; }
        public string? AttachmentUrl { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewRemark { get; set; }
    }

    public class UpdateAttendanceSettingsDto
    {
        public int LateGraceMinutes { get; set; } = 5;
        public int EarlyLeaveGraceMinutes { get; set; } = 5;
        public bool AllowNoSchedulePunch { get; set; } = true;
        public bool RequireApprovalForLate { get; set; } = true;
        public bool RequireApprovalForEarlyLeave { get; set; } = true;
        public bool RequireApprovalForNoSchedule { get; set; } = true;
        public bool RequireApprovalForDuplicate { get; set; } = true;
    }

    public class AttendanceSettingsDto : UpdateAttendanceSettingsDto { }
}
