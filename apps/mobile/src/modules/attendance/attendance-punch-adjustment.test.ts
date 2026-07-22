import assert from "node:assert/strict";
import {
  canRequestAttendanceAdjustmentForDate,
  buildAttendancePunchAdjustmentResetKey,
  normalizeAttendancePunchAdjustmentPreview,
  buildAttendancePunchAdjustmentFingerprint,
  buildAttendancePunchAdjustmentPayload,
  createAttendanceAdjustmentRequestGate,
  normalizeAttendancePunchAdjustment,
  runLatestAttendanceAdjustmentRequest,
  validateAttendancePunchAdjustment,
} from "./attendance-punch-adjustment";
import { normalizeAttendanceToday } from "./attendance-today-normalization";

async function main() {

assert.equal(canRequestAttendanceAdjustmentForDate("2026-07-19", "2026-07-21"), true);
assert.equal(canRequestAttendanceAdjustmentForDate("2026-07-18", "2026-07-21"), false);
assert.equal(canRequestAttendanceAdjustmentForDate("2026-07-22", "2026-07-21"), false);
assert.equal(
  canRequestAttendanceAdjustmentForDate("2026-10-03", "2026-10-05"),
  true,
  "日期窗口必须按 UTC 日序计算，不受夏令时切换影响",
);
assert.equal(canRequestAttendanceAdjustmentForDate("invalid", "2026-07-21"), false);

assert.deepEqual(validateAttendancePunchAdjustment({
  storeCode: "A",
  punchType: "ClockIn",
  requestedPunchTimeLocal: "2026-07-21T09:00",
  reason: "漏打卡",
}), []);
assert.deepEqual(validateAttendancePunchAdjustment({
  storeCode: "",
  punchType: "ClockOut",
  requestedPunchTimeLocal: "",
  reason: " ",
}), ["storeCode", "requestedPunchTimeLocal", "reason"]);

const fingerprintPayload = {
  storeCode: "A",
  scheduleGuid: "schedule-1",
  originalPunchGuid: "punch-1",
  punchType: "ClockIn" as const,
  requestedPunchTimeLocal: "2026-07-21T09:00",
  reason: " 修正时间 ",
};
assert.equal(
  buildAttendancePunchAdjustmentFingerprint(fingerprintPayload),
  buildAttendancePunchAdjustmentFingerprint({ ...fingerprintPayload, reason: "修正时间" }),
  "原因首尾空格不应让已预览 payload 失效",
);
assert.notEqual(
  buildAttendancePunchAdjustmentFingerprint(fingerprintPayload),
  buildAttendancePunchAdjustmentFingerprint({ ...fingerprintPayload, storeCode: "B" }),
  "分店变化必须使预览失效",
);

const twoSchedulesToday = normalizeAttendanceToday({
  workDate: "2026-07-21",
  schedules: [
    { scheduleGuid: "schedule-1", storeCode: "A", status: "Active" },
    { scheduleGuid: "schedule-2", storeCode: "A", status: "Active" },
  ],
});
const secondScheduleMissingPunch = buildAttendancePunchAdjustmentPayload({
  storeCode: "A",
  today: twoSchedulesToday,
  scheduleGuid: "schedule-2",
  punchType: "ClockOut",
  requestedPunchTimeLocal: "2026-07-21T18:00",
  reason: "第二条排班漏打卡",
});
assert.equal(secondScheduleMissingPunch.scheduleGuid, "schedule-2");
assert.equal(secondScheduleMissingPunch.originalPunchGuid, undefined);
assert.equal(
  (secondScheduleMissingPunch as typeof secondScheduleMissingPunch & { requestedPunchTimeUtc?: string })
    .requestedPunchTimeUtc,
  new Date("2026-07-21T18:00").toISOString(),
  "补卡 payload 必须将手机本地输入转换为 requestedPunchTimeUtc",
);
assert.notEqual(
  buildAttendancePunchAdjustmentFingerprint(secondScheduleMissingPunch),
  buildAttendancePunchAdjustmentFingerprint({
    ...secondScheduleMissingPunch,
    requestedPunchTimeUtc: "2026-07-21T07:59:00.000Z",
  }),
  "UTC instant 变化必须使既有 preview 失效，避免提交另一瞬间",
);

const stableResetKey = buildAttendancePunchAdjustmentResetKey(
  "2026-07-21",
  "A",
  twoSchedulesToday.scheduleSessions,
);
assert.equal(
  stableResetKey,
  buildAttendancePunchAdjustmentResetKey(
    "2026-07-21",
    "A",
    [...twoSchedulesToday.scheduleSessions].reverse(),
  ),
  "React Query refetch 或返回顺序变化不能重置同一份补卡草稿",
);
assert.notEqual(
  stableResetKey,
  buildAttendancePunchAdjustmentResetKey("2026-07-21", "B", twoSchedulesToday.scheduleSessions),
  "切换分店必须重置草稿",
);
assert.notEqual(
  stableResetKey,
  buildAttendancePunchAdjustmentResetKey("2026-07-20", "A", twoSchedulesToday.scheduleSessions),
  "切换日期必须重置草稿",
);

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

const requestGate = createAttendanceAdjustmentRequestGate();
let currentFingerprint = "A";
const requestA = requestGate.begin("A");
const deferredA = deferred<string>();
const applied: string[] = [];
const errors: string[] = [];
const runA = runLatestAttendanceAdjustmentRequest({
  gate: requestGate,
  request: requestA,
  getCurrentFingerprint: () => currentFingerprint,
  operation: () => deferredA.promise,
  onSuccess: (value) => { applied.push(value); },
  onError: (error) => { errors.push(String(error)); },
});

currentFingerprint = "B";
const requestB = requestGate.begin("B");
const deferredB = deferred<string>();
const runB = runLatestAttendanceAdjustmentRequest({
  gate: requestGate,
  request: requestB,
  getCurrentFingerprint: () => currentFingerprint,
  operation: () => deferredB.promise,
  onSuccess: (value) => { applied.push(value); },
  onError: (error) => { errors.push(String(error)); },
});
deferredA.reject(new Error("A failed"));
deferredB.resolve("B applied");
await Promise.all([runA, runB]);
assert.deepEqual(applied, ["B applied"], "A 请求 resolve/reject 不得污染 B 草稿");
assert.deepEqual([...errors], []);
assert.equal(requestGate.isCurrent(requestB, currentFingerprint), false, "完成后必须清理 request id");

currentFingerprint = "C";
const requestC = requestGate.begin("C");
const deferredC = deferred<string>();
const runC = runLatestAttendanceAdjustmentRequest({
  gate: requestGate,
  request: requestC,
  getCurrentFingerprint: () => currentFingerprint,
  operation: () => deferredC.promise,
  onSuccess: (value) => { applied.push(value); },
  onError: (error) => { errors.push((error as Error).message); },
});
deferredC.reject(new Error("C failed"));
await runC;
assert.deepEqual(errors, ["C failed"], "当前草稿请求 reject 应正常落地错误");
assert.equal(requestGate.isCurrent(requestC, currentFingerprint), false);

const normalizedCreatedAt = normalizeAttendancePunchAdjustment({
  AdjustmentGuid: "adjustment-1",
  StoreCode: "A",
  PunchType: "ClockIn",
  RequestedPunchTimeLocal: "2026-07-21T09:00",
  RequestedPunchTimeUtc: "2026-07-20T23:00:00Z",
  Reason: "补卡",
  Status: "Pending",
  CreatedAt: "2026-07-21T10:00:00Z",
});
assert.equal(normalizedCreatedAt.submittedAt, "2026-07-21T10:00:00Z");
assert.equal(
  (normalizedCreatedAt as typeof normalizedCreatedAt & { requestedPunchTimeUtc?: string })
    .requestedPunchTimeUtc,
  "2026-07-20T23:00:00Z",
  "补卡 DTO normalizer 必须保留后端结构化 UTC instant",
);
assert.equal(
  normalizeAttendancePunchAdjustment({
    adjustmentGuid: "adjustment-2",
    storeCode: "A",
    punchType: "ClockOut",
    requestedPunchTimeLocal: "2026-07-21T17:00",
    reason: "补卡",
    status: "Applied",
    createdAt: "2026-07-21T11:00:00Z",
  }).submittedAt,
  "2026-07-21T11:00:00Z",
);

const preview = normalizeAttendancePunchAdjustmentPreview({
  IsValid: true,
  ExistingSession: {
    ScheduleGuid: "schedule-1",
    StoreCode: "A",
    StartTime: "09:00",
    EndTime: "17:00",
    WorkedMinutes: 400,
    CandidateOvertimeMinutes: 0,
    Segments: [{ SegmentIndex: 1, ClockIn: "2026-07-21T09:10", ClockOut: "2026-07-21T16:50", DurationMinutes: 400 }],
  },
  ProposedSession: {
    ScheduleGuid: "schedule-1",
    StoreCode: "A",
    StartTime: "09:00",
    EndTime: "17:00",
    WorkedMinutes: 480,
    CandidateOvertimeMinutes: 15,
    Segments: [{ SegmentIndex: 1, ClockIn: "2026-07-21T09:00", ClockOut: "2026-07-21T17:15", DurationMinutes: 495 }],
  },
  WorkedMinutesDelta: 80,
  CandidateOvertimeMinutesDelta: 15,
  WouldAutoApprove: true,
});

assert.equal(preview.isValid, true);
assert.equal(preview.existingSession?.workedMinutes, 400);
assert.equal(preview.proposedSession?.segments[0]?.segmentIndex, 1);
assert.equal(preview.workedMinutesDelta, 80);
assert.equal(preview.candidateOvertimeMinutesDelta, 15);
assert.equal(preview.wouldAutoApprove, true);

console.log("attendance-punch-adjustment.test.ts: ok");
}

void main();
