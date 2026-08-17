import assert from "node:assert/strict";
import test from "node:test";

import {
  createInitialSalesHidScanState,
  reduceSalesHidScanChange,
  SALES_HID_FAST_GAP_MS,
  type SalesHidScanIdleState,
} from "./sales-hid-scan-idle";

function feedCumulative(
  state: SalesHidScanIdleState,
  values: readonly string[],
  gapMs: number,
  firstGapMs: number = gapMs,
): SalesHidScanIdleState {
  let current = state;
  let nowMs = current.lastChangeAt === 0 ? 1000 : current.lastChangeAt;
  for (const [index, value] of values.entries()) {
    nowMs += index === 0 ? firstGapMs : gapMs;
    current = reduceSalesHidScanChange(current, { value, nowMs });
  }
  return current;
}

test("慢速草稿推进基线，快速扫描从完整草稿后截取追加条码", () => {
  const draft = feedCumulative(
    createInitialSalesHidScanState(""),
    ["A", "AB", "ABC"],
    SALES_HID_FAST_GAP_MS + 1,
  );
  assert.equal(draft.previousValue, "ABC");

  const scanned = feedCumulative(
    draft,
    ["ABC1", "ABC12", "ABC123", "ABC1234", "ABC12345", "ABC123456"],
    SALES_HID_FAST_GAP_MS,
    SALES_HID_FAST_GAP_MS + 1,
  );
  assert.equal(scanned.confirmed, true);
  assert.equal(scanned.pendingCode, "123456");
});

test("DataWedge 整串写入时立即确认追加条码并保留手动草稿", () => {
  const draft = feedCumulative(
    createInitialSalesHidScanState(""),
    ["A", "AB", "ABC"],
    SALES_HID_FAST_GAP_MS + 1,
  );
  const dataWedgeChange = {
    value: "ABC123456",
    nowMs: draft.lastChangeAt + 1_000,
    dataWedgeBatch: true,
  };

  const scanned = reduceSalesHidScanChange(draft, dataWedgeChange);

  assert.equal(scanned.baseline, "ABC");
  assert.equal(scanned.confirmed, true);
  assert.equal(scanned.pendingCode, "123456");
});

test("不足连续快速间隔不会确认 HID", () => {
  const afterTwoFast = feedCumulative(
    createInitialSalesHidScanState("ABC"),
    ["ABC1", "ABC12"],
    SALES_HID_FAST_GAP_MS,
  );
  assert.equal(afterTwoFast.rapidStreak, 1);
  assert.equal(afterTwoFast.confirmed, false);
  assert.equal(afterTwoFast.pendingCode, null);

  const slowAfter = reduceSalesHidScanChange(afterTwoFast, {
    value: "ABC123",
    nowMs: 5000,
  });
  assert.equal(slowAfter.confirmed, false);
  assert.equal(slowAfter.pendingCode, null);
});

test("清空输入会重置节奏与基线", () => {
  const confirmed = feedCumulative(
    createInitialSalesHidScanState("ABC"),
    ["ABC1", "ABC12", "ABC123", "ABC1234"],
    SALES_HID_FAST_GAP_MS,
  );
  assert.equal(confirmed.confirmed, true);

  const cleared = reduceSalesHidScanChange(confirmed, {
    value: "",
    nowMs: 9000,
  });
  assert.equal(cleared.baseline, "");
  assert.equal(cleared.rapidStreak, 0);
  assert.equal(cleared.confirmed, false);
  assert.equal(cleared.pendingCode, null);
});

test("非尾部插入或整段替换会成为新草稿，后续扫码不会截入旧字符", () => {
  const replaced = reduceSalesHidScanChange(
    createInitialSalesHidScanState("ABC"),
    { value: "AXBC", nowMs: 1_000 },
  );
  assert.equal(replaced.baseline, "AXBC");

  const scanned = feedCumulative(
    replaced,
    ["AXBC1", "AXBC12", "AXBC123", "AXBC1234"],
    SALES_HID_FAST_GAP_MS,
  );
  assert.equal(scanned.confirmed, true);
  assert.equal(scanned.pendingCode, "1234");
});
