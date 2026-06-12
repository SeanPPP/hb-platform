import {
  PUBLIC_HOLIDAY_SYNC_DAYS_AHEAD,
  buildPublicHolidaySyncWindow,
  normalizeAustralianHolidayJurisdiction,
  resolveAustralianHolidayJurisdiction,
} from "./public-holiday-sync";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertNotEqual(actual: unknown, expected: unknown, label: string) {
  if (actual === expected) {
    throw new Error(`${label}: did not expect ${String(expected)}`);
  }
}

assertEqual(resolveAustralianHolidayJurisdiction("2000"), "NSW", "Sydney postcode maps to NSW");
assertEqual(resolveAustralianHolidayJurisdiction(4000), "QLD", "Brisbane postcode maps to QLD");
assertEqual(resolveAustralianHolidayJurisdiction(" 2485 "), "NSW", "trimmed northern NSW postcode maps to NSW");
assertEqual(resolveAustralianHolidayJurisdiction("9726"), "QLD", "QLD PO box postcode maps to QLD");
assertEqual(resolveAustralianHolidayJurisdiction("2406"), null, "border postcode without a unique state stays unknown");
assertEqual(resolveAustralianHolidayJurisdiction("0800"), null, "NT postcode is outside NSW and QLD");
assertEqual(resolveAustralianHolidayJurisdiction(undefined), null, "missing postcode is unknown");
assertEqual(normalizeAustralianHolidayJurisdiction("New South Wales"), "NSW", "full NSW state name normalizes");
assertEqual(normalizeAustralianHolidayJurisdiction("queensland"), "QLD", "lowercase QLD state name normalizes");
assertEqual(normalizeAustralianHolidayJurisdiction("Victoria"), null, "unsupported state does not normalize");

const defaultWindow = buildPublicHolidaySyncWindow("2026-05-25");
assertEqual(defaultWindow.fromDate, "2026-05-25", "default window starts on the base date");
assertEqual(defaultWindow.toDate, "2026-06-24", "default 30 day window includes day 30");
assertEqual(defaultWindow.daysAhead, PUBLIC_HOLIDAY_SYNC_DAYS_AHEAD, "default window keeps configured horizon");

const explicitThirtyDayWindow = buildPublicHolidaySyncWindow("2026-05-25", 30);
assertEqual(explicitThirtyDayWindow.fromDate, "2026-05-25", "explicit 30 day window starts on the base date");
assertEqual(explicitThirtyDayWindow.toDate, "2026-06-24", "explicit 30 day window includes today plus day 30");
assertNotEqual(explicitThirtyDayWindow.toDate, "2026-06-25", "explicit 30 day window excludes day 31");

const customWindow = buildPublicHolidaySyncWindow("2026-05-25", 1);
assertEqual(customWindow.fromDate, "2026-05-25", "custom window starts on base date");
assertEqual(customWindow.toDate, "2026-05-26", "custom one day window includes tomorrow");
