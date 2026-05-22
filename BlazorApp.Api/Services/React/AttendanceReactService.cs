using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class AttendanceReactService : IAttendanceReactService
    {
        private const string DefaultStoreTimeZone = "Australia/Sydney";
        private static readonly HashSet<string> SupportedStoreTimeZones = new(
            new[] { "Australia/Brisbane", "Australia/Melbourne", DefaultStoreTimeZone },
            StringComparer.OrdinalIgnoreCase
        );

        private readonly ISqlSugarClient _db;
        private readonly ICurrentUserService _currentUserService;
        private readonly ICurrentUserManageableStoreScopeService _scopeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AttendanceReactService> _logger;

        public AttendanceReactService(
            SqlSugarContext context,
            ICurrentUserService currentUserService,
            ICurrentUserManageableStoreScopeService scopeService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AttendanceReactService> logger
        )
        {
            _db = context.Db;
            _currentUserService = currentUserService;
            _scopeService = scopeService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<ApiResponse<List<AttendanceScheduleDto>>> GetSchedulesAsync(
            AttendanceScheduleQueryDto query
        )
        {
            var storeAccess = await ResolveManagedStoreAccessAsync(query.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<List<AttendanceScheduleDto>>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var rows = await _db.Queryable<AttendanceSchedule>()
                .Where(item => !item.IsDeleted)
                .WhereIF(!string.IsNullOrWhiteSpace(query.StoreCode), item => item.StoreCode == query.StoreCode!.Trim())
                .WhereIF(string.IsNullOrWhiteSpace(query.StoreCode) && storeAccess.StoreCodes.Count > 0, item => storeAccess.StoreCodes.Contains(item.StoreCode))
                .WhereIF(!string.IsNullOrWhiteSpace(query.UserGuid), item => item.UserGuid == query.UserGuid!.Trim())
                .WhereIF(query.WeekStartDate.HasValue, item => item.WorkDate >= query.WeekStartDate!.Value.Date && item.WorkDate <= query.WeekStartDate!.Value.Date.AddDays(6))
                .WhereIF(query.FromDate.HasValue, item => item.WorkDate >= query.FromDate!.Value.Date)
                .WhereIF(query.ToDate.HasValue, item => item.WorkDate <= query.ToDate!.Value.Date)
                .OrderBy(item => item.WorkDate)
                .OrderBy(item => item.StartTime)
                .ToListAsync();

            return ApiResponse<List<AttendanceScheduleDto>>.OK(rows.Select(item => ToDto(item)).ToList());
        }

        public Task<ApiResponse<List<AttendanceScheduleDto>>> GetWeekSchedulesAsync(
            AttendanceScheduleQueryDto query
        )
        {
            query.WeekStartDate ??= GetWeekStart(DateTime.Today);
            return GetSchedulesAsync(query);
        }

        public async Task<ApiResponse<AttendanceScheduleDto>> CreateScheduleAsync(
            CreateAttendanceScheduleDto request
        )
        {
            var validation = ValidateSchedulePayload(request.StoreCode, request.UserGuid, request.StartTime, request.EndTime);
            if (!validation.Success)
            {
                return ApiResponse<AttendanceScheduleDto>.Error(validation.Message, validation.ErrorCode);
            }

            var storeAccess = await ResolveManagedStoreAccessAsync(request.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<AttendanceScheduleDto>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            if (await HasOverlappingScheduleAsync(null, request.StoreCode, request.UserGuid, request.WorkDate.Date, request.StartTime, request.EndTime))
            {
                return ApiResponse<AttendanceScheduleDto>.Error("同员工同分店同日排班时间不能重叠", "SCHEDULE_OVERLAP");
            }

            var now = DateTime.UtcNow;
            var model = new AttendanceSchedule
            {
                ScheduleGuid = Guid.NewGuid().ToString(),
                StoreCode = request.StoreCode.Trim(),
                UserGuid = request.UserGuid.Trim(),
                WorkDate = request.WorkDate.Date,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                Status = "Draft",
                Remark = request.Remark,
                CreatedAt = now,
                CreatedBy = _currentUserService.GetCurrentUsername(),
                UpdatedAt = now,
                UpdatedBy = _currentUserService.GetCurrentUsername(),
            };

            await _db.Insertable(model).ExecuteCommandAsync();
            return ApiResponse<AttendanceScheduleDto>.OK(ToDto(model), "排班已创建");
        }

        public async Task<ApiResponse<AttendanceScheduleDto>> UpdateScheduleAsync(
            string scheduleGuid,
            UpdateAttendanceScheduleDto request
        )
        {
            var model = await _db.Queryable<AttendanceSchedule>()
                .FirstAsync(item => item.ScheduleGuid == scheduleGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<AttendanceScheduleDto>.Error("排班不存在", "NOT_FOUND");
            }

            var storeAccess = await ResolveManagedStoreAccessAsync(model.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<AttendanceScheduleDto>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            if (request.StartTime >= request.EndTime)
            {
                return ApiResponse<AttendanceScheduleDto>.Error("排班结束时间必须晚于开始时间", "INVALID_TIME_RANGE");
            }

            if (await HasOverlappingScheduleAsync(model.ScheduleGuid, model.StoreCode, model.UserGuid, request.WorkDate.Date, request.StartTime, request.EndTime))
            {
                return ApiResponse<AttendanceScheduleDto>.Error("同员工同分店同日排班时间不能重叠", "SCHEDULE_OVERLAP");
            }

            model.WorkDate = request.WorkDate.Date;
            model.StartTime = request.StartTime;
            model.EndTime = request.EndTime;
            model.Status = NormalizeScheduleStatus(request.Status, defaultStatus: model.Status);
            model.Remark = request.Remark;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            return ApiResponse<AttendanceScheduleDto>.OK(ToDto(model), "排班已更新");
        }

        public async Task<ApiResponse<int>> PublishWeekAsync(PublishAttendanceWeekDto request)
        {
            if (string.IsNullOrWhiteSpace(request.StoreCode))
            {
                return ApiResponse<int>.Error("分店代码不能为空", "STORE_REQUIRED");
            }

            var storeCode = request.StoreCode.Trim();
            var storeAccess = await ResolveManagedStoreAccessAsync(storeCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<int>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var weekStart = request.WeekStartDate.Date;
            var weekEnd = weekStart.AddDays(6);
            var now = DateTime.UtcNow;
            var username = _currentUserService.GetCurrentUsername();
            var affected = await _db.Updateable<AttendanceSchedule>()
                .SetColumns(item => item.Status == "Active")
                .SetColumns(item => item.UpdatedAt == now)
                .SetColumns(item => item.UpdatedBy == username)
                .Where(item =>
                    !item.IsDeleted
                    && item.StoreCode == storeCode
                    && item.Status == "Draft"
                    && item.WorkDate >= weekStart
                    && item.WorkDate <= weekEnd
                )
                .ExecuteCommandAsync();

            return ApiResponse<int>.OK(affected, "排班已发布");
        }

        public async Task<ApiResponse<bool>> DeleteScheduleAsync(string scheduleGuid)
        {
            var model = await _db.Queryable<AttendanceSchedule>()
                .FirstAsync(item => item.ScheduleGuid == scheduleGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<bool>.Error("排班不存在", "NOT_FOUND");
            }

            var storeAccess = await ResolveManagedStoreAccessAsync(model.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<bool>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            model.Status = "Cancelled";
            model.IsDeleted = true;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            return ApiResponse<bool>.OK(true, "排班已取消");
        }

        public async Task<ApiResponse<AttendanceTodayDto>> GetMyTodayAsync(
            DateTime? workDate = null,
            string? storeCode = null
        )
        {
            var userGuid = ResolveCurrentUserGuid();
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ApiResponse<AttendanceTodayDto>.Error("无法识别当前员工", "USER_NOT_FOUND");
            }

            var access = await ResolveRelatedStoreAccessAsync(userGuid, storeCode);
            if (!access.Success)
            {
                return ApiResponse<AttendanceTodayDto>.Error(access.Message, access.ErrorCode);
            }

            var today = (workDate ?? DateTime.Today).Date;
            var tomorrow = today.AddDays(1);
            var schedules = await _db.Queryable<AttendanceSchedule>()
                .Where(item =>
                    !item.IsDeleted
                    && item.Status == "Active"
                    && item.UserGuid == userGuid
                    && item.WorkDate >= today
                    && item.WorkDate < tomorrow
                )
                .WhereIF(access.StoreCodes.Count > 0, item => access.StoreCodes.Contains(item.StoreCode))
                .ToListAsync();
            var punches = await _db.Queryable<AttendancePunch>()
                .Where(item => !item.IsDeleted && item.UserGuid == userGuid && item.WorkDate >= today && item.WorkDate < tomorrow)
                .WhereIF(access.StoreCodes.Count > 0, item => access.StoreCodes.Contains(item.StoreCode))
                .ToListAsync();
            var holidays = await _db.Queryable<AttendanceStoreHoliday>()
                .Where(item => !item.IsDeleted && item.HolidayDate >= today && item.HolidayDate < tomorrow)
                .WhereIF(access.StoreCodes.Count > 0, item => access.StoreCodes.Contains(item.StoreCode))
                .ToListAsync();

            return ApiResponse<AttendanceTodayDto>.OK(new AttendanceTodayDto
            {
                WorkDate = today,
                Schedules = schedules.Select(item => ToDto(item)).ToList(),
                Punches = punches.Select(ToDto).ToList(),
                Holidays = holidays.Select(ToDto).ToList(),
            });
        }

        public async Task<ApiResponse<List<AttendanceScheduleDto>>> GetMyWeekAsync(
            DateTime? weekStartDate,
            string? storeCode = null
        )
        {
            var userGuid = ResolveCurrentUserGuid();
            var access = await ResolveRelatedStoreAccessAsync(userGuid, storeCode);
            if (!access.Success)
            {
                return ApiResponse<List<AttendanceScheduleDto>>.Error(access.Message, access.ErrorCode);
            }

            var start = (weekStartDate ?? GetWeekStart(DateTime.Today)).Date;
            var end = start.AddDays(6);
            var rows = await _db.Queryable<AttendanceSchedule>()
                .Where(item =>
                    !item.IsDeleted
                    && item.Status == "Active"
                    && item.WorkDate >= start
                    && item.WorkDate <= end
                )
                .WhereIF(access.StoreCodes.Count > 0, item => access.StoreCodes.Contains(item.StoreCode))
                .OrderBy(item => item.WorkDate)
                .OrderBy(item => item.StartTime)
                .ToListAsync();

            var result = rows.Select(item => ToDto(item, userGuid)).ToList();
            await PopulateScheduleDisplayFieldsAsync(result);
            return ApiResponse<List<AttendanceScheduleDto>>.OK(result);
        }

        public async Task<ApiResponse<List<AttendanceAvailabilityDto>>> GetMyAvailabilityAsync(
            DateTime? weekStartDate,
            string? storeCode = null
        )
        {
            var userGuid = ResolveCurrentUserGuid();
            var access = await ResolveRelatedStoreAccessAsync(userGuid, storeCode);
            if (!access.Success)
            {
                return ApiResponse<List<AttendanceAvailabilityDto>>.Error(access.Message, access.ErrorCode);
            }

            var start = (weekStartDate ?? GetWeekStart(DateTime.Today)).Date;
            var nextWeek = start.AddDays(7);
            var rows = await _db.Queryable<AttendanceAvailability>()
                .Where(item => !item.IsDeleted && item.UserGuid == userGuid && item.WeekStartDate >= start && item.WeekStartDate < nextWeek)
                .WhereIF(access.StoreCodes.Count > 0, item => access.StoreCodes.Contains(item.StoreCode))
                .OrderBy(item => item.AvailableDate)
                .OrderBy(item => item.StartTime)
                .ToListAsync();

            return ApiResponse<List<AttendanceAvailabilityDto>>.OK(rows.Select(ToDto).ToList());
        }

        public async Task<ApiResponse<List<AttendanceAvailabilityDto>>> CreateMyAvailabilityAsync(
            CreateAttendanceAvailabilityDto request
        )
        {
            var userGuid = ResolveCurrentUserGuid();
            var access = await ResolveRelatedStoreAccessAsync(userGuid, request.StoreCode);
            if (!access.Success)
            {
                return ApiResponse<List<AttendanceAvailabilityDto>>.Error(access.Message, access.ErrorCode);
            }

            var weekStart = request.WeekStartDate.Date;
            if (request.Segments.Count == 0)
            {
                return ApiResponse<List<AttendanceAvailabilityDto>>.Error("请至少提交一个可上班时间段", "EMPTY_SEGMENTS");
            }

            if (request.Segments.Any(segment => segment.StartTime >= segment.EndTime))
            {
                return ApiResponse<List<AttendanceAvailabilityDto>>.Error("可上班结束时间必须晚于开始时间", "INVALID_TIME_RANGE");
            }

            if (request.Segments.Any(segment => segment.AvailableDate.Date < weekStart || segment.AvailableDate.Date > weekStart.AddDays(6)))
            {
                return ApiResponse<List<AttendanceAvailabilityDto>>.Error("可上班日期必须在指定周内", "DATE_OUT_OF_WEEK");
            }

            var now = DateTime.UtcNow;
            var rows = request.Segments.Select(segment => new AttendanceAvailability
            {
                AvailabilityGuid = Guid.NewGuid().ToString(),
                StoreCode = request.StoreCode.Trim(),
                UserGuid = userGuid,
                WeekStartDate = weekStart,
                AvailableDate = segment.AvailableDate.Date,
                StartTime = segment.StartTime,
                EndTime = segment.EndTime,
                Status = "Active",
                Remark = segment.Remark,
                CreatedAt = now,
                CreatedBy = _currentUserService.GetCurrentUsername(),
                UpdatedAt = now,
                UpdatedBy = _currentUserService.GetCurrentUsername(),
            }).ToList();

            await _db.Insertable(rows).ExecuteCommandAsync();
            return ApiResponse<List<AttendanceAvailabilityDto>>.OK(rows.Select(ToDto).ToList(), "可上班时间已提交");
        }

        public async Task<ApiResponse<AttendanceAvailabilityDto>> UpdateMyAvailabilityAsync(
            string availabilityGuid,
            UpdateAttendanceAvailabilityDto request
        )
        {
            var userGuid = ResolveCurrentUserGuid();
            var model = await _db.Queryable<AttendanceAvailability>()
                .FirstAsync(item => item.AvailabilityGuid == availabilityGuid && item.UserGuid == userGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<AttendanceAvailabilityDto>.Error("可上班时间不存在", "NOT_FOUND");
            }

            var access = await ResolveRelatedStoreAccessAsync(userGuid, model.StoreCode);
            if (!access.Success)
            {
                return ApiResponse<AttendanceAvailabilityDto>.Error(access.Message, access.ErrorCode);
            }

            model.AvailableDate = request.AvailableDate.Date;
            model.WeekStartDate = GetWeekStart(request.AvailableDate.Date);
            model.StartTime = request.StartTime;
            model.EndTime = request.EndTime;
            model.Remark = request.Remark;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            return ApiResponse<AttendanceAvailabilityDto>.OK(ToDto(model), "可上班时间已更新");
        }

        public async Task<ApiResponse<bool>> CancelMyAvailabilityAsync(string availabilityGuid)
        {
            var userGuid = ResolveCurrentUserGuid();
            var model = await _db.Queryable<AttendanceAvailability>()
                .FirstAsync(item => item.AvailabilityGuid == availabilityGuid && item.UserGuid == userGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<bool>.Error("可上班时间不存在", "NOT_FOUND");
            }

            model.Status = "Cancelled";
            model.IsDeleted = true;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            return ApiResponse<bool>.OK(true, "可上班时间已取消");
        }

        public async Task<ApiResponse<AttendancePunchDto>> PunchAsync(AttendancePunchRequestDto request)
        {
            var userGuid = ResolveCurrentUserGuid();
            var storeCode = request.StoreCode?.Trim();
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return ApiResponse<AttendancePunchDto>.Error("请选择分店后再打卡", "STORE_REQUIRED");
            }

            var access = await ResolveRelatedStoreAccessAsync(userGuid, storeCode);
            if (!access.Success)
            {
                return ApiResponse<AttendancePunchDto>.Error(access.Message, access.ErrorCode);
            }

            var storeTimeZone = NormalizeStoreTimeZone(request.StoreTimeZone);
            var punchUtc = DateTime.SpecifyKind(request.PunchTimeUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
            var punchLocal = ConvertUtcToStoreLocal(punchUtc, storeTimeZone);
            var workDate = punchLocal.Date;
            var punchType = NormalizePunchType(request.PunchType);
            var settings = await GetOrCreateSettingsModelAsync();

            var schedule = await FindScheduleForPunchAsync(storeCode, userGuid, workDate, punchLocal.TimeOfDay);
            var isDuplicate = await _db.Queryable<AttendancePunch>().AnyAsync(item =>
                !item.IsDeleted
                && item.StoreCode == storeCode
                && item.UserGuid == userGuid
                && item.WorkDate == workDate
                && item.PunchType == punchType
            );

            var status = ResolvePunchStatus(schedule, punchType, punchLocal.TimeOfDay, settings, isDuplicate);
            if (status == "NoSchedule" && !settings.AllowNoSchedulePunch)
            {
                return ApiResponse<AttendancePunchDto>.Error("当前没有排班，无法打卡", "NO_SCHEDULE");
            }

            var now = DateTime.UtcNow;
            var punch = new AttendancePunch
            {
                PunchGuid = Guid.NewGuid().ToString(),
                ScheduleGuid = schedule?.ScheduleGuid,
                StoreCode = storeCode,
                UserGuid = userGuid,
                WorkDate = workDate,
                StoreTimeZone = storeTimeZone,
                PunchType = punchType,
                PunchTimeUtc = punchUtc,
                PunchTimeLocal = punchLocal,
                Status = status,
                DeviceId = request.DeviceId,
                Source = "App",
                Remark = request.Remark,
                CreatedAt = now,
                CreatedBy = _currentUserService.GetCurrentUsername(),
            };

            await _db.Insertable(punch).ExecuteCommandAsync();
            if (RequiresApproval(status, settings))
            {
                await CreatePendingApprovalAsync("Punch", punch.PunchGuid, punch.StoreCode, userGuid);
            }

            return ApiResponse<AttendancePunchDto>.OK(ToDto(punch), "打卡已保存");
        }

        public async Task<ApiResponse<List<AttendanceLeaveRequestDto>>> GetMyLeaveRequestsAsync()
        {
            var userGuid = ResolveCurrentUserGuid();
            var rows = await _db.Queryable<AttendanceLeaveRequest>()
                .Where(item => !item.IsDeleted && item.UserGuid == userGuid)
                .OrderByDescending(item => item.CreatedAt)
                .ToListAsync();
            return ApiResponse<List<AttendanceLeaveRequestDto>>.OK(rows.Select(ToDto).ToList());
        }

        public async Task<ApiResponse<AttendanceLeaveRequestDto>> CreateMyLeaveRequestAsync(
            CreateAttendanceLeaveRequestDto request
        )
        {
            var userGuid = ResolveCurrentUserGuid();
            var access = await ResolveRelatedStoreAccessAsync(userGuid, request.StoreCode);
            if (!access.Success)
            {
                return ApiResponse<AttendanceLeaveRequestDto>.Error(access.Message, access.ErrorCode);
            }

            var now = DateTime.UtcNow;
            var model = new AttendanceLeaveRequest
            {
                LeaveGuid = Guid.NewGuid().ToString(),
                StoreCode = request.StoreCode.Trim(),
                UserGuid = userGuid,
                LeaveType = request.LeaveType.Trim(),
                StartDate = request.StartDate.Date,
                EndDate = request.EndDate.Date,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                Reason = request.Reason,
                AttachmentUrl = request.AttachmentUrl,
                Status = "Pending",
                CreatedAt = now,
                CreatedBy = _currentUserService.GetCurrentUsername(),
            };

            await _db.Insertable(model).ExecuteCommandAsync();
            await CreatePendingApprovalAsync("Leave", model.LeaveGuid, model.StoreCode, userGuid);
            return ApiResponse<AttendanceLeaveRequestDto>.OK(ToDto(model), "请假申请已提交");
        }

        public async Task<ApiResponse<bool>> CancelMyLeaveRequestAsync(string leaveGuid)
        {
            var userGuid = ResolveCurrentUserGuid();
            var model = await _db.Queryable<AttendanceLeaveRequest>()
                .FirstAsync(item => item.LeaveGuid == leaveGuid && item.UserGuid == userGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<bool>.Error("请假申请不存在", "NOT_FOUND");
            }

            model.Status = "Cancelled";
            model.IsDeleted = true;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            await _db.Updateable<AttendanceApproval>()
                .SetColumns(item => item.ReviewStatus == "Cancelled")
                .Where(item => item.SourceType == "Leave" && item.SourceGuid == leaveGuid && item.ReviewStatus == "Pending")
                .ExecuteCommandAsync();
            return ApiResponse<bool>.OK(true, "请假申请已取消");
        }

        public async Task<ApiResponse<List<AttendanceAvailabilityDto>>> GetAvailabilityAsync(
            AttendanceAvailabilityQueryDto query
        )
        {
            var storeAccess = await ResolveManagedStoreAccessAsync(query.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<List<AttendanceAvailabilityDto>>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var rows = await _db.Queryable<AttendanceAvailability>()
                .Where(item => !item.IsDeleted)
                .WhereIF(!string.IsNullOrWhiteSpace(query.StoreCode), item => item.StoreCode == query.StoreCode!.Trim())
                .WhereIF(string.IsNullOrWhiteSpace(query.StoreCode) && storeAccess.StoreCodes.Count > 0, item => storeAccess.StoreCodes.Contains(item.StoreCode))
                .WhereIF(!string.IsNullOrWhiteSpace(query.UserGuid), item => item.UserGuid == query.UserGuid!.Trim())
                .WhereIF(query.WeekStartDate.HasValue, item => item.WeekStartDate >= query.WeekStartDate!.Value.Date && item.WeekStartDate < query.WeekStartDate!.Value.Date.AddDays(1))
                .OrderBy(item => item.AvailableDate)
                .OrderBy(item => item.StartTime)
                .ToListAsync();
            return ApiResponse<List<AttendanceAvailabilityDto>>.OK(rows.Select(ToDto).ToList());
        }

        public async Task<ApiResponse<List<AttendancePunchDto>>> GetPunchesAsync(
            AttendancePunchQueryDto query
        )
        {
            var storeAccess = await ResolveManagedStoreAccessAsync(query.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<List<AttendancePunchDto>>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var rows = await _db.Queryable<AttendancePunch>()
                .Where(item => !item.IsDeleted)
                .WhereIF(!string.IsNullOrWhiteSpace(query.StoreCode), item => item.StoreCode == query.StoreCode!.Trim())
                .WhereIF(string.IsNullOrWhiteSpace(query.StoreCode) && storeAccess.StoreCodes.Count > 0, item => storeAccess.StoreCodes.Contains(item.StoreCode))
                .WhereIF(!string.IsNullOrWhiteSpace(query.UserGuid), item => item.UserGuid == query.UserGuid!.Trim())
                .WhereIF(query.FromDate.HasValue, item => item.WorkDate >= query.FromDate!.Value.Date)
                .WhereIF(query.ToDate.HasValue, item => item.WorkDate <= query.ToDate!.Value.Date)
                .WhereIF(!string.IsNullOrWhiteSpace(query.Status), item => item.Status == query.Status!.Trim())
                .OrderByDescending(item => item.PunchTimeUtc)
                .ToListAsync();
            return ApiResponse<List<AttendancePunchDto>>.OK(rows.Select(ToDto).ToList());
        }

        public async Task<ApiResponse<List<AttendanceApprovalDto>>> GetApprovalsAsync(
            AttendanceApprovalQueryDto query
        )
        {
            var storeAccess = await ResolveManagedStoreAccessAsync(query.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<List<AttendanceApprovalDto>>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var rows = await _db.Queryable<AttendanceApproval>()
                .Where(item => !item.IsDeleted)
                .WhereIF(!string.IsNullOrWhiteSpace(query.StoreCode), item => item.StoreCode == query.StoreCode!.Trim())
                .WhereIF(string.IsNullOrWhiteSpace(query.StoreCode) && storeAccess.StoreCodes.Count > 0, item => storeAccess.StoreCodes.Contains(item.StoreCode))
                .WhereIF(!string.IsNullOrWhiteSpace(query.SourceType), item => item.SourceType == query.SourceType!.Trim())
                .WhereIF(!string.IsNullOrWhiteSpace(query.ReviewStatus), item => item.ReviewStatus == query.ReviewStatus!.Trim())
                .OrderByDescending(item => item.CreatedAt)
                .ToListAsync();
            var result = rows.Select(ToDto).ToList();
            await PopulateApprovalDisplayFieldsAsync(result);
            return ApiResponse<List<AttendanceApprovalDto>>.OK(result);
        }

        public Task<ApiResponse<List<AttendanceApprovalDto>>> GetPendingApprovalsAsync(
            AttendanceApprovalQueryDto query
        )
        {
            query.ReviewStatus = "Pending";
            return GetApprovalsAsync(query);
        }

        public Task<ApiResponse<AttendanceApprovalDto>> ApproveAsync(
            string approvalGuid,
            ReviewAttendanceApprovalDto request
        ) => ReviewApprovalAsync(approvalGuid, "Approved", request.ReviewRemark);

        public Task<ApiResponse<AttendanceApprovalDto>> RejectAsync(
            string approvalGuid,
            ReviewAttendanceApprovalDto request
        ) => ReviewApprovalAsync(approvalGuid, "Rejected", request.ReviewRemark);

        public async Task<ApiResponse<List<AttendanceStoreHolidayDto>>> GetHolidaysAsync(
            AttendanceStoreHolidayQueryDto query
        )
        {
            var storeAccess = await ResolveManagedStoreAccessAsync(query.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<List<AttendanceStoreHolidayDto>>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var rows = await _db.Queryable<AttendanceStoreHoliday>()
                .Where(item => !item.IsDeleted)
                .WhereIF(!string.IsNullOrWhiteSpace(query.StoreCode), item => item.StoreCode == query.StoreCode!.Trim())
                .WhereIF(string.IsNullOrWhiteSpace(query.StoreCode) && storeAccess.StoreCodes.Count > 0, item => storeAccess.StoreCodes.Contains(item.StoreCode))
                .WhereIF(query.FromDate.HasValue, item => item.HolidayDate >= query.FromDate!.Value.Date)
                .WhereIF(query.ToDate.HasValue, item => item.HolidayDate <= query.ToDate!.Value.Date)
                .OrderBy(item => item.HolidayDate)
                .ToListAsync();
            return ApiResponse<List<AttendanceStoreHolidayDto>>.OK(rows.Select(ToDto).ToList());
        }

        public async Task<ApiResponse<AttendanceStoreHolidayDto>> CreateHolidayAsync(
            CreateAttendanceStoreHolidayDto request
        )
        {
            var storeAccess = await ResolveManagedStoreAccessAsync(request.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<AttendanceStoreHolidayDto>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var now = DateTime.UtcNow;
            var model = new AttendanceStoreHoliday
            {
                HolidayGuid = Guid.NewGuid().ToString(),
                StoreCode = request.StoreCode.Trim(),
                HolidayDate = request.HolidayDate.Date,
                HolidayName = request.HolidayName.Trim(),
                BusinessStatus = NormalizeBusinessStatus(request.BusinessStatus),
                OpenTime = request.OpenTime,
                CloseTime = request.CloseTime,
                IsPaidHoliday = request.IsPaidHoliday,
                Remark = request.Remark,
                CreatedAt = now,
                CreatedBy = _currentUserService.GetCurrentUsername(),
                UpdatedAt = now,
                UpdatedBy = _currentUserService.GetCurrentUsername(),
            };
            await _db.Insertable(model).ExecuteCommandAsync();
            return ApiResponse<AttendanceStoreHolidayDto>.OK(ToDto(model), "公共假期已创建");
        }

        public async Task<ApiResponse<AttendanceStoreHolidayDto>> UpdateHolidayAsync(
            string holidayGuid,
            UpdateAttendanceStoreHolidayDto request
        )
        {
            var model = await _db.Queryable<AttendanceStoreHoliday>()
                .FirstAsync(item => item.HolidayGuid == holidayGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<AttendanceStoreHolidayDto>.Error("公共假期不存在", "NOT_FOUND");
            }

            var storeAccess = await ResolveManagedStoreAccessAsync(model.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<AttendanceStoreHolidayDto>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            model.HolidayDate = request.HolidayDate.Date;
            model.HolidayName = request.HolidayName.Trim();
            model.BusinessStatus = NormalizeBusinessStatus(request.BusinessStatus);
            model.OpenTime = request.OpenTime;
            model.CloseTime = request.CloseTime;
            model.IsPaidHoliday = request.IsPaidHoliday;
            model.Remark = request.Remark;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            return ApiResponse<AttendanceStoreHolidayDto>.OK(ToDto(model), "公共假期已更新");
        }

        public async Task<ApiResponse<bool>> DeleteHolidayAsync(string holidayGuid)
        {
            var model = await _db.Queryable<AttendanceStoreHoliday>()
                .FirstAsync(item => item.HolidayGuid == holidayGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<bool>.Error("公共假期不存在", "NOT_FOUND");
            }

            var storeAccess = await ResolveManagedStoreAccessAsync(model.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<bool>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            model.IsDeleted = true;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();
            return ApiResponse<bool>.OK(true, "公共假期已删除");
        }

        public async Task<ApiResponse<AttendanceSettingsDto>> GetSettingsAsync()
        {
            var settings = await GetOrCreateSettingsModelAsync();
            return ApiResponse<AttendanceSettingsDto>.OK(ToDto(settings));
        }

        public async Task<ApiResponse<AttendanceSettingsDto>> UpdateSettingsAsync(
            UpdateAttendanceSettingsDto request
        )
        {
            if (!IsAdmin())
            {
                return ApiResponse<AttendanceSettingsDto>.Error("只有 Admin 可以修改考勤设置", "ADMIN_REQUIRED");
            }

            var settings = await GetOrCreateSettingsModelAsync();
            settings.LateGraceMinutes = Math.Max(0, request.LateGraceMinutes);
            settings.EarlyLeaveGraceMinutes = Math.Max(0, request.EarlyLeaveGraceMinutes);
            settings.AllowNoSchedulePunch = request.AllowNoSchedulePunch;
            settings.RequireApprovalForLate = request.RequireApprovalForLate;
            settings.RequireApprovalForEarlyLeave = request.RequireApprovalForEarlyLeave;
            settings.RequireApprovalForNoSchedule = request.RequireApprovalForNoSchedule;
            settings.RequireApprovalForDuplicate = request.RequireApprovalForDuplicate;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(settings).ExecuteCommandAsync();
            return ApiResponse<AttendanceSettingsDto>.OK(ToDto(settings), "考勤设置已更新");
        }

        private async Task<ApiResponse<AttendanceApprovalDto>> ReviewApprovalAsync(
            string approvalGuid,
            string reviewStatus,
            string? reviewRemark
        )
        {
            var model = await _db.Queryable<AttendanceApproval>()
                .FirstAsync(item => item.ApprovalGuid == approvalGuid && !item.IsDeleted);
            if (model == null)
            {
                return ApiResponse<AttendanceApprovalDto>.Error("审核记录不存在", "NOT_FOUND");
            }

            var storeAccess = await ResolveManagedStoreAccessAsync(model.StoreCode);
            if (!storeAccess.Success)
            {
                return ApiResponse<AttendanceApprovalDto>.Error(storeAccess.Message, storeAccess.ErrorCode);
            }

            var reviewer = ResolveCurrentUserGuid();
            model.ReviewStatus = reviewStatus;
            model.ReviewRemark = reviewRemark;
            model.ReviewerUserGuid = reviewer;
            model.ReviewedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = _currentUserService.GetCurrentUsername();
            await _db.Updateable(model).ExecuteCommandAsync();

            if (model.SourceType == "Punch")
            {
                await _db.Updateable<AttendancePunch>()
                    .SetColumns(item => item.Status == reviewStatus)
                    .Where(item => item.PunchGuid == model.SourceGuid)
                    .ExecuteCommandAsync();
            }
            else if (model.SourceType == "Leave")
            {
                await _db.Updateable<AttendanceLeaveRequest>()
                    .SetColumns(item => item.Status == reviewStatus)
                    .SetColumns(item => item.ReviewedBy == reviewer)
                    .SetColumns(item => item.ReviewedAt == model.ReviewedAt)
                    .SetColumns(item => item.ReviewRemark == reviewRemark)
                    .Where(item => item.LeaveGuid == model.SourceGuid)
                    .ExecuteCommandAsync();
            }

            return ApiResponse<AttendanceApprovalDto>.OK(ToDto(model), "审核已处理");
        }

        private async Task CreatePendingApprovalAsync(
            string sourceType,
            string sourceGuid,
            string storeCode,
            string applicantUserGuid
        )
        {
            var exists = await _db.Queryable<AttendanceApproval>().AnyAsync(item =>
                !item.IsDeleted
                && item.SourceType == sourceType
                && item.SourceGuid == sourceGuid
                && item.ReviewStatus == "Pending"
            );
            if (exists)
            {
                return;
            }

            await _db.Insertable(new AttendanceApproval
            {
                ApprovalGuid = Guid.NewGuid().ToString(),
                SourceType = sourceType,
                SourceGuid = sourceGuid,
                StoreCode = storeCode,
                ApplicantUserGuid = applicantUserGuid,
                ReviewStatus = "Pending",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetCurrentUsername(),
            }).ExecuteCommandAsync();
        }

        private async Task<bool> HasOverlappingScheduleAsync(
            string? currentScheduleGuid,
            string storeCode,
            string userGuid,
            DateTime workDate,
            TimeSpan startTime,
            TimeSpan endTime
        )
        {
            var rows = await _db.Queryable<AttendanceSchedule>()
                .Where(item =>
                    !item.IsDeleted
                    && item.Status != "Cancelled"
                    && item.StoreCode == storeCode.Trim()
                    && item.UserGuid == userGuid.Trim()
                    && item.WorkDate >= workDate.Date
                    && item.WorkDate < workDate.Date.AddDays(1)
                )
                .WhereIF(!string.IsNullOrWhiteSpace(currentScheduleGuid), item => item.ScheduleGuid != currentScheduleGuid)
                .ToListAsync();

            return rows.Any(item => startTime < item.EndTime && endTime > item.StartTime);
        }

        private async Task<AttendanceSchedule?> FindScheduleForPunchAsync(
            string storeCode,
            string userGuid,
            DateTime workDate,
            TimeSpan localTime
        )
        {
            var rows = await _db.Queryable<AttendanceSchedule>()
                .Where(item =>
                    !item.IsDeleted
                    && item.Status == "Active"
                    && item.StoreCode == storeCode.Trim()
                    && item.UserGuid == userGuid
                    && item.WorkDate >= workDate.Date
                    && item.WorkDate < workDate.Date.AddDays(1)
                )
                .ToListAsync();

            return rows
                .OrderBy(item => Math.Abs((item.StartTime - localTime).TotalMinutes))
                .FirstOrDefault();
        }

        private static string ResolvePunchStatus(
            AttendanceSchedule? schedule,
            string punchType,
            TimeSpan localTime,
            AttendanceSettings settings,
            bool isDuplicate
        )
        {
            if (isDuplicate)
            {
                return "Duplicate";
            }

            if (schedule == null)
            {
                return "NoSchedule";
            }

            if (punchType == "ClockIn" && localTime > schedule.StartTime.Add(TimeSpan.FromMinutes(settings.LateGraceMinutes)))
            {
                return "Late";
            }

            if (punchType == "ClockOut" && localTime < schedule.EndTime.Subtract(TimeSpan.FromMinutes(settings.EarlyLeaveGraceMinutes)))
            {
                return "EarlyLeave";
            }

            return "Normal";
        }

        private static bool RequiresApproval(string status, AttendanceSettings settings) =>
            status switch
            {
                "Late" => settings.RequireApprovalForLate,
                "EarlyLeave" => settings.RequireApprovalForEarlyLeave,
                "NoSchedule" => settings.RequireApprovalForNoSchedule,
                "Duplicate" => settings.RequireApprovalForDuplicate,
                _ => false,
            };

        private async Task<AttendanceSettings> GetOrCreateSettingsModelAsync()
        {
            var settings = await _db.Queryable<AttendanceSettings>()
                .Where(item => !item.IsDeleted)
                .OrderBy(item => item.Id)
                .FirstAsync();
            if (settings != null)
            {
                return settings;
            }

            settings = new AttendanceSettings
            {
                LateGraceMinutes = 5,
                EarlyLeaveGraceMinutes = 5,
                AllowNoSchedulePunch = true,
                RequireApprovalForLate = true,
                RequireApprovalForEarlyLeave = true,
                RequireApprovalForNoSchedule = true,
                RequireApprovalForDuplicate = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "system",
            };
            await _db.Insertable(settings).ExecuteCommandAsync();
            return settings;
        }

        private async Task<StoreAccessResult> ResolveManagedStoreAccessAsync(string? requestedStoreCode)
        {
            var scope = await _scopeService.GetScopeAsync();
            if (!scope.IsAllowed)
            {
                return StoreAccessResult.Forbidden(scope.Message);
            }

            var requested = requestedStoreCode?.Trim();
            if (!string.IsNullOrWhiteSpace(requested) && !scope.CanAccessStoreCode(requested))
            {
                return StoreAccessResult.Forbidden("没有权限访问该分店", "FORBIDDEN_STORE");
            }

            return StoreAccessResult.Allowed(
                string.IsNullOrWhiteSpace(requested)
                    ? scope.StoreCodes.ToList()
                    : new List<string> { requested }
            );
        }

        private async Task<StoreAccessResult> ResolveRelatedStoreAccessAsync(
            string userGuid,
            string? requestedStoreCode
        )
        {
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return StoreAccessResult.Forbidden("无法识别当前员工", "USER_NOT_FOUND");
            }

            if (IsAdmin())
            {
                return StoreAccessResult.Allowed(
                    string.IsNullOrWhiteSpace(requestedStoreCode)
                        ? new List<string>()
                        : new List<string> { requestedStoreCode.Trim() }
                );
            }

            var storeCodes = await GetRelatedStoreCodesAsync(userGuid);
            var requested = requestedStoreCode?.Trim();
            if (!string.IsNullOrWhiteSpace(requested) && !storeCodes.Contains(requested, StringComparer.OrdinalIgnoreCase))
            {
                return StoreAccessResult.Forbidden("没有权限访问该分店", "FORBIDDEN_STORE");
            }

            return StoreAccessResult.Allowed(
                string.IsNullOrWhiteSpace(requested) ? storeCodes : new List<string> { requested }
            );
        }

        private async Task<List<string>> GetRelatedStoreCodesAsync(string userGuid)
        {
            return await _db.Queryable<UserStore>()
                .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                .Where((us, s) => us.UserGUID == userGuid && !us.IsDeleted && !s.IsDeleted)
                .Select((us, s) => s.StoreCode)
                .ToListAsync();
        }

        private async Task PopulateScheduleDisplayFieldsAsync(List<AttendanceScheduleDto> schedules)
        {
            if (schedules.Count == 0)
            {
                return;
            }

            var userGuids = schedules.Select(item => item.UserGuid).Distinct().ToList();
            var storeCodes = schedules.Select(item => item.StoreCode).Distinct().ToList();
            var users = await _db.Queryable<User>()
                .Where(item => userGuids.Contains(item.UserGUID))
                .ToListAsync();
            var stores = await _db.Queryable<Store>()
                .Where(item => storeCodes.Contains(item.StoreCode))
                .ToListAsync();
            var userMap = users.ToDictionary(item => item.UserGUID, StringComparer.OrdinalIgnoreCase);
            var storeMap = stores.ToDictionary(item => item.StoreCode, StringComparer.OrdinalIgnoreCase);

            foreach (var schedule in schedules)
            {
                if (userMap.TryGetValue(schedule.UserGuid, out var user))
                {
                    schedule.EmployeeName = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.Username
                        : user.FullName;
                }

                if (storeMap.TryGetValue(schedule.StoreCode, out var store))
                {
                    schedule.StoreName = store.StoreName;
                }
            }
        }

        private async Task PopulateApprovalDisplayFieldsAsync(List<AttendanceApprovalDto> approvals)
        {
            if (approvals.Count == 0)
            {
                return;
            }

            var userGuids = approvals.Select(item => item.ApplicantUserGuid).Distinct().ToList();
            var storeCodes = approvals.Select(item => item.StoreCode).Distinct().ToList();
            var punchGuids = approvals
                .Where(item => item.SourceType.Equals("Punch", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.SourceGuid)
                .Distinct()
                .ToList();
            var leaveGuids = approvals
                .Where(item => item.SourceType.Equals("Leave", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.SourceGuid)
                .Distinct()
                .ToList();

            var users = await _db.Queryable<User>()
                .Where(item => userGuids.Contains(item.UserGUID))
                .ToListAsync();
            var stores = await _db.Queryable<Store>()
                .Where(item => storeCodes.Contains(item.StoreCode))
                .ToListAsync();
            var punches = punchGuids.Count == 0
                ? new List<AttendancePunch>()
                : await _db.Queryable<AttendancePunch>()
                    .Where(item => punchGuids.Contains(item.PunchGuid))
                    .ToListAsync();
            var leaves = leaveGuids.Count == 0
                ? new List<AttendanceLeaveRequest>()
                : await _db.Queryable<AttendanceLeaveRequest>()
                    .Where(item => leaveGuids.Contains(item.LeaveGuid))
                    .ToListAsync();

            var userMap = users.ToDictionary(item => item.UserGUID, StringComparer.OrdinalIgnoreCase);
            var storeMap = stores.ToDictionary(item => item.StoreCode, StringComparer.OrdinalIgnoreCase);
            var punchMap = punches.ToDictionary(item => item.PunchGuid, StringComparer.OrdinalIgnoreCase);
            var leaveMap = leaves.ToDictionary(item => item.LeaveGuid, StringComparer.OrdinalIgnoreCase);

            foreach (var approval in approvals)
            {
                if (userMap.TryGetValue(approval.ApplicantUserGuid, out var user))
                {
                    approval.EmployeeName = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.Username
                        : user.FullName;
                }

                if (storeMap.TryGetValue(approval.StoreCode, out var store))
                {
                    approval.StoreName = store.StoreName;
                }

                if (approval.SourceType.Equals("Punch", StringComparison.OrdinalIgnoreCase)
                    && punchMap.TryGetValue(approval.SourceGuid, out var punch))
                {
                    approval.WorkDate = punch.WorkDate;
                    approval.Title = punch.PunchType;
                    approval.Detail = $"{punch.PunchType} · {punch.Status}";
                }
                else if (approval.SourceType.Equals("Leave", StringComparison.OrdinalIgnoreCase)
                    && leaveMap.TryGetValue(approval.SourceGuid, out var leave))
                {
                    approval.WorkDate = leave.StartDate;
                    approval.Title = leave.LeaveType;
                    approval.Detail = $"{leave.StartDate:yyyy-MM-dd} - {leave.EndDate:yyyy-MM-dd}"
                        + (string.IsNullOrWhiteSpace(leave.Reason) ? string.Empty : $" · {leave.Reason}");
                }
                else
                {
                    approval.Title = approval.SourceType;
                }
            }
        }

        private string ResolveCurrentUserGuid()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst("userGuid")?.Value
                ?? user?.FindFirst("userId")?.Value
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? _currentUserService.GetCurrentUserGuid();
        }

        private bool IsAdmin()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.IsInRole("Admin") == true || user?.IsInRole("管理员") == true;
        }

        private static ValidationResult ValidateSchedulePayload(
            string storeCode,
            string userGuid,
            TimeSpan startTime,
            TimeSpan endTime
        )
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return ValidationResult.Error("分店代码不能为空", "STORE_REQUIRED");
            }

            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return ValidationResult.Error("员工不能为空", "USER_REQUIRED");
            }

            if (startTime >= endTime)
            {
                return ValidationResult.Error("排班结束时间必须晚于开始时间", "INVALID_TIME_RANGE");
            }

            return ValidationResult.OK();
        }

        private static string NormalizeStoreTimeZone(string? storeTimeZone)
        {
            var normalized = storeTimeZone?.Trim();
            return !string.IsNullOrWhiteSpace(normalized) && SupportedStoreTimeZones.Contains(normalized)
                ? normalized
                : DefaultStoreTimeZone;
        }

        private static DateTime ConvertUtcToStoreLocal(DateTime utc, string storeTimeZone)
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(storeTimeZone);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timezone);
            }
            catch (TimeZoneNotFoundException)
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(DefaultStoreTimeZone);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timezone);
            }
        }

        private static string NormalizePunchType(string? punchType) =>
            punchType?.Trim().Equals("ClockOut", StringComparison.OrdinalIgnoreCase) == true
                ? "ClockOut"
                : "ClockIn";

        private static string NormalizeScheduleStatus(string? status, string defaultStatus)
        {
            var normalized = status?.Trim();
            return normalized switch
            {
                "Draft" or "Active" or "Cancelled" => normalized,
                _ => defaultStatus,
            };
        }

        private static string NormalizeBusinessStatus(string? status)
        {
            var normalized = status?.Trim();
            return normalized is "Closed" or "Partial" ? normalized : "Open";
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-diff);
        }

        private static AttendanceScheduleDto ToDto(AttendanceSchedule item, string? currentUserGuid = null) => new()
        {
            ScheduleGuid = item.ScheduleGuid,
            StoreCode = item.StoreCode,
            UserGuid = item.UserGuid,
            IsMine = !string.IsNullOrWhiteSpace(currentUserGuid)
                && item.UserGuid.Equals(currentUserGuid, StringComparison.OrdinalIgnoreCase),
            WorkDate = item.WorkDate,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            Status = item.Status,
            Remark = item.Remark,
        };

        private static AttendanceAvailabilityDto ToDto(AttendanceAvailability item) => new()
        {
            AvailabilityGuid = item.AvailabilityGuid,
            StoreCode = item.StoreCode,
            UserGuid = item.UserGuid,
            WeekStartDate = item.WeekStartDate,
            AvailableDate = item.AvailableDate,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            Status = item.Status,
            Remark = item.Remark,
        };

        private static AttendancePunchDto ToDto(AttendancePunch item) => new()
        {
            PunchGuid = item.PunchGuid,
            ScheduleGuid = item.ScheduleGuid,
            StoreCode = item.StoreCode,
            UserGuid = item.UserGuid,
            WorkDate = item.WorkDate,
            StoreTimeZone = item.StoreTimeZone,
            PunchType = item.PunchType,
            PunchTimeUtc = item.PunchTimeUtc,
            PunchTimeLocal = item.PunchTimeLocal,
            Status = item.Status,
            DeviceId = item.DeviceId,
            Source = item.Source,
            Remark = item.Remark,
        };

        private static AttendanceApprovalDto ToDto(AttendanceApproval item) => new()
        {
            ApprovalGuid = item.ApprovalGuid,
            SourceType = item.SourceType,
            SourceGuid = item.SourceGuid,
            StoreCode = item.StoreCode,
            ApplicantUserGuid = item.ApplicantUserGuid,
            ReviewerUserGuid = item.ReviewerUserGuid,
            Title = item.SourceType,
            ReviewStatus = item.ReviewStatus,
            ReviewRemark = item.ReviewRemark,
            ReviewedAt = item.ReviewedAt,
            CreatedAt = item.CreatedAt,
        };

        private static AttendanceStoreHolidayDto ToDto(AttendanceStoreHoliday item) => new()
        {
            HolidayGuid = item.HolidayGuid,
            StoreCode = item.StoreCode,
            HolidayDate = item.HolidayDate,
            HolidayName = item.HolidayName,
            BusinessStatus = item.BusinessStatus,
            OpenTime = item.OpenTime,
            CloseTime = item.CloseTime,
            IsPaidHoliday = item.IsPaidHoliday,
            Remark = item.Remark,
        };

        private static AttendanceLeaveRequestDto ToDto(AttendanceLeaveRequest item) => new()
        {
            LeaveGuid = item.LeaveGuid,
            StoreCode = item.StoreCode,
            UserGuid = item.UserGuid,
            LeaveType = item.LeaveType,
            StartDate = item.StartDate,
            EndDate = item.EndDate,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            Reason = item.Reason,
            AttachmentUrl = item.AttachmentUrl,
            Status = item.Status,
            ReviewedBy = item.ReviewedBy,
            ReviewedAt = item.ReviewedAt,
            ReviewRemark = item.ReviewRemark,
        };

        private static AttendanceSettingsDto ToDto(AttendanceSettings item) => new()
        {
            LateGraceMinutes = item.LateGraceMinutes,
            EarlyLeaveGraceMinutes = item.EarlyLeaveGraceMinutes,
            AllowNoSchedulePunch = item.AllowNoSchedulePunch,
            RequireApprovalForLate = item.RequireApprovalForLate,
            RequireApprovalForEarlyLeave = item.RequireApprovalForEarlyLeave,
            RequireApprovalForNoSchedule = item.RequireApprovalForNoSchedule,
            RequireApprovalForDuplicate = item.RequireApprovalForDuplicate,
        };

        private sealed class StoreAccessResult
        {
            public bool Success { get; private init; }
            public string Message { get; private init; } = string.Empty;
            public string? ErrorCode { get; private init; }
            public List<string> StoreCodes { get; private init; } = new();

            public static StoreAccessResult Allowed(List<string> storeCodes) => new()
            {
                Success = true,
                StoreCodes = storeCodes,
            };

            public static StoreAccessResult Forbidden(string message, string errorCode = "FORBIDDEN") => new()
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
            };
        }

        private sealed class ValidationResult
        {
            public bool Success { get; private init; }
            public string Message { get; private init; } = string.Empty;
            public string? ErrorCode { get; private init; }

            public static ValidationResult OK() => new() { Success = true };
            public static ValidationResult Error(string message, string errorCode) => new()
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
            };
        }
    }
}
