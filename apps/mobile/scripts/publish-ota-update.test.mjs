import assert from "node:assert/strict";

import {
  buildOtaRegistrationPayload,
  buildRegistrationUrl,
  parseEasUpdateOutput,
  registerOtaUpdate,
} from "./publish-ota-update.mjs";

const sampleOutput = `
Branch             preview
Runtime version    1.0.1
Update group ID    11111111-2222-3333-4444-555555555555
Android update ID  aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
Message            提示测试机下载安装新版 APK
Commit             abcdef1234567890
EAS Dashboard      https://expo.dev/accounts/example/projects/hbweb-expo/updates/11111111-2222-3333-4444-555555555555
`;

const parsed = parseEasUpdateOutput(sampleOutput);

assert.equal(parsed.branch, "");
assert.equal(parsed.runtimeVersion, "");
assert.equal(parsed.updateGroupId, "");
assert.equal(parsed.androidUpdateId, "");
assert.equal(parsed.message, "");
assert.equal(parsed.gitCommitHash, "");
assert.equal(parsed.dashboardUrl, "");

const jsonOutput = JSON.stringify({
  branch: "preview",
  runtimeVersion: "1.0.1",
  group: {
    id: "22222222-3333-4444-5555-666666666666",
  },
  message: "JSON OTA 发布",
  gitCommitHash: "fedcba9876543210",
  dashboardUrl:
    "https://expo.dev/accounts/example/projects/hbweb-expo/updates/22222222-3333-4444-5555-666666666666",
  updates: [
    {
      id: "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb",
      platform: "android",
      runtimeVersion: "1.0.1",
    },
  ],
});
const parsedJson = parseEasUpdateOutput(jsonOutput);

assert.equal(parsedJson.branch, "preview");
assert.equal(parsedJson.runtimeVersion, "1.0.1");
assert.equal(parsedJson.updateGroupId, "22222222-3333-4444-5555-666666666666");
assert.equal(parsedJson.androidUpdateId, "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb");
assert.equal(parsedJson.message, "JSON OTA 发布");
assert.equal(parsedJson.gitCommitHash, "fedcba9876543210");
assert.equal(
  parsedJson.dashboardUrl,
  "https://expo.dev/accounts/example/projects/hbweb-expo/updates/22222222-3333-4444-5555-666666666666"
);

const updatesOnlyJsonOutput = JSON.stringify({
  branch: "preview",
  updates: [
    {
      id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      platform: "android",
      runtimeVersion: "1.0.1",
    },
  ],
});
const parsedUpdatesOnlyJson = parseEasUpdateOutput(updatesOnlyJsonOutput);

assert.equal(parsedUpdatesOnlyJson.branch, "preview");
assert.equal(parsedUpdatesOnlyJson.runtimeVersion, "1.0.1");
assert.equal(parsedUpdatesOnlyJson.updateGroupId, "");
assert.equal(parsedUpdatesOnlyJson.androidUpdateId, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

const updatesOnlyPayload = buildOtaRegistrationPayload(
  parsedUpdatesOnlyJson,
  {
    channel: "preview",
    profile: "preview",
    runtimeVersion: "1.0.1",
    message: "fallback message",
  },
  "2026-06-22T00:00:00.000Z"
);

assert.equal(updatesOnlyPayload.updateGroupId, null);
assert.equal(updatesOnlyPayload.androidUpdateId, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

const payload = buildOtaRegistrationPayload(
  parsedJson,
  {
    channel: "preview",
    profile: "preview",
    runtimeVersion: "1.0.1",
    message: "fallback message",
  },
  "2026-06-22T00:00:00.000Z"
);

assert.deepEqual(payload, {
  updateGroupId: "22222222-3333-4444-5555-666666666666",
  androidUpdateId: "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb",
  channel: "preview",
  branch: "preview",
  platform: "android",
  runtimeVersion: "1.0.1",
  message: "JSON OTA 发布",
  gitCommitHash: "fedcba9876543210",
  dashboardUrl: "https://expo.dev/accounts/example/projects/hbweb-expo/updates/22222222-3333-4444-5555-666666666666",
  publishedAt: "2026-06-22T00:00:00.000Z",
  isRollback: false,
  rollbackOfGroupId: null,
});

const manualTextPayload = buildOtaRegistrationPayload(
  parsed,
  {
    channel: "preview",
    profile: "preview",
    runtimeVersion: "1.0.1",
    message: "fallback message",
  },
  "2026-06-22T00:00:00.000Z"
);

assert.equal(manualTextPayload.updateGroupId, null);
assert.equal(manualTextPayload.androidUpdateId, null);
assert.equal(manualTextPayload.runtimeVersion, "1.0.1");
assert.equal(manualTextPayload.message, "fallback message");

assert.equal(
  buildRegistrationUrl("https://hotbargain.vip"),
  "https://hotbargain.vip/api/mobile-app-builds/ota-updates"
);
assert.equal(
  buildRegistrationUrl("https://hotbargain.vip/api"),
  "https://hotbargain.vip/api/mobile-app-builds/ota-updates"
);

const originalFetch = globalThis.fetch;
const originalBaseUrl = process.env.HBWEB_API_BASE_URL;
const originalToken = process.env.HBWEB_API_TOKEN;

process.env.HBWEB_API_BASE_URL = "https://hotbargain.vip";
process.env.HBWEB_API_TOKEN = "test-token";

try {
  globalThis.fetch = async () => ({
    ok: true,
    status: 200,
    statusText: "OK",
    text: async () =>
      JSON.stringify({
        success: false,
        message: "UpdateGroupId 必须是 EAS update group UUID",
        errorCode: "INVALID_UPDATE_GROUP_ID",
      }),
  });

  await assert.rejects(
    registerOtaUpdate(payload),
    /后台登记失败：UpdateGroupId 必须是 EAS update group UUID \/ INVALID_UPDATE_GROUP_ID/,
  );

  globalThis.fetch = async () => ({
    ok: true,
    status: 200,
    statusText: "OK",
    text: async () =>
      JSON.stringify({
        success: true,
        data: {
          updateGroupId: payload.updateGroupId,
        },
      }),
  });

  const registerResult = await registerOtaUpdate(payload);
  assert.deepEqual(registerResult, {
    skipped: false,
    url: "https://hotbargain.vip/api/mobile-app-builds/ota-updates",
  });
} finally {
  globalThis.fetch = originalFetch;
  if (originalBaseUrl === undefined) {
    delete process.env.HBWEB_API_BASE_URL;
  } else {
    process.env.HBWEB_API_BASE_URL = originalBaseUrl;
  }
  if (originalToken === undefined) {
    delete process.env.HBWEB_API_TOKEN;
  } else {
    process.env.HBWEB_API_TOKEN = originalToken;
  }
}

console.log("publish OTA update script tests passed.");
