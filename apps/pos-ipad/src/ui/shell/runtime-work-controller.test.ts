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
    "updates-start",
    "sync-foreground",
    "updates-foreground",
  ]);
  releaseHardware?.();
  await Promise.all([started, foreground]);
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
