import assert from "node:assert/strict";
import test from "node:test";

import { normalizeLocalHistoryQuery } from "./local-history-domain";

test("本机历史查询只保留日期、关键字、游标和不超过 50 的页大小", () => {
  const query = normalizeLocalHistoryQuery({
    soldFromIso: "2026-07-31T00:00:00+10:00",
    soldToIso: "2026-07-31T23:59:59.999+10:00",
    keyword: "  tea  ",
    cursor: 42,
    limit: 50,
  });

  assert.deepEqual(query, {
    soldFromIso: "2026-07-30T14:00:00.000Z",
    soldToIso: "2026-07-31T13:59:59.999Z",
    keyword: "tea",
    cursor: 42,
    limit: 50,
  });
  assert.deepEqual(Object.keys(query).sort(), [
    "cursor",
    "keyword",
    "limit",
    "soldFromIso",
    "soldToIso",
  ]);
});

test("非法日期、反向范围、控制字符、非法游标和超过 50 条均 fail closed", () => {
  const base = {
    soldFromIso: "2026-07-30T14:00:00.000Z",
    soldToIso: "2026-07-31T13:59:59.999Z",
    keyword: null,
    cursor: null,
    limit: 50,
  } as const;

  for (const query of [
    { ...base, soldFromIso: "2026-07-31" },
    {
      ...base,
      soldFromIso: "2026-08-01T00:00:00.000Z",
      soldToIso: "2026-07-31T00:00:00.000Z",
    },
    { ...base, keyword: "tea\u0000card" },
    { ...base, cursor: 0 },
    { ...base, cursor: 1.5 },
    { ...base, limit: 51 },
  ]) {
    assert.throws(() => normalizeLocalHistoryQuery(query));
  }
});
