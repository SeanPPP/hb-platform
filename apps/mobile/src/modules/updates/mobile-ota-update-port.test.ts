import assert from "node:assert/strict";
import test from "node:test";

import {
  MobileOtaUpdatePort,
  type MobileOtaUpdatesRuntimePort,
} from "./mobile-ota-update-port";

const decision = {
  state: "required" as const,
  policyVersion: "8",
  appKey: "mobile" as const,
  platform: "Android" as const,
  required: true,
  clientChannel: "production" as const,
  releaseChannel: "mobile-production-android-release-20260827-abcd",
  runtimeVersion: "1.0.2",
  updateId: "33333333-3333-4333-8333-333333333333",
  updateGroupId: "44444444-4444-4444-8444-444444444444",
  releaseMessage: "必须更新",
};

function runtime(trace: string[]): MobileOtaUpdatesRuntimePort {
  const manifest = {
    id: decision.updateId,
    runtimeVersion: decision.runtimeVersion,
  };
  return {
    setUpdateRequestHeadersOverride(headers) {
      trace.push(headers ? `set:${headers["expo-channel-name"]}` : "clear");
    },
    async checkForUpdateAsync() {
      trace.push("check");
      return { isAvailable: true, manifest };
    },
    async fetchUpdateAsync() {
      trace.push("fetch");
      return { isNew: true, manifest };
    },
    async reloadAsync() {
      trace.push("reload");
    },
  };
}

test("只在命中策略后临时 override，check/fetch 双验证后才标记已下载", async () => {
  const trace: string[] = [];
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: "1.0.2",
    updates: runtime(trace),
  });

  assert.deepEqual(await port.download(decision), {
    state: "downloaded",
    reason: null,
  });
  assert.deepEqual(trace, [
    "clear",
    `set:${decision.releaseChannel}`,
    "check",
    "fetch",
    "clear",
  ]);
  await port.reload();
  assert.equal(trace.at(-1), "reload");
});

test("check 或 fetch 任一 manifest 的 runtime/updateId 不匹配均拒绝", async () => {
  for (const stage of ["check", "fetch"] as const) {
    let reloads = 0;
    const port = new MobileOtaUpdatePort({
      enabled: true,
      runtimeVersion: decision.runtimeVersion,
      updates: {
        setUpdateRequestHeadersOverride() {},
        async checkForUpdateAsync() {
          return {
            isAvailable: true,
            manifest: {
              id: stage === "check" ? "wrong" : decision.updateId,
              runtimeVersion: decision.runtimeVersion,
            },
          };
        },
        async fetchUpdateAsync() {
          return {
            isNew: true,
            manifest: {
              id: stage === "fetch" ? "wrong" : decision.updateId,
              runtimeVersion: decision.runtimeVersion,
            },
          };
        },
        async reloadAsync() {
          reloads += 1;
        },
      },
    });

    assert.deepEqual(await port.download(decision), {
      state: "rejected",
      reason: "update-id-mismatch",
    });
    await assert.rejects(() => port.reload(), /not ready/i);
    assert.equal(reloads, 0);
  }
});

test("override 清理失败优先 fail-closed，绝不允许 reload", async () => {
  let overrideSet = false;
  let reloads = 0;
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: decision.runtimeVersion,
    updates: {
      setUpdateRequestHeadersOverride(headers) {
        if (headers) {
          overrideSet = true;
        } else if (overrideSet) {
          throw new Error("clear failed");
        }
      },
      async checkForUpdateAsync() {
        return {
          isAvailable: true,
          manifest: { id: decision.updateId, runtimeVersion: decision.runtimeVersion },
        };
      },
      async fetchUpdateAsync() {
        return {
          isNew: true,
          manifest: { id: decision.updateId, runtimeVersion: decision.runtimeVersion },
        };
      },
      async reloadAsync() {
        reloads += 1;
      },
    },
  });

  assert.deepEqual(await port.download(decision), {
    state: "rejected",
    reason: "channel-clear-failed",
  });
  await assert.rejects(() => port.reload(), /not ready/i);
  assert.equal(reloads, 0);
});

test("冷启动清理首次失败后，用户下载重试会先重新清理并在成功后继续", async () => {
  const trace: string[] = [];
  let clearAttempts = 0;
  const updates = runtime(trace);
  const originalSetOverride = updates.setUpdateRequestHeadersOverride;
  updates.setUpdateRequestHeadersOverride = (headers) => {
    if (!headers) {
      clearAttempts += 1;
      trace.push(`clear-attempt:${clearAttempts}`);
      if (clearAttempts === 1) throw new Error("startup clear failed once");
      return;
    }
    originalSetOverride(headers);
  };
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: decision.runtimeVersion,
    updates,
  });

  assert.deepEqual(await port.download(decision), {
    state: "downloaded",
    reason: null,
  });
  assert.deepEqual(trace, [
    "clear-attempt:1",
    "clear-attempt:2",
    `set:${decision.releaseChannel}`,
    "check",
    "fetch",
    "clear-attempt:3",
  ]);
});

test("冷启动清理重试仍失败时不 check/reload，下一次下载仍可再次清理恢复", async () => {
  const trace: string[] = [];
  let clearAttempts = 0;
  let reloads = 0;
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: decision.runtimeVersion,
    updates: {
      setUpdateRequestHeadersOverride(headers) {
        if (headers) {
          trace.push(`set:${headers["expo-channel-name"]}`);
          return;
        }
        clearAttempts += 1;
        trace.push(`clear-attempt:${clearAttempts}`);
        if (clearAttempts <= 2) throw new Error("clear still failed");
      },
      async checkForUpdateAsync() {
        trace.push("check");
        return {
          isAvailable: true,
          manifest: { id: decision.updateId, runtimeVersion: decision.runtimeVersion },
        };
      },
      async fetchUpdateAsync() {
        trace.push("fetch");
        return {
          isNew: true,
          manifest: { id: decision.updateId, runtimeVersion: decision.runtimeVersion },
        };
      },
      async reloadAsync() {
        reloads += 1;
      },
    },
  });

  assert.deepEqual(await port.download(decision), {
    state: "rejected",
    reason: "channel-clear-failed",
  });
  assert.deepEqual(trace, ["clear-attempt:1", "clear-attempt:2"]);
  assert.equal(reloads, 0);

  assert.deepEqual(await port.download(decision), {
    state: "downloaded",
    reason: null,
  });
  assert.deepEqual(trace, [
    "clear-attempt:1",
    "clear-attempt:2",
    "clear-attempt:3",
    `set:${decision.releaseChannel}`,
    "check",
    "fetch",
    "clear-attempt:4",
  ]);
  assert.equal(reloads, 0);
});
