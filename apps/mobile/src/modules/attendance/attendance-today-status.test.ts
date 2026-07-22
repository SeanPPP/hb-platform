import assert from "node:assert/strict";
import { normalizeAttendanceToday } from "./attendance-today-normalization";
import { resolveAttendanceTodayStatus } from "./attendance-today-status";

const onBreak = normalizeAttendanceToday({
  workDate: "2026-07-21",
  nextPunchType: "ClockIn",
  canClockIn: true,
  canClockOut: false,
  schedules: [{
    scheduleGuid: "schedule-1",
    storeCode: "A",
    status: "Active",
    scheduleState: "OnBreak",
    segments: [{ segmentIndex: 1, clockIn: "2026-07-21T09:00", clockOut: "2026-07-21T12:00" }],
  }],
  punches: [{ punchGuid: "out-1", scheduleGuid: "schedule-1", punchType: "ClockOut", punchTimeLocal: "2026-07-21T12:00", status: "Normal" }],
});
assert.equal(resolveAttendanceTodayStatus(onBreak, true), "readyToClockIn");

const legacyOnBreak = normalizeAttendanceToday({
  workDate: "2026-07-21",
  nextPunchType: "ClockIn",
  canClockIn: false,
  canClockOut: false,
  schedules: [{
    scheduleGuid: "legacy-break",
    storeCode: "A",
    status: "Active",
    scheduleState: "OnBreak",
    segments: [{ segmentIndex: 1, clockIn: "2026-07-21T09:00", clockOut: "2026-07-21T12:00" }],
  }],
  punches: [
    { punchGuid: "legacy-in", scheduleGuid: "legacy-break", punchType: "ClockIn", punchTimeLocal: "2026-07-21T09:00", status: "Normal" },
    { punchGuid: "legacy-out", scheduleGuid: "legacy-break", punchType: "ClockOut", punchTimeLocal: "2026-07-21T12:00", status: "Normal" },
  ],
});
assert.equal(
  resolveAttendanceTodayStatus(legacyOnBreak, true),
  "readyToClockIn",
  "OnBreak 必须优先于旧响应的完成态回退，允许继续下一段上班",
);

const completed = normalizeAttendanceToday({
  workDate: "2026-07-21",
  nextPunchType: "ClockIn",
  canClockIn: true,
  canClockOut: false,
  schedules: [{ scheduleGuid: "done", storeCode: "A", status: "Active", scheduleState: "Completed", segments: [] }],
  punches: [{ punchGuid: "out-done", scheduleGuid: "done", punchType: "ClockOut", punchTimeLocal: "2026-07-21T17:00", status: "Normal" }],
});
assert.equal(resolveAttendanceTodayStatus(completed, true), "completed");

const legacyCompleted = normalizeAttendanceToday({
  workDate: "2026-07-21",
  nextPunchType: "ClockIn",
  canClockIn: false,
  canClockOut: false,
  schedules: [{ scheduleGuid: "legacy-done", storeCode: "A", status: "Active" }],
  punches: [
    { punchGuid: "legacy-done-in", scheduleGuid: "legacy-done", punchType: "ClockIn", punchTimeLocal: "2026-07-21T09:00", status: "Normal" },
    { punchGuid: "legacy-done-out", scheduleGuid: "legacy-done", punchType: "ClockOut", punchTimeLocal: "2026-07-21T17:00", status: "Normal" },
  ],
});
assert.equal(
  resolveAttendanceTodayStatus(legacyCompleted, true),
  "completed",
  "旧后端和 iOS Review 的完整 in/out 且双操作关闭时必须显示已完成",
);

const working = normalizeAttendanceToday({
  workDate: "2026-07-21",
  nextPunchType: "ClockOut",
  canClockIn: false,
  canClockOut: true,
  schedules: [{ scheduleGuid: "working", storeCode: "A", status: "Active", scheduleState: "Working", segments: [] }],
});
assert.equal(resolveAttendanceTodayStatus(working, true), "readyToClockOut");
assert.equal(resolveAttendanceTodayStatus(working, false), "viewOnly");

console.log("attendance-today-status.test.ts: ok");
