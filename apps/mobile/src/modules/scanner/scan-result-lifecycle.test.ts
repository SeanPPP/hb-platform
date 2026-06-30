import assert from "node:assert/strict";
import {
  canUpdateAddScanFeedback,
  shouldFlushCartSyncOnAppStateChange,
  shouldFlushCartSyncImmediately,
} from "./scan-result-lifecycle";

function run() {
  assert.equal(
    shouldFlushCartSyncOnAppStateChange("active", "inactive"),
    true,
    "active -> inactive 应立即刷新购物车同步队列"
  );
  assert.equal(
    shouldFlushCartSyncOnAppStateChange("active", "background"),
    true,
    "active -> background 应立即刷新购物车同步队列"
  );
  assert.equal(
    shouldFlushCartSyncOnAppStateChange("background", "active"),
    false,
    "回到前台不应重复刷新购物车同步队列"
  );
  assert.equal(
    shouldFlushCartSyncOnAppStateChange("active", "active"),
    false,
    "状态未变化时不应刷新购物车同步队列"
  );
  assert.equal(
    shouldFlushCartSyncImmediately(6, 6, "active", true),
    true,
    "批量满时应立即同步购物车队列"
  );
  assert.equal(
    shouldFlushCartSyncImmediately(1, 6, "background", true),
    true,
    "后台中新入队的购物车同步不能等待 timer"
  );
  assert.equal(
    shouldFlushCartSyncImmediately(1, 6, "active", false),
    true,
    "组件卸载后新入队的购物车同步不能等待 timer"
  );
  assert.equal(
    shouldFlushCartSyncImmediately(1, 6, "active", true),
    false,
    "前台小批量仍按防抖 timer 合并同步"
  );

  assert.equal(
    canUpdateAddScanFeedback("trace-new", "trace-new"),
    true,
    "最新扫码允许更新加购反馈"
  );
  assert.equal(
    canUpdateAddScanFeedback("trace-new", "trace-old"),
    false,
    "旧扫码不能覆盖最新加购反馈"
  );
  assert.equal(
    canUpdateAddScanFeedback(null, "trace-any"),
    true,
    "未记录最新扫码时保守允许更新反馈"
  );

  console.log("scan-result-lifecycle.test.ts: ok");
}

run();
