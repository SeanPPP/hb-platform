const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

export type AvailabilityQuickSelection = "all" | "weekdays" | "weekend";

export interface AvailabilityMonthCell {
  date: string;
  day: number;
  isCurrentMonth: boolean;
}

function pad2(value: number) {
  return String(value).padStart(2, "0");
}

/** 日期选择器只处理本地日历日，禁止用 UTC 解析 YYYY-MM-DD 造成跨日偏移。 */
export function parseAvailabilityDate(value?: string) {
  const match = value?.match(DATE_PATTERN);
  if (!match) return undefined;

  const year = Number(match[1]);
  const month = Number(match[2]) - 1;
  const day = Number(match[3]);
  const date = new Date(year, month, day);
  return date.getFullYear() === year && date.getMonth() === month && date.getDate() === day
    ? date
    : undefined;
}

export function formatAvailabilityDate(date: Date) {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
}

export function normalizeAvailabilityDate(value?: string, fallback = new Date()) {
  return formatAvailabilityDate(parseAvailabilityDate(value) ?? fallback);
}

export function addAvailabilityDays(value: string | Date, days: number) {
  const date = typeof value === "string" ? parseAvailabilityDate(value) : value;
  const source = date ? new Date(date.getFullYear(), date.getMonth(), date.getDate()) : new Date();
  source.setDate(source.getDate() + days);
  return formatAvailabilityDate(source);
}

export function startOfAvailabilityWeek(value: string | Date) {
  const date = typeof value === "string" ? parseAvailabilityDate(value) : value;
  const source = date ? new Date(date.getFullYear(), date.getMonth(), date.getDate()) : new Date();
  const mondayOffset = (source.getDay() + 6) % 7;
  source.setDate(source.getDate() - mondayOffset);
  return formatAvailabilityDate(source);
}

export function getAvailabilityWeek(value: string) {
  const monday = startOfAvailabilityWeek(value);
  return Array.from({ length: 7 }, (_, index) => addAvailabilityDays(monday, index));
}

export function normalizeAvailabilityDates(dates: string[]): string[] {
  return Array.from(new Set(dates.filter((value) => Boolean(parseAvailabilityDate(value)))))
    .sort((left, right) => left.localeCompare(right));
}

/** 替换当前周，其他周的选择保持不变。 */
export function replaceAvailabilityWeek(
  selected: Iterable<string>,
  weekDate: string,
  selection: AvailabilityQuickSelection,
) {
  const week = getAvailabilityWeek(weekDate);
  const weekSet = new Set(week);
  const preserved = normalizeAvailabilityDates(Array.from(selected)).filter((date) => !weekSet.has(date));
  const replacement = selection === "all"
    ? week
    : selection === "weekdays"
      ? week.slice(0, 5)
      : week.slice(5);
  return normalizeAvailabilityDates([...preserved, ...replacement]);
}

export function toggleAvailabilityDate(
  selected: Iterable<string>,
  date: string,
  singleDate = false,
) {
  if (!parseAvailabilityDate(date)) return normalizeAvailabilityDates(Array.from(selected));
  if (singleDate) return [date];

  const current = normalizeAvailabilityDates(Array.from(selected));
  return current.includes(date)
    ? current.filter((item) => item !== date)
    : normalizeAvailabilityDates([...current, date]);
}

export function getAvailabilityMonthKey(value: string | Date) {
  const date = typeof value === "string" ? parseAvailabilityDate(value) : value;
  const source = date ?? new Date();
  return `${source.getFullYear()}-${pad2(source.getMonth() + 1)}`;
}

export function shiftAvailabilityMonth(monthKey: string, offset: number) {
  const match = /^(\d{4})-(\d{2})$/.exec(monthKey);
  const source = match
    ? new Date(Number(match[1]), Number(match[2]) - 1 + offset, 1)
    : new Date();
  return getAvailabilityMonthKey(source);
}

export function buildAvailabilityMonthGrid(monthKey: string): AvailabilityMonthCell[] {
  const match = /^(\d{4})-(\d{2})$/.exec(monthKey);
  const monthStart = match
    ? new Date(Number(match[1]), Number(match[2]) - 1, 1)
    : new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  const mondayOffset = (monthStart.getDay() + 6) % 7;
  const gridStart = new Date(monthStart.getFullYear(), monthStart.getMonth(), 1 - mondayOffset);

  return Array.from({ length: 42 }, (_, index) => {
    const date = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + index);
    return {
      date: formatAvailabilityDate(date),
      day: date.getDate(),
      isCurrentMonth: date.getMonth() === monthStart.getMonth() && date.getFullYear() === monthStart.getFullYear(),
    };
  });
}

export function formatAvailabilityMonthLabel(monthKey: string) {
  const match = /^(\d{4})-(\d{2})$/.exec(monthKey);
  return match ? `${match[1]}-${match[2]}` : monthKey;
}
