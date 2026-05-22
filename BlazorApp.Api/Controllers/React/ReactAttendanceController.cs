using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/attendance")]
    [Authorize]
    public class ReactAttendanceController : ControllerBase
    {
        private readonly IAttendanceReactService _service;

        public ReactAttendanceController(IAttendanceReactService service)
        {
            _service = service;
        }

        [HttpGet("schedules")]
        [Authorize(Policy = Permissions.Attendance.Schedule.ViewStore)]
        public async Task<IActionResult> GetSchedules([FromQuery] AttendanceScheduleQueryDto query) =>
            Ok(await _service.GetSchedulesAsync(query));

        [HttpGet("schedules/week")]
        [Authorize(Policy = Permissions.Attendance.Schedule.ViewStore)]
        public async Task<IActionResult> GetWeekSchedules([FromQuery] AttendanceScheduleQueryDto query) =>
            Ok(await _service.GetWeekSchedulesAsync(query));

        [HttpPost("schedules")]
        [Authorize(Policy = Permissions.Attendance.Schedule.EditManagedStore)]
        public async Task<IActionResult> CreateSchedule([FromBody] CreateAttendanceScheduleDto request) =>
            Ok(await _service.CreateScheduleAsync(request));

        [HttpPut("schedules/{scheduleGuid}")]
        [Authorize(Policy = Permissions.Attendance.Schedule.EditManagedStore)]
        public async Task<IActionResult> UpdateSchedule(
            string scheduleGuid,
            [FromBody] UpdateAttendanceScheduleDto request
        ) => Ok(await _service.UpdateScheduleAsync(scheduleGuid, request));

        [HttpPost("schedules/publish-week")]
        [Authorize(Policy = Permissions.Attendance.Schedule.EditManagedStore)]
        public async Task<IActionResult> PublishWeek([FromBody] PublishAttendanceWeekDto request) =>
            Ok(await _service.PublishWeekAsync(request));

        [HttpDelete("schedules/{scheduleGuid}")]
        [Authorize(Policy = Permissions.Attendance.Schedule.EditManagedStore)]
        public async Task<IActionResult> DeleteSchedule(string scheduleGuid) =>
            Ok(await _service.DeleteScheduleAsync(scheduleGuid));

        [HttpGet("my/today")]
        [Authorize(Policy = Permissions.Attendance.Schedule.ViewSelf)]
        public async Task<IActionResult> GetMyToday(
            [FromQuery] string? storeCode = null,
            [FromQuery] DateTime? workDate = null
        ) => Ok(await _service.GetMyTodayAsync(workDate, storeCode));

        [HttpGet("my/week")]
        [Authorize(Policy = Permissions.Attendance.Schedule.ViewSelf)]
        public async Task<IActionResult> GetMyWeek(
            [FromQuery] DateTime? weekStartDate = null,
            [FromQuery] string? storeCode = null
        ) => Ok(await _service.GetMyWeekAsync(weekStartDate, storeCode));

        [HttpGet("my/availability")]
        [Authorize(Policy = Permissions.Attendance.Availability.SubmitSelf)]
        public async Task<IActionResult> GetMyAvailability(
            [FromQuery] DateTime? weekStartDate = null,
            [FromQuery] string? storeCode = null
        ) => Ok(await _service.GetMyAvailabilityAsync(weekStartDate, storeCode));

        [HttpPost("my/availability")]
        [Authorize(Policy = Permissions.Attendance.Availability.SubmitSelf)]
        public async Task<IActionResult> CreateMyAvailability(
            [FromBody] CreateAttendanceAvailabilityDto request
        ) => Ok(await _service.CreateMyAvailabilityAsync(request));

        [HttpPut("my/availability/{availabilityGuid}")]
        [Authorize(Policy = Permissions.Attendance.Availability.SubmitSelf)]
        public async Task<IActionResult> UpdateMyAvailability(
            string availabilityGuid,
            [FromBody] UpdateAttendanceAvailabilityDto request
        ) => Ok(await _service.UpdateMyAvailabilityAsync(availabilityGuid, request));

        [HttpPost("my/availability/{availabilityGuid}/cancel")]
        [Authorize(Policy = Permissions.Attendance.Availability.SubmitSelf)]
        public async Task<IActionResult> CancelMyAvailability(string availabilityGuid) =>
            Ok(await _service.CancelMyAvailabilityAsync(availabilityGuid));

        [HttpPost("punch")]
        [Authorize(Policy = Permissions.Attendance.Punch.Self)]
        public async Task<IActionResult> Punch([FromBody] AttendancePunchRequestDto request) =>
            Ok(await _service.PunchAsync(request));

        [HttpGet("my/leave-requests")]
        [Authorize(Policy = Permissions.Attendance.Leave.ApplySelf)]
        public async Task<IActionResult> GetMyLeaveRequests() =>
            Ok(await _service.GetMyLeaveRequestsAsync());

        [HttpPost("my/leave-requests")]
        [Authorize(Policy = Permissions.Attendance.Leave.ApplySelf)]
        public async Task<IActionResult> CreateMyLeaveRequest(
            [FromBody] CreateAttendanceLeaveRequestDto request
        ) => Ok(await _service.CreateMyLeaveRequestAsync(request));

        [HttpPost("my/leave-requests/{leaveGuid}/cancel")]
        [Authorize(Policy = Permissions.Attendance.Leave.ApplySelf)]
        public async Task<IActionResult> CancelMyLeaveRequest(string leaveGuid) =>
            Ok(await _service.CancelMyLeaveRequestAsync(leaveGuid));

        [HttpGet("availability")]
        [Authorize(Policy = Permissions.Attendance.Availability.ViewManagedStore)]
        public async Task<IActionResult> GetAvailability([FromQuery] AttendanceAvailabilityQueryDto query) =>
            Ok(await _service.GetAvailabilityAsync(query));

        [HttpGet("punches")]
        [Authorize(Policy = Permissions.Attendance.Punch.ViewManagedStore)]
        public async Task<IActionResult> GetPunches([FromQuery] AttendancePunchQueryDto query) =>
            Ok(await _service.GetPunchesAsync(query));

        [HttpGet("approvals")]
        [Authorize(Policy = Permissions.Attendance.Approval.ViewManagedStore)]
        public async Task<IActionResult> GetApprovals([FromQuery] AttendanceApprovalQueryDto query) =>
            Ok(await _service.GetApprovalsAsync(query));

        [HttpGet("approvals/pending")]
        [Authorize(Policy = Permissions.Attendance.Approval.ViewManagedStore)]
        public async Task<IActionResult> GetPendingApprovals([FromQuery] AttendanceApprovalQueryDto query) =>
            Ok(await _service.GetPendingApprovalsAsync(query));

        [HttpPost("approvals/{approvalGuid}/approve")]
        [Authorize(Policy = Permissions.Attendance.Approval.ReviewManagedStore)]
        public async Task<IActionResult> Approve(
            string approvalGuid,
            [FromBody] ReviewAttendanceApprovalDto request
        ) => Ok(await _service.ApproveAsync(approvalGuid, request));

        [HttpPost("approvals/{approvalGuid}/reject")]
        [Authorize(Policy = Permissions.Attendance.Approval.ReviewManagedStore)]
        public async Task<IActionResult> Reject(
            string approvalGuid,
            [FromBody] ReviewAttendanceApprovalDto request
        ) => Ok(await _service.RejectAsync(approvalGuid, request));

        [HttpGet("holidays")]
        [Authorize(Policy = Permissions.Attendance.Holiday.ViewStore)]
        public async Task<IActionResult> GetHolidays([FromQuery] AttendanceStoreHolidayQueryDto query) =>
            Ok(await _service.GetHolidaysAsync(query));

        [HttpPost("holidays")]
        [Authorize(Policy = Permissions.Attendance.Holiday.EditManagedStore)]
        public async Task<IActionResult> CreateHoliday([FromBody] CreateAttendanceStoreHolidayDto request) =>
            Ok(await _service.CreateHolidayAsync(request));

        [HttpPut("holidays/{holidayGuid}")]
        [Authorize(Policy = Permissions.Attendance.Holiday.EditManagedStore)]
        public async Task<IActionResult> UpdateHoliday(
            string holidayGuid,
            [FromBody] UpdateAttendanceStoreHolidayDto request
        ) => Ok(await _service.UpdateHolidayAsync(holidayGuid, request));

        [HttpDelete("holidays/{holidayGuid}")]
        [Authorize(Policy = Permissions.Attendance.Holiday.EditManagedStore)]
        public async Task<IActionResult> DeleteHoliday(string holidayGuid) =>
            Ok(await _service.DeleteHolidayAsync(holidayGuid));

        [HttpGet("settings")]
        [Authorize(Policy = Permissions.Attendance.Settings.Edit)]
        public async Task<IActionResult> GetSettings() => Ok(await _service.GetSettingsAsync());

        [HttpPut("settings")]
        [Authorize(Policy = Permissions.Attendance.Settings.Edit)]
        public async Task<IActionResult> UpdateSettings([FromBody] UpdateAttendanceSettingsDto request) =>
            Ok(await _service.UpdateSettingsAsync(request));
    }
}
