import assert from "node:assert/strict";
import test from "node:test";

import { createFixedIsoClock, createSequenceIdFactory } from "./fixed-values";

test("固定时钟返回已规范化且不可漂移的 ISO 时间", () => {
  const now = createFixedIsoClock("2026-08-25T10:11:12+10:00");
  assert.equal(now(), "2026-08-25T00:11:12.000Z");
  assert.equal(now(), "2026-08-25T00:11:12.000Z");
  assert.throws(() => createFixedIsoClock("not-a-date"), /ISO/i);
});

test("顺序 ID 工厂按 fixture 顺序返回，并在耗尽时失败关闭", () => {
  const nextId = createSequenceIdFactory(["attempt-1", "attempt-2"]);
  assert.equal(nextId(), "attempt-1");
  assert.equal(nextId(), "attempt-2");
  assert.throws(() => nextId(), /exhausted/i);
  assert.throws(() => createSequenceIdFactory(["ok", " "]), /non-empty/i);
});
