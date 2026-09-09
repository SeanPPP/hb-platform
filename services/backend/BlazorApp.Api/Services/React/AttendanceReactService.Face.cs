using BlazorApp.Api.Services.Attendance;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React;

public partial class AttendanceReactService
{
    // 人脸路径明确携带类型与原始发生时间；不调用 QR 的“自动决定上下班”分支。
    internal async Task<string> CommitFacePunchAsync(FaceAttendanceEvent row, string actor, CancellationToken ct)
    {
        var existing = await _db.Queryable<AttendancePunch>().FirstAsync(x => x.FaceEventGuid == row.EventGuid);
        if (existing != null) return existing.PunchGuid;
        var store = await _db.Queryable<Store>().FirstAsync(x => !x.IsDeleted && x.StoreCode == row.StoreCode);
        if (store == null) throw new FaceAttendanceException(409, "STORE_NOT_FOUND", "分店不存在");
        var zone = ResolveStoreTimeZoneFromStore(store) ?? DefaultStoreTimeZone;
        var utc = NormalizeFaceOccurredAtUtc(row.OccurredAtUtc);
        var local = ConvertUtcToStoreLocal(utc, zone);
        var resource = AttendanceDailyMutationLock.BuildResource(row.UserGuid, row.StoreCode, local.Date);
        await using var processLock = await AttendanceDailyMutationLock.AcquireProcessAsync(resource);
        await _db.Ado.BeginTranAsync();
        try
        {
            await AttendanceDailyMutationLock.AcquireDatabaseAsync(_db, resource);
            existing = await _db.Queryable<AttendancePunch>().FirstAsync(x => x.FaceEventGuid == row.EventGuid);
            if (existing != null) { await _db.Ado.CommitTranAsync(); return existing.PunchGuid; }
            // 人脸核验完成到正式写卡之间可能发生撤销或人员关系变化，必须在同一员工锁内二次确认。
            if (!await FaceAttendanceService.IsSubjectCurrentAsync(_db, row))
                throw new FaceAttendanceException(409, "ROSTER_OR_ENROLLMENT_CHANGED", "员工名单或模板版本已变化，需要人工处理");
            var settings = await GetOrCreateSettingsModelAsync();
            var requested = row.PunchType == "clockIn" ? "ClockIn" : row.PunchType == "clockOut" ? "ClockOut" : string.Empty;
            if (string.IsNullOrEmpty(requested)) throw new FaceAttendanceException(409, "FACE_PUNCH_TYPE_INVALID", "人脸考勤类型无效");

            // 离线事件必须服从员工完整 UTC 时间线；同一时刻的另一设备事件也会造成零时长班段，必须复核。
            if (await _db.Queryable<AttendancePunch>()
                    .AnyAsync(item => !item.IsDeleted && item.UserGuid == row.UserGuid && item.PunchTimeUtc >= utc))
                throw new FaceAttendanceException(409, "FACE_PUNCH_OUT_OF_ORDER", "不能在已有同一或更晚考勤后插入人脸事件");
            var priorTimeline = requested == "ClockIn"
                ? await _db.Queryable<AttendancePunch>()
                    .Where(item => !item.IsDeleted && item.UserGuid == row.UserGuid && item.PunchTimeUtc <= utc)
                    .ToListAsync()
                : new List<AttendancePunch>();
            if (requested == "ClockIn" && HasOpenOtherStoreSegment(priorTimeline, row.StoreCode))
                throw new FaceAttendanceException(409, "CROSS_STORE_OPEN_SEGMENT", "其他分店仍有未结束的上班会话");
            var limit = await ResolveSegmentLimitAsync(row.UserGuid, row.StoreCode);
            var schedule = await FindFaceScheduleForPunchAsync(
                row.StoreCode,
                row.UserGuid,
                local,
                limit,
                settings);
            if (schedule == null && !settings.AllowNoSchedulePunch) throw new FaceAttendanceException(409, "NO_SCHEDULE", "当前没有有效排班");
            var today = await _db.Queryable<AttendancePunch>().Where(x => !x.IsDeleted && x.UserGuid == row.UserGuid && x.WorkDate >= local.Date && x.WorkDate < local.Date.AddDays(1)).ToListAsync();
            var storePunches = today.Where(x => x.StoreCode.Equals(row.StoreCode, StringComparison.OrdinalIgnoreCase)).ToList();
            var schedulePunches = schedule == null
                ? storePunches
                : await _db.Queryable<AttendancePunch>()
                    .Where(x => !x.IsDeleted && x.ScheduleGuid == schedule.ScheduleGuid)
                    .ToListAsync();
            var session = schedule == null
                ? null
                : AttendanceWorkSessionCalculator.Calculate(
                    schedule,
                    schedulePunches,
                    limit,
                    local,
                    settings.EarlyLeaveGraceMinutes,
                    settings.LateGraceMinutes);
            // 与 QR 写入一致：已完成的班段不能通过离线补传绕过每日上限。
            if (session != null && !session.HasOpenSegment && session.Segments.Count >= limit)
                throw new FaceAttendanceException(409, "SEGMENT_LIMIT_REACHED", "班段数量已达到上限");
            if (session == null && storePunches.Any(x => x.ScheduleGuid != null)
                && CountEffectiveStoreSegments(storePunches) >= limit)
                throw new FaceAttendanceException(409, "SEGMENT_LIMIT_REACHED", "班段数量已达到上限");
            if (session == null && storePunches.Any(x => x.PunchType == "ClockIn") && storePunches.Any(x => x.PunchType == "ClockOut"))
                throw new FaceAttendanceException(409, "DAY_COMPLETE", "今日打卡已完成");

            var expected = session == null
                ? (storePunches.Count(x => x.PunchType == "ClockIn") > storePunches.Count(x => x.PunchType == "ClockOut") ? "ClockOut" : "ClockIn")
                : session.HasOpenSegment ? "ClockOut" : "ClockIn";
            if (!requested.Equals(expected, StringComparison.Ordinal)) throw new FaceAttendanceException(409, "FACE_PUNCH_SEQUENCE_CONFLICT", "人脸上下班类型与有效考勤会话不一致");
            if (requested == "ClockIn" && CountEffectiveStoreSegments(storePunches) >= (schedule == null ? 1 : limit))
                throw new FaceAttendanceException(409, "SEGMENT_LIMIT_REACHED", "班段数量已达到上限");
            var adjacent = await GetAdjacentBusinessDayPunchesAsync(row.UserGuid, local.Date);
            var isFirstClockIn = requested == "ClockIn" && (session?.Segments.Count ?? 0) == 0;
            var isFinalClockOut = requested == "ClockOut" && schedule != null
                && ((session?.Segments.Count ?? 0) >= limit
                    || local >= GetScheduledEndLocal(schedule).AddMinutes(-settings.EarlyLeaveGraceMinutes));
            var punch = new AttendancePunch { PunchGuid = Guid.NewGuid().ToString(), ScheduleGuid = schedule?.ScheduleGuid, StoreCode = row.StoreCode, UserGuid = row.UserGuid, WorkDate = schedule?.WorkDate.Date ?? local.Date, StoreTimeZone = zone, PunchType = requested, PunchTimeUtc = utc, PunchTimeLocal = local, Status = ResolveFacePunchStatus(schedule, requested, local, settings, isFirstClockIn, isFinalClockOut), Source = "Face", DeviceId = row.HardwareId, PosDeviceCode = row.DeviceCode, SigningKeyId = row.KeyId, FaceEventGuid = row.EventGuid, Remark = "Face attendance verified", CreatedAt = _timeProvider.GetUtcNow().UtcDateTime, CreatedBy = actor };
            if (requested == "ClockOut" && HasCrossScheduleOverlap(adjacent.Append(punch).ToList())) throw new FaceAttendanceException(409, "CROSS_STORE_PUNCH_OVERLAP", "下班会与其他分店完成班段重叠");
            await _db.Insertable(punch).ExecuteCommandAsync();
            if (RequiresApproval(punch.Status, settings)) await CreatePendingApprovalAsync("Punch", punch.PunchGuid, punch.StoreCode, punch.UserGuid);
            if (schedule != null)
            {
                var updated = AttendanceWorkSessionCalculator.Calculate(schedule, schedulePunches.Append(punch), limit, local, settings.EarlyLeaveGraceMinutes, settings.LateGraceMinutes);
                await ReconcileOvertimeApprovalAsync(schedule, updated); await ReconcileFinalPunchApprovalsAsync(schedule, updated, settings); await ReconcileMissingClockOutApprovalAsync(schedule, updated);
            }
            await _db.Ado.CommitTranAsync(); return punch.PunchGuid;
        }
        catch { await _db.Ado.RollbackTranAsync(); throw; }
    }

