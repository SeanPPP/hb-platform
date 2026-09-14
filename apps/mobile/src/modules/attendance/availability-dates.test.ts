import assert from "node:assert/strict";
import {
  addAvailabilityDays,
  buildAvailabilityMonthGrid,
  getAvailabilityWeek,
  replaceAvailabilityWeek,
  normalizeAvailabilityDates,
  startOfAvailabilityWeek,
  toggleAvailabilityDate,
} from "./availability-dates";

assert.equal(startOfAvailabilityWeek("2026-01-01"), "2025-12-29", "跨年周必须从周一开始");
assert.deepEqual(getAvailabilityWeek("2025-12-31"), [
  "2025-12-29", "2025-12-30", "2025-12-31", "2026-01-01", "2026-01-02", "2026-01-03", "2026-01-04",
]);
assert.equal(addAvailabilityDays("2026-02-28", 1), "2026-03-01", "跨月加日必须保持日历日语义");

assert.deepEqual(
  toggleAvailabilityDate(["2026-03-02", "2026-03-06"], "2026-03-04"),
  ["2026-03-02", "2026-03-04", "2026-03-06"],
  "非连续日期必须可以加入",
);
assert.deepEqual(
  toggleAvailabilityDate(["2026-03-02", "2026-03-04"], "2026-03-04"),
  ["2026-03-02"],
  "再次点按必须只移除对应日期",
);
assert.deepEqual(
  normalizeAvailabilityDates(["2026-03-04", "bad", "2026-03-04", "2026-03-02"]),
  ["2026-03-02", "2026-03-04"],
  "提交集合必须去重并按日期排序",
);

const currentWeek = "2026-03-09";
assert.deepEqual(
  replaceAvailabilityWeek(["2026-03-02", "2026-03-11", "2026-03-21"], currentWeek, "weekdays"),
  ["2026-03-02", "2026-03-09", "2026-03-10", "2026-03-11", "2026-03-12", "2026-03-13", "2026-03-21"],
  "工作日快捷选择只替换当前周并保留其他周",
);
assert.deepEqual(
  replaceAvailabilityWeek(["2026-03-09", "2026-03-09", "2026-03-15"], currentWeek, "all"),
  ["2026-03-09", "2026-03-10", "2026-03-11", "2026-03-12", "2026-03-13", "2026-03-14", "2026-03-15"],
  "整周选择不产生重复日期",
);
assert.deepEqual(
  replaceAvailabilityWeek(["2026-03-02", "2026-03-10"], currentWeek, "weekend"),
  ["2026-03-02", "2026-03-14", "2026-03-15"],
  "周末快捷选择也只替换当前周",
);
assert.deepEqual(toggleAvailabilityDate(["2026-03-09", "2026-03-10"], "2026-03-12", true), ["2026-03-12"], "单日期模式只能保留一个日期");

const februaryGrid = buildAvailabilityMonthGrid("2026-02");
assert.equal(februaryGrid.length, 42, "月份网格固定六周，短屏和跨月排列稳定");
assert.equal(februaryGrid[0]?.date, "2026-01-26", "月份网格从周一开始并包含上月日期");
assert.equal(februaryGrid[35]?.date, "2026-03-02", "月份网格必须包含下月日期");
assert.equal(februaryGrid.filter((cell) => cell.isCurrentMonth).length, 28);

console.log("availability-dates.test.ts: ok");
