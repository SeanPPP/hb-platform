export const PUBLIC_HOLIDAY_SYNC_DAYS_AHEAD = 30;

export type AustralianHolidayJurisdiction = "NSW" | "QLD";

export interface PublicHolidaySyncWindow {
  fromDate: string;
  toDate: string;
  daysAhead: number;
}

const NSW_POSTCODE_RANGES: ReadonlyArray<readonly [number, number]> = [
  [1000, 2599],
  [2619, 2899],
  [2921, 2999],
];

const QLD_POSTCODE_RANGES: ReadonlyArray<readonly [number, number]> = [
  [4000, 4999],
  [9000, 9999],
];

const AMBIGUOUS_POSTCODES = new Set([2406]);

interface LocalDateParts {
  year: number;
  month: number;
  day: number;
}

function padDatePart(value: number) {
  return String(value).padStart(2, "0");
}

function toDateString(parts: LocalDateParts) {
  return `${parts.year}-${padDatePart(parts.month)}-${padDatePart(parts.day)}`;
}

function parseLocalDate(value: Date | string): LocalDateParts {
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    return {
      year: value.getFullYear(),
      month: value.getMonth() + 1,
      day: value.getDate(),
    };
  }

  const normalized = String(value).trim().slice(0, 10);
  const match = normalized.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (match) {
    return {
      year: Number(match[1]),
      month: Number(match[2]),
      day: Number(match[3]),
    };
  }

  const today = new Date();
  return {
    year: today.getFullYear(),
    month: today.getMonth() + 1,
    day: today.getDate(),
  };
}

function addDays(parts: LocalDateParts, days: number): LocalDateParts {
  const result = new Date(Date.UTC(parts.year, parts.month - 1, parts.day + days));
  return {
    year: result.getUTCFullYear(),
    month: result.getUTCMonth() + 1,
    day: result.getUTCDate(),
  };
}

function isWithinRanges(value: number, ranges: ReadonlyArray<readonly [number, number]>) {
  return ranges.some(([start, end]) => value >= start && value <= end);
}

export function buildPublicHolidaySyncWindow(
  baseDate: Date | string = new Date(),
  daysAhead = PUBLIC_HOLIDAY_SYNC_DAYS_AHEAD,
): PublicHolidaySyncWindow {
  const normalizedDaysAhead = Math.max(0, Math.floor(daysAhead));
  const from = parseLocalDate(baseDate);
  const to = addDays(from, normalizedDaysAhead);

  return {
    fromDate: toDateString(from),
    toDate: toDateString(to),
    daysAhead: normalizedDaysAhead,
  };
}

export function normalizeAustralianHolidayJurisdiction(
  value: string | null | undefined,
): AustralianHolidayJurisdiction | null {
  const normalized = value?.trim().toLowerCase();
  if (!normalized) {
    return null;
  }

  if (normalized === "nsw" || normalized === "new south wales") {
    return "NSW";
  }

  if (normalized === "qld" || normalized === "queensland") {
    return "QLD";
  }

  return null;
}

export function resolveAustralianHolidayJurisdiction(
  postcode: string | number | null | undefined,
): AustralianHolidayJurisdiction | null {
  if (postcode == null) {
    return null;
  }

  const normalized = String(postcode).trim();
  if (!/^\d{4}$/.test(normalized)) {
    return null;
  }

  const value = Number(normalized);
  if (AMBIGUOUS_POSTCODES.has(value)) {
    return null;
  }

  if (isWithinRanges(value, NSW_POSTCODE_RANGES)) {
    return "NSW";
  }

  if (isWithinRanges(value, QLD_POSTCODE_RANGES)) {
    return "QLD";
  }

  return null;
}
