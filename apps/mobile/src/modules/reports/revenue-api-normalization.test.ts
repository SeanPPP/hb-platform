import assert from "node:assert/strict";
import {
  normalizeBranchRevenueRows,
  normalizeDailyRevenueRows,
  normalizeHourlyRevenueRows,
} from "./api";

const branchRows = normalizeBranchRevenueRows([
  {
    BranchCode: "S1",
    BranchName: "分店一",
    Revenue: 120,
    RevenueLY: 100,
    OrderCount: 6,
    Aov: 20,
  },
]);

assert.equal(branchRows[0]?.compareRevenue, 100);
assert.equal(branchRows[0]?.revenueDelta, 20);
assert.equal(branchRows[0]?.revenueDeltaRatio, 0.2);
assert.equal(branchRows[0]?.averageTransaction, 20);

const hourlyRows = normalizeHourlyRevenueRows([
  {
    Hour: "09:00",
    Revenue: 80,
    RevenueLY: 100,
    OrderCount: 4,
  },
]);

assert.equal(hourlyRows[0]?.hour, 9);
assert.equal(hourlyRows[0]?.label, "09:00");
assert.equal(hourlyRows[0]?.compareRevenue, 100);
assert.equal(hourlyRows[0]?.revenueDelta, -20);
assert.equal(hourlyRows[0]?.transactions, 4);

const dailyRows = normalizeDailyRevenueRows([
  {
    Date: "2026-07-04T00:00:00",
    BranchCode: "S1",
    BranchName: "分店一",
    Revenue: 150,
    RevenueLY: 0,
    OrderCount: 7,
  },
]);

assert.equal(dailyRows[0]?.date, "2026-07-04");
assert.equal(dailyRows[0]?.compareRevenue, 0);
assert.equal(dailyRows[0]?.revenueDeltaRatio, null);