    private async Task<AttendanceSchedule?> FindFaceScheduleForPunchAsync(
        string storeCode,
        string userGuid,
        DateTime local,
        int segmentLimit,
        AttendanceSettings settings)
    {
        var sameDay = await FindScheduleForPunchAsync(
            storeCode,
            userGuid,
            local.Date,
            local.TimeOfDay,
            segmentLimit,
            settings);
        if (sameDay != null)
            return sameDay;

        var activeSchedules = await _db.Queryable<AttendanceSchedule>()
            .Where(item => !item.IsDeleted
                && item.Status == "Active"
                && item.StoreCode == storeCode.Trim()
                && item.UserGuid == userGuid)
            .ToListAsync();
        var sameDayOvernight = activeSchedules
            .Where(item => item.WorkDate.Date == local.Date
                && item.EndTime < item.StartTime
                && local.TimeOfDay >= item.StartTime)
            .OrderByDescending(item => item.StartTime)
            .ToList();
        if (sameDayOvernight.Count > 0)
        {
            var sameDayGuids = sameDayOvernight.Select(item => item.ScheduleGuid).ToList();
            var sameDayPunches = await _db.Queryable<AttendancePunch>()
                .Where(item => !item.IsDeleted && item.ScheduleGuid != null && sameDayGuids.Contains(item.ScheduleGuid))
                .ToListAsync();
            // 通用选择器的“当前时间小于 EndTime”无法描述 22:00–06:00；首张上班卡在这里绑定开始日排班。
            return sameDayOvernight
                .Select(schedule => new
                {
                    Schedule = schedule,
                    Session = AttendanceWorkSessionCalculator.Calculate(
                        schedule,
                        sameDayPunches,
                        segmentLimit,
                        local,
                        settings.EarlyLeaveGraceMinutes,
                        settings.LateGraceMinutes),
                })
                .OrderByDescending(item => item.Session.HasOpenSegment)
                .ThenByDescending(item => item.Schedule.StartTime)
                .Select(item => item.Schedule)
                .First();
        }

        // 跨午夜班次的业务日属于开始日期；次日凌晨必须继续绑定前一天仍未结束的会话。
        var overnight = activeSchedules
            // SQLite 的 DateTime/TimeSpan 存储表示因 provider 而异，跨午夜判断统一用 CLR 语义。
            .Where(item => item.WorkDate.Date == local.Date.AddDays(-1)
                && item.EndTime < item.StartTime)
            .ToList();
        if (overnight.Count == 0)
            return null;

        var scheduleGuids = overnight.Select(item => item.ScheduleGuid).ToList();
        var punches = await _db.Queryable<AttendancePunch>()
            .Where(item => !item.IsDeleted && item.ScheduleGuid != null && scheduleGuids.Contains(item.ScheduleGuid))
            .ToListAsync();
        return overnight
            .Select(schedule => new
            {
                Schedule = schedule,
                Session = AttendanceWorkSessionCalculator.Calculate(
                    schedule,
                    punches,
                    segmentLimit,
                    local,
                    settings.EarlyLeaveGraceMinutes,
                    settings.LateGraceMinutes),
            })
            .Where(item => item.Session.HasOpenSegment)
            .OrderByDescending(item => item.Session.Segments.Last().ClockIn?.PunchTimeUtc)
            .Select(item => item.Schedule)
            .FirstOrDefault();
    }

