import assert from "node:assert/strict";
import test from "node:test";

import {
  MOBILE_OTA_REQUIRED_CACHE_KEY,
  checkMobileOtaUpdate,
  getMobileOtaBoundaryMode,
  readCachedMobileOtaRequiredDecision,
  type MobileOtaUpdateContext,
  type MobileOtaUpdateStorage,
} from "./mobile-ota-update";

test("启用后的首次 render 在策略初始化前保持 checking，required 后不渲染业务内容", () => {
  assert.equal(
    getMobileOtaBoundaryMode({ enabled: true, initialized: false, state: null }),
    "checking",
  );
  assert.equal(
    getMobileOtaBoundaryMode({ enabled: true, initialized: true, state: "required" }),
    "required",
  );
  assert.equal(
    getMobileOtaBoundaryMode({ enabled: true, initialized: true, state: "optional" }),
    "content",
  );
});

const context: MobileOtaUpdateContext = {
  apiBaseUrl: "https://hotbargain.vip/api",
  appKey: "mobile",
  platform: "Android",
  clientChannel: "production",
  runtimeVersion: "1.0.2",
  currentUpdateId: "11111111-1111-4111-8111-111111111111",
  currentUpdateGroupId: null,
};

const requiredDecision = {
  state: "required" as const,
  policyVersion: "7",
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

function createMemoryStorage(): MobileOtaUpdateStorage & { records: Map<string, unknown> } {
  const records = new Map<string, unknown>();
  return {
    records,
    async getObject<T>(key: string) {
      return (records.get(key) as T | undefined) ?? null;
    },
    async setObject(key: string, value: unknown) {
      records.set(key, structuredClone(value));
    },
    async removeItem(key: string) {
      records.delete(key);
    },
  };
}

test("可信 required 会缓存，离线或失形响应继续门禁", async () => {
  const storage = createMemoryStorage();
  const online = await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => requiredDecision,
  });
  assert.equal(online.source, "server");
  assert.equal(online.decision?.state, "required");
  assert.ok(storage.records.has(MOBILE_OTA_REQUIRED_CACHE_KEY));

  const offline = await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => {
      throw new Error("offline");
    },
  });
  assert.equal(offline.source, "cache");
  assert.equal(offline.decision?.updateId, requiredDecision.updateId);
});

test("首次离线不得凭空锁死", async () => {
  const outcome = await checkMobileOtaUpdate({
    context,
    storage: createMemoryStorage(),
    fetchDecision: async () => {
      throw new Error("timeout");
    },
  });
  assert.equal(outcome.source, "none");
  assert.equal(outcome.decision, null);
});

test("required 缓存严格绑定中心/app/channel/platform/runtime/版本/目标", async () => {
  const storage = createMemoryStorage();
  await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => requiredDecision,
  });

  for (const changed of [
    { ...context, apiBaseUrl: "https://example.invalid/api" },
    { ...context, clientChannel: "preview" as const },
    { ...context, platform: "iOS" as const },
    { ...context, runtimeVersion: "2.0.0" },
  ]) {
    assert.equal(
      await readCachedMobileOtaRequiredDecision(storage, changed),
      null,
    );
  }
});

test("本地缓存即使自洽也不能把任意 release channel 伪装成 required", async () => {
  const storage = createMemoryStorage();
  await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => requiredDecision,
  });
  const cached = structuredClone(
    storage.records.get(MOBILE_OTA_REQUIRED_CACHE_KEY) as {
      decision: typeof requiredDecision;
      targetIdentity: string;
    },
  );
  cached.decision.releaseChannel = "mobile-preview-ios-release-attacker";
  cached.targetIdentity = JSON.stringify([
    cached.decision.policyVersion,
    cached.decision.releaseChannel,
    cached.decision.runtimeVersion,
    cached.decision.updateId,
    cached.decision.updateGroupId,
  ]);
  storage.records.set(MOBILE_OTA_REQUIRED_CACHE_KEY, cached);

  assert.equal(
    await readCachedMobileOtaRequiredDecision(storage, context),
    null,
  );
});

test("可信 none 与确认已运行目标 Update ID 都清除旧 required", async () => {
  const storage = createMemoryStorage();
  await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => requiredDecision,
  });

  const none = await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => ({
      state: "none",
      policyVersion: "9",
      appKey: "mobile",
      platform: "Android",
      required: false,
      clientChannel: "production",
      releaseChannel: null,
      runtimeVersion: "1.0.2",
      updateId: null,
      updateGroupId: null,
      releaseMessage: null,
    }),
  });
  assert.equal(none.decision?.state, "none");
  assert.equal(none.decision?.policyVersion, "9");
  assert.equal(storage.records.has(MOBILE_OTA_REQUIRED_CACHE_KEY), false);

  await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => requiredDecision,
  });
  const alreadyRunning = await checkMobileOtaUpdate({
    context: { ...context, currentUpdateId: requiredDecision.updateId },
    storage,
    fetchDecision: async () => requiredDecision,
  });
  assert.equal(alreadyRunning.alreadyRunningTarget, true);
  assert.equal(alreadyRunning.decision?.state, "none");
  assert.equal(storage.records.has(MOBILE_OTA_REQUIRED_CACHE_KEY), false);
});

test("冷启动已运行缓存目标时即使离线也清缓存，不得再次形成 required 门禁", async () => {
  const storage = createMemoryStorage();
  await checkMobileOtaUpdate({
    context,
    storage,
    fetchDecision: async () => requiredDecision,
  });

  const runningContext = {
    ...context,
    currentUpdateId: requiredDecision.updateId.toUpperCase(),
  };
  assert.equal(
    await readCachedMobileOtaRequiredDecision(storage, runningContext),
    null,
  );
  assert.equal(storage.records.has(MOBILE_OTA_REQUIRED_CACHE_KEY), false);

  const offline = await checkMobileOtaUpdate({
    context: runningContext,
    storage,
    fetchDecision: async () => {
      throw new Error("offline after verified reload");
    },
  });
  assert.equal(offline.source, "none");
  assert.equal(offline.decision, null);
});
