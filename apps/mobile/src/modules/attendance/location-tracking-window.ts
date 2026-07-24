export const ATTENDANCE_LOCATION_MAX_TRACKING_MS = 10 * 60 * 60 * 1000;

export interface AttendanceLocationTrackingWindow {
  startedAtUtc: string;
  workDate: string;
  storeTimeZone: string;
}

export interface AttendanceLocationBatchDecision {
  shouldStop: boolean;
  capturedAtUtc?: string;
}

const utcTextPattern = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-]\d{2}:\d{2})?$/i;

function utcTimestamp(value?: string) {
  const trimmed = value?.trim();
  if (!trimmed || !utcTextPattern.test(trimmed)) {
    return undefined;
  }
  // 服务端 SQL DateTime UTC 字段可能不带 Z；这里只处理明确的 UTC 语义字段。
  const normalized = /(?:Z|[+-]\d{2}:\d{2})$/i.test(trimmed)
    ? trimmed
    : `${trimmed}Z`;
  const timestamp = Date.parse(normalized);
  return Number.isFinite(timestamp) ? timestamp : undefined;
}

function dateInTimeZone(timestamp: number, timeZone: string) {
  try {
    const parts = new Intl.DateTimeFormat("en-CA", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    }).formatToParts(new Date(timestamp));
    const year = parts.find((part) => part.type === "year")?.value;
    const month = parts.find((part) => part.type === "month")?.value;
    const day = parts.find((part) => part.type === "day")?.value;
    return year && month && day ? `${year}-${month}-${day}` : undefined;
  } catch {
    return undefined;
  }
}

export function evaluateAttendanceLocationBatch(
  window: AttendanceLocationTrackingWindow,
  capturedAtUtcValues: string[],
  nowUtc: string,
): AttendanceLocationBatchDecision {
  const startedAt = utcTimestamp(window.startedAtUtc);
  const now = utcTimestamp(nowUtc);
  const workDate = /^\d{4}-\d{2}-\d{2}$/.test(window.workDate)
    ? window.workDate
    : undefined;
  const startedDate = startedAt === undefined
    ? undefined
    : dateInTimeZone(startedAt, window.storeTimeZone);
  const nowDate = now === undefined
    ? undefined
    : dateInTimeZone(now, window.storeTimeZone);
  if (
    startedAt === undefined
    || now === undefined
    || !workDate
    || startedDate !== workDate
    || !nowDate
    || now < startedAt
  ) {
    return { shouldStop: true, capturedAtUtc: undefined };
  }

  const maximumEnd = startedAt + ATTENDANCE_LOCATION_MAX_TRACKING_MS;
  const shouldStop = now >= maximumEnd || nowDate !== workDate;
  const capturedAtUtc = capturedAtUtcValues
    .map((value, index) => ({
      value,
      index,
      timestamp: utcTimestamp(value),
    }))
    .filter((item): item is { value: string; index: number; timestamp: number } => (
      item.timestamp !== undefined
      && item.timestamp >= startedAt
      && item.timestamp < maximumEnd
      && item.timestamp <= now
      && dateInTimeZone(item.timestamp, window.storeTimeZone) === workDate
    ))
    .sort((left, right) => right.timestamp - left.timestamp || right.index - left.index)
    .at(0)?.value;

  return {
    shouldStop,
    capturedAtUtc,
  };
}
