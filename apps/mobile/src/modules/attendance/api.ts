import { apiClient } from "@/shared/api/client";
import type {
  AttendanceApproval,
  AttendanceApprovalPayload,
  AttendanceDirectUploadRequest,
  AttendanceDirectUploadSignature,
  AttendanceAvailability,
  AttendanceAvailabilityPayload,
  AttendanceHolidayQueryParams,
  AttendanceHolidaySyncPayload,
  AttendanceHolidaySyncResult,
  AttendanceLeaveRequest,
  AttendanceLeaveRequestPayload,
  AttendancePublishWeekPayload,
  AttendancePunch,
  AttendancePunchPayload,
  AttendancePunchType,
  AttendanceSchedule,
  AttendanceSchedulePayload,
  AttendanceScheduleUpdatePayload,
  AttendanceScheduleWeekParams,
  AttendanceStoreHoliday,
  AttendanceStoreHolidayPayload,
  AttendanceToday,
  AttendanceWeek,
  AttendanceWeekDay,
} from "@/modules/attendance/types";
import {
  buildPublicHolidaySyncWindow,
  normalizeAustralianHolidayJurisdiction,
  resolveAustralianHolidayJurisdiction,
} from "@/modules/attendance/public-holiday-sync";

type ApiRecord = Record<string, unknown>;

const ATTENDANCE_BASE = "/react/v1/attendance";

function isRecord(value: unknown): value is ApiRecord {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function pick(raw: ApiRecord, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asString(value: unknown, fallback = ""): string {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }
  return fallback;
}

function asOptionalString(value: unknown): string | undefined {
  const normalized = asString(value).trim();
  return normalized ? normalized : undefined;
}

function asDateString(value: unknown): string {
  const normalized = asString(value);
  return normalized.includes("T") ? normalized.slice(0, 10) : normalized;
}

function asBoolean(value: unknown, fallback = false): boolean {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    const normalized = value.toLowerCase();
    if (normalized === "true") return true;
    if (normalized === "false") return false;
  }
  return fallback;
}

function asNumber(value: unknown, fallback = 0): number {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return fallback;
}

