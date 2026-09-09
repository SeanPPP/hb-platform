import assert from "node:assert/strict";
import test from "node:test";
import { completeAttendancePostSave } from "./attendance-post-save";

test("已保存提示立即出现，刷新挂起不阻塞定位生命周期", async () => {
  const events: string[] = [];
  let finishTracking!: () => void;
  const completed = completeAttendancePostSave({
    notifySaved: () => { events.push("saved"); },
    refresh: async () => { events.push("refresh"); await new Promise(() => undefined); },
    track: async () => { events.push("tracking"); await new Promise<void>((resolve) => { finishTracking = resolve; }); },
    onRefreshError: () => { events.push("refreshFailed"); },
  });
  assert.equal(events[0], "saved");
  assert.ok(events.includes("tracking"));
  finishTracking();
  await completed;
});

test("刷新失败不会覆盖已保存结果，定位失败独立返回", async () => {
  const events: string[] = [];
  const trackingError = new Error("tracking failed");
  await assert.rejects(completeAttendancePostSave({
    notifySaved: () => { events.push("saved"); },
    refresh: async () => { throw new Error("refresh failed"); },
    track: async () => { throw trackingError; },
    onRefreshError: () => { events.push("refreshFailed"); },
  }), (error) => error === trackingError);
  assert.deepEqual(events, ["saved", "refreshFailed"]);
});

test("提示或刷新同步抛错也不能跳过必须执行的定位生命周期", async () => {
  let tracked = 0;
  await assert.rejects(completeAttendancePostSave({
    notifySaved: () => { throw new Error("feedback failed"); },
    refresh: () => { throw new Error("refresh failed"); },
    track: async () => { tracked += 1; },
    onRefreshError: () => undefined,
  }), /feedback failed/);
  assert.equal(tracked, 1);
});
