import assert from "node:assert/strict";
import test from "node:test";

import { RuntimeWorkController } from "./runtime-work-controller";

test("启动和前台同时触发时外设 drain 单飞，同步各走对应耐久入口", async () => {
  const calls: string[] = [];
  let releaseHardware: (() => void) | undefined;
  const hardwarePending = new Promise<void>((resolve) => {
    releaseHardware = resolve;
  });
  const controller = new RuntimeWorkController({
    sync: {
      async onApplicationStarted() {
        calls.push("sync-start");
      },
      async onForeground() {
        calls.push("sync-foreground");
      },
      async onNetworkChanged(isOnline) {
        calls.push(`sync-network:${isOnline}`);
      },
    },
    fulfilment: {
      async drainAutomaticQueue() {
        calls.push("hardware");
        await hardwarePending;
      },
    },
    appUpdates: {
      async refreshOnStartup() {
        calls.push("updates-start");
      },
      async refreshOnForeground() {
        calls.push("updates-foreground");
      },
      async refreshOnNetworkAvailable() {
        calls.push("updates-network");
      },
    },
  });

  const started = controller.onApplicationStarted();
  const foreground = controller.onForeground();
  await Promise.resolve();

  assert.deepEqual(calls, [
    "sync-start",
    "hardware",
    "sync-foreground",
  ]);
  releaseHardware?.();
  await Promise.all([started, foreground]);
  assert.deepEqual(calls, [
    "sync-start",
    "hardware",
    "sync-foreground",
    "updates-start",
    "updates-foreground",
  ]);
});

test("联网变化只触发同步协调器，不把打印或钱箱与网络状态错误绑定", async () => {
  const calls: string[] = [];
  const controller = new RuntimeWorkController({
    sync: {
      async onApplicationStarted() {},
      async onForeground() {},
      async onNetworkChanged(isOnline) {
        calls.push(`network:${isOnline}`);
      },
    },
    fulfilment: {
      async drainAutomaticQueue() {
        calls.push("hardware");
      },
    },
    appUpdates: {
      async refreshOnStartup() {
        calls.push("updates-start");
      },
      async refreshOnForeground() {
        calls.push("updates-foreground");
      },
      async refreshOnNetworkAvailable() {
        calls.push("updates-network");
      },
    },
  });

  await controller.onNetworkChanged(false);
  await controller.onNetworkChanged(true);

  assert.deepEqual(calls, [
    "network:false",
    "network:true",
    "updates-network",
  ]);
});

test("任一后台域失败不阻断有旧 device key 的人脸耐久同步", async () => {
  const calls: string[] = [];
  const controller = new RuntimeWorkController({
    sync: { async onApplicationStarted() { throw new Error("order-sync"); }, async onForeground() { throw new Error("order-sync"); }, async onNetworkChanged() { throw new Error("order-sync"); } },
    fulfilment: { async drainAutomaticQueue() { throw new Error("hardware"); } },
    appUpdates: { async refreshOnStartup() { throw new Error("ota"); }, async refreshOnForeground() { throw new Error("ota"); }, async refreshOnNetworkAvailable() { throw new Error("ota"); } },
    faceAttendance: { async refresh() { calls.push("face-refresh"); throw new Error("roster"); }, async sync() { calls.push("face-sync"); } },
  });
  await assert.rejects(() => controller.onApplicationStarted(), AggregateError);
  await assert.rejects(() => controller.onForeground(), AggregateError);
  await assert.rejects(() => controller.onNetworkChanged(true), AggregateError);
  assert.deepEqual(calls, ["face-refresh", "face-sync", "face-refresh", "face-sync", "face-refresh", "face-sync"]);
});


test("仅 roster 刷新失败时仍同步人脸队列，并把刷新错误交给 runtime bridge", async () => {
  const calls: string[] = [];
  const controller = new RuntimeWorkController({
    sync: { async onApplicationStarted() { calls.push("order"); }, async onForeground() {}, async onNetworkChanged() {} },
    fulfilment: { async drainAutomaticQueue() { calls.push("hardware"); } },
    appUpdates: { async refreshOnStartup() { calls.push("ota"); }, async refreshOnForeground() {}, async refreshOnNetworkAvailable() {} },
    faceAttendance: { async refresh() { calls.push("face-refresh"); throw new Error("roster"); }, async sync() { calls.push("face-sync"); } },
  });
  await assert.rejects(() => controller.onApplicationStarted(), AggregateError);
  assert.deepEqual(calls, ["order", "hardware", "ota", "face-refresh", "face-sync"]);
});