function formatDateOnly(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function parseDateOnly(value: string) {
  const normalized = value.slice(0, 10);
  const parsed = new Date(`${normalized}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

function addDays(value: string, days: number) {
  const parsed = parseDateOnly(value);
  if (!parsed) {
    return value;
  }
  parsed.setDate(parsed.getDate() + days);
  return formatDateOnly(parsed);
}

function getArray(payload: unknown): ApiRecord[] {
  const candidate = Array.isArray(payload)
    ? payload
    : isRecord(payload)
      ? pick(payload, "items", "Items", "rows", "Rows", "list", "List", "data", "Data")
      : [];

  return Array.isArray(candidate) ? candidate.filter(isRecord) : [];
}

function normalizeSchedule(raw: ApiRecord): AttendanceSchedule {
  return {
    scheduleGuid: asString(pick(raw, "scheduleGuid", "ScheduleGuid", "guid", "Guid")),
    storeCode: asString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    userGuid: asString(pick(raw, "userGuid", "UserGuid", "employeeGuid", "EmployeeGuid")),
    employeeName: asOptionalString(pick(raw, "employeeName", "EmployeeName", "userName", "UserName")),
    workDate: asDateString(pick(raw, "workDate", "WorkDate", "date", "Date")),
    startTime: asString(pick(raw, "startTime", "StartTime")),
    endTime: asString(pick(raw, "endTime", "EndTime")),
    status: asString(pick(raw, "status", "Status"), "Scheduled"),
    remark: asOptionalString(pick(raw, "remark", "Remark", "note", "Note")),
    isMine: asBoolean(pick(raw, "isMine", "IsMine", "mine", "Mine")),
    holidayName: asOptionalString(pick(raw, "holidayName", "HolidayName")),
    holidayBusinessStatus: asOptionalString(pick(raw, "holidayBusinessStatus", "HolidayBusinessStatus")),
  };
}

function normalizePunch(raw: ApiRecord): AttendancePunch {
  return {
    punchGuid: asString(pick(raw, "punchGuid", "PunchGuid", "guid", "Guid")),
    scheduleGuid: asOptionalString(pick(raw, "scheduleGuid", "ScheduleGuid")),
    storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    workDate: asDateString(pick(raw, "workDate", "WorkDate")),
    punchType: asString(pick(raw, "punchType", "PunchType"), "ClockIn"),
    punchTimeUtc: asOptionalString(pick(raw, "punchTimeUtc", "PunchTimeUtc")),
    punchTimeLocal: asOptionalString(pick(raw, "punchTimeLocal", "PunchTimeLocal")),
    status: asString(pick(raw, "status", "Status"), "Normal"),
    statusReason: asOptionalString(pick(raw, "statusReason", "StatusReason", "reason", "Reason")),
  };
}

export function normalizeHoliday(payload: unknown): AttendanceStoreHoliday {
  const raw = isRecord(payload) ? payload : {};
  return {
    holidayGuid: asString(pick(raw, "holidayGuid", "HolidayGuid", "guid", "Guid")),
    storeCode: asString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    holidayDate: asDateString(pick(raw, "holidayDate", "HolidayDate", "date", "Date")),
    holidayName: asString(pick(raw, "holidayName", "HolidayName", "name", "Name")),
    businessStatus: asString(pick(raw, "businessStatus", "BusinessStatus"), "Open"),
    openTime: asOptionalString(pick(raw, "openTime", "OpenTime")),
    closeTime: asOptionalString(pick(raw, "closeTime", "CloseTime")),
    isPaidHoliday: asBoolean(pick(raw, "isPaidHoliday", "IsPaidHoliday")),
    remark: asOptionalString(pick(raw, "remark", "Remark", "note", "Note")),
  };
}

function normalizeStringArray(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  const items = value.map((item) => asString(item).trim()).filter(Boolean);
  return items.length ? items : undefined;
}

function normalizeHolidaySyncResult(
  payload: unknown,
  fallback: AttendanceHolidaySyncPayload,
): AttendanceHolidaySyncResult {
  const raw = isRecord(payload) ? payload : {};
  const fallbackWindow = buildPublicHolidaySyncWindow(new Date(), fallback.daysAhead);
  const holidays = (
    Array.isArray(payload)
      ? getArray(payload)
      : getArray(pick(raw, "holidays", "Holidays", "items", "Items", "created", "Created"))
  ).map(normalizeHoliday);
  const syncedCount =
    asNumber(pick(raw, "syncedCount", "SyncedCount", "totalCount", "TotalCount"), holidays.length) ||
    holidays.length;

  return {
    storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")) ?? fallback.storeCode,
    jurisdiction:
      normalizeAustralianHolidayJurisdiction(
        asOptionalString(pick(raw, "jurisdiction", "Jurisdiction", "state", "State", "stateCode", "StateCode")),
      ) ?? fallback.jurisdiction,
    fromDate: asDateString(pick(raw, "fromDate", "FromDate")) || fallback.fromDate || fallbackWindow.fromDate,
    toDate: asDateString(pick(raw, "toDate", "ToDate")) || fallback.toDate || fallbackWindow.toDate,
    syncedCount,
    createdCount: asNumber(pick(raw, "createdCount", "CreatedCount"), syncedCount),
    updatedCount: asNumber(pick(raw, "updatedCount", "UpdatedCount"), 0),
    skippedCount: asNumber(pick(raw, "skippedCount", "SkippedCount"), 0),
    holidays,
    skippedStores: normalizeStringArray(pick(raw, "skippedStores", "SkippedStores")),
    syncedAt: asOptionalString(pick(raw, "syncedAt", "SyncedAt")),
  };
}

function normalizeToday(payload: unknown): AttendanceToday {
  const raw = isRecord(payload) ? payload : {};
  const punches = getArray(pick(raw, "punches", "Punches")).map(normalizePunch);
  const holidays = getArray(pick(raw, "holidays", "Holidays")).map(normalizeHoliday);
  const primaryHoliday = holidays[0];
  const hasClockIn = punches.some((item) => item.punchType === "ClockIn");
  const hasClockOut = punches.some((item) => item.punchType === "ClockOut");
  const fallbackNextPunchType = hasClockIn && !hasClockOut ? "ClockOut" : "ClockIn";
  return {
    workDate: asDateString(pick(raw, "workDate", "WorkDate")),
    storeTimeZone: asOptionalString(pick(raw, "storeTimeZone", "StoreTimeZone")),
    holidayName: asOptionalString(pick(raw, "holidayName", "HolidayName")) ?? primaryHoliday?.holidayName,
    holidayBusinessStatus:
      asOptionalString(pick(raw, "holidayBusinessStatus", "HolidayBusinessStatus")) ?? primaryHoliday?.businessStatus,
    holidays,
    schedules: getArray(pick(raw, "schedules", "Schedules")).map(normalizeSchedule),
    punches,
    nextPunchType: asString(pick(raw, "nextPunchType", "NextPunchType"), fallbackNextPunchType) as AttendancePunchType,
    canClockIn: asBoolean(pick(raw, "canClockIn", "CanClockIn"), !hasClockIn),
    canClockOut: asBoolean(pick(raw, "canClockOut", "CanClockOut"), hasClockIn && !hasClockOut),
  };
}

function normalizeWeekDay(raw: ApiRecord): AttendanceWeekDay {
  return {
    workDate: asDateString(pick(raw, "workDate", "WorkDate", "date", "Date")),
    dayOfWeek: asNumber(pick(raw, "dayOfWeek", "DayOfWeek"), 0),
    holidayName: asOptionalString(pick(raw, "holidayName", "HolidayName")),
    holidayBusinessStatus: asOptionalString(pick(raw, "holidayBusinessStatus", "HolidayBusinessStatus")),
    schedules: getArray(pick(raw, "schedules", "Schedules")).map(normalizeSchedule),
  };
}

function normalizeWeek(payload: unknown, fallbackWeekStart?: string): AttendanceWeek {
  const raw = isRecord(payload) ? payload : {};
  const rawDays = getArray(pick(raw, "days", "Days"));
  const rawSchedules = Array.isArray(payload) ? getArray(payload).map(normalizeSchedule) : [];
  if (!rawDays.length && (rawSchedules.length || fallbackWeekStart)) {
    const dates = rawSchedules.map((item) => item.workDate).filter(Boolean).sort();
    const weekStart = fallbackWeekStart ?? dates[0] ?? "";
    const grouped = rawSchedules.reduce<Record<string, AttendanceSchedule[]>>((current, schedule) => {
      const key = schedule.workDate;
      current[key] = [...(current[key] ?? []), schedule];
      return current;
    }, {});
    const days = Array.from({ length: 7 }).map((_, index) => {
      const workDate = weekStart ? addDays(weekStart, index) : dates[index] ?? "";
      return {
        workDate,
        dayOfWeek: index,
        schedules: grouped[workDate] ?? grouped[`${workDate}T00:00:00`] ?? [],
      };
    });
    return {
      weekStart,
      weekEnd: days[6]?.workDate ?? (weekStart ? addDays(weekStart, 6) : dates[dates.length - 1] ?? ""),
      days,
    };
  }
  return {
    weekStart: asDateString(pick(raw, "weekStart", "WeekStart")),
    weekEnd: asDateString(pick(raw, "weekEnd", "WeekEnd")),
    days: rawDays.length ? rawDays.map(normalizeWeekDay) : getArray(payload).map(normalizeWeekDay),
  };
}

function normalizeAvailability(raw: ApiRecord): AttendanceAvailability {
  return {
    availabilityGuid: asString(pick(raw, "availabilityGuid", "AvailabilityGuid", "guid", "Guid")),
    storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    workDate: asDateString(pick(raw, "workDate", "WorkDate", "availableDate", "AvailableDate")),
    startTime: asString(pick(raw, "startTime", "StartTime")),
    endTime: asString(pick(raw, "endTime", "EndTime")),
    note: asOptionalString(pick(raw, "note", "Note", "remark", "Remark")),
    status: asString(pick(raw, "status", "Status"), "Submitted"),
  };
}

function normalizeLeaveRequest(raw: ApiRecord): AttendanceLeaveRequest {
  return {
    leaveGuid: asString(pick(raw, "leaveGuid", "LeaveGuid", "guid", "Guid")),
    storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    leaveType: asString(pick(raw, "leaveType", "LeaveType"), "AnnualLeave"),
    startDate: asDateString(pick(raw, "startDate", "StartDate")),
    endDate: asDateString(pick(raw, "endDate", "EndDate")),
    startTime: asOptionalString(pick(raw, "startTime", "StartTime")),
    endTime: asOptionalString(pick(raw, "endTime", "EndTime")),
    reason: asOptionalString(pick(raw, "reason", "Reason")),
    attachmentUrl: asOptionalString(pick(raw, "attachmentUrl", "AttachmentUrl")),
    status: asString(pick(raw, "status", "Status"), "Pending"),
    submittedAt: asOptionalString(pick(raw, "submittedAt", "SubmittedAt", "createdAt", "CreatedAt")),
  };
}

function normalizeDirectUploadSignature(payload: unknown): AttendanceDirectUploadSignature {
  const data = isRecord(payload) ? payload : {};
  const headersValue = pick(data, "headers", "Headers");
  const headers =
    headersValue && typeof headersValue === "object"
      ? Object.fromEntries(
          Object.entries(headersValue as Record<string, unknown>).map(([key, value]) => [
            key,
            typeof value === "string" ? value : String(value ?? ""),
          ])
        )
      : {};

  return {
    url: asString(pick(data, "url", "Url")),
    objectKey: asString(pick(data, "objectKey", "ObjectKey")),
    headers,
  };
}

function normalizeApproval(raw: ApiRecord): AttendanceApproval {
  const sourceType = asString(pick(raw, "sourceType", "SourceType"), "Punch");
  const status = asString(pick(raw, "status", "Status"), "Pending");
  return {
    approvalGuid: asString(pick(raw, "approvalGuid", "ApprovalGuid", "guid", "Guid")),
    sourceGuid: asString(pick(raw, "sourceGuid", "SourceGuid", "punchGuid", "PunchGuid", "leaveGuid", "LeaveGuid")),
    sourceType,
    employeeName: asOptionalString(pick(raw, "employeeName", "EmployeeName", "userName", "UserName")),
    storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    workDate: asOptionalString(pick(raw, "workDate", "WorkDate", "startDate", "StartDate")),
    title: asString(pick(raw, "title", "Title"), sourceType),
    detail: asOptionalString(pick(raw, "detail", "Detail", "reason", "Reason", "statusReason", "StatusReason")),
    status,
    submittedAt: asOptionalString(pick(raw, "submittedAt", "SubmittedAt", "createdAt", "CreatedAt")),
  };
}

function sanitizePayload<T extends Record<string, unknown>>(payload: T): T {
  return Object.fromEntries(
    Object.entries(payload).map(([key, value]) => [
      key,
      typeof value === "string" ? value.trim() || undefined : value,
    ])
  ) as T;
}

function weekStartFromDate(value: string) {
  const parsed = parseDateOnly(value);
  if (!parsed) {
    return value;
  }
  const diff = (parsed.getDay() + 6) % 7;
  parsed.setDate(parsed.getDate() - diff);
  return formatDateOnly(parsed);
}

function toCreateAvailabilityPayload(payload: AttendanceAvailabilityPayload) {
  return {
    storeCode: payload.storeCode,
    weekStartDate: weekStartFromDate(payload.workDate),
    segments: [
      {
        availableDate: payload.workDate,
        startTime: payload.startTime,
        endTime: payload.endTime,
        remark: payload.note,
      },
    ],
  };
}

function toUpdateAvailabilityPayload(payload: AttendanceAvailabilityPayload) {
  return {
    availableDate: payload.workDate,
    startTime: payload.startTime,
    endTime: payload.endTime,
    remark: payload.note,
  };
}

function toCreateSchedulePayload(payload: AttendanceSchedulePayload) {
  return sanitizePayload({
    storeCode: payload.storeCode,
    userGuid: payload.userGuid,
    workDate: payload.workDate,
    startTime: payload.startTime,
    endTime: payload.endTime,
    status: payload.status ?? "Draft",
    remark: payload.remark,
  });
}

function toUpdateSchedulePayload(payload: AttendanceScheduleUpdatePayload) {
  return sanitizePayload({
    workDate: payload.workDate,
    startTime: payload.startTime,
    endTime: payload.endTime,
    status: payload.status,
    remark: payload.remark,
  });
}

function toHolidayPayload(payload: AttendanceStoreHolidayPayload) {
  return sanitizePayload({
    storeCode: payload.storeCode,
    holidayDate: payload.holidayDate,
    holidayName: payload.holidayName,
    businessStatus: payload.businessStatus,
    openTime: payload.openTime,
    closeTime: payload.closeTime,
    isPaidHoliday: payload.isPaidHoliday,
    remark: payload.remark,
  });
}

function toHolidaySyncPayload(payload: AttendanceHolidaySyncPayload) {
  const storeCode = payload.storeCode?.trim();
  const jurisdiction =
    normalizeAustralianHolidayJurisdiction(payload.jurisdiction) ??
    resolveAustralianHolidayJurisdiction(payload.postcode) ??
    undefined;

  if (!storeCode) {
    throw new Error("Store code is required to sync public holidays.");
  }

  const range =
    payload.fromDate && payload.toDate
      ? {
          fromDate: payload.fromDate,
          toDate: payload.toDate,
          daysAhead: payload.daysAhead,
        }
      : buildPublicHolidaySyncWindow(new Date(), payload.daysAhead);

  return sanitizePayload({
    storeCode,
    postcode: payload.postcode,
    jurisdiction,
    stateCode: jurisdiction,
    fromDate: range.fromDate,
    toDate: range.toDate,
    daysAhead: range.daysAhead,
  });
}

function toLeaveRequestPayload(payload: AttendanceLeaveRequestPayload) {
  return sanitizePayload({
    userGuid: payload.userGuid,
    storeCode: payload.storeCode,
    leaveType: payload.leaveType,
    startDate: payload.startDate,
    endDate: payload.endDate,
    startTime: payload.startTime,
    endTime: payload.endTime,
    reason: payload.reason,
    attachmentUrl: payload.attachmentUrl,
  });
}

export async function getMyAttendanceToday(storeCode?: string, workDate?: string): Promise<AttendanceToday> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/my/today`, { params: { storeCode, workDate } });
  const today = normalizeToday(response.data);
  return {
    ...today,
    schedules: today.schedules.filter((item) => item.status.toLowerCase() === "active"),
  };
}

export async function getMyAttendanceWeek(storeCode?: string, weekStartDate?: string): Promise<AttendanceWeek> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/my/week`, { params: { storeCode, weekStartDate } });
  const week = normalizeWeek(response.data, weekStartDate);
  return {
    ...week,
    days: week.days.map((day) => ({
      ...day,
      schedules: day.schedules.filter((item) => item.status.toLowerCase() === "active"),
    })),
  };
}

export async function getMyAvailability(storeCode?: string, weekStartDate?: string): Promise<AttendanceAvailability[]> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/my/availability`, { params: { storeCode, weekStartDate } });
  return getArray(response.data).map(normalizeAvailability);
}

