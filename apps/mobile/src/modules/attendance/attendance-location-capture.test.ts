import assert from "node:assert/strict";
import test from "node:test";
import {
  createAttendanceLocationCapture,
  type AttendanceLocationFix,
} from "./attendance-location-capture";

const fix = (timestamp: number): AttendanceLocationFix => ({
  timestamp,
  coords: { latitude: -27.47, longitude: 153.03, accuracy: 8 },
});

test("扫码预热只建立一个订阅，复用本次会话的新鲜定位", async () => {
  let now = 1_000;
  let emit!: (position: AttendanceLocationFix) => void;
  let starts = 0;
  let stops = 0;
  const capture = createAttendanceLocationCapture({
    now: () => now,
    watch: async (onPosition) => {
      starts += 1;
      emit = onPosition;
      return { remove: () => { stops += 1; } };
    },
  });
  capture.prewarm();
  await Promise.resolve();
  const first = capture.capture();
  const second = capture.capture();
  emit(fix(now));
  assert.equal((await first).timestamp, now);
  assert.equal((await second).timestamp, now);
  now += 500;
  assert.equal((await capture.capture()).timestamp, 1_000);
  assert.equal(starts, 1);
  capture.dispose();
  capture.dispose();
  assert.equal(stops, 1);
});

test("旧坐标、会话开始前坐标及异常坐标不得用于打卡", async () => {
  let now = 20_000;
  let emit!: (position: AttendanceLocationFix) => void;
  const capture = createAttendanceLocationCapture({
    now: () => now,
    watch: async (onPosition) => {
      emit = onPosition;
      return { remove() {} };
    },
  });
  let settled = false;
  const pending = capture.capture().then((value) => { settled = true; return value; });
  await Promise.resolve();
  emit(fix(1_000));
  emit({ ...fix(now), coords: { latitude: Number.NaN, longitude: 0 } });
  await Promise.resolve();
  assert.equal(settled, false);
  emit(fix(now));
  await pending;
  now += 11_000;
  settled = false;
  const fresh = capture.capture().then((value) => { settled = true; return value; });
  await Promise.resolve();
  assert.equal(settled, false);
  emit(fix(now));
  assert.equal((await fresh).timestamp, now);
  capture.dispose();
});

test("定位超时有界失败，停止订阅并拒绝迟到坐标", async () => {
  let emit!: (position: AttendanceLocationFix) => void;
  let stopped = 0;
  const capture = createAttendanceLocationCapture({
    timeoutMs: 15,
    watch: async (onPosition) => {
      emit = onPosition;
      return { remove: () => { stopped += 1; } };
    },
  });
  await assert.rejects(capture.capture(), { code: "LOCATION_TIMEOUT" });
  emit(fix(Date.now()));
  assert.equal(stopped, 1);
  await assert.rejects(capture.capture(), { code: "LOCATION_TIMEOUT" });
});

test("关闭相机时订阅尚未返回，也必须清理迟到订阅", async () => {
  let ready!: (subscription: { remove(): void }) => void;
  let stopped = 0;
  const capture = createAttendanceLocationCapture({
    watch: () => new Promise((resolve) => { ready = resolve; }),
  });
  const pending = capture.capture();
  capture.dispose();
  await assert.rejects(pending, { code: "LOCATION_CANCELLED" });
  ready({ remove: () => { stopped += 1; } });
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(stopped, 1);
});

test("预热权限失败被收敛，采集时返回原始错误", async () => {
  const denied = Object.assign(new Error("denied"), { code: "LOCATION_PERMISSION_REQUIRED" });
  const capture = createAttendanceLocationCapture({ watch: async () => { throw denied; } });
  capture.prewarm();
  await Promise.resolve();
  await assert.rejects(capture.capture(), (error) => error === denied);
  capture.dispose();
});
