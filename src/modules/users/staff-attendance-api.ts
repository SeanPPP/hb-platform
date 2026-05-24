import { isAxiosError } from "axios";
import { apiClient } from "@/shared/api/client";
import type {
  AttendanceSchedule,
  AttendanceWeek,
  AttendanceWeekDay,
} from "@/modules/attendance/types";
import type {
  StaffAttendanceRecord,
  StaffAttendanceRecordQueryParams,
  StaffAttendanceRecordType,
  StaffAttendanceScheduleQueryParams,
} from "@/modules/users/staff-attendance-types";

type ApiRecord = Record<string, unknown>;

const ATTENDANCE_BASE = "/react/v1/attendance";

export class StaffAttendanceEndpointUnavailableError extends Error {
  endpoint: string;

  constructor(endpoint: string) {
    super(`Attendance endpoint unavailable: ${endpoint}`);
    this.name = "StaffAttendanceEndpointUnavailableError";
    this.endpoint = endpoint;
  }
}

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

function asBoolean(value: unknown, fallback = false): boolean {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") {
    const normalized = value.toLowerCase();
    if (normalized === "true") {
      return true;
    }
    if (normalized === "false") {
      return false;
    }
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

function asDateString(value: unknown): string {
  const normalized = asString(value);
  return normalized.includes("T") ? normalized.slice(0, 10) : normalized;
}

function parseDateOnly(value: string) {
  const normalized = value.slice(0, 10);
  const parsed = new Date(`${normalized}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

function formatDateOnly(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function addDays(value: string, days: number) {
  const parsed = parseDateOnly(value);
  if (!parsed) {
    return value;
  }
  parsed.setDate(parsed.getDate() + days);
  return formatDateOnly(parsed);
}

function getCurrentWeekStartDate() {
  const date = new Date();
  const day = date.getDay();
  const diff = (day + 6) % 7;
  date.setDate(date.getDate() - diff);
  date.setHours(0, 0, 0, 0);
  return formatDateOnly(date);
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
    status: asString(pick(raw, "status", "Status"), "Active"),
    remark: asOptionalString(pick(raw, "remark", "Remark", "note", "Note")),
    isMine: asBoolean(pick(raw, "isMine", "IsMine", "mine", "Mine"), true),
    holidayName: asOptionalString(pick(raw, "holidayName", "HolidayName")),
    holidayBusinessStatus: asOptionalString(pick(raw, "holidayBusinessStatus", "HolidayBusinessStatus")),
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
    const weekStart = fallbackWeekStart ?? rawSchedules[0]?.workDate ?? getCurrentWeekStartDate();
    const grouped = rawSchedules.reduce<Record<string, AttendanceSchedule[]>>((current, schedule) => {
      const key = schedule.workDate;
      current[key] = [...(current[key] ?? []), schedule];
      return current;
    }, {});

    const days = Array.from({ length: 7 }).map((_, index) => {
      const workDate = addDays(weekStart, index);
      return {
        workDate,
        dayOfWeek: index,
        schedules: grouped[workDate] ?? grouped[`${workDate}T00:00:00`] ?? [],
      };
    });

    return {
      weekStart,
      weekEnd: days[6]?.workDate ?? addDays(weekStart, 6),
      days,
    };
  }

  return {
    weekStart: asDateString(pick(raw, "weekStart", "WeekStart")) || (fallbackWeekStart ?? getCurrentWeekStartDate()),
    weekEnd: asDateString(pick(raw, "weekEnd", "WeekEnd")) || addDays(fallbackWeekStart ?? getCurrentWeekStartDate(), 6),
    days: rawDays.length
      ? rawDays.map(normalizeWeekDay)
      : Array.from({ length: 7 }).map((_, index) => ({
          workDate: addDays(fallbackWeekStart ?? getCurrentWeekStartDate(), index),
          dayOfWeek: index,
          schedules: [],
        })),
  };
}

function resolveRecordType(raw: ApiRecord): StaffAttendanceRecordType {
  const explicitType = asOptionalString(pick(raw, "recordType", "RecordType", "type", "Type"));
  if (explicitType) {
    return explicitType;
  }
  if (pick(raw, "punchType", "PunchType", "punchGuid", "PunchGuid") !== undefined) {
    return "Punch";
  }
  if (pick(raw, "leaveType", "LeaveType", "leaveGuid", "LeaveGuid") !== undefined) {
    return "Leave";
  }
  if (pick(raw, "approvalGuid", "ApprovalGuid", "sourceType", "SourceType") !== undefined) {
    return "Approval";
  }
  return "Punch";
}

function normalizeAttendanceRecord(type: StaffAttendanceRecordType, raw: ApiRecord): StaffAttendanceRecord {
  if (type === "Leave") {
    return {
      recordGuid: asString(pick(raw, "leaveGuid", "LeaveGuid", "guid", "Guid")),
      type,
      status: asString(pick(raw, "status", "Status"), "Pending"),
      workDate: asDateString(pick(raw, "startDate", "StartDate", "workDate", "WorkDate")),
      submittedAt: asOptionalString(pick(raw, "submittedAt", "SubmittedAt", "createdAt", "CreatedAt")),
      storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
      storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
      detail: asOptionalString(pick(raw, "reason", "Reason", "detail", "Detail")),
      leaveType: asOptionalString(pick(raw, "leaveType", "LeaveType")),
      startTime: asDateString(pick(raw, "startDate", "StartDate")),
      endTime: asDateString(pick(raw, "endDate", "EndDate")),
    };
  }

  if (type === "Approval") {
    return {
      recordGuid: asString(pick(raw, "approvalGuid", "ApprovalGuid", "guid", "Guid")),
      type,
      status: asString(pick(raw, "status", "Status"), "Pending"),
      workDate: asDateString(pick(raw, "workDate", "WorkDate", "startDate", "StartDate")),
      submittedAt: asOptionalString(pick(raw, "submittedAt", "SubmittedAt", "createdAt", "CreatedAt")),
      storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
      storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
      detail: asOptionalString(pick(raw, "detail", "Detail", "reason", "Reason", "statusReason", "StatusReason")),
      sourceType: asOptionalString(pick(raw, "sourceType", "SourceType")),
    };
  }

  return {
    recordGuid: asString(pick(raw, "punchGuid", "PunchGuid", "guid", "Guid")),
    type: "Punch",
    status: asString(pick(raw, "status", "Status"), "Normal"),
    workDate: asDateString(pick(raw, "workDate", "WorkDate")),
    submittedAt: asOptionalString(
      pick(raw, "punchTimeLocal", "PunchTimeLocal", "punchTimeUtc", "PunchTimeUtc", "createdAt", "CreatedAt"),
    ),
    storeCode: asOptionalString(pick(raw, "storeCode", "StoreCode")),
    storeName: asOptionalString(pick(raw, "storeName", "StoreName")),
    detail: asOptionalString(pick(raw, "statusReason", "StatusReason", "reason", "Reason")),
    punchType: asOptionalString(pick(raw, "punchType", "PunchType")),
  };
}

function collectTypedRecordEntries(payload: unknown) {
  if (isRecord(payload)) {
    const typedGroups: Array<{ type: StaffAttendanceRecordType; items: ApiRecord[] }> = [
      {
        type: "Punch",
        items: getArray(pick(payload, "punches", "Punches", "punchRecords", "PunchRecords")),
      },
      {
        type: "Leave",
        items: getArray(pick(payload, "leaves", "Leaves", "leaveRequests", "LeaveRequests")),
      },
      {
        type: "Approval",
        items: getArray(pick(payload, "approvals", "Approvals", "approvalRecords", "ApprovalRecords")),
      },
    ].filter((group) => group.items.length);

    if (typedGroups.length) {
      return typedGroups.flatMap((group) =>
        group.items.map((item) => ({ type: group.type, raw: item })),
      );
    }
  }

  return getArray(payload).map((raw) => ({
    type: resolveRecordType(raw),
    raw,
  }));
}

function toComparableTimestamp(record: StaffAttendanceRecord) {
  const candidate = record.submittedAt ?? record.workDate ?? record.startTime ?? "";
  const parsed = new Date(candidate);
  return Number.isNaN(parsed.getTime()) ? 0 : parsed.getTime();
}

function rethrowUnavailableEndpoint(error: unknown, endpoint: string): never {
  if (isAxiosError(error) && error.response?.status === 404) {
    throw new StaffAttendanceEndpointUnavailableError(endpoint);
  }
  throw error;
}

export async function getStaffAttendanceWeek(
  params: StaffAttendanceScheduleQueryParams,
): Promise<AttendanceWeek> {
  const endpoint =
    `${ATTENDANCE_BASE}/employees/${encodeURIComponent(params.userGuid)}/week`;

  try {
    const response = await apiClient.get(endpoint, {
      params: {
        storeCode: params.storeCode?.trim() || undefined,
        weekStartDate: params.weekStartDate,
      },
    });

    return normalizeWeek(response.data, params.weekStartDate);
  } catch (error) {
    rethrowUnavailableEndpoint(error, endpoint);
  }
}

export async function getStaffAttendanceRecords(
  params: StaffAttendanceRecordQueryParams,
): Promise<StaffAttendanceRecord[]> {
  const endpoint =
    `${ATTENDANCE_BASE}/employees/${encodeURIComponent(params.userGuid)}/records`;

  try {
    const response = await apiClient.get(endpoint, {
      params: {
        storeCode: params.storeCode?.trim() || undefined,
        limit: params.limit ?? 20,
      },
    });

    return collectTypedRecordEntries(response.data)
      .map(({ type, raw }) => normalizeAttendanceRecord(type, raw))
      .filter((item) => Boolean(item.recordGuid || item.workDate))
      .sort((left, right) => toComparableTimestamp(right) - toComparableTimestamp(left))
      .slice(0, params.limit ?? 20);
  } catch (error) {
    rethrowUnavailableEndpoint(error, endpoint);
  }
}
