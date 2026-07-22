import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "PunchAdjustmentCard.tsx"), "utf8");

assert.match(source, /validateAttendancePunchAdjustment/);
assert.match(source, /onPreview/);
assert.match(source, /preview\.proposedSession/);
assert.match(source, /workedMinutesDelta/);
assert.match(source, /candidateOvertimeMinutesDelta/);
assert.match(source, /wouldAutoApprove/);
assert.match(source, /reason/);
assert.match(source, /isManagerDirect/);
assert.match(source, /onSubmit/);
assert.match(source, /today\?\.canRequestAdjustment\s*\?\?\s*isWithinSelfWindow/,
  "后端资格字段应优先，旧响应才回退本地 2 天窗口");
assert.doesNotMatch(source, /canOpenForm\s*=\s*isManagerStore\s*\|\|/,
  "店长身份不能绕过 2 天窗口");
assert.match(source, /buildAttendancePunchAdjustmentResetKey/,
  "补卡草稿只应由日期、分店和稳定排班键控制重置");
assert.match(source, /\}, \[resetKey\]\);/,
  "普通 Today refetch 不得触发补卡草稿重置");
assert.doesNotMatch(source, /\}, \[selectedDate, selectableSchedules, today\]\);/,
  "不得直接依赖 React Query 返回的 today 或排班数组引用");
assert.match(source, /setScheduleGuid\(selectableSchedules\[0\]\?\.scheduleGuid\)[\s\S]{0,300}setOriginalPunchGuid\(undefined\)[\s\S]{0,300}setPreview\(undefined\)[\s\S]{0,300}setLocalError\(""\)/,
  "日期、分店或排班集合变化时必须回到有效排班并清空原打卡、预览和错误");
assert.match(source, /previewFingerprint/);
assert.match(source, /buildAttendancePunchAdjustmentFingerprint\(payload\)/);
assert.match(source, /previewFingerprint\s*!==\s*buildAttendancePunchAdjustmentFingerprint\(payload\)/,
  "提交前必须确认表单 payload 与预览一致");
assert.match(source, /selectableSchedules\.map/,
  "同日多排班必须提供显式 schedule 选择");
assert.match(source, /setScheduleGuid\(session\.scheduleGuid\)/,
  "选择第二条排班必须写入 payload scheduleGuid");
assert.match(source, /previewRequestGateRef/);
assert.match(source, /submitRequestGateRef/);
assert.match(source, /runLatestAttendanceAdjustmentRequest/,
  "preview 与 submit 的 resolve/reject/cleanup 必须统一经过请求隔离器");
assert.match(source, /toAttendanceDeviceLocalTime\(punch\.punchTimeUtc\)/,
  "选择已有打卡必须由 UTC instant 回填到手机本地输入，而非直接使用门店 local 字符串");
assert.match(source, /requestedPunchTimeUtc/,
  "表单 payload 必须包含供 preview 和 submit 共用的 UTC instant");
assert.match(source, /preview\.previewRevision/,
  "提交必须读取当前 preview 返回的 revision");
assert.match(source, /previewRevision:\s*preview\.previewRevision/,
  "提交 payload 必须原样回传当前 preview revision");
assert.match(source, /previewRevisionMissing/,
  "缺少 preview revision 时必须显示明确错误");
assert.match(source, /disabled=\{isBusy \|\| !canSubmit\}/,
  "缺少或过期 preview revision 时必须禁用提交");

console.log("punch-adjustment-card-contract.test.ts: ok");
