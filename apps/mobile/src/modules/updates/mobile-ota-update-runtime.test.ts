import assert from "node:assert/strict";
import test from "node:test";

import {
  MOBILE_OTA_UPDATE_CENTER_BASE_URL,
  resolveMobileOtaRuntimeContext,
} from "./mobile-ota-update-runtime";

test("客户端 scope 必须直接来自原生 platform/channel/runtime/update identity", () => {
  assert.deepEqual(
    resolveMobileOtaRuntimeContext({
      platform: "ios",
      channel: "production",
      runtimeVersion: "1.0.2",
      updateId: "11111111-1111-4111-8111-111111111111",
      manifest: {
        metadata: {
          updateGroupId: "22222222-2222-4222-8222-222222222222",
        },
      },
    }),
    {
      apiBaseUrl: MOBILE_OTA_UPDATE_CENTER_BASE_URL,
      appKey: "mobile",
      platform: "iOS",
      clientChannel: "production",
      updateChannel: "production",
      runtimeVersion: "1.0.2",
      currentUpdateId: "11111111-1111-4111-8111-111111111111",
      currentUpdateGroupId: "22222222-2222-4222-8222-222222222222",
    },
  );
});

test("唯一 release channel 保留原值，并严格映射回当前平台的控制面 channel", () => {
  assert.deepEqual(
    resolveMobileOtaRuntimeContext({
      platform: "android",
      channel: "mobile-production-android-release-20260830t085910361z-54642a7a",
      runtimeVersion: "1.0.2",
      updateId: "11111111-1111-4111-8111-111111111111",
    }),
    {
      apiBaseUrl: MOBILE_OTA_UPDATE_CENTER_BASE_URL,
      appKey: "mobile",
      platform: "Android",
      clientChannel: "production",
      updateChannel: "mobile-production-android-release-20260830t085910361z-54642a7a",
      runtimeVersion: "1.0.2",
      currentUpdateId: "11111111-1111-4111-8111-111111111111",
      currentUpdateGroupId: null,
    },
  );
});

test("跨平台或格式异常的唯一 channel、非 iOS/Android 与空 Runtime 均拒绝", () => {
  for (const input of [
    { platform: "web", channel: "production", runtimeVersion: "1.0.2" },
    { platform: "android", channel: "rogue", runtimeVersion: "1.0.2" },
    {
      platform: "android",
      channel: "mobile-production-ios-release-20260830t085910361z-54642a7a",
      runtimeVersion: "1.0.2",
    },
    {
      platform: "android",
      channel: "mobile-production-android-release-",
      runtimeVersion: "1.0.2",
    },
    { platform: "android", channel: "preview", runtimeVersion: "" },
  ]) {
    assert.throws(() => resolveMobileOtaRuntimeContext({ ...input, updateId: null }), /Mobile OTA runtime/i);
  }
});
