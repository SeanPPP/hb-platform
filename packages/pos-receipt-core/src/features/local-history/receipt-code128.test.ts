import assert from "node:assert/strict";
import test from "node:test";

import { receiptCode128Runs } from "./receipt-code128";

test("Code 128-B 以标准 start、校验码和 stop pattern 生成条空宽度", () => {
  const runs = receiptCode128Runs("A");

  assert.deepEqual(
    runs.slice(0, 6).map((run) => run.modules),
    [2, 1, 1, 2, 1, 4],
  );
  assert.deepEqual(
    runs.slice(-7).map((run) => run.modules),
    [2, 3, 3, 1, 1, 1, 2],
  );
  assert.equal(
    runs.reduce((total, run) => total + run.modules, 0),
    ("A".length + 2) * 11 + 13,
  );
  assert.ok(runs.every((run, index) => run.bar === (index % 2 === 0)));
});

test("Code 128-B 拒绝空值与非可打印 ASCII，避免生成不可扫描占位图", () => {
  assert.throws(() => receiptCode128Runs(""), /printable ASCII/u);
  assert.throws(() => receiptCode128Runs("订单-42"), /printable ASCII/u);
});
