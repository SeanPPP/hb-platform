import type {
  DailyTrendPoint,
  SeasonalDailySales,
  SeasonalInboundRecord,
  SeasonalRange,
  SeasonalRangeError,
  SeasonalRanges,
  WeeklyTrendPoint,
} from "./types";

/** 与后端 SeasonalProductInsightRules 保持一致。 */
export const SEASON_START_MONTH = 8;
export const SEASONAL_MAX_RANGE_DAYS = 366;
export const SEASONAL_PERMISSION = "SeasonalProductInsights.View";

const DAY_MS = 86_400_000;

// 日期一律按 YYYY-MM-DD 的 UTC 零点换算，避免设备时区让日期偏移一天。
function toUtc(date: string) {
  return Date.parse(`${date.slice(0, 10)}T00:00:00Z`);
}

function fromUtc(ms: number) {
  return new Date(ms).toISOString().slice(0, 10);
}

export function isValidIsoDate(value: string) {
  return (
    /^\d{4}-\d{2}-\d{2}$/.test(value) &&
    Number.isFinite(toUtc(value)) &&
    fromUtc(toUtc(value)) === value
  );
}

export function addDays(date: string, days: number) {
  return fromUtc(toUtc(date) + days * DAY_MS);
}

export function daysBetweenInclusive(range: SeasonalRange) {
  return Math.round((toUtc(range.endDate) - toUtc(range.startDate)) / DAY_MS) + 1;
}

/** 季节默认从 8 月 1 日开始；1–7 月仍属于上一年 8 月起的季节。 */
export function seasonStart(today: string) {
  const year = Number(today.slice(0, 4));
  const month = Number(today.slice(5, 7));
  const seasonYear = month >= SEASON_START_MONTH ? year : year - 1;
  return `${seasonYear}-${String(SEASON_START_MONTH).padStart(2, "0")}-01`;
}

export function validateSeasonalRange(range: SeasonalRange): SeasonalRangeError | null {
  if (!isValidIsoDate(range.startDate) || !isValidIsoDate(range.endDate)) return "format";
  if (range.startDate > range.endDate) return "order";
  if (daysBetweenInclusive(range) > SEASONAL_MAX_RANGE_DAYS) return "tooLong";
  return null;
}

export function rangesDiffer(ranges: SeasonalRanges) {
  return (
    ranges.inbound.startDate !== ranges.sales.startDate ||
    ranges.inbound.endDate !== ranges.sales.endDate
  );
}

export function canViewSeasonalProductInsights(
  isAuthenticated: boolean,
  hasPermission: (permission: string) => boolean,
  isReview: boolean,
) {
  return isAuthenticated && !isReview && hasPermission(SEASONAL_PERMISSION);
}

/** 日视图：真实日期轴，只放有销售记录的日期，无记录的日期留空而不是画成 0。 */
export function buildDailyTrend(daily: SeasonalDailySales[], range: SeasonalRange) {
  const dayCount = daysBetweenInclusive(range);
  const start = toUtc(range.startDate);
  const points: DailyTrendPoint[] = daily
    .map((item) => ({
      index: Math.round((toUtc(item.date) - start) / DAY_MS),
      date: item.date.slice(0, 10),
      quantity: item.quantity,
    }))
    .filter((point) => point.index >= 0 && point.index < dayCount)
    .sort((a, b) => a.index - b.index);
  return { dayCount, points };
}

function mondayOf(date: string) {
  const ms = toUtc(date);
  const weekday = new Date(ms).getUTCDay();
  const offset = (weekday + 6) % 7;
  return fromUtc(ms - offset * DAY_MS);
}

/** 周视图：周一为一周起点，区间内每一周都给出（含 0 销量周），首尾不足 7 天的标记为不完整周。 */
export function buildWeeklyTrend(
  daily: SeasonalDailySales[],
  inbound: SeasonalInboundRecord[],
  range: SeasonalRange,
): WeeklyTrendPoint[] {
  const weeks: WeeklyTrendPoint[] = [];
  for (let monday = mondayOf(range.startDate); monday <= range.endDate; monday = addDays(monday, 7)) {
    const sunday = addDays(monday, 6);
    const startDate = monday < range.startDate ? range.startDate : monday;
    const endDate = sunday > range.endDate ? range.endDate : sunday;
    weeks.push({
      startDate,
      endDate,
      quantity: 0,
      inboundQuantity: 0,
      partial: startDate !== monday || endDate !== sunday,
    });
  }
  const find = (date: string) =>
    weeks.find((week) => date >= week.startDate && date <= week.endDate);
  for (const item of daily) {
    const week = find(item.date.slice(0, 10));
    if (week) week.quantity += item.quantity;
  }
  for (const record of inbound) {
    const week = find(record.date.slice(0, 10));
    if (week) week.inboundQuantity += record.quantity;
  }
  return weeks;
}

export function summarizeDaily(points: DailyTrendPoint[], dayCount: number) {
  const total = points.reduce((sum, point) => sum + point.quantity, 0);
  const peak = points.reduce<DailyTrendPoint | null>(
    (best, point) => (!best || point.quantity > best.quantity ? point : best),
    null,
  );
  return {
    recordedDays: points.length,
    dayCount,
    averagePerRecordedDay: points.length ? total / points.length : 0,
    peak,
  };
}

export function summarizeWeekly(weeks: WeeklyTrendPoint[]) {
  const full = weeks.filter((week) => !week.partial);
  const best = full.reduce<WeeklyTrendPoint | null>(
    (top, week) => (!top || week.quantity > top.quantity ? week : top),
    null,
  );
  const last = full.at(-1);
  const previous = full.at(-2);
  return {
    averagePerFullWeek: full.length
      ? full.reduce((sum, week) => sum + week.quantity, 0) / full.length
      : null,
    best,
    // 上一个完整周为 0 时环比无意义，返回 null 而不是无穷大。
    weekOverWeek:
      last && previous && previous.quantity > 0
        ? (last.quantity - previous.quantity) / previous.quantity
        : null,
  };
}

/** 进货记录日期落在销售区间内的，才能在趋势图上标出到货。 */
export function inboundMarkers(inbound: SeasonalInboundRecord[], range: SeasonalRange) {
  const byDate = new Map<string, number>();
  for (const record of inbound) {
    const date = record.date.slice(0, 10);
    if (date < range.startDate || date > range.endDate) continue;
    byDate.set(date, (byDate.get(date) ?? 0) + record.quantity);
  }
  const start = toUtc(range.startDate);
  return [...byDate.entries()]
    .map(([date, quantity]) => ({
      date,
      quantity,
      index: Math.round((toUtc(date) - start) / DAY_MS),
    }))
    .sort((a, b) => a.index - b.index);
}

export function formatQuantity(value: number) {
  const rounded = Math.round(value * 100) / 100;
  const sign = rounded < 0 ? "−" : "";
  return `${sign}${Math.abs(rounded).toLocaleString("en-AU", { maximumFractionDigits: 2 })}`;
}

export function shortDate(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  return match ? `${match[2]}/${match[3]}` : "—";
}

export function weekdayIndex(value: string) {
  return new Date(toUtc(value)).getUTCDay();
}
