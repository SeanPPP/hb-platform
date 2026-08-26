import assert from "node:assert/strict";
import test from "node:test";

import { ScanTimingCollector } from "./scan-timing";

import {
  POS_CLIENT_METRICS,
  type ClientMetricDraft,
} from "@/core/performance/client-metrics";

test("HID 最后字符到 cart 发布按同一单调时钟上报且完成幂等", () => {
  let now = 100;
  const records: ClientMetricDraft[] = [];
  const timing = new ScanTimingCollector({
    now: () => now,
    record: (draft) => records.push(draft),
  });

  timing.noteHidCharacter();
  now = 185;
  timing.beginHid("scan-1");
  now = 235;
  timing.complete("scan-1", "success");
  timing.complete("scan-1", "success");

  assert.deepEqual(records, [
    {
      metric: POS_CLIENT_METRICS.scanToCart,
      valueMs: 135,
      dimensions: { outcome: "success" },
    },
  ]);
});

test("HID 失败路径也上报并使用最近一个字符作为起点", () => {
  let now = 10;
  const records: ClientMetricDraft[] = [];
  const timing = new ScanTimingCollector({
    now: () => now,
    record: (draft) => records.push(draft),
  });

  timing.noteHidCharacter();
  now = 20;
  timing.noteHidCharacter();
  now = 105;
  timing.beginHid("scan-failed");
  now = 140;
  timing.complete("scan-failed", "failure");

  assert.equal(records.length, 1);
  assert.equal(records[0]?.valueMs, 120);
  assert.deepEqual(records[0]?.dimensions, { outcome: "failure" });
});
