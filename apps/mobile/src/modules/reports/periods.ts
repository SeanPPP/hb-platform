export type RevenuePeriodMode = "day" | "week" | "month";
export type RevenueCompareMode =
  | "previousPeriod"
  | "lastYearSameWeekday"
  | "lastYearIsoWeek"
  | "lastYearSameMonth";

export interface RevenuePeriod {
  mode: RevenuePeriodMode;
  startDate: string;
  endDate: string;
}

const DAY_MS = 24 * 60 * 60 * 1000;

function pad(value: number) {
  return String(value).padStart(2, "0");
}

export function formatDateKey(date: Date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function parseDateKey(value: string) {
  const [year, month, day] = value.split("-").map(Number);
  if (!year || !month || !day) {
    throw new Error(`Invalid date key: ${value}`);
  }
  return new Date(year, month - 1, day);
}

function addDays(date: Date, days: number) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

function addMonths(date: Date, months: number) {
  return new Date(date.getFullYear(), date.getMonth() + months, date.getDate());
}

function startOfWeek(date: Date) {
  const day = date.getDay();
  const offset = day === 0 ? -6 : 1 - day;
  return addDays(date, offset);
}

function getIsoWeekInfo(date: Date) {
  const normalized = new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const day = (normalized.getDay() + 6) % 7;
  normalized.setDate(normalized.getDate() + 3 - day);
  const weekYear = normalized.getFullYear();
  const weekOne = new Date(weekYear, 0, 4);
  const week =
    1 +
    Math.round(
      ((normalized.getTime() - weekOne.getTime()) / DAY_MS - 3 + ((weekOne.getDay() + 6) % 7)) / 7
    );
  return { weekYear, week, weekday: day + 1 };
}

function dateFromIsoWeek(weekYear: number, week: number, weekday: number) {
  const weekOne = new Date(weekYear, 0, 4);
  const weekOneStart = startOfWeek(weekOne);
  return addDays(weekOneStart, (week - 1) * 7 + weekday - 1);
}

function endOfMonth(date: Date) {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
}

function daysBetween(startDate: string, endDate: string) {
  return Math.round((parseDateKey(endDate).getTime() - parseDateKey(startDate).getTime()) / DAY_MS) + 1;
}

function shiftPeriod(period: RevenuePeriod, days: number): RevenuePeriod {
  return {
    mode: period.mode,
    startDate: formatDateKey(addDays(parseDateKey(period.startDate), days)),
    endDate: formatDateKey(addDays(parseDateKey(period.endDate), days)),
  };
}

export function getDefaultRevenuePeriod(mode: RevenuePeriodMode, anchor = new Date()): RevenuePeriod {
  if (mode === "day") {
    return { mode, startDate: formatDateKey(anchor), endDate: formatDateKey(anchor) };
  }

  if (mode === "week") {
    const start = startOfWeek(anchor);
    return { mode, startDate: formatDateKey(start), endDate: formatDateKey(addDays(start, 6)) };
  }

  const start = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
  return { mode, startDate: formatDateKey(start), endDate: formatDateKey(endOfMonth(start)) };
}

export function getPreviousRevenuePeriod(period: RevenuePeriod): RevenuePeriod {
  if (period.mode === "month") {
    const start = addMonths(parseDateKey(period.startDate), -1);
    return { mode: period.mode, startDate: formatDateKey(start), endDate: formatDateKey(endOfMonth(start)) };
  }

  return shiftPeriod(period, -daysBetween(period.startDate, period.endDate));
}

export function getNextRevenuePeriod(period: RevenuePeriod): RevenuePeriod {
  if (period.mode === "month") {
    const start = addMonths(parseDateKey(period.startDate), 1);
    return { mode: period.mode, startDate: formatDateKey(start), endDate: formatDateKey(endOfMonth(start)) };
  }

  return shiftPeriod(period, daysBetween(period.startDate, period.endDate));
}

export function getYesterdayRevenuePeriod(anchor = new Date()): RevenuePeriod {
  const date = addDays(anchor, -1);
  return { mode: "day", startDate: formatDateKey(date), endDate: formatDateKey(date) };
}

export function getLastWeekRevenuePeriod(anchor = new Date()): RevenuePeriod {
  const start = addDays(startOfWeek(anchor), -7);
  return { mode: "week", startDate: formatDateKey(start), endDate: formatDateKey(addDays(start, 6)) };
}

export function getLastMonthRevenuePeriod(anchor = new Date()): RevenuePeriod {
  const start = new Date(anchor.getFullYear(), anchor.getMonth() - 1, 1);
  return { mode: "month", startDate: formatDateKey(start), endDate: formatDateKey(endOfMonth(start)) };
}

export function getLastYearSameWeekdayPeriod(period: RevenuePeriod): RevenuePeriod {
  // 日报同比按去年同 ISO 周、同星期几取数。
  return getLastYearIsoWeekPeriod(period);
}

export function getLastYearIsoWeekPeriod(period: RevenuePeriod): RevenuePeriod {
  const { weekYear, week, weekday } = getIsoWeekInfo(parseDateKey(period.startDate));
  const weekStart = dateFromIsoWeek(weekYear - 1, week, weekday);
  return {
    mode: period.mode,
    startDate: formatDateKey(weekStart),
    endDate: formatDateKey(addDays(weekStart, daysBetween(period.startDate, period.endDate) - 1)),
  };
}

export function getLastYearSameMonthPeriod(period: RevenuePeriod): RevenuePeriod {
  const start = parseDateKey(period.startDate);
  const compareStart = new Date(start.getFullYear() - 1, start.getMonth(), 1);
  return {
    mode: period.mode,
    startDate: formatDateKey(compareStart),
    endDate: formatDateKey(endOfMonth(compareStart)),
  };
}

export function getCompareRevenuePeriod(period: RevenuePeriod, compareMode: RevenueCompareMode) {
  if (compareMode === "lastYearSameWeekday") {
    return getLastYearSameWeekdayPeriod(period);
  }
  if (compareMode === "lastYearIsoWeek") {
    return getLastYearIsoWeekPeriod(period);
  }
  if (compareMode === "lastYearSameMonth") {
    return getLastYearSameMonthPeriod(period);
  }
  return getPreviousRevenuePeriod(period);
}
