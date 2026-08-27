import assert from "node:assert/strict";
import test from "node:test";

import { fetchMobileOtaUpdateDecision } from "./mobile-ota-update-api";

const context = {
  apiBaseUrl: "https://hotbargain.vip/api",
  appKey: "mobile" as const,
  platform: "iOS" as const,
  clientChannel: "production" as const,
  runtimeVersion: "1.0.2",
  currentUpdateId: "11111111-1111-4111-8111-111111111111",
  currentUpdateGroupId: "22222222-2222-4222-8222-222222222222",
};

test("Mobile OTA 决策请求只发送最小匿名上下文并严格校验回显", async () => {
  let request:
    | {
        url: string;
        params: Record<string, unknown>;
        headers: Record<string, string>;
        timeout: number;
      }
    | undefined;
  const decision = await fetchMobileOtaUpdateDecision(
    context,
    async (url, config) => {
      request = {
        url,
        params: config.params,
        headers: config.headers,
        timeout: config.timeout,
      };
      return {
        data: {
          success: true,
          data: {
            state: "required",
            policyVersion: "4",
            appKey: "mobile",
            platform: "iOS",
            required: true,
            clientChannel: "production",
            releaseChannel: "mobile-production-ios-release-20260827-abcd",
            runtimeVersion: "1.0.2",
            updateId: "33333333-3333-4333-8333-333333333333",
            updateGroupId: "44444444-4444-4444-8444-444444444444",
            releaseMessage: "必须更新",
          },
        },
      };
    },
  );

  assert.equal(request?.url, "https://hotbargain.vip/api/app-updates/mobile-ota");
  assert.deepEqual(request?.params, {
    platform: "iOS",
    clientChannel: "production",
    runtimeVersion: "1.0.2",
    currentUpdateId: context.currentUpdateId,
    currentUpdateGroupId: context.currentUpdateGroupId,
  });
  assert.deepEqual(request?.headers, { Accept: "application/json" });
  assert.equal(request?.timeout, 8_000);
  assert.equal(decision.state, "required");
  assert.equal(decision.releaseChannel, "mobile-production-ios-release-20260827-abcd");
});

test("none 必须精确为空目标且回显当前 Runtime", async () => {
  const decision = await fetchMobileOtaUpdateDecision(context, async () => ({
    data: {
      state: "none",
      policyVersion: "none",
      appKey: "mobile",
      platform: "iOS",
      required: false,
      clientChannel: "production",
      releaseChannel: null,
      runtimeVersion: "1.0.2",
      updateId: null,
      updateGroupId: null,
      releaseMessage: null,
    },
  }));

  assert.equal(decision.state, "none");
});

test("disabled/runtime-mismatch/already-current 的 none 可回显正整数 policyVersion", async () => {
  const decision = await fetchMobileOtaUpdateDecision(context, async () => ({
    data: {
      state: "none",
      policyVersion: "12",
      appKey: "mobile",
      platform: "iOS",
      required: false,
      clientChannel: "production",
      releaseChannel: null,
      runtimeVersion: "1.0.2",
      updateId: null,
      updateGroupId: null,
      releaseMessage: null,
    },
  }));
  assert.equal(decision.policyVersion, "12");
});

test("失形响应、跨 scope 回显与任意 channel 均 fail-closed", async () => {
  const active = {
    state: "optional",
    policyVersion: "5",
    appKey: "mobile",
    platform: "iOS",
    required: false,
    clientChannel: "production",
    releaseChannel: "mobile-production-ios-release-20260827-abcd",
    runtimeVersion: "1.0.2",
    updateId: "33333333-3333-4333-8333-333333333333",
    updateGroupId: "44444444-4444-4444-8444-444444444444",
    releaseMessage: null,
  };

  for (const payload of [
    { ...active, platform: "Android" },
    { ...active, clientChannel: "preview" },
    { ...active, runtimeVersion: "2.0.0" },
    { ...active, releaseChannel: "attacker-channel" },
    { ...active, releaseChannel: "mobile-production-ios-release-" },
    { ...active, updateId: ` ${active.updateId}` },
    { ...active, updateGroupId: `${active.updateGroupId} ` },
    { ...active, debug: true },
    { ...active, state: "required", required: false },
  ]) {
    await assert.rejects(
      () => fetchMobileOtaUpdateDecision(context, async () => ({ data: payload })),
      /Mobile OTA decision/i,
    );
  }
});
