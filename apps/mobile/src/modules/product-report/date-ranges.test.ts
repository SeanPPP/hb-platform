import assert from "node:assert/strict";
import {
  getDefaultProductReportRange,
  getProductReportCompareRange,
  getProductReportQuickRange,
  isValidProductReportDateRange,
} from "./date-ranges";

const anchor = new Date(2026, 6, 4);

assert.deepEqual(getDefaultProductReportRange(anchor), {
  key: "today",
  mode: "day",
  startDate: "2026-07-04",
  endDate: "2026-07-04",
});

assert.deepEqual(getProductReportQuickRange("yesterday", anchor), {
  key: "yesterday",
  mode: "day",
  startDate: "2026-07-03",
  endDate: "2026-07-03",
});

assert.deepEqual(getProductReportQuickRange("thisWeek", anchor), {
  key: "thisWeek",
  mode: "week",
  startDate: "2026-06-29",
  endDate: "2026-07-05",
});

assert.deepEqual(getProductReportQuickRange("lastWeek", anchor), {
  key: "lastWeek",
  mode: "week",
  startDate: "2026-06-22",
  endDate: "2026-06-28",
});

assert.deepEqual(getProductReportQuickRange("thisMonth", anchor), {
  key: "thisMonth",
  mode: "month",
  startDate: "2026-07-01",
  endDate: "2026-07-31",
});

assert.deepEqual(getProductReportQuickRange("lastMonth", anchor), {
  key: "lastMonth",
  mode: "month",
  startDate: "2026-06-01",
  endDate: "2026-06-30",
});

assert.deepEqual(getProductReportCompareRange(getProductReportQuickRange("today", anchor)), {
  mode: "day",
  startDate: "2025-07-05",
  endDate: "2025-07-05",
});

assert.deepEqual(getProductReportCompareRange(getProductReportQuickRange("thisMonth", anchor)), {
  mode: "month",
  startDate: "2025-07-01",
  endDate: "2025-07-31",
});

assert.equal(isValidProductReportDateRange("2026-07-01", "2026-07-04"), true);
assert.equal(isValidProductReportDateRange("2026-7-01", "2026-07-04"), false);
assert.equal(isValidProductReportDateRange("2026-02-30", "2026-07-04"), false);
assert.equal(isValidProductReportDateRange("2026-07-04", "2026-07-01"), false);
