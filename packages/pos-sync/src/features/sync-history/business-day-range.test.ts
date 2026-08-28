import assert from "node:assert/strict";
import test from "node:test";

import { businessDayUtcRange } from "./business-day-range";

test("Australia/Brisbane 业务日从本地午夜映射到正确 UTC 闭区间", () => {
  assert.deepEqual(
    businessDayUtcRange(
      "2026-07-28",
      "2026-07-28",
      "Australia/Brisbane",
    ),
    {
      dateFromIso: "2026-07-27T14:00:00.000Z",
      dateToIso: "2026-07-28T13:59:59.999Z",
    },
  );
});

for (const timeZone of [
  "Australia/Melbourne",
  "Australia/Sydney",
] as const) {
  test(`${timeZone} DST 开始日为 23 小时业务日`, () => {
    const range = businessDayUtcRange(
      "2026-10-04",
      "2026-10-04",
      timeZone,
    );
    assert.deepEqual(range, {
      dateFromIso: "2026-10-03T14:00:00.000Z",
      dateToIso: "2026-10-04T12:59:59.999Z",
    });
    assert.equal(rangeDurationHours(range), 23);
  });

  test(`${timeZone} DST 结束日为 25 小时业务日`, () => {
    const range = businessDayUtcRange(
      "2026-04-05",
      "2026-04-05",
      timeZone,
    );
    assert.deepEqual(range, {
      dateFromIso: "2026-04-04T13:00:00.000Z",
      dateToIso: "2026-04-05T13:59:59.999Z",
    });
    assert.equal(rangeDurationHours(range), 25);
  });
}

test("非法日期、反向范围和非法 IANA 时区均 fail closed", () => {
  for (const [from, to, timeZone] of [
    ["2026-02-30", "2026-03-01", "Australia/Brisbane"],
    ["2026-7-1", "2026-07-02", "Australia/Brisbane"],
    ["2026-07-29", "2026-07-28", "Australia/Brisbane"],
    ["2026-07-28", "2026-07-28", "Australia/Not_A_Zone"],
    ["2026-07-28", "2026-07-28", " "],
  ] as const) {
    assert.equal(businessDayUtcRange(from, to, timeZone), null);
  }
});

function rangeDurationHours(
  range: ReturnType<typeof businessDayUtcRange>,
): number {
  assert.ok(range?.dateFromIso);
  assert.ok(range.dateToIso);
  return (
    (Date.parse(range.dateToIso) + 1 - Date.parse(range.dateFromIso)) /
    3_600_000
  );
}
