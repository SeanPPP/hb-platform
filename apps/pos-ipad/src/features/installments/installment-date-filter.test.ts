import assert from "node:assert/strict";
import test from "node:test";

import {
  isValidInstallmentDateFilter,
  resolveInstallmentDateRange,
} from "./installment-date-filter";

test("预设日期按门店本地自然日转换为 UTC 闭区间", () => {
  const now = new Date("2026-08-03T00:30:00.000Z");

  assert.deepEqual(
    resolveInstallmentDateRange(
      { preset: "today", fromDate: null, toDate: null },
      now,
      "Australia/Brisbane",
    ),
    {
      createdFromIso: "2026-08-02T14:00:00.000Z",
      createdToIso: "2026-08-03T13:59:59.999Z",
    },
  );
  assert.deepEqual(
    resolveInstallmentDateRange(
      { preset: "last7", fromDate: null, toDate: null },
      now,
      "Australia/Brisbane",
    ),
    {
      createdFromIso: "2026-07-27T14:00:00.000Z",
      createdToIso: "2026-08-03T13:59:59.999Z",
    },
  );
  assert.deepEqual(
    resolveInstallmentDateRange(
      { preset: "last30", fromDate: null, toDate: null },
      now,
      "Australia/Brisbane",
    ),
    {
      createdFromIso: "2026-07-04T14:00:00.000Z",
      createdToIso: "2026-08-03T13:59:59.999Z",
    },
  );
});

test("custom 日期验证 from <= to，并覆盖 DST 长短日", () => {
  assert.equal(
    isValidInstallmentDateFilter({
      preset: "custom",
      fromDate: "2026-08-04",
      toDate: "2026-08-03",
    }),
    false,
  );
  assert.equal(
    isValidInstallmentDateFilter({
      preset: "custom",
      fromDate: "2026-02-30",
      toDate: "2026-03-01",
    }),
    false,
  );
  assert.deepEqual(
    resolveInstallmentDateRange(
      {
        preset: "custom",
        fromDate: "2026-10-04",
        toDate: "2026-10-04",
      },
      new Date("2026-10-04T01:00:00.000Z"),
      "Australia/Sydney",
    ),
    {
      createdFromIso: "2026-10-03T14:00:00.000Z",
      createdToIso: "2026-10-04T12:59:59.999Z",
    },
  );
});

test("all 不限制日期；非法时钟或时区 fail closed", () => {
  assert.deepEqual(
    resolveInstallmentDateRange(
      { preset: "all", fromDate: null, toDate: null },
      new Date("2026-08-03T00:30:00.000Z"),
      "Australia/Brisbane",
    ),
    { createdFromIso: null, createdToIso: null },
  );
  assert.equal(
    resolveInstallmentDateRange(
      { preset: "today", fromDate: null, toDate: null },
      new Date(Number.NaN),
      "Australia/Brisbane",
    ),
    null,
  );
  assert.equal(
    resolveInstallmentDateRange(
      { preset: "today", fromDate: null, toDate: null },
      new Date("2026-08-03T00:30:00.000Z"),
      "Australia/Not_A_Zone",
    ),
    null,
  );
});
