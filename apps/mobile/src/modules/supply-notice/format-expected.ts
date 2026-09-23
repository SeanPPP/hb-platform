import type { SupplyExpectedPrecision } from "./types";

export interface SupplyExpectedLike {
  expectedFrom: string | null;
  expectedTo: string | null;
  expectedPrecision: SupplyExpectedPrecision;
  isOverdue: boolean;
}

type Translate = (key: string, params?: Record<string, unknown>) => string;

function parseDate(value: string | null): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value ?? "");
  if (!match) {
    return null;
  }
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

/**
 * 把“预计恢复订货”的时间段按录入精度还原成人话；不依赖 dayjs，日期按纯业务日期解析，不受设备时区影响。
 * 某日 → 10月5日；范围 → 10月5日 – 10月10日；某月 → 10 月（跨年带年份）；待定 / 逾期 → 对应提示。
 */
export function formatSupplyExpected(value: SupplyExpectedLike, t: Translate, thisYear = new Date().getFullYear()): string {
  if (value.isOverdue) {
    return t("expectedOverdue");
  }
  const from = parseDate(value.expectedFrom);
  if (!from) {
    return t("expectedUnknown");
  }
  const formatDay = (date: { year: number; month: number; day: number }) =>
    date.year === thisYear ? `${date.month}月${date.day}日` : `${date.year}年${date.month}月${date.day}日`;

  switch (value.expectedPrecision) {
    case "Month":
      return from.year === thisYear
        ? t("expectedMonth", { month: from.month })
        : t("expectedMonthWithYear", { year: from.year, month: from.month });
    case "Range": {
      const to = parseDate(value.expectedTo);
      if (to && (to.year !== from.year || to.month !== from.month || to.day !== from.day)) {
        return t("expectedRange", { from: formatDay(from), to: formatDay(to) });
      }
      return formatDay(from);
    }
    case "Day":
      return formatDay(from);
    default:
      return t("expectedUnknown");
  }
}
