import {
  formatDateKey,
  getCompareRevenuePeriod,
  getDefaultRevenuePeriod,
  getLastMonthRevenuePeriod,
  getLastWeekRevenuePeriod,
  getYesterdayRevenuePeriod,
  parseDateKey,
  type RevenueCompareMode,
  type RevenuePeriodMode,
} from "../reports/periods";

export type ProductReportQuickRangeKey =
  | "today"
  | "yesterday"
  | "thisWeek"
  | "lastWeek"
  | "thisMonth"
  | "lastMonth";

export interface ProductReportDateRange {
  key: ProductReportQuickRangeKey | "custom";
  mode: RevenuePeriodMode;
  startDate: string;
  endDate: string;
}

const DATE_KEY_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

export function getDefaultProductReportRange(anchor = new Date()): ProductReportDateRange {
  const period = getDefaultRevenuePeriod("day", anchor);
  return { key: "today", ...period };
}

export function getProductReportQuickRange(
  key: ProductReportQuickRangeKey,
  anchor = new Date()
): ProductReportDateRange {
  if (key === "today") {
    return getDefaultProductReportRange(anchor);
  }

  if (key === "yesterday") {
    return { key, ...getYesterdayRevenuePeriod(anchor) };
  }

  if (key === "thisWeek") {
    return { key, ...getDefaultRevenuePeriod("week", anchor) };
  }

  if (key === "lastWeek") {
    return { key, ...getLastWeekRevenuePeriod(anchor) };
  }

  if (key === "thisMonth") {
    return { key, ...getDefaultRevenuePeriod("month", anchor) };
  }

  return { key, ...getLastMonthRevenuePeriod(anchor) };
}

export function getProductReportCompareRange(range: ProductReportDateRange) {
  const compareMode: RevenueCompareMode =
    range.mode === "month"
      ? "lastYearSameMonth"
      : range.mode === "week"
        ? "lastYearIsoWeek"
        : "lastYearSameWeekday";
  return getCompareRevenuePeriod(range, compareMode);
}

export function getDashboardCompareMode(range: ProductReportDateRange): "ByDate" | "ByWeek" {
  return range.mode === "month" ? "ByDate" : "ByWeek";
}

function isExactDateKey(value: string) {
  if (!DATE_KEY_PATTERN.test(value)) {
    return false;
  }

  const parsed = parseDateKey(value);
  return formatDateKey(parsed) === value;
}

export function isValidProductReportDateRange(startDate: string, endDate: string) {
  if (!isExactDateKey(startDate) || !isExactDateKey(endDate)) {
    return false;
  }

  return parseDateKey(startDate).getTime() <= parseDateKey(endDate).getTime();
}

export function getCustomProductReportRange(
  startDate: string,
  endDate: string
): ProductReportDateRange | null {
  if (!isValidProductReportDateRange(startDate, endDate)) {
    return null;
  }

  return {
    key: "custom",
    mode: startDate === endDate ? "day" : "day",
    startDate,
    endDate,
  };
}
