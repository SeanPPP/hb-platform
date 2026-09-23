import assert from "node:assert/strict";
import { test } from "node:test";
import {
  buildDailyTrend,
  buildWeeklyTrend,
  canViewSeasonalProductInsights,
  formatQuantity,
  inboundMarkers,
  rangesDiffer,
  seasonStart,
  summarizeDaily,
  summarizeWeekly,
  validateSeasonalRange,
} from "./logic";

const salesRange = { startDate: "2026-08-01", endDate: "2026-09-23" };

test("季节起点：默认从最近一个 8 月 1 日开始", () => {
  assert.equal(seasonStart("2026-09-23"), "2026-08-01");
  assert.equal(seasonStart("2026-08-01"), "2026-08-01");
  assert.equal(seasonStart("2026-07-31"), "2025-08-01");
  assert.equal(seasonStart("2027-03-01"), "2026-08-01");
});

test("区间校验：格式、先后与一年上限和后端一致", () => {
  assert.equal(validateSeasonalRange(salesRange), null);
  assert.equal(validateSeasonalRange({ startDate: "2025-09-23", endDate: "2026-09-23" }), null);
  assert.equal(validateSeasonalRange({ startDate: "2025-09-22", endDate: "2026-09-23" }), "tooLong");
  assert.equal(validateSeasonalRange({ startDate: "2026-09-02", endDate: "2026-09-01" }), "order");
  assert.equal(validateSeasonalRange({ startDate: "2026-02-30", endDate: "2026-03-01" }), "format");
  assert.equal(validateSeasonalRange({ startDate: "2026/08/01", endDate: "2026-09-01" }), "format");
});

test("两个区间只要起止有一端不同就视为不一致", () => {
  assert.equal(rangesDiffer({ inbound: salesRange, sales: { ...salesRange } }), false);
  assert.equal(rangesDiffer({ inbound: { ...salesRange, startDate: "2026-08-15" }, sales: salesRange }), true);
  assert.equal(rangesDiffer({ inbound: salesRange, sales: { ...salesRange, endDate: "2026-09-22" } }), true);
});

test("日视图：按真实日期定位，无记录的日期不补 0，区间外的记录丢弃", () => {
  const trend = buildDailyTrend(
    [
      { date: "2026-09-23T00:00:00", quantity: 4, amount: 8 },
      { date: "2026-08-01T00:00:00", quantity: 7, amount: 14 },
      { date: "2026-08-05", quantity: 12, amount: 24 },
      { date: "2026-07-31", quantity: 99, amount: 1 },
    ],
    salesRange,
  );
  assert.equal(trend.dayCount, 54);
  assert.deepEqual(
    trend.points.map((point) => [point.index, point.date, point.quantity]),
    [
      [0, "2026-08-01", 7],
      [4, "2026-08-05", 12],
      [53, "2026-09-23", 4],
    ],
  );
  const summary = summarizeDaily(trend.points, trend.dayCount);
  assert.equal(summary.recordedDays, 3);
  assert.equal(summary.averagePerRecordedDay, 23 / 3);
  assert.equal(summary.peak?.date, "2026-08-05");
});

test("周视图：周一为起点，首尾不足 7 天标为不完整周，并汇总当周进货", () => {
  const weeks = buildWeeklyTrend(
    [
      { date: "2026-08-01", quantity: 5, amount: 0 },
      { date: "2026-08-03", quantity: 10, amount: 0 },
      { date: "2026-08-09", quantity: 2, amount: 0 },
      { date: "2026-09-23", quantity: 6, amount: 0 },
    ],
    [
      { id: "a", date: "2026-08-04T00:00:00", documentNo: "A", quantity: 100 },
      { id: "b", date: "2026-08-05", documentNo: "B", quantity: 20 },
    ],
    salesRange,
  );
  // 08/01 是周六：第一周只有 08/01–08/02；09/23 是周三：最后一周只有 09/21–09/23。
  assert.equal(weeks.length, 9);
  assert.deepEqual(
    [weeks[0].startDate, weeks[0].endDate, weeks[0].quantity, weeks[0].partial],
    ["2026-08-01", "2026-08-02", 5, true],
  );
  assert.deepEqual(
    [weeks[1].startDate, weeks[1].endDate, weeks[1].quantity, weeks[1].inboundQuantity, weeks[1].partial],
    ["2026-08-03", "2026-08-09", 12, 120, false],
  );
  assert.deepEqual([weeks[8].startDate, weeks[8].endDate, weeks[8].quantity, weeks[8].partial], ["2026-09-21", "2026-09-23", 6, true]);
  // 区间内没有销售的完整周也要给出（0 销量），不能跳过。
  assert.equal(weeks[2].quantity, 0);
});

test("周汇总：只用完整周算周均与环比，上周为 0 时环比为空", () => {
  const week = (startDate: string, quantity: number, partial = false) => ({
    startDate,
    endDate: startDate,
    quantity,
    inboundQuantity: 0,
    partial,
  });
  const summary = summarizeWeekly([week("2026-08-01", 500, true), week("2026-08-03", 100), week("2026-08-10", 150), week("2026-08-17", 30, true)]);
  assert.equal(summary.averagePerFullWeek, 125);
  assert.equal(summary.best?.startDate, "2026-08-10");
  assert.equal(summary.weekOverWeek, 0.5);
  assert.equal(summarizeWeekly([week("2026-08-03", 0), week("2026-08-10", 10)]).weekOverWeek, null);
  assert.equal(summarizeWeekly([week("2026-08-01", 5, true)]).averagePerFullWeek, null);
});

test("到货标注：只取销售区间内的进货，同日合并", () => {
  const markers = inboundMarkers(
    [
      { id: "a", date: "2026-08-10T00:00:00", documentNo: "A", quantity: 10 },
      { id: "b", date: "2026-08-10", documentNo: "B", quantity: 5 },
      { id: "c", date: "2026-07-20", documentNo: "C", quantity: 99 },
    ],
    salesRange,
  );
  assert.deepEqual(markers, [{ date: "2026-08-10", quantity: 15, index: 9 }]);
});

test("数量格式：千分位、最多两位小数、负数用减号", () => {
  assert.equal(formatQuantity(1397), "1,397");
  assert.equal(formatQuantity(540.004), "540");
  assert.equal(formatQuantity(-181), "−181");
  assert.equal(formatQuantity(6.5), "6.5");
});

test("权限：必须登录、非审核模式且有独立权限码", () => {
  const has = (code: string) => code === "SeasonalProductInsights.View";
  assert.equal(canViewSeasonalProductInsights(true, has, false), true);
  assert.equal(canViewSeasonalProductInsights(false, has, false), false);
  assert.equal(canViewSeasonalProductInsights(true, has, true), false);
  assert.equal(canViewSeasonalProductInsights(true, (code) => code === "StoreProducts.View", false), false);
});
