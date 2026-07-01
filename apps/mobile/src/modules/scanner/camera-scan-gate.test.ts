import assert from "node:assert/strict";
import { shouldForwardCameraScan } from "./camera-scan-gate";

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

console.log("camera-scan-gate.test.ts: ok");
