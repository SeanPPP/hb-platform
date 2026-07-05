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
    OrderCountLY: 5,
    Aov: 20,
    AovLY: 20,
  },
]);

assert.equal(branchRows[0]?.compareRevenue, 100);
assert.equal(branchRows[0]?.revenueDelta, 20);
assert.equal(branchRows[0]?.revenueDeltaRatio, 0.2);
assert.equal(branchRows[0]?.transactions, 6);
assert.equal(branchRows[0]?.compareTransactions, 5);
assert.equal(branchRows[0]?.averageTransaction, 20);
assert.equal(branchRows[0]?.compareAverageTransaction, 20);

const highGrowthRows = normalizeBranchRevenueRows([
  {
    BranchCode: "S2",
    Revenue: 250,
    RevenueLY: 100,
  },
]);

assert.equal(highGrowthRows[0]?.revenueDeltaRatio, 1.5);

const hourlyRows = normalizeHourlyRevenueRows([
  {
    Hour: "09:00",
    Revenue: 80,
    RevenueLY: 100,
    OrderCount: 4,
    OrderCountLY: 5,
  },
]);

assert.equal(hourlyRows[0]?.hour, 9);
assert.equal(hourlyRows[0]?.label, "09:00");
assert.equal(hourlyRows[0]?.compareRevenue, 100);
assert.equal(hourlyRows[0]?.revenueDelta, -20);
assert.equal(hourlyRows[0]?.transactions, 4);
assert.equal(hourlyRows[0]?.compareTransactions, 5);
assert.equal(hourlyRows[0]?.averageTransaction, 20);
assert.equal(hourlyRows[0]?.compareAverageTransaction, 20);

const dailyRows = normalizeDailyRevenueRows([
  {
    Date: "2026-07-04T00:00:00",
    BranchCode: "S1",
    BranchName: "分店一",
    Revenue: 150,
    RevenueLY: 0,
    OrderCount: 7,
    OrderCountLY: 0,
  },
]);

assert.equal(dailyRows[0]?.date, "2026-07-04");
assert.equal(dailyRows[0]?.compareRevenue, 0);
assert.equal(dailyRows[0]?.revenueDeltaRatio, null);
assert.equal(dailyRows[0]?.averageTransaction, 150 / 7);
assert.equal(dailyRows[0]?.compareAverageTransaction, 0);