    private static string ResolveFacePunchStatus(
        AttendanceSchedule? schedule,
        string punchType,
        DateTime local,
        AttendanceSettings settings,
        bool isFirstClockIn,
        bool isFinalClockOut)
    {
        if (schedule == null || schedule.EndTime >= schedule.StartTime || punchType != "ClockOut")
            return ResolveSegmentPunchStatus(schedule, punchType, local.TimeOfDay, settings, isFirstClockIn, isFinalClockOut);
        if (!isFinalClockOut)
            return "Break";

        // 跨午夜排班的下班边界在次日，不能用只有时分秒的比较把凌晨误判为早退。
        var scheduledEnd = GetScheduledEndLocal(schedule);
        if (local < scheduledEnd.AddMinutes(-settings.EarlyLeaveGraceMinutes))
            return "EarlyLeave";
        return local - scheduledEnd >= TimeSpan.FromMinutes(15) ? "LateLeave" : "Normal";
    }

    private static DateTime GetScheduledEndLocal(AttendanceSchedule schedule) =>
        schedule.WorkDate.Date.Add(schedule.EndTime).AddDays(schedule.EndTime < schedule.StartTime ? 1 : 0);

    private static DateTime NormalizeFaceOccurredAtUtc(DateTime value) => value.Kind switch
    {
        // SQL Server/SQLite 常将 UTC 时间读为 Unspecified；它仍是 UTC 墙钟值，不能按宿主本地时区再换算一次。
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => value,
    };

    private static bool HasOpenOtherStoreSegment(IEnumerable<AttendancePunch> source, string storeCode)
    {
        var superseded = source
            .Where(item => !string.IsNullOrWhiteSpace(item.SupersedesPunchGuid))
            .Select(item => item.SupersedesPunchGuid!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source
            .Where(item => !superseded.Contains(item.PunchGuid))
            .GroupBy(item => item.ScheduleGuid ?? item.StoreCode, StringComparer.OrdinalIgnoreCase)
            .Any(group =>
            {
                AttendancePunch? open = null;
                foreach (var punch in group.OrderBy(item => item.PunchTimeUtc).ThenBy(item => item.Id))
                {
                    if (punch.PunchType.Equals("ClockIn", StringComparison.OrdinalIgnoreCase))
                        open ??= punch;
                    else if (punch.PunchType.Equals("ClockOut", StringComparison.OrdinalIgnoreCase) && open != null)
                        open = null;
                }
                return open != null && !open.StoreCode.Equals(storeCode, StringComparison.OrdinalIgnoreCase);
            });
    }
}
