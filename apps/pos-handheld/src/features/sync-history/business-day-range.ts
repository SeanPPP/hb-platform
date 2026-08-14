export type BusinessDayUtcRange = Readonly<{
  dateFromIso: string | null;
  dateToIso: string | null;
}>;

type CalendarDate = Readonly<{
  year: number;
  month: number;
  day: number;
}>;

type ZonedDateTimeParts = CalendarDate &
  Readonly<{
    hour: number;
    minute: number;
    second: number;
  }>;

/**
 * 将门店 IANA 时区中的业务日转换成闭区间 UTC 范围。
 *
 * 结束边界使用“次日本地午夜 - 1ms”，因此 23/25 小时 DST 业务日无需固定偏移。
 * 任一日期或时区非法时返回 null，调用方必须 fail closed。
 */
export function businessDayUtcRange(
  dateFromInput: string,
  dateToInput: string,
  explicitTimeZone?: string,
): BusinessDayUtcRange | null {
  const timeZone = resolveBusinessTimeZone(explicitTimeZone);
  if (!timeZone) return null;

  const dateFromText = dateFromInput.trim();
  const dateToText = dateToInput.trim();
  const dateFrom = dateFromText ? parseCalendarDate(dateFromText) : null;
  const dateTo = dateToText ? parseCalendarDate(dateToText) : null;
  if ((dateFromText && !dateFrom) || (dateToText && !dateTo)) {
    return null;
  }

  const formatter = createZonedFormatter(timeZone);
  const fromEpoch = dateFrom
    ? zonedMidnightUtcEpoch(dateFrom, formatter)
    : null;
  const toExclusiveEpoch = dateTo
    ? zonedMidnightUtcEpoch(nextCalendarDate(dateTo), formatter)
    : null;
  if (
    (dateFrom && fromEpoch === null) ||
    (dateTo && toExclusiveEpoch === null)
  ) {
    return null;
  }

  const toInclusiveEpoch =
    toExclusiveEpoch === null ? null : toExclusiveEpoch - 1;
  if (
    fromEpoch !== null &&
    toInclusiveEpoch !== null &&
    fromEpoch > toInclusiveEpoch
  ) {
    return null;
  }
  return {
    dateFromIso:
      fromEpoch === null ? null : new Date(fromEpoch).toISOString(),
    dateToIso:
      toInclusiveEpoch === null
        ? null
        : new Date(toInclusiveEpoch).toISOString(),
  };
}

export function resolveBusinessTimeZone(
  explicitTimeZone?: string,
): string | null {
  const timeZone =
    explicitTimeZone === undefined
      ? Intl.DateTimeFormat().resolvedOptions().timeZone
      : explicitTimeZone.trim();
  if (!timeZone) return null;
  try {
    new Intl.DateTimeFormat("en-AU", { timeZone }).format(0);
    return timeZone;
  } catch {
    return null;
  }
}

function parseCalendarDate(value: string): CalendarDate | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || year > 9_999) return null;

  const epoch = utcEpoch({ year, month, day, hour: 0, minute: 0, second: 0 });
  const candidate = new Date(epoch);
  return candidate.getUTCFullYear() === year &&
    candidate.getUTCMonth() === month - 1 &&
    candidate.getUTCDate() === day
    ? { year, month, day }
    : null;
}

function nextCalendarDate(value: CalendarDate): CalendarDate {
  const candidate = new Date(
    utcEpoch({ ...value, hour: 0, minute: 0, second: 0 }),
  );
  candidate.setUTCDate(candidate.getUTCDate() + 1);
  return {
    year: candidate.getUTCFullYear(),
    month: candidate.getUTCMonth() + 1,
    day: candidate.getUTCDate(),
  };
}

function createZonedFormatter(timeZone: string): Intl.DateTimeFormat {
  return new Intl.DateTimeFormat("en-AU", {
    calendar: "gregory",
    day: "2-digit",
    hour: "2-digit",
    hourCycle: "h23",
    minute: "2-digit",
    month: "2-digit",
    numberingSystem: "latn",
    second: "2-digit",
    timeZone,
    year: "numeric",
  });
}

function zonedMidnightUtcEpoch(
  target: CalendarDate,
  formatter: Intl.DateTimeFormat,
): number | null {
  const localEpoch = utcEpoch({
    ...target,
    hour: 0,
    minute: 0,
    second: 0,
  });
  let candidate = localEpoch;

  // Intl 没有直接暴露 offset；用格式化后的本地墙钟时间反推，并在 DST 边界收敛。
  for (let iteration = 0; iteration < 6; iteration += 1) {
    const observed = zonedParts(candidate, formatter);
    if (!observed) return null;
    const observedAsUtc = utcEpoch(observed);
    const nextCandidate = localEpoch - (observedAsUtc - candidate);
    if (nextCandidate === candidate) break;
    candidate = nextCandidate;
  }

  const resolved = zonedParts(candidate, formatter);
  return resolved &&
    resolved.year === target.year &&
    resolved.month === target.month &&
    resolved.day === target.day &&
    resolved.hour === 0 &&
    resolved.minute === 0 &&
    resolved.second === 0
    ? candidate
    : null;
}

function zonedParts(
  epoch: number,
  formatter: Intl.DateTimeFormat,
): ZonedDateTimeParts | null {
  const values = new Map<string, number>();
  for (const part of formatter.formatToParts(new Date(epoch))) {
    if (
      part.type === "year" ||
      part.type === "month" ||
      part.type === "day" ||
      part.type === "hour" ||
      part.type === "minute" ||
      part.type === "second"
    ) {
      values.set(part.type, Number(part.value));
    }
  }
  const result: ZonedDateTimeParts = {
    year: values.get("year") ?? Number.NaN,
    month: values.get("month") ?? Number.NaN,
    day: values.get("day") ?? Number.NaN,
    hour: values.get("hour") ?? Number.NaN,
    minute: values.get("minute") ?? Number.NaN,
    second: values.get("second") ?? Number.NaN,
  };
  return Object.values(result).every(Number.isFinite) ? result : null;
}

function utcEpoch(value: ZonedDateTimeParts): number {
  const candidate = new Date(0);
  candidate.setUTCFullYear(value.year, value.month - 1, value.day);
  candidate.setUTCHours(
    value.hour,
    value.minute,
    value.second,
    0,
  );
  return candidate.getTime();
}
