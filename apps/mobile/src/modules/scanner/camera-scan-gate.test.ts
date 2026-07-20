import assert from "node:assert/strict";
import {
  createCameraScanGateController,
  shouldForwardCameraScan,
} from "./camera-scan-gate";

const baseOptions = {
  cooldownMs: 1200,
  ignoreWhileProcessing: false,
  suppressRepeatsUntilChange: false,
};

assert.equal(
  shouldForwardCameraScan({ value: "", timestamp: 0, processing: false }, "9300", 1000, baseOptions),
  true,
  "新条码应允许转发"
);

assert.equal(
  shouldForwardCameraScan({ value: "9300", timestamp: 1000, processing: false }, "9300", 1500, baseOptions),
  false,
  "单次模式保留原冷却窗口"
);

assert.equal(
  shouldForwardCameraScan({ value: "9300", timestamp: 1000, processing: false }, "9300", 2500, baseOptions),
  true,
  "单次模式冷却后仍允许同条码"
);

assert.equal(
  shouldForwardCameraScan(
    { value: "9300", timestamp: 1000, processing: false },
    "9300",
    2500,
    { ...baseOptions, suppressRepeatsUntilChange: true }
  ),
  false,
  "连续模式同条码未变化前不能重复触发"
);

assert.equal(
  shouldForwardCameraScan(
    { value: "9300", timestamp: 1000, processing: false },
    "9400",
    2600,
    { ...baseOptions, suppressRepeatsUntilChange: true }
  ),
  true,
  "连续模式切换到新条码后允许触发"
);

assert.equal(
  shouldForwardCameraScan(
    { value: "attendance-token-1", timestamp: 1000, processing: false },
    "attendance-token-2",
    2600,
    { ...baseOptions, singleScanUntilReset: true }
  ),
  false,
  "本会话已转发任意条码后，即使条码变化也必须等待 resetKey 重置"
);

assert.equal(
  shouldForwardCameraScan(
    { value: "9300", timestamp: 1000, processing: false },
    "9300",
    2500,
    { ...baseOptions, ignoreWhileProcessing: true }
  ),
  true,
  "首页连续加购不启用永久同码拦截时，冷却后应允许同条码再次触发"
);

assert.equal(
  shouldForwardCameraScan(
    { value: "9300", timestamp: 1000, processing: true },
    "9400",
    2600,
    { ...baseOptions, ignoreWhileProcessing: true }
  ),
  false,
  "处理中不接受新的相机事件，避免乱序覆盖"
);

const singleScanOptions = {
  ...baseOptions,
  ignoreWhileProcessing: true,
  singleScanUntilReset: true,
};
const attendanceGate = createCameraScanGateController("attendance-open-1");

const firstLease = attendanceGate.tryStart(
  "attendance-open-1",
  "attendance-token-1",
  1000,
  singleScanOptions
);
assert.ok(firstLease, "a. 新会话首个 token 应允许转发");
attendanceGate.finish(firstLease);
assert.equal(
  attendanceGate.tryStart(
    "attendance-open-1",
    "attendance-token-2",
    2000,
    singleScanOptions
  ),
  null,
  "b. 同会话即使 token 轮换也必须拒绝"
);

attendanceGate.setCurrentResetKey("attendance-retry-1");
const retryLease = attendanceGate.tryStart(
  "attendance-retry-1",
  "attendance-token-3",
  3000,
  singleScanOptions
);
assert.ok(retryLease, "c. reset/retry 后首个 token 应立即允许");

attendanceGate.setCurrentResetKey("attendance-closed");
attendanceGate.setCurrentResetKey("attendance-open-2");
assert.ok(
  attendanceGate.tryStart(
    "attendance-open-2",
    "attendance-token-4",
    4000,
    singleScanOptions
  ),
  "d. close/reopen 后新会话首个 token 应允许"
);

const processingOptions = {
  ...baseOptions,
  ignoreWhileProcessing: true,
};
const raceGate = createCameraScanGateController("old-session");
const oldLease = raceGate.tryStart("old-session", "old-token", 5000, processingOptions);
assert.ok(oldLease, "旧会话首个请求应成功取得 lease");

raceGate.setCurrentResetKey("new-session");
const newLease = raceGate.tryStart("new-session", "new-token", 5200, processingOptions);
assert.ok(newLease, "新 callback 应在同步重置后取得新 lease");
assert.equal(
  raceGate.tryStart("old-session", "stale-token", 5300, processingOptions),
  null,
  "e. 新 callback 已建立后仍必须拒绝队列中的旧 callback"
);

raceGate.finish(oldLease);
assert.equal(
  raceGate.tryStart("new-session", "blocked-token", 5400, processingOptions),
  null,
  "f. 旧 lease finish 不得释放仍在处理中的新 lease"
);

raceGate.finish(newLease);
assert.ok(
  raceGate.tryStart("new-session", "next-token", 5500, processingOptions),
  "g. 新 lease finish 后非 single 模式应正常释放"
);

console.log("camera-scan-gate.test.ts: ok");