export async function createAvailability(payload: AttendanceAvailabilityPayload): Promise<AttendanceAvailability> {
  const response = await apiClient.post(`${ATTENDANCE_BASE}/my/availability`, sanitizePayload(toCreateAvailabilityPayload(payload)));
  const rows = getArray(response.data);
  return normalizeAvailability(rows[0] ?? {});
}

export async function updateAvailability(
  availabilityGuid: string,
  payload: AttendanceAvailabilityPayload
): Promise<AttendanceAvailability> {
  const response = await apiClient.put(
    `${ATTENDANCE_BASE}/my/availability/${encodeURIComponent(availabilityGuid)}`,
    sanitizePayload(toUpdateAvailabilityPayload(payload))
  );
  return normalizeAvailability(isRecord(response.data) ? response.data : {});
}

export async function cancelAvailability(availabilityGuid: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/my/availability/${encodeURIComponent(availabilityGuid)}/cancel`);
}

export async function punchAttendance(
  payload: AttendancePunchPayload,
): Promise<AttendancePunch> {
  const response = await apiClient.post(
    `${ATTENDANCE_BASE}/punch`,
    sanitizePayload({ ...payload }),
  );
  return normalizePunch(isRecord(response.data) ? response.data : {});
}

export async function getMyLeaveRequests(): Promise<AttendanceLeaveRequest[]> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/my/leave-requests`);
  return getArray(response.data).map(normalizeLeaveRequest);
}

