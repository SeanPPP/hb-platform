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

test("下载后恢复当前 channel，用户确认 reload 时才切换并保留目标 channel", async () => {
  const trace: string[] = [];
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: "1.0.2",
    currentChannel: "production",
    updates: runtime(trace),
  });

  assert.deepEqual(await port.download(decision), {
    state: "downloaded",
    reason: null,
  });
  assert.deepEqual(trace, [
    `set:${decision.releaseChannel}`,
    "check",
    "fetch",
    "set:production",
  ]);
  await port.reload();
  assert.deepEqual(trace, [
    `set:${decision.releaseChannel}`,
    "check",
    "fetch",
    "set:production",
    `set:${decision.releaseChannel}`,
    "reload",
  ]);
});

test("check 或 fetch 任一 manifest 的 runtime/updateId 不匹配均拒绝", async () => {
  for (const stage of ["check", "fetch"] as const) {
    let reloads = 0;
    const port = new MobileOtaUpdatePort({
      enabled: true,
      runtimeVersion: decision.runtimeVersion,
      currentChannel: "production",
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

test("下载成功后旧 channel 恢复失败也 fail-closed", async () => {
  let reloads = 0;
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: decision.runtimeVersion,
    currentChannel: "production",
    updates: {
      setUpdateRequestHeadersOverride(headers) {
        if (headers?.["expo-channel-name"] === "production") {
          throw new Error("restore failed");
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
    reason: "channel-restore-failed",
  });
  await assert.rejects(() => port.reload(), /not ready/i);
  assert.equal(reloads, 0);
});

test("下载不可用时恢复当前正在运行的唯一 channel", async () => {
  const trace: string[] = [];
  const currentChannel = "mobile-production-android-release-previous";
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: decision.runtimeVersion,
    currentChannel,
    updates: {
      ...runtime(trace),
      async checkForUpdateAsync() {
        trace.push("check");
        return { isAvailable: false };
      },
    },
  });

  assert.deepEqual(await port.download(decision), {
    state: "unavailable",
    reason: "not-available",
  });
  assert.deepEqual(trace, [
    `set:${decision.releaseChannel}`,
    "check",
    `set:${currentChannel}`,
  ]);
});

test("reload 失败时恢复原 channel，并要求重新下载后才能再次 reload", async () => {
  const trace: string[] = [];
  const port = new MobileOtaUpdatePort({
    enabled: true,
    runtimeVersion: decision.runtimeVersion,
    currentChannel: "production",
    updates: {
      setUpdateRequestHeadersOverride(headers) {
        trace.push(headers ? `set:${headers["expo-channel-name"]}` : "clear");
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
        trace.push("reload");
        throw new Error("reload failed");
      },
    },
  });

  assert.deepEqual(await port.download(decision), {
    state: "downloaded",
    reason: null,
  });
  assert.equal(port.isReady(decision), true);
  await assert.rejects(() => port.reload(), /reload failed/);
  assert.equal(port.isReady(decision), false);
  assert.deepEqual(trace, [
    `set:${decision.releaseChannel}`,
    "check",
    "fetch",
    "set:production",
    `set:${decision.releaseChannel}`,
    "reload",
    "set:production",
  ]);
});
