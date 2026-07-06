import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";

import {
  buildOtaRegistrationPayload,
  buildRegistrationUrl,
  buildTokenPreflightUrl,
  getRequiredRegistrationGaps,
  parseEasUpdateOutput,
  preflightOtaRegistration,
  registerOtaUpdate,
  runPublishOtaUpdate,
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
assert.deepEqual(
  getRequiredRegistrationGaps(updatesOnlyPayload),
  ["updateGroupId"],
);

const updateListShapeJsonOutput = JSON.stringify({
  name: "preview",
  currentPage: [
    {
      branch: "preview",
      message: "提示测试机下载安装新版 APK",
      runtimeVersion: "1.0.1",
      group: "25cc3688-779d-4f66-82f0-2f7d6486586f",
      platforms: "android",
    },
  ],
});
const parsedUpdateListShapeJson = parseEasUpdateOutput(updateListShapeJsonOutput);

assert.equal(parsedUpdateListShapeJson.branch, "preview");
assert.equal(parsedUpdateListShapeJson.runtimeVersion, "1.0.1");
assert.equal(parsedUpdateListShapeJson.updateGroupId, "25cc3688-779d-4f66-82f0-2f7d6486586f");
assert.equal(parsedUpdateListShapeJson.androidUpdateId, "");

const updateListShapePayload = buildOtaRegistrationPayload(
  parsedUpdateListShapeJson,
  {
    channel: "preview",
    profile: "preview",
    runtimeVersion: "1.0.1",
    message: "fallback message",
  },
  "2026-06-22T00:00:00.000Z"
);

assert.equal(updateListShapePayload.updateGroupId, "25cc3688-779d-4f66-82f0-2f7d6486586f");
assert.equal(updateListShapePayload.androidUpdateId, null);
assert.deepEqual(getRequiredRegistrationGaps(updateListShapePayload), []);

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
assert.equal(
  buildTokenPreflightUrl("https://hotbargain.vip/api"),
  "https://hotbargain.vip/api/Auth/current"
);
assert.equal(
  buildTokenPreflightUrl("https://hotbargain.vip/api", "hbsvc_abcdefghijklmnopqrstuvwxyz"),
  "https://hotbargain.vip/api/service-api-tokens/current"
);

const originalFetch = globalThis.fetch;
const originalBaseUrl = process.env.HBWEB_API_BASE_URL;
const originalToken = process.env.HBWEB_API_TOKEN;
const silentLogger = {
  log() {},
  warn() {},
};
function createCollectingLogger() {
  const logs = [];
  const warns = [];

  return {
    logs,
    warns,
    logger: {
      log(message) {
        logs.push(String(message));
      },
      warn(message) {
        warns.push(String(message));
      },
    },
  };
}

const publishOptions = {
  channel: "preview",
  profile: "preview",
  runtimeVersion: "1.0.2",
  message: "JSON OTA 发布",
};
const legacyPublishOptions = {
  ...publishOptions,
  runtimeVersion: "1.0.1",
  message: "提示测试机下载安装新版 APK",
  nativeInstaller: "disabled",
};

process.env.HBWEB_API_BASE_URL = "https://hotbargain.vip";
process.env.HBWEB_API_TOKEN = "test-token";

try {
  let easRunCount = 0;
  delete process.env.HBWEB_API_BASE_URL;
  delete process.env.HBWEB_API_TOKEN;

  await runPublishOtaUpdate(
    { ...publishOptions, dryRun: true },
    {
      createDryRunOutputFn: async () => jsonOutput,
      logger: silentLogger,
      runCommandFn: async () => {
        easRunCount += 1;
        return { stdout: jsonOutput };
      },
    },
  );
  assert.equal(easRunCount, 0);

  await assert.rejects(
    runPublishOtaUpdate(publishOptions, {
      logger: silentLogger,
      runCommandFn: async () => {
        easRunCount += 1;
        return { stdout: jsonOutput };
      },
    }),
    /发布 OTA 前必须配置 HBWEB_API_BASE_URL 和 HBWEB_API_TOKEN/,
  );
  assert.equal(easRunCount, 0);

  await assert.rejects(
    runPublishOtaUpdate({ ...publishOptions, nativeInstaller: "maybe" }, {
      logger: silentLogger,
      runCommandFn: async () => {
        easRunCount += 1;
        return { stdout: jsonOutput };
      },
    }),
    /--native-installer 必须是 enabled 或 disabled/,
  );
  assert.equal(easRunCount, 0);

  await assert.rejects(
    runPublishOtaUpdate({ ...publishOptions, runtimeVersion: "1.0.1" }, {
      logger: silentLogger,
      runCommandFn: async () => {
        easRunCount += 1;
        return { stdout: jsonOutput };
      },
    }),
    /--runtime-version 1\.0\.1 是旧 APK 过渡 OTA，必须设置 --native-installer disabled/,
  );
  assert.equal(easRunCount, 0);

  process.env.HBWEB_API_BASE_URL = "https://hotbargain.vip";
  process.env.HBWEB_API_TOKEN = "invalid-token";
  globalThis.fetch = async () => ({
    ok: false,
    status: 401,
    statusText: "Unauthorized",
    text: async () => "invalid token",
  });

  await assert.rejects(
    runPublishOtaUpdate(publishOptions, {
      logger: silentLogger,
      runCommandFn: async () => {
        easRunCount += 1;
        return { stdout: jsonOutput };
      },
    }),
    /后台 token 验证失败：HTTP 401 Unauthorized - invalid token/,
  );
  assert.equal(easRunCount, 0);

  process.env.HBWEB_API_TOKEN = "readonly-token";
  globalThis.fetch = async () => ({
    ok: true,
    status: 200,
    statusText: "OK",
    text: async () => JSON.stringify({ success: true, data: { permissions: ["System.ViewAppDownloads"] } }),
  });

  await assert.rejects(
    runPublishOtaUpdate(publishOptions, {
      logger: silentLogger,
      runCommandFn: async () => {
        easRunCount += 1;
        return { stdout: jsonOutput };
      },
    }),
    /后台 token 验证失败：缺少 System.ManageAppDownloads 权限/,
  );
  assert.equal(easRunCount, 0);

  process.env.HBWEB_API_TOKEN = "admin-token";
  globalThis.fetch = async () => ({
    ok: true,
    status: 200,
    statusText: "OK",
    text: async () =>
      JSON.stringify({
        success: true,
        data: { roles: [{ roleName: "Admin" }], permissions: [] },
      }),
  });

  await preflightOtaRegistration();

  process.env.HBWEB_API_TOKEN = "hbsvc_missing_scope";
  globalThis.fetch = async () => ({
    ok: true,
    status: 200,
    statusText: "OK",
    text: async () =>
      JSON.stringify({
        success: true,
        data: { scopes: ["System.ViewAppDownloads"] },
      }),
  });

  await assert.rejects(
    preflightOtaRegistration(),
    /后台 token 验证失败：缺少 System.ManageAppDownloads scope/,
  );

  const serviceTokenPreflightCalls = [];
  process.env.HBWEB_API_TOKEN = "hbsvc_valid_token";
  globalThis.fetch = async (url, init) => {
    serviceTokenPreflightCalls.push({ url, init });
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      text: async () =>
        JSON.stringify({
          success: true,
          data: { scopes: ["System.ManageAppDownloads"] },
        }),
    };
  };

  await preflightOtaRegistration();
  assert.equal(serviceTokenPreflightCalls[0].url, "https://hotbargain.vip/api/service-api-tokens/current");
  assert.equal(serviceTokenPreflightCalls[0].init.method, "GET");
  assert.equal(serviceTokenPreflightCalls[0].init.headers.Authorization, "Bearer hbsvc_valid_token");

  const preflightCalls = [];
  process.env.HBWEB_API_TOKEN = "test-token";
  globalThis.fetch = async (url, init) => {
    preflightCalls.push({ url, init });
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      text: async () =>
        JSON.stringify({
          success: true,
          data: { permissions: ["System.ManageAppDownloads"] },
        }),
    };
  };

  await preflightOtaRegistration();
  assert.equal(preflightCalls[0].url, "https://hotbargain.vip/api/Auth/current");
  assert.equal(preflightCalls[0].init.method, "GET");
  assert.equal(preflightCalls[0].init.headers.Authorization, "Bearer test-token");

  const publishFetchCalls = [];
  globalThis.fetch = async (url, init) => {
    publishFetchCalls.push({ url, init });
    if (init.method === "GET") {
      return {
        ok: true,
        status: 200,
        statusText: "OK",
        text: async () =>
          JSON.stringify({
            success: true,
            data: { permissions: ["System.ManageAppDownloads"] },
          }),
      };
    }

    return {
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
    };
  };

  await runPublishOtaUpdate(publishOptions, {
    logger: silentLogger,
    runCommandFn: async () => {
      easRunCount += 1;
      return { stdout: jsonOutput };
    },
  });
  assert.equal(easRunCount, 1);
  assert.equal(publishFetchCalls[0].url, "https://hotbargain.vip/api/Auth/current");
  assert.equal(publishFetchCalls[0].init.headers.Authorization, "Bearer test-token");
  assert.equal(publishFetchCalls[1].url, "https://hotbargain.vip/api/mobile-app-builds/ota-updates");
  assert.equal(publishFetchCalls[1].init.headers.Authorization, "Bearer test-token");

  const defaultPublishLogger = createCollectingLogger();
  let defaultEasCommand = null;
  await runPublishOtaUpdate(publishOptions, {
    logger: defaultPublishLogger.logger,
    preflightOtaRegistrationFn: async () => {},
    registerOtaUpdateFn: async () => ({
      skipped: false,
      url: "https://hotbargain.vip/api/mobile-app-builds/ota-updates",
    }),
    runCommandFn: async (command) => {
      defaultEasCommand = command;
      return { stdout: jsonOutput };
    },
  });
  assert.equal(defaultEasCommand.env.EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED, "true");
  assert.equal(defaultEasCommand.env.HBWEB_API_TOKEN, undefined);
  assert.ok(
    defaultPublishLogger.logs.includes("- EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED=true"),
  );

  const disabledPublishLogger = createCollectingLogger();
  let disabledEasCommand = null;
  await runPublishOtaUpdate({ ...publishOptions, nativeInstaller: "disabled" }, {
    logger: disabledPublishLogger.logger,
    preflightOtaRegistrationFn: async () => {},
    registerOtaUpdateFn: async () => ({
      skipped: false,
      url: "https://hotbargain.vip/api/mobile-app-builds/ota-updates",
    }),
    runCommandFn: async (command) => {
      disabledEasCommand = command;
      return { stdout: jsonOutput };
    },
  });
  assert.equal(disabledEasCommand.env.EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED, "false");
  assert.equal(disabledEasCommand.env.HBWEB_API_TOKEN, undefined);
  assert.ok(
    disabledPublishLogger.logs.includes("- EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED=false"),
  );

  const legacyPublishLogger = createCollectingLogger();
  let legacyEasCommand = null;
  await runPublishOtaUpdate(legacyPublishOptions, {
    logger: legacyPublishLogger.logger,
    preflightOtaRegistrationFn: async () => {},
    registerOtaUpdateFn: async () => ({
      skipped: false,
      url: "https://hotbargain.vip/api/mobile-app-builds/ota-updates",
    }),
    runCommandFn: async (command) => {
      legacyEasCommand = command;
      return { stdout: jsonOutput };
    },
  });
  assert.equal(legacyEasCommand.env.EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED, "false");
  assert.equal(legacyEasCommand.env.EXPO_PUBLIC_RUNTIME_VERSION, "1.0.1");
  assert.equal(legacyEasCommand.env.HBWEB_API_TOKEN, undefined);
  assert.ok(
    legacyPublishLogger.logs.includes("- EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED=false"),
  );

  const serviceTokenPublishFetchCalls = [];
  process.env.HBWEB_API_TOKEN = "hbsvc_publish_token";
  globalThis.fetch = async (url, init) => {
    serviceTokenPublishFetchCalls.push({ url, init });
    if (init.method === "GET") {
      return {
        ok: true,
        status: 200,
        statusText: "OK",
        text: async () =>
          JSON.stringify({
            success: true,
            data: { scopes: ["System.ManageAppDownloads"] },
          }),
      };
    }

    return {
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
    };
  };

  await runPublishOtaUpdate(publishOptions, {
    logger: silentLogger,
    runCommandFn: async () => {
      easRunCount += 1;
      return { stdout: jsonOutput };
    },
  });
  assert.equal(easRunCount, 2);
  assert.equal(serviceTokenPublishFetchCalls[0].url, "https://hotbargain.vip/api/service-api-tokens/current");
  assert.equal(serviceTokenPublishFetchCalls[0].init.headers.Authorization, "Bearer hbsvc_publish_token");
  assert.equal(serviceTokenPublishFetchCalls[1].url, "https://hotbargain.vip/api/mobile-app-builds/ota-updates");
  assert.equal(serviceTokenPublishFetchCalls[1].init.headers.Authorization, "Bearer hbsvc_publish_token");

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

const packageJson = JSON.parse(
  await readFile(path.resolve("package.json"), "utf8"),
);
assert.match(
  packageJson.scripts["ota:legacy-apk-notice:preview"],
  /--native-installer disabled/,
);
assert.match(
  packageJson.scripts["ota:legacy-apk-notice:preview"],
  /--runtime-version 1\.0\.1/,
);
assert.match(
  packageJson.scripts["ota:legacy-apk-notice:production"],
  /--native-installer disabled/,
);
assert.match(
  packageJson.scripts["ota:legacy-apk-notice:production"],
  /--runtime-version 1\.0\.1/,
);

console.log("publish OTA update script tests passed.");