export async function createLeaveRequest(payload: AttendanceLeaveRequestPayload): Promise<AttendanceLeaveRequest> {
  const response = await apiClient.post(`${ATTENDANCE_BASE}/my/leave-requests`, toLeaveRequestPayload(payload));
  return normalizeLeaveRequest(isRecord(response.data) ? response.data : {});
}

export async function createManagedLeaveRequest(payload: AttendanceLeaveRequestPayload): Promise<AttendanceLeaveRequest> {
  const response = await apiClient.post(`${ATTENDANCE_BASE}/managed/leave-requests`, toLeaveRequestPayload(payload));
  return normalizeLeaveRequest(isRecord(response.data) ? response.data : {});
}

export async function cancelLeaveRequest(leaveGuid: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/my/leave-requests/${encodeURIComponent(leaveGuid)}/cancel`);
}

export async function getAttendanceLeaveAttachmentUploadSignature(
  request: AttendanceDirectUploadRequest
): Promise<AttendanceDirectUploadSignature> {
  const response = await apiClient.post(`${ATTENDANCE_BASE}/leave-attachments/upload-signature`, sanitizePayload({ ...request }));
  return normalizeDirectUploadSignature(response.data);
}

export async function getPendingApprovals(storeCode?: string): Promise<AttendanceApproval[]> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/approvals/pending`, { params: { storeCode } });
  return getArray(response.data).map(normalizeApproval);
}

