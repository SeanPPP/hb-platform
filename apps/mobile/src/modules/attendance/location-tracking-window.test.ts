import assert from "node:assert/strict";
import {
  ATTENDANCE_LOCATION_MAX_TRACKING_MS,
  evaluateAttendanceLocationBatch,
  type AttendanceLocationTrackingWindow,
} from "./location-tracking-window";

const sydneyMorningShift: AttendanceLocationTrackingWindow = {
  startedAtUtc: "2026-07-23T22:00:00",
  workDate: "2026-07-24",
  storeTimeZone: "Australia/Sydney",
};

assert.equal(
  ATTENDANCE_LOCATION_MAX_TRACKING_MS,
  10 * 60 * 60 * 1000,
  "未下班定位上限必须固定为 10 小时",
);

assert.deepEqual(
  evaluateAttendanceLocationBatch(
    sydneyMorningShift,
    ["2026-07-23T22:20:00Z"],
    "2026-07-23T22:20:10Z",
  ),
  {
    shouldStop: false,
    capturedAtUtc: "2026-07-23T22:20:00Z",
  },
  "上班后且未达到截止时间的样本应继续上传",
);

assert.deepEqual(
  evaluateAttendanceLocationBatch(
    sydneyMorningShift,
    ["2026-07-24T07:59:00Z", "2026-07-24T08:00:00Z"],
    "2026-07-24T08:00:00Z",
  ),
  {
    shouldStop: true,
    capturedAtUtc: "2026-07-24T07:59:00Z",
  },
  "达到 10 小时时必须停止，并排除截止点及其后的样本",
);

const sydneyEveningShift: AttendanceLocationTrackingWindow = {
  startedAtUtc: "2026-07-24T10:00:00Z",
  workDate: "2026-07-24",
  storeTimeZone: "Australia/Sydney",
};

assert.deepEqual(
  evaluateAttendanceLocationBatch(
    sydneyEveningShift,
    ["2026-07-24T13:40:00Z", "2026-07-24T14:00:00Z"],
    "2026-07-24T14:00:00Z",
  ),
  {
    shouldStop: true,
    capturedAtUtc: "2026-07-24T13:40:00Z",
  },
  "跨过门店本地午夜时必须停止，并且午夜样本不得上传",
);

assert.deepEqual(
  evaluateAttendanceLocationBatch(
    sydneyMorningShift,
    ["2026-07-23T21:59:59Z"],
    "2026-07-23T22:05:00Z",
  ),
  {
    shouldStop: false,
    capturedAtUtc: undefined,
  },
  "上班打卡之前的延迟样本不得混入班中轨迹",
);

assert.deepEqual(
  evaluateAttendanceLocationBatch(
    {
      ...sydneyMorningShift,
      storeTimeZone: "Invalid/Zone",
    },
    ["2026-07-23T22:20:00Z"],
    "2026-07-23T22:20:10Z",
  ),
  {
    shouldStop: true,
    capturedAtUtc: undefined,
  },
  "无法确认门店日期时必须安全停止，不能继续记录位置",
);

assert.deepEqual(
  evaluateAttendanceLocationBatch(
    {
      startedAtUtc: "2026-11-01T04:30:00Z",
      workDate: "2026-11-01",
      storeTimeZone: "America/New_York",
    },
    ["2026-11-01T06:30:00Z"],
    "2026-11-01T06:30:05Z",
  ),
  {
    shouldStop: false,
    capturedAtUtc: "2026-11-01T06:30:00Z",
  },
  "DST 回拨日必须按 UTC 时长和门店本地日期判断",
);

console.log("location-tracking-window.test.ts: ok");
