import type { InstallmentDateFilter } from "./installment-models";

import {
  businessDayUtcRange,
  resolveBusinessTimeZone,
} from "@hb/pos-sync/features/sync-history/business-day-range";

export type InstallmentHistoryDateRange = Readonly<{
  createdFromIso: string | null;
  createdToIso: string | null;
}>;

export function isValidInstallmentDateFilter(
  filter: InstallmentDateFilter,
): boolean {
  if (
    filter.preset === "all" ||
    filter.preset === "today" ||
    filter.preset === "last7" ||
    filter.preset === "last30"
  ) {
    return filter.fromDate === null && filter.toDate === null;
  }
  if (filter.preset !== "custom") return false;
  const fromDate = parseCalendarDate(filter.fromDate);
  const toDate = parseCalendarDate(filter.toDate);
  return fromDate !== null && toDate !== null && fromDate <= toDate;
}

/** 将可信筛选转换为 Hbpos 使用的 UTC 闭区间；非法输入一律 fail closed。 */
export function resolveInstallmentDateRange(
  filter: InstallmentDateFilter,
  now: Date,
  businessTimeZone: string,
): InstallmentHistoryDateRange | null {
  if (!isValidInstallmentDateFilter(filter)) return null;
  if (filter.preset === "all") {
    return Object.freeze({ createdFromIso: null, createdToIso: null });
  }
  if (!Number.isFinite(now.getTime())) return null;
  const timeZone = resolveBusinessTimeZone(businessTimeZone);
  if (!timeZone) return null;

  let fromDate: string;
  let toDate: string;
  if (filter.preset === "custom") {
    fromDate = filter.fromDate!;
    toDate = filter.toDate!;
  } else {
    const today = dateInTimeZone(now, timeZone);
    if (!today) return null;
    const daysBefore =
      filter.preset === "today" ? 0 : filter.preset === "last7" ? 6 : 29;
    fromDate = addCalendarDays(today, -daysBefore);
    toDate = today;
  }

  const range = businessDayUtcRange(fromDate, toDate, timeZone);
  return range
    ? Object.freeze({
        createdFromIso: range.dateFromIso,
        createdToIso: range.dateToIso,
      })
    : null;
}

function dateInTimeZone(now: Date, timeZone: string): string | null {
  const parts = new Map(
    new Intl.DateTimeFormat("en-CA", {
      calendar: "gregory",
      day: "2-digit",
      month: "2-digit",
      numberingSystem: "latn",
      timeZone,
      year: "numeric",
    })
      .formatToParts(now)
      .filter(
        (part) =>
          part.type === "year" ||
          part.type === "month" ||
          part.type === "day",
      )
      .map((part) => [part.type, part.value]),
  );
  const value =
    `${parts.get("year") ?? ""}-` +
    `${parts.get("month") ?? ""}-` +
    `${parts.get("day") ?? ""}`;
  return parseCalendarDate(value);
}

function addCalendarDays(value: string, amount: number): string {
  const [year, month, day] = value.split("-").map(Number);
  const date = new Date(0);
  date.setUTCFullYear(year!, month! - 1, day! + amount);
  date.setUTCHours(0, 0, 0, 0);
  return [
    String(date.getUTCFullYear()).padStart(4, "0"),
    String(date.getUTCMonth() + 1).padStart(2, "0"),
    String(date.getUTCDate()).padStart(2, "0"),
  ].join("-");
}

function parseCalendarDate(value: string | null): string | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/u.test(value)) return null;
  const [year, month, day] = value.split("-").map(Number);
  const candidate = new Date(0);
  candidate.setUTCFullYear(year!, month! - 1, day!);
  candidate.setUTCHours(0, 0, 0, 0);
  return candidate.getUTCFullYear() === year &&
    candidate.getUTCMonth() === month! - 1 &&
    candidate.getUTCDate() === day
    ? value
    : null;
}
