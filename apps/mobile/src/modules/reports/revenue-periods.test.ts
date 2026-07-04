import assert from "node:assert/strict";
import {
  getCompareRevenuePeriod,
  getDefaultRevenuePeriod,
  getLastMonthRevenuePeriod,
  getLastWeekRevenuePeriod,
  getLastYearIsoWeekPeriod,
  getLastYearSameMonthPeriod,
  getLastYearSameWeekdayPeriod,
  getNextRevenuePeriod,
  getPreviousRevenuePeriod,
  getYesterdayRevenuePeriod,
} from "./periods";

const anchor = new Date(2026, 6, 4);

assert.deepEqual(getYesterdayRevenuePeriod(anchor), {
  mode: "day",
  startDate: "2026-07-03",
  endDate: "2026-07-03",
});
assert.deepEqual(getDefaultRevenuePeriod("day", anchor), {
  mode: "day",
  startDate: "2026-07-04",
  endDate: "2026-07-04",
});

assert.deepEqual(getLastWeekRevenuePeriod(anchor), {
  mode: "week",
  startDate: "2026-06-22",
  endDate: "2026-06-28",
});
assert.deepEqual(getDefaultRevenuePeriod("week", anchor), {
  mode: "week",
  startDate: "2026-06-29",
  endDate: "2026-07-05",
});

assert.deepEqual(getLastMonthRevenuePeriod(anchor), {
  mode: "month",
  startDate: "2026-06-01",
  endDate: "2026-06-30",
});
assert.deepEqual(getDefaultRevenuePeriod("month", anchor), {
  mode: "month",
  startDate: "2026-07-01",
  endDate: "2026-07-31",
});

assert.deepEqual(getLastYearSameWeekdayPeriod(getDefaultRevenuePeriod("day", anchor)), {
  mode: "day",
  startDate: "2025-07-05",
  endDate: "2025-07-05",
});

const week = { mode: "week" as const, startDate: "2026-06-22", endDate: "2026-06-28" };
assert.deepEqual(getPreviousRevenuePeriod(week), {
  mode: "week",
  startDate: "2026-06-15",
  endDate: "2026-06-21",
});
assert.deepEqual(getNextRevenuePeriod(week), {
  mode: "week",
  startDate: "2026-06-29",
  endDate: "2026-07-05",
});
assert.deepEqual(getLastYearSameWeekdayPeriod(week), {
  mode: "week",
  startDate: "2025-06-23",
  endDate: "2025-06-29",
});
assert.deepEqual(getLastYearIsoWeekPeriod(week), {
  mode: "week",
  startDate: "2025-06-23",
  endDate: "2025-06-29",
});

const month = { mode: "month" as const, startDate: "2026-03-01", endDate: "2026-03-31" };
assert.deepEqual(getPreviousRevenuePeriod(month), {
  mode: "month",
  startDate: "2026-02-01",
  endDate: "2026-02-28",
});
assert.deepEqual(getLastYearSameMonthPeriod(month), {
  mode: "month",
  startDate: "2025-03-01",
  endDate: "2025-03-31",
});
assert.deepEqual(getCompareRevenuePeriod(month, "lastYearSameMonth"), getLastYearSameMonthPeriod(month));
