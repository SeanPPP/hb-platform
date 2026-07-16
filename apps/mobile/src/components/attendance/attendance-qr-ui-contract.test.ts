import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const source = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "AttendanceScreen.tsx"),
  "utf8",
);
const todayCardSource = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "TodayPunchCard.tsx"),
  "utf8",
);

assert.match(source, /attendanceScannerError/,
  "扫描器必须维护 Modal 内可见错误状态");
assert.match(source, /setAttendanceScannerError\(/,
  "本地格式、门店或权限错误必须写入扫描器错误状态");
assert.match(source, /attendanceScannerPaused/,
  "任一失败后必须暂停相机扫码事件");
assert.match(source, /retryAttendanceScan/,
  "Modal 必须提供显式重新扫描动作并重置 gate");
assert.match(source, /scanner\.retry/,
  "重新扫描按钮必须使用中英文 i18n 文案");
assert.match(source, /selectable[^>]*>\s*\{attendanceScannerError\}/,
  "Modal 内错误必须可见且可复制，用户才能修正后重扫或取消");
assert.match(todayCardSource, /canOpenAttendanceQrScanner/,
  "扫码入口必须使用与当前门店打卡完成状态无关的启用规则");
assert.doesNotMatch(todayCardSource, /\bcanPunch\b/,
  "扫码入口不得被当前门店 canPunch 或 DAY_COMPLETE 禁用");

const validateIndex = source.indexOf("validateAttendanceQrToken(qrToken)");
const networkIndex = source.indexOf("verifyAttendanceNetworkReachability()");
const resolveIndex = source.indexOf("resolveAttendanceQr(qrToken)");
const preparationIndex = source.indexOf("prepareAttendanceQrPunch(qrToday.nextPunchType");
const punchIndex = source.indexOf("punchMutation.mutateAsync(");
const trackingIndex = source.indexOf("applyAttendanceTrackingLifecycle(result");
const uiGateAfterTrackingIndex = source.indexOf(
  "if (!attendanceScannerSessionGate.isActive(session)) return;",
  trackingIndex,
);
assert.ok(validateIndex >= 0, "扫码后必须先做 opaque token 格式预检");
assert.ok(networkIndex > validateIndex, "格式预检后必须先确认联网");
assert.ok(resolveIndex > networkIndex, "联网后必须调用后端 resolve");
assert.ok(preparationIndex > resolveIndex, "后端 resolve 门店后才准备权限与实时 GPS");
assert.ok(punchIndex > preparationIndex, "权限与最新 GPS 完成后才允许 punch");
assert.ok(trackingIndex > punchIndex, "服务端 punch 成功后必须立即执行 tracking 生命周期");
assert.ok(uiGateAfterTrackingIndex > trackingIndex,
  "tracking 生命周期必须位于服务端成功后的 UI session gate 之前");
assert.doesNotMatch(source, /parseAttendanceQrMetadata/,
  "AttendanceScreen 不得从二维码 payload 推断门店或设备");
assert.match(source, /const resetAttendanceScannerUi = \(\) => \{[\s\S]{0,300}setAttendanceScannerSubmitting\(false\)/,
  "关闭或显式重试必须清理 resolve/punch 提交态");

console.log("attendance-qr-ui-contract.test.ts: ok");
