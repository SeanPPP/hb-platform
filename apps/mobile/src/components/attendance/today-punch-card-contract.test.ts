import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "TodayPunchCard.tsx"), "utf8");

assert.match(source, /buildAttendanceTodayDisplay\(today\)/,
  "今日考勤卡必须使用统一的分店/排班/班段展示模型");
assert.match(source, /display\.stores\.map/,
  "今日考勤卡必须按分店渲染，而不是只取第一条排班");
assert.match(source, /session\.segments\.map/,
  "同一排班必须渲染多个打卡班段");
assert.match(source, /isBreakAfter/,
  "中间 ClockOut 必须展示休息中状态");
assert.match(source, /relatedStoreReminders/,
  "跨店未下班/已上班必须显示关联提醒");
assert.match(source, /overtime\.candidateMinutes/,
  "必须展示后端给出的候选加班结果");
assert.match(source, /overtime\.approvedMinutes/,
  "必须展示后端给出的批准加班结果");
assert.match(source, /today\.timeline\.unscheduledPunches/,
  "未排班打卡必须显示明确分组标题");
assert.match(source, /clockInMinutes\s*!==\s*undefined/,
  "后端未返回上班异常分钟时只能显示状态，不能伪造 0 分钟");
assert.match(source, /clockOutMinutes\s*!==\s*undefined/,
  "后端未返回下班异常分钟时只能显示状态，不能伪造 0 分钟");
assert.doesNotMatch(
  source,
  /earlyArrivalMinutes\s*\?\?\s*segment\.clockIn\.lateMinutes\s*\?\?\s*0/,
  "上班异常分钟缺失时不得固定显示 0",
);
assert.doesNotMatch(
  source,
  /lateDepartureMinutes\s*\?\?\s*segment\.clockOut\.earlyLeaveMinutes\s*\?\?\s*0/,
  "下班异常分钟缺失时不得固定显示 0",
);
assert.match(source, /resolveAttendancePunchDisplayTime\(segment\.clockIn\)/,
  "今日卡打卡显示必须优先使用 punchTimeUtc 的手机本地格式化结果");
assert.match(source, /resolveAttendancePunchDisplayTime\(segment\.clockOut\)/,
  "今日卡下班时间也必须按 UTC instant 显示到手机本地");
assert.match(source, /formatTime\(session\.startTime\)[\s\S]{0,80}formatTime\(session\.endTime\)/,
  "排班开始结束时间属于门店本地 wall time，禁止经过手机时区转换");

console.log("today-punch-card-contract.test.ts: ok");
