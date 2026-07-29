import assert from "node:assert/strict";
import test from "node:test";

import { ExpoAppUpdateRestartPort } from "./expo-app-update-restart";

test("EAS 更新只有下载到新 bundle 后才 reload，并严格按 check/fetch/reload 顺序执行", async () => {
  const trace: string[] = [];
  const safety = {
    hasActiveCart: false,
    hasUnresolvedPayment: false,
    hasPendingDurableWrite: false,
  };
  const port = new ExpoAppUpdateRestartPort({
    getSafetySnapshot: async () => safety,
    updates: {
      async checkForUpdateAsync() {
        trace.push("check");
        return { isAvailable: true };
      },
      async fetchUpdateAsync() {
        trace.push("fetch");
        return { isNew: true };
      },
      async reloadAsync() {
        trace.push("reload");
      },
    },
  });

  assert.equal(await port.getSafetySnapshot(), safety);
  await port.restart();
  assert.deepEqual(trace, ["check", "fetch", "reload"]);
});

test("没有兼容 OTA 或 fetch 未得到新 bundle 时绝不 reload", async () => {
  let fetchCalls = 0;
  let reloadCalls = 0;
  const unavailable = new ExpoAppUpdateRestartPort({
    getSafetySnapshot: () => ({
      hasActiveCart: false,
      hasUnresolvedPayment: false,
      hasPendingDurableWrite: false,
    }),
    updates: {
      async checkForUpdateAsync() {
        return { isAvailable: false };
      },
      async fetchUpdateAsync() {
        fetchCalls += 1;
        return { isNew: true };
      },
      async reloadAsync() {
        reloadCalls += 1;
      },
    },
  });
  await unavailable.restart();

  const unchanged = new ExpoAppUpdateRestartPort({
    getSafetySnapshot: unavailable.getSafetySnapshot,
    updates: {
      async checkForUpdateAsync() {
        return { isAvailable: true };
      },
      async fetchUpdateAsync() {
        fetchCalls += 1;
        return { isNew: false };
      },
      async reloadAsync() {
        reloadCalls += 1;
      },
    },
  });
  await unchanged.restart();

  assert.equal(fetchCalls, 1);
  assert.equal(reloadCalls, 0);
});