export async function approveAttendanceApproval(payload: AttendanceApprovalPayload): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/approvals/${encodeURIComponent(payload.approvalGuid)}/approve`, {
    reviewRemark: payload.remark?.trim() || undefined,
  });
}

export async function rejectAttendanceApproval(payload: AttendanceApprovalPayload): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/approvals/${encodeURIComponent(payload.approvalGuid)}/reject`, {
    reviewRemark: payload.remark?.trim() || undefined,
  });
}

export async function getAttendanceSchedulesWeek(params: AttendanceScheduleWeekParams): Promise<AttendanceSchedule[]> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/schedules/week`, {
    params: {
      storeCode: params.storeCode,
      weekStartDate: params.weekStartDate,
    },
  });
  return getArray(response.data).map(normalizeSchedule);
}

export async function getAttendanceHolidays(params: AttendanceHolidayQueryParams = {}): Promise<AttendanceStoreHoliday[]> {
  const response = await apiClient.get(`${ATTENDANCE_BASE}/holidays`, {
    params: {
      storeCode: params.storeCode,
      fromDate: params.fromDate,
      toDate: params.toDate,
    },
  });
  return getArray(response.data).map(normalizeHoliday);
}

export async function syncAttendanceHolidays(payload: AttendanceHolidaySyncPayload): Promise<AttendanceHolidaySyncResult> {
  const requestPayload = toHolidaySyncPayload(payload);
  const response = await apiClient.post(`${ATTENDANCE_BASE}/holidays/sync`, requestPayload);
  return normalizeHolidaySyncResult(response.data, requestPayload);
}

export async function createAttendanceHoliday(payload: AttendanceStoreHolidayPayload): Promise<AttendanceStoreHoliday> {
  const response = await apiClient.post(`${ATTENDANCE_BASE}/holidays`, toHolidayPayload(payload));
  return normalizeHoliday(response.data);
}

export async function updateAttendanceHoliday(
  holidayGuid: string,
  payload: AttendanceStoreHolidayPayload
): Promise<AttendanceStoreHoliday> {
  const response = await apiClient.put(
    `${ATTENDANCE_BASE}/holidays/${encodeURIComponent(holidayGuid)}`,
    toHolidayPayload(payload)
  );
  return normalizeHoliday(response.data);
}

export async function deleteAttendanceHoliday(holidayGuid: string): Promise<void> {
  await apiClient.delete(`${ATTENDANCE_BASE}/holidays/${encodeURIComponent(holidayGuid)}`);
}

export async function createAttendanceSchedule(payload: AttendanceSchedulePayload): Promise<AttendanceSchedule> {
  const response = await apiClient.post(`${ATTENDANCE_BASE}/schedules`, toCreateSchedulePayload(payload));
  return normalizeSchedule(isRecord(response.data) ? response.data : {});
}

export async function updateAttendanceSchedule(
  scheduleGuid: string,
  payload: AttendanceScheduleUpdatePayload
): Promise<AttendanceSchedule> {
  const response = await apiClient.put(
    `${ATTENDANCE_BASE}/schedules/${encodeURIComponent(scheduleGuid)}`,
    toUpdateSchedulePayload(payload)
  );
  return normalizeSchedule(isRecord(response.data) ? response.data : {});
}

export async function deleteAttendanceSchedule(scheduleGuid: string): Promise<void> {
  await apiClient.delete(`${ATTENDANCE_BASE}/schedules/${encodeURIComponent(scheduleGuid)}`);
}

export async function publishAttendanceSchedulesWeek(payload: AttendancePublishWeekPayload): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/schedules/publish-week`, sanitizePayload({ ...payload }));
}
