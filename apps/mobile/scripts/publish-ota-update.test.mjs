import assert from "node:assert/strict";
import { Readable } from "node:stream";
import test from "node:test";

import {
  APP_OTA_PREFLIGHT_PATH,
  APP_OTA_REGISTER_PATH,
  EAS_CLI_VERSION,
  LEGACY_MOBILE_OTA_REGISTER_PATH,
  OtaPublishBatchError,
  assertReleaseChannelsUnused,
  buildEasChannelListCommand,
  buildEasChannelViewCommand,
  buildEasUpdateCommand,
  buildLegacyBootstrapPayload,
  buildLegacyRegistrationUrl,
  buildOtaReleasePayload,
  buildPreflightUrl,
  buildRegistrationUrl,
  createReleaseChannel,
  parseEasUpdateOutput,
  parseEasChannelViewOutput,
  parseEasChannelListOutput,
  parsePublishOtaArgs,
  preflightOtaRelease,
  readAccessTokenFromStdin,
  registerLegacyBootstrapUpdate,
  registerOtaRelease,
  resolvePublishAccessToken,
  runPublishMobileOtaRelease,
} from "./publish-ota-update.mjs";

const batchId = "153e4567-e89b-42d3-a456-426614174000";
const iosGroupId = "223e4567-e89b-42d3-a456-426614174000";
const iosUpdateId = "323e4567-e89b-42d3-a456-426614174000";
const androidGroupId = "423e4567-e89b-42d3-a456-426614174000";
const androidUpdateId = "523e4567-e89b-42d3-a456-426614174000";
const rollbackSourceId = "623e4567-e89b-42d3-a456-426614174000";
const serviceToken = "hbsvc_abcdefghijklmnopqrstuvwxyz";
const projectId = "3b37541e-6191-460d-9a57-fe6691e206cf";
const iosReleaseChannel = "mobile-production-ios-release-20260827t101500000z-a1b2c3d4";
const androidReleaseChannel = "mobile-production-android-release-20260827t101500000z-d4e5f6a7";
const liveEnvironment = Object.freeze({
  HBWEB_API_BASE_URL: "https://hotbargain.vip/api",
  HBWEB_API_TOKEN: serviceToken,
  HBWEB_OTA_ADMIN_JWT: "must-not-leak",
  HBWEB_APP_UPDATE_DECISION_READ_TOKEN: "must-not-leak",
  HBPOS_OTA_CENTER_ACCESS_TOKEN: "must-not-leak",
  THIRD_PARTY_SECRET: "must-not-leak",
  EXPO_TOKEN: "expo-auth-remains",
  expo_token: "lowercase-must-not-leak",
  ExPo_ToKeN: "mixed-case-must-not-leak",
  VENDOR_ACCESS_TOKEN: "vendor-must-not-leak",
});

function releaseChannelFor(platform, environment = "production") {
  if (environment === "preview") {
    return `mobile-preview-${platform}-release-20260827t101500000z-${platform === "ios" ? "a1b2c3d4" : "d4e5f6a7"}`;
  }
  return platform === "ios" ? iosReleaseChannel : androidReleaseChannel;
}

function createJsonOutput(platform, channel = releaseChannelFor(platform), overrides = {}) {
  const groupId = platform === "ios" ? iosGroupId : androidGroupId;
  const updateId = platform === "ios" ? iosUpdateId : androidUpdateId;
  return JSON.stringify([
    {
      id: updateId,
      createdAt: "2026-08-27T10:16:00.000Z",
      group: groupId,
      branch: channel,
      runtimeVersion: "1.0.2",
      platform,
      message: "修复订货流程",
      manifestPermalink: `https://expo.dev/projects/hbweb-expo/updates/${groupId}`,
      gitCommitHash: "abcdef1234567890",
      ...overrides,
    },
  ]);
}

function createChannelViewOutput(channel, overrides = {}, platformOverride = null) {
  const branchId = `${channel}-branch-id`;
  const platform = platformOverride ?? (channel.includes("-ios-") ? "ios" : "android");
  const groupId = platform === "ios" ? iosGroupId : androidGroupId;
  const updateId = platform === "ios" ? iosUpdateId : androidUpdateId;
  return JSON.stringify({
    currentPage: {
      id: `${channel}-channel-id`,
      name: channel,
      isPaused: false,
      branchMapping: JSON.stringify({
        data: [{ branchId, branchMappingLogic: "true" }],
        version: 0,
      }),
      updateBranches: [{
        id: branchId,
        name: channel,
        updateGroups: [[{
          id: updateId,
          group: groupId,
          createdAt: "2026-08-27T10:16:00.000Z",
          runtimeVersion: "1.0.2",
          platform,
          message: "修复订货流程",
          manifestPermalink: `https://expo.dev/projects/hbweb-expo/updates/${groupId}`,
          gitCommitHash: "abcdef1234567890",
        }]],
      }],
      ...overrides,
    },
  });
}

function createChannelListOutput(channels) {
  return JSON.stringify({
    currentPage: channels.map((name, index) => ({
      id: `channel-${index}`,
      name,
      isPaused: false,
    })),
  });
}

function createOptions(overrides = {}) {
  return {
    environment: "production",
    platform: "ios",
    runtimeVersion: "1.0.2",
    message: "修复订货流程",
    bootstrapLegacyFixedChannel: false,
    ...overrides,
  };
}

function createDependencies(overrides = {}) {
  return {
    environment: liveEnvironment,
    logger: { log() {}, warn() {} },
    accessToken: serviceToken,
    createReleaseBatchIdFn: () => batchId,
    createReleaseChannelFn: (environment, platform) =>
      releaseChannelFor(platform, environment),
    nowIsoFn: () => "2026-08-27T10:15:00.000Z",
    preflightOtaReleaseFn: async () => ({ valid: true }),
    assertReleaseChannelsUnusedFn: async () => undefined,
    registerOtaReleaseFn: async (payload) => ({
      release: { id: `${payload.platform}-release-id`, ...payload },
      idempotent: false,
    }),
    registerLegacyBootstrapUpdateFn: async (payload) => ({
      id: "723e4567-e89b-42d3-a456-426614174000",
      ...payload,
    }),
    runCommandFn: async (command) => {
      if (command.args[1] === "channel:view") {
        const channel = command.args[2];
        return { stdout: createChannelViewOutput(channel), stderr: "" };
      }
      const platform = command.args[command.args.indexOf("--platform") + 1];
      const channel = command.args[command.args.indexOf("--channel") + 1];
      return { stdout: createJsonOutput(platform, channel), stderr: "" };
    },
    readRecoveryManifestFn: async () => null,
    writeRecoveryManifestFn: async () => "/tmp/mobile-ota-recovery.json",
    ...overrides,
  };
}

test("CLI 强制 environment/platform/runtime/message，并支持 all 与 register-only", () => {
  assert.deepEqual(
    parsePublishOtaArgs([
      "--environment", "preview",
      "--platform", "all",
      "--runtime-version", "1.0.2",
      "--message", "修复订货流程",
      "--access-token-stdin",
    ]),
    {
      environment: "preview",
      platform: "all",
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      accessTokenStdin: true,
      dryRun: false,
      help: false,
      rollbackOfReleaseId: null,
      registerOnlyFile: null,
      bootstrapLegacyFixedChannel: false,
    },
  );
  assert.equal(
    parsePublishOtaArgs(["--register-only", "recovery.json", "--access-token-stdin"])
      .registerOnlyFile,
    "recovery.json",
  );
  assert.equal(
    parsePublishOtaArgs([
      "--bootstrap-legacy-fixed-channel",
      "--environment", "production",
      "--platform", "ios",
      "--runtime-version", "1.0.2",
      "--message", "安装受控 coordinator",
    ]).bootstrapLegacyFixedChannel,
    true,
  );
  for (const argv of [
    ["--platform", "ios", "--runtime-version", "1", "--message", "x"],
    ["--environment", "production", "--runtime-version", "1", "--message", "x"],
    ["--environment", "staging", "--platform", "ios", "--runtime-version", "1", "--message", "x"],
    ["--environment", "production", "--platform", "web", "--runtime-version", "1", "--message", "x"],
    ["--environment", "production", "--platform", "ios", "--runtime-version", "1", "--message", "x", "--rollback-of-release-id", "not-a-uuid"],
    ["--environment", "production", "--platform", "all", "--runtime-version", "1", "--message", "x", "--rollback-of-release-id", rollbackSourceId],
    ["--bootstrap-legacy-fixed-channel", "--environment", "production", "--platform", "all", "--runtime-version", "1", "--message", "x"],
    ["--bootstrap-legacy-fixed-channel", "--environment", "production", "--platform", "ios", "--runtime-version", "1", "--message", "x", "--rollback-of-release-id", rollbackSourceId],
    ["--bootstrap-legacy-fixed-channel", "--register-only", "recovery.json"],
    ["--environment", "production", "--platform", "ios", "--runtime-version", " 1.0.2", "--message", "x"],
    ["--environment", "production", "--platform", "ios", "--runtime-version", "x".repeat(121), "--message", "x"],
    ["--environment", "production", "--platform", "ios", "--runtime-version", "1.0.2", "--message", " x"],
    ["--environment", "production", "--platform", "ios", "--runtime-version", "1.0.2", "--message", "x".repeat(1_001)],
  ]) {
    assert.throws(
      () => parsePublishOtaArgs(argv),
      /environment|platform|runtime|message|rollback|register-only|bootstrap/i,
    );
  }
  assert.equal(
    parsePublishOtaArgs([
      "--environment", "production",
      "--platform", "ios",
      "--runtime-version", "x".repeat(120),
      "--message", "x".repeat(1_000),
    ]).message.length,
    1_000,
  );
});

test("bootstrap EAS 命令只接受当前 environment 固定 channel，仍固定 CLI 并剔除凭据", () => {
  const options = createOptions({
    environment: "preview",
    platform: "ios",
    releaseChannel: "preview",
    bootstrapLegacyFixedChannel: true,
  });
  const update = buildEasUpdateCommand(options, liveEnvironment);
  assert.deepEqual(update.args.slice(0, 7), [
    `eas-cli@${EAS_CLI_VERSION}`,
    "update",
    "--channel",
    "preview",
    "--platform",
    "ios",
    "--message",
  ]);
  assert.equal(update.env.HBWEB_API_TOKEN, undefined);
  assert.equal(update.env.EXPO_TOKEN, "expo-auth-remains");
  assert.equal(buildEasChannelViewCommand(options, liveEnvironment).args[2], "preview");
  assert.throws(
    () => buildEasUpdateCommand({ ...options, releaseChannel: "production" }),
    /channel/i,
  );
});

test("release channel 按 Mobile 环境和平台唯一派生", () => {
  assert.equal(
    createReleaseChannel("production", "ios", "2026-08-27T10:15:00.000Z", "A1B2C3D4-ffff"),
    iosReleaseChannel,
  );
  assert.equal(
    createReleaseChannel("preview", "android", "2026-08-27T10:15:00.000Z", "D4E5F6A7-ffff"),
    "mobile-preview-android-release-20260827t101500000z-d4e5f6a7",
  );
});

test("EAS 命令固定 CLI/平台/channel/runtime 并剔除所有后台凭据", () => {
  const command = buildEasUpdateCommand(
    createOptions({ releaseChannel: iosReleaseChannel }),
    liveEnvironment,
  );
  assert.deepEqual(command.args, [
    `eas-cli@${EAS_CLI_VERSION}`,
    "update",
    "--channel",
    iosReleaseChannel,
    "--platform",
    "ios",
    "--message",
    "修复订货流程",
    "--json",
    "--non-interactive",
  ]);
  assert.equal(command.env.EXPO_PUBLIC_APP_BUILD_PROFILE, "production");
  assert.equal(command.env.EXPO_PUBLIC_RUNTIME_VERSION, "1.0.2");
  assert.equal(command.env.HBWEB_API_TOKEN, undefined);
  assert.equal(command.env.HBWEB_OTA_ADMIN_JWT, undefined);
  assert.equal(command.env.HBWEB_APP_UPDATE_DECISION_READ_TOKEN, undefined);
  assert.equal(command.env.HBPOS_OTA_CENTER_ACCESS_TOKEN, undefined);
  assert.equal(command.env.THIRD_PARTY_SECRET, undefined);
  assert.equal(command.env.EXPO_TOKEN, "expo-auth-remains");
  assert.equal(command.env.expo_token, undefined);
  assert.equal(command.env.ExPo_ToKeN, undefined);
  assert.equal(command.env.VENDOR_ACCESS_TOKEN, undefined);

  const channelViewCommand = buildEasChannelViewCommand(
    createOptions({ releaseChannel: iosReleaseChannel }),
    liveEnvironment,
  );
  assert.deepEqual(channelViewCommand.args, [
    `eas-cli@${EAS_CLI_VERSION}`,
    "channel:view",
    iosReleaseChannel,
    "--json",
    "--non-interactive",
  ]);
  assert.equal(channelViewCommand.env.HBWEB_API_TOKEN, undefined);
  assert.equal(channelViewCommand.env.EXPO_TOKEN, "expo-auth-remains");
  assert.equal(channelViewCommand.env.expo_token, undefined);
  assert.equal(channelViewCommand.env.ExPo_ToKeN, undefined);
  assert.equal(channelViewCommand.env.VENDOR_ACCESS_TOKEN, undefined);

  const channelListCommand = buildEasChannelListCommand(
    createOptions({ releaseChannel: iosReleaseChannel }),
    25,
    liveEnvironment,
  );
  assert.deepEqual(channelListCommand.args, [
    `eas-cli@${EAS_CLI_VERSION}`,
    "channel:list",
    "--json",
    "--non-interactive",
    "--limit",
    "25",
    "--offset",
    "25",
  ]);
  assert.equal(channelListCommand.env.EXPO_TOKEN, "expo-auth-remains");
  assert.equal(channelListCommand.env.expo_token, undefined);
  assert.equal(channelListCommand.env.ExPo_ToKeN, undefined);
  assert.equal(channelListCommand.env.VENDOR_ACCESS_TOKEN, undefined);
  assert.deepEqual(
    parseEasChannelListOutput(createChannelListOutput(["production", "preview"])),
    ["production", "preview"],
  );
});

test("固定 EAS CLI 发布 JSON 与 channel:view 分别验证发布事实和 channel 到 branch 映射", () => {
  const parsed = parseEasUpdateOutput(createJsonOutput("ios"), "ios");
  assert.deepEqual(parsed, {
    updateGroupId: iosGroupId,
    updateId: iosUpdateId,
    channel: "",
    branch: iosReleaseChannel,
    platform: "ios",
    runtimeVersion: "1.0.2",
    message: "修复订货流程",
    gitCommitHash: "abcdef1234567890",
    dashboardUrl: `https://expo.dev/projects/hbweb-expo/updates/${iosGroupId}`,
    publishedAt: "2026-08-27T10:16:00.000Z",
  });
  assert.equal(parseEasUpdateOutput(createJsonOutput("android"), "ios").updateId, "");
  assert.equal(parseEasUpdateOutput(`Update group ${iosGroupId}`, "ios").updateId, "");

  assert.deepEqual(parseEasChannelViewOutput(
    createChannelViewOutput(iosReleaseChannel),
    iosReleaseChannel,
  ), {
    channel: iosReleaseChannel,
    branch: iosReleaseChannel,
    branchId: `${iosReleaseChannel}-branch-id`,
  });
  assert.throws(
    () => parseEasChannelViewOutput(
      createChannelViewOutput(iosReleaseChannel, { name: androidReleaseChannel }),
      iosReleaseChannel,
    ),
    /channel/i,
  );
});

test("不可变发布 payload 不携带投放策略", () => {
  const options = {
    releaseBatchId: batchId,
    environment: "production",
    releaseChannel: iosReleaseChannel,
    runtimeVersion: "1.0.2",
    message: "修复订货流程",
    platform: "ios",
    rollbackOfReleaseId: null,
  };
  const parsed = parseEasUpdateOutput(createJsonOutput("ios"), "ios");
  const payload = buildOtaReleasePayload(parsed, options);
  assert.deepEqual(payload, {
    releaseBatchId: batchId,
    appKey: "mobile",
    environment: "production",
    clientChannel: "production",
    releaseChannel: iosReleaseChannel,
    easBranch: iosReleaseChannel,
    projectName: "hbweb-expo",
    easProjectId: projectId,
    platform: "ios",
    runtimeVersion: "1.0.2",
    updateGroupId: iosGroupId,
    updateId: iosUpdateId,
    message: "修复订货流程",
    gitCommitHash: "abcdef1234567890",
    dashboardUrl: `https://expo.dev/projects/hbweb-expo/updates/${iosGroupId}`,
    publishedAtUtc: "2026-08-27T10:16:00.000Z",
    isRollback: false,
    rollbackOfReleaseId: null,
  });
  assert.equal("enabled" in payload, false);
  assert.equal("required" in payload, false);
  assert.equal("policyVersion" in payload, false);

  const httpsPrefix = "https://expo.dev/";
  const maximumDashboardUrl = `${httpsPrefix}${"x".repeat(2_048 - httpsPrefix.length)}`;
  assert.equal(
    buildOtaReleasePayload({ ...parsed, dashboardUrl: maximumDashboardUrl }, options)
      .dashboardUrl.length,
    2_048,
  );
  assert.equal(
    buildOtaReleasePayload({ ...parsed, dashboardUrl: "" }, options).dashboardUrl,
    null,
  );
  for (const dashboardUrl of [
    `${maximumDashboardUrl}x`,
    "http://expo.dev/update",
    " https://expo.dev/update",
  ]) {
    assert.throws(
      () => buildOtaReleasePayload({ ...parsed, dashboardUrl }, options),
      /dashboardUrl/i,
    );
  }
});

test("bootstrap 旧登记 payload 只包含 fixed-channel 发布事实和显式迁移开关", () => {
  const parsed = parseEasUpdateOutput(createJsonOutput("ios", "production"), "ios");
  const payload = buildLegacyBootstrapPayload(parsed, createOptions({
    releaseBatchId: batchId,
    releaseChannel: "production",
    bootstrapLegacyFixedChannel: true,
  }));
  assert.deepEqual(payload, {
    projectName: "hbweb-expo",
    updateGroupId: iosGroupId,
    updateId: iosUpdateId,
    androidUpdateId: null,
    channel: "production",
    branch: "production",
    platform: "ios",
    runtimeVersion: "1.0.2",
    message: "修复订货流程",
    gitCommitHash: "abcdef1234567890",
    dashboardUrl: `https://expo.dev/projects/hbweb-expo/updates/${iosGroupId}`,
    publishedAt: "2026-08-27T10:16:00.000Z",
    isRollback: false,
    rollbackOfGroupId: null,
    bootstrapLegacyFixedChannel: true,
  });
  assert.equal("releaseBatchId" in payload, false);
  assert.equal("enabled" in payload, false);
  assert.equal("required" in payload, false);

  const androidPayload = buildLegacyBootstrapPayload(
    parseEasUpdateOutput(createJsonOutput("android", "preview"), "android"),
    createOptions({
      environment: "preview",
      platform: "android",
      releaseChannel: "preview",
      bootstrapLegacyFixedChannel: true,
    }),
  );
  assert.equal(androidPayload.updateId, androidUpdateId);
  assert.equal(androidPayload.androidUpdateId, androidUpdateId);
  assert.equal(androidPayload.channel, "preview");
  assert.equal(androidPayload.branch, "preview");
});

test("bootstrap 先 preflight，再写固定 channel、权威回读并且只登记旧接口", async () => {
  const trace = [];
  let legacyPayload;
  const result = await runPublishMobileOtaRelease(
    createOptions({ bootstrapLegacyFixedChannel: true }),
    createDependencies({
      preflightOtaReleaseFn: async (payload) => {
        trace.push("preflight");
        assert.deepEqual(payload, {
          releaseBatchId: batchId,
          appKey: "mobile",
          environment: "production",
          clientChannel: "production",
          releaseChannel: "production",
          easBranch: "production",
          projectName: "hbweb-expo",
          easProjectId: projectId,
          platform: "ios",
          runtimeVersion: "1.0.2",
          bootstrapLegacyFixedChannel: true,
        });
        return { valid: true };
      },
      assertReleaseChannelsUnusedFn: async () => {
        throw new Error("bootstrap must skip unused proof for its existing fixed channel");
      },
      runCommandFn: async (command) => {
        if (command.args[1] === "channel:view") {
          trace.push(trace.includes("eas:update") ? "channel:view:after" : "channel:view:before");
          return { stdout: createChannelViewOutput("production", {}, "ios"), stderr: "" };
        }
        trace.push("eas:update");
        assert.equal(command.args[command.args.indexOf("--channel") + 1], "production");
        return { stdout: createJsonOutput("ios", "production"), stderr: "" };
      },
      registerOtaReleaseFn: async () => {
        throw new Error("bootstrap must never write AppOtaRelease");
      },
      registerLegacyBootstrapUpdateFn: async (payload) => {
        trace.push("legacy:register");
        legacyPayload = payload;
        return { id: "723e4567-e89b-42d3-a456-426614174000", ...payload };
      },
    }),
  );
  assert.deepEqual(trace, [
    "preflight",
    "channel:view:before",
    "eas:update",
    "channel:view:after",
    "legacy:register",
  ]);
  assert.equal(legacyPayload.bootstrapLegacyFixedChannel, true);
  assert.equal(legacyPayload.channel, "production");
  assert.equal(legacyPayload.branch, "production");
  assert.equal(result.results[0].status, "registered");
  assert.equal(result.results[0].releaseChannel, "production");
});

test("bootstrap 写入前 fixed channel branch 映射漂移时 fail-closed，EAS update 调用数为零", async () => {
  let updateCalls = 0;
  let registerCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions({ bootstrapLegacyFixedChannel: true }),
      createDependencies({
        runCommandFn: async (command) => {
          if (command.args[1] === "update") {
            updateCalls += 1;
            return { stdout: createJsonOutput("ios", "production"), stderr: "" };
          }
          return {
            stdout: createChannelViewOutput("production", {
              updateBranches: [{
                id: "production-branch-id",
                name: "unexpected-branch",
                updateGroups: [],
              }],
            }, "ios"),
            stderr: "",
          };
        },
        registerLegacyBootstrapUpdateFn: async () => {
          registerCalls += 1;
          return {};
        },
      }),
    ),
    /branch identity|channel/i,
  );
  assert.equal(updateCalls, 0);
  assert.equal(registerCalls, 0);
});

test("运行入口在 preflight 前拒绝未规范化或超长 Runtime/message", async () => {
  let preflightCalls = 0;
  let commandCalls = 0;
  for (const overrides of [
    { runtimeVersion: " 1.0.2" },
    { runtimeVersion: "x".repeat(121) },
    { message: "修复订货流程 " },
    { message: "x".repeat(1_001) },
  ]) {
    await assert.rejects(
      () => runPublishMobileOtaRelease(
        createOptions({ bootstrapLegacyFixedChannel: true, ...overrides }),
        createDependencies({
          preflightOtaReleaseFn: async () => { preflightCalls += 1; },
          runCommandFn: async () => { commandCalls += 1; },
        }),
      ),
      /runtime|message/i,
    );
  }
  assert.equal(preflightCalls, 0);
  assert.equal(commandCalls, 0);
});

test("bootstrap preflight 失败时不会检查 channel、执行 EAS 或登记", async () => {
  let sideEffectCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions({ bootstrapLegacyFixedChannel: true }),
      createDependencies({
        preflightOtaReleaseFn: async () => {
          throw new Error("bootstrap window disabled");
        },
        assertReleaseChannelsUnusedFn: async () => { sideEffectCalls += 1; },
        runCommandFn: async () => { sideEffectCalls += 1; },
        registerLegacyBootstrapUpdateFn: async () => { sideEffectCalls += 1; },
      }),
    ),
    /window disabled/i,
  );
  assert.equal(sideEffectCalls, 0);
});

test("bootstrap 登记失败写无凭据 recovery，register-only 权威回读后只补旧登记", async () => {
  let recovery;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions({ bootstrapLegacyFixedChannel: true }),
      createDependencies({
        runCommandFn: async (command) => {
          if (command.args[1] === "channel:view") {
            return { stdout: createChannelViewOutput("production", {}, "ios"), stderr: "" };
          }
          return { stdout: createJsonOutput("ios", "production"), stderr: "" };
        },
        registerLegacyBootstrapUpdateFn: async () => {
          throw new Error(`legacy register failed Bearer ${serviceToken}`);
        },
        writeRecoveryManifestFn: async (manifest) => {
          recovery = manifest;
          return "/tmp/mobile-bootstrap-recovery.json";
        },
      }),
    ),
    (error) => {
      assert.ok(error instanceof OtaPublishBatchError);
      assert.equal(error.results[0].status, "registration-failed");
      assert.equal(error.recoveryPath, "/tmp/mobile-bootstrap-recovery.json");
      return true;
    },
  );
  assert.deepEqual(Object.keys(recovery).sort(), [
    "appKey",
    "bootstrapLegacyFixedChannel",
    "createdAtUtc",
    "environment",
    "releaseBatchId",
    "releases",
    "schemaVersion",
  ]);
  assert.equal(recovery.bootstrapLegacyFixedChannel, true);
  assert.equal(recovery.releases.length, 1);
  assert.equal(recovery.releases[0].channel, "production");
  assert.equal(recovery.releases[0].bootstrapLegacyFixedChannel, true);
  assert.equal(JSON.stringify(recovery).includes(serviceToken), false);

  const trace = [];
  const restored = await runPublishMobileOtaRelease(
    { registerOnlyFile: "mobile-bootstrap-recovery.json", accessTokenStdin: true },
    createDependencies({
      readRecoveryManifestFn: async () => recovery,
      preflightOtaReleaseFn: async () => {
        throw new Error("bootstrap register-only must not preflight an existing fixed channel");
      },
      runCommandFn: async (command) => {
        trace.push(command.args[1]);
        assert.equal(command.args[1], "channel:view");
        assert.equal(command.args[2], "production");
        return { stdout: createChannelViewOutput("production", {}, "ios"), stderr: "" };
      },
      registerOtaReleaseFn: async () => {
        throw new Error("bootstrap register-only must not write AppOtaRelease");
      },
      registerLegacyBootstrapUpdateFn: async (payload) => {
        trace.push("legacy:register");
        return { id: "723e4567-e89b-42d3-a456-426614174000", ...payload };
      },
    }),
  );
  assert.deepEqual(trace, ["channel:view", "legacy:register"]);
  assert.equal(restored.results[0].releaseId, "723e4567-e89b-42d3-a456-426614174000");
});

test("bootstrap register-only 在权威身份漂移或 recovery 越权时 fail-closed", async () => {
  const payload = buildLegacyBootstrapPayload(
    parseEasUpdateOutput(createJsonOutput("ios", "production"), "ios"),
    createOptions({
      releaseBatchId: batchId,
      releaseChannel: "production",
      bootstrapLegacyFixedChannel: true,
    }),
  );
  const manifest = {
    schemaVersion: 1,
    releaseBatchId: batchId,
    appKey: "mobile",
    environment: "production",
    createdAtUtc: "2026-08-27T10:15:00.000Z",
    bootstrapLegacyFixedChannel: true,
    releases: [payload],
  };
  let registerCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      { registerOnlyFile: "recovery.json", accessTokenStdin: true },
      createDependencies({
        readRecoveryManifestFn: async () => manifest,
        runCommandFn: async () => ({
          stdout: createChannelViewOutput("production", {
            updateBranches: [{
              id: "production-branch-id",
              name: "production",
              updateGroups: [[{
                id: iosUpdateId,
                group: iosGroupId,
                createdAt: "2026-08-27T10:16:00.000Z",
                runtimeVersion: "1.0.2",
                platform: "ios",
                message: "修复订货流程",
                manifestPermalink: `https://expo.dev/projects/hbweb-expo/updates/${iosGroupId}`,
                gitCommitHash: "different-commit",
              }]],
            }],
          }, "ios"),
          stderr: "",
        }),
        registerLegacyBootstrapUpdateFn: async () => {
          registerCalls += 1;
          return { id: "723e4567-e89b-42d3-a456-426614174000", ...payload };
        },
      }),
    ),
    /gitCommitHash|update/i,
  );
  assert.equal(registerCalls, 0);

  await assert.rejects(
    () => runPublishMobileOtaRelease(
      { registerOnlyFile: "recovery.json", accessTokenStdin: true },
      createDependencies({
        readRecoveryManifestFn: async () => ({
          ...manifest,
          releases: [{ ...payload, channel: "preview", branch: "preview" }],
        }),
        registerLegacyBootstrapUpdateFn: async () => {
          registerCalls += 1;
          return { id: "723e4567-e89b-42d3-a456-426614174000", ...payload };
        },
      }),
    ),
    /bootstrap recovery manifest/i,
  );
  assert.equal(registerCalls, 0);
});

test("all 会先完成两个 preflight，再以同 batch 分平台发布和登记", async () => {
  const trace = [];
  const result = await runPublishMobileOtaRelease(
    createOptions({ platform: "all" }),
    createDependencies({
      preflightOtaReleaseFn: async (payload) => {
        trace.push(`preflight:${payload.platform}`);
        return { valid: true };
      },
      assertReleaseChannelsUnusedFn: async (plans) => {
        trace.push(`unused:${plans.map((plan) => plan.platform).join(",")}`);
      },
      runCommandFn: async (command) => {
        if (command.args[1] === "channel:view") {
          const channel = command.args[2];
          trace.push(`verify:${channel.includes("android") ? "android" : "ios"}`);
          return { stdout: createChannelViewOutput(channel), stderr: "" };
        }
        const platform = command.args[command.args.indexOf("--platform") + 1];
        const channel = command.args[command.args.indexOf("--channel") + 1];
        trace.push(`eas:${platform}`);
        return { stdout: createJsonOutput(platform, channel), stderr: "" };
      },
      registerOtaReleaseFn: async (payload) => {
        trace.push(`register:${payload.platform}`);
        return { release: { id: `${payload.platform}-release-id` }, idempotent: false };
      },
    }),
  );
  assert.deepEqual(trace, [
    "preflight:android",
    "preflight:ios",
    "unused:android,ios",
    "eas:android",
    "verify:android",
    "register:android",
    "eas:ios",
    "verify:ios",
    "register:ios",
  ]);
  assert.equal(result.releaseBatchId, batchId);
  assert.deepEqual(result.results.map((item) => item.status), ["registered", "registered"]);
});

test("所有 lane 必须在首次 EAS update 前取得完整 channel list 的 unused 证明", async () => {
  const pages = [
    Array.from({ length: 25 }, (_, index) => `historical-${index}`),
    [iosReleaseChannel],
  ];
  let pageCalls = 0;
  await assert.rejects(
    () => assertReleaseChannelsUnused(
      [createOptions({ releaseChannel: iosReleaseChannel })],
      liveEnvironment,
      async (command) => {
        assert.equal(command.args[1], "channel:list");
        const output = createChannelListOutput(pages[pageCalls] ?? []);
        pageCalls += 1;
        return { stdout: output, stderr: "" };
      },
    ),
    /already exists|已存在/i,
  );
  assert.equal(pageCalls, 2);

  await assert.rejects(
    () => assertReleaseChannelsUnused(
      [createOptions({ releaseChannel: iosReleaseChannel })],
      liveEnvironment,
      async () => ({ stdout: "network gateway error", stderr: "" }),
    ),
    /channel:list/i,
  );
});

test("unused 权威证明失败时不会执行任一平台 EAS update", async () => {
  let updateCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions({ platform: "all" }),
      createDependencies({
        assertReleaseChannelsUnusedFn: async () => {
          throw new Error("EAS channel list could not prove unused");
        },
        runCommandFn: async () => {
          updateCalls += 1;
          throw new Error("must not execute EAS update");
        },
      }),
    ),
    /prove unused/i,
  );
  assert.equal(updateCalls, 0);
});

test("rollback 来源 UUID 会随单平台 preflight 在任何 EAS 写入前校验", async () => {
  const trace = [];
  await runPublishMobileOtaRelease(
    createOptions({ rollbackOfReleaseId: rollbackSourceId }),
    createDependencies({
      preflightOtaReleaseFn: async (payload) => {
        trace.push({ kind: "preflight", rollbackOfReleaseId: payload.rollbackOfReleaseId });
        return { valid: true };
      },
      runCommandFn: async (command) => {
        if (command.args[1] === "channel:view") {
          return { stdout: createChannelViewOutput(command.args[2]), stderr: "" };
        }
        trace.push({ kind: "eas" });
        return { stdout: createJsonOutput("ios"), stderr: "" };
      },
    }),
  );
  assert.deepEqual(trace, [
    { kind: "preflight", rollbackOfReleaseId: rollbackSourceId },
    { kind: "eas" },
  ]);
});

test("all 单平台失败保留成功平台并整体 non-zero", async () => {
  const registered = [];
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions({ platform: "all" }),
      createDependencies({
        runCommandFn: async (command) => {
          if (command.args[1] === "channel:view") {
            const channel = command.args[2];
            return { stdout: createChannelViewOutput(channel), stderr: "" };
          }
          const platform = command.args[command.args.indexOf("--platform") + 1];
          const channel = command.args[command.args.indexOf("--channel") + 1];
          if (platform === "android") throw new Error("android EAS failed");
          return { stdout: createJsonOutput(platform, channel), stderr: "" };
        },
        registerOtaReleaseFn: async (payload) => {
          registered.push(payload.platform);
          return { release: { id: `${payload.platform}-release-id` }, idempotent: false };
        },
      }),
    ),
    (error) => {
      assert.ok(error instanceof OtaPublishBatchError);
      assert.equal(error.exitCode, 2);
      assert.deepEqual(error.results.map((item) => item.status), ["publish-failed", "registered"]);
      return true;
    },
  );
  assert.deepEqual(registered, ["ios"]);
});

test("EAS 发布成功但 channel 权威回读失形时写可恢复事实并标记 published-unverified", async () => {
  let recovery;
  let registerCalls = 0;
  let updateCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions(),
      createDependencies({
        runCommandFn: async (command) => {
          if (command.args[1] === "update") {
            updateCalls += 1;
            return { stdout: createJsonOutput("ios"), stderr: "" };
          }
          return {
            stdout: createChannelViewOutput(iosReleaseChannel, {
              name: androidReleaseChannel,
            }),
            stderr: "",
          };
        },
        registerOtaReleaseFn: async () => {
          registerCalls += 1;
          throw new Error("must not register an unverified channel");
        },
        writeRecoveryManifestFn: async (manifest) => {
          recovery = manifest;
          return "/tmp/mobile-unverified-recovery.json";
        },
      }),
    ),
    (error) => {
      assert.ok(error instanceof OtaPublishBatchError);
      assert.equal(error.results[0].status, "published-unverified");
      assert.equal(error.recoveryPath, "/tmp/mobile-unverified-recovery.json");
      return true;
    },
  );
  assert.equal(updateCalls, 1);
  assert.equal(registerCalls, 0);
  assert.equal(recovery.releases[0].updateId, iosUpdateId);
  assert.equal(recovery.releases[0].releaseChannel, iosReleaseChannel);
});

test("EAS 发布 JSON 的 update/group/时间失形时在 channel 回读和登记前 fail-closed", async () => {
  let channelViewCalls = 0;
  let registerCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions(),
      createDependencies({
        runCommandFn: async (command) => {
          if (command.args[1] === "channel:view") {
            channelViewCalls += 1;
            return { stdout: createChannelViewOutput(command.args[2]), stderr: "" };
          }
          return {
            stdout: createJsonOutput("ios", iosReleaseChannel, {
              id: "not-a-uuid",
              group: "also-not-a-uuid",
              createdAt: "not-a-date",
            }),
            stderr: "",
          };
        },
        registerOtaReleaseFn: async () => {
          registerCalls += 1;
          return { release: { id: "release-id" }, idempotent: false };
        },
      }),
    ),
    (error) => {
      assert.ok(error instanceof OtaPublishBatchError);
      assert.equal(error.results[0].status, "published-unverified");
      return true;
    },
  );
  assert.equal(channelViewCalls, 0);
  assert.equal(registerCalls, 0);
});

test("EAS 成功但登记失败写无凭据恢复 manifest，并禁止盲目重发", async () => {
  let recovery;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions(),
      createDependencies({
        registerOtaReleaseFn: async () => {
          throw new Error(`register failed Bearer ${serviceToken}`);
        },
        writeRecoveryManifestFn: async (manifest) => {
          recovery = manifest;
          return "/tmp/mobile-recovery.json";
        },
      }),
    ),
    OtaPublishBatchError,
  );
  assert.equal(recovery.releases.length, 1);
  assert.equal(JSON.stringify(recovery).includes(serviceToken), false);
  assert.equal(recovery.releases[0].updateId, iosUpdateId);
});

test("recovery 文件写入失败时输出无凭据 manifest 并保留 non-zero 结果", async () => {
  const logs = [];
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      createOptions(),
      createDependencies({
        logger: {
          log(value) { logs.push(String(value)); },
          warn(value) { logs.push(String(value)); },
        },
        registerOtaReleaseFn: async () => {
          throw new Error(`register failed Bearer ${serviceToken}`);
        },
        writeRecoveryManifestFn: async () => {
          throw new Error("disk full");
        },
      }),
    ),
    (error) => {
      assert.ok(error instanceof OtaPublishBatchError);
      assert.equal(error.results[0].status, "registration-failed");
      return true;
    },
  );
  const output = logs.join("\n");
  assert.match(output, /schemaVersion/);
  assert.equal(output.includes(serviceToken), false);
  assert.match(output, /禁止重发/);
});

test("register-only 重做只读 channel 权威回读和完整 fingerprint 幂等登记，不执行 preflight/EAS 写入", async () => {
  let updateCalls = 0;
  let channelViewCalls = 0;
  let preflightCalls = 0;
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: null,
    },
  );
  const result = await runPublishMobileOtaRelease(
    { registerOnlyFile: "recovery.json", accessTokenStdin: true },
    createDependencies({
      readRecoveryManifestFn: async () => ({
        schemaVersion: 1,
        releaseBatchId: batchId,
        appKey: "mobile",
        environment: "production",
        createdAtUtc: "2026-08-27T10:15:00.000Z",
        releases: [payload],
      }),
      runCommandFn: async (command) => {
        if (command.args[1] === "update") {
          updateCalls += 1;
          throw new Error("must not publish");
        }
        channelViewCalls += 1;
        return { stdout: createChannelViewOutput(command.args[2]), stderr: "" };
      },
      preflightOtaReleaseFn: async () => {
        preflightCalls += 1;
        throw new Error("register-only must not preflight an already-used channel");
      },
      registerOtaReleaseFn: async () => ({ release: { id: "release-id" }, idempotent: true }),
    }),
  );
  assert.equal(updateCalls, 0);
  assert.equal(channelViewCalls, 1);
  assert.equal(preflightCalls, 0);
  assert.equal(result.results[0].status, "registered");
  assert.equal(result.results[0].idempotent, true);
});

test("register-only 的 channel 权威回读不匹配时 fail-closed，禁止盲补登记", async () => {
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: null,
    },
  );
  let registerCalls = 0;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      { registerOnlyFile: "recovery.json", accessTokenStdin: true },
      createDependencies({
        readRecoveryManifestFn: async () => ({
          schemaVersion: 1,
          releaseBatchId: batchId,
          appKey: "mobile",
          environment: "production",
          createdAtUtc: "2026-08-27T10:15:00.000Z",
          releases: [payload],
        }),
        runCommandFn: async () => ({
          stdout: createChannelViewOutput(iosReleaseChannel, {
            name: androidReleaseChannel,
          }),
          stderr: "",
        }),
        registerOtaReleaseFn: async () => {
          registerCalls += 1;
          return { release: { id: "release-id" }, idempotent: true };
        },
      }),
    ),
    /channel/i,
  );
  assert.equal(registerCalls, 0);
});

test("register-only 即使 channel 映射匹配也必须拒绝 update/runtime/platform 身份漂移", async () => {
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: null,
    },
  );
  let registerCalls = 0;
  const branchId = `${iosReleaseChannel}-branch-id`;
  await assert.rejects(
    () => runPublishMobileOtaRelease(
      { registerOnlyFile: "recovery.json", accessTokenStdin: true },
      createDependencies({
        readRecoveryManifestFn: async () => ({
          schemaVersion: 1,
          releaseBatchId: batchId,
          appKey: "mobile",
          environment: "production",
          createdAtUtc: "2026-08-27T10:15:00.000Z",
          releases: [payload],
        }),
        runCommandFn: async () => ({
          stdout: createChannelViewOutput(iosReleaseChannel, {
            updateBranches: [{
              id: branchId,
              name: iosReleaseChannel,
              updateGroups: [[{
                id: androidUpdateId,
                group: iosGroupId,
                createdAt: "2026-08-27T10:16:00.000Z",
                runtimeVersion: "2.0.0",
                platform: "android",
                message: "修复订货流程",
                manifestPermalink: `https://expo.dev/projects/hbweb-expo/updates/${iosGroupId}`,
                gitCommitHash: "abcdef1234567890",
              }]],
            }],
          }),
          stderr: "",
        }),
        registerOtaReleaseFn: async () => {
          registerCalls += 1;
          return { release: { id: "release-id" }, idempotent: true };
        },
      }),
    ),
    /update|runtime|platform/i,
  );
  assert.equal(registerCalls, 0);
});

test("register-only 在发出请求前拒绝跨 lane、额外字段或漂移的 recovery 事实", async () => {
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: null,
    },
  );
  let registerCalls = 0;
  for (const forgedRelease of [
    { ...payload, releaseChannel: androidReleaseChannel },
    { ...payload, adminJwt: "must-never-be-forwarded" },
    { ...payload, releaseBatchId: "253e4567-e89b-42d3-a456-426614174000" },
    { ...payload, dashboardUrl: `https://expo.dev/${"x".repeat(2_049)}` },
  ]) {
    await assert.rejects(
      () => runPublishMobileOtaRelease(
        { registerOnlyFile: "recovery.json", accessTokenStdin: true },
        createDependencies({
          readRecoveryManifestFn: async () => ({
            schemaVersion: 1,
            releaseBatchId: batchId,
            appKey: "mobile",
            environment: "production",
            createdAtUtc: "2026-08-27T10:15:00.000Z",
            releases: [forgedRelease],
          }),
          registerOtaReleaseFn: async () => {
            registerCalls += 1;
            return { release: { id: "release-id" }, idempotent: true };
          },
        }),
      ),
      /recovery manifest/i,
    );
  }
  assert.equal(registerCalls, 0);
});

test("管理员 JWT 只允许 stdin，服务 token 可由专用环境变量提供", async () => {
  assert.equal(
    await readAccessTokenFromStdin(Readable.from(["  jwt-from-stdin\n"])),
    "jwt-from-stdin",
  );
  assert.equal(
    await resolvePublishAccessToken(
      { accessTokenStdin: false },
      { HBWEB_API_TOKEN: serviceToken },
    ),
    serviceToken,
  );
  await assert.rejects(
    () => resolvePublishAccessToken(
      { accessTokenStdin: false },
      { HBWEB_API_TOKEN: "eyJhbGciOiJSUzI1NiJ9.payload.signature" },
    ),
    /stdin/i,
  );
  await assert.rejects(
    () => readAccessTokenFromStdin(Object.assign(Readable.from([]), { isTTY: true })),
    /TTY/i,
  );
});

test("后台 URL 区分不可变发布 register 与 bootstrap 旧登记路径", () => {
  assert.equal(APP_OTA_PREFLIGHT_PATH, "/api/app-ota-releases/preflight");
  assert.equal(APP_OTA_REGISTER_PATH, "/api/app-ota-releases/register");
  assert.equal(LEGACY_MOBILE_OTA_REGISTER_PATH, "/api/mobile-app-builds/ota-updates");
  assert.equal(
    buildPreflightUrl("https://hotbargain.vip/api"),
    "https://hotbargain.vip/api/app-ota-releases/preflight",
  );
  assert.equal(
    buildRegistrationUrl("https://hotbargain.vip"),
    "https://hotbargain.vip/api/app-ota-releases/register",
  );
  assert.equal(
    buildLegacyRegistrationUrl("https://hotbargain.vip/api"),
    "https://hotbargain.vip/api/mobile-app-builds/ota-updates",
  );
});

test("preflight/register 只向后台发送 Bearer 与 JSON，严格读取业务响应", async () => {
  const requests = [];
  const fetchFn = async (url, config) => {
    requests.push({ url, config });
    const requestBody = JSON.parse(config.body);
    const registeredFact = { ...requestBody };
    delete registeredFact.easProjectId;
    if (registeredFact.releaseBatchId) {
      registeredFact.releaseBatchId = registeredFact.releaseBatchId.toUpperCase();
    }
    if (registeredFact.updateGroupId) {
      registeredFact.updateGroupId = registeredFact.updateGroupId.toUpperCase();
    }
    if (registeredFact.updateId) {
      registeredFact.updateId = registeredFact.updateId.toUpperCase();
    }
    if (registeredFact.publishedAtUtc) {
      registeredFact.publishedAtUtc = registeredFact.publishedAtUtc.replace(/Z$/, "");
    }
    const data = url.endsWith("/preflight")
      ? { valid: true }
      : {
        release: {
          id: "723E4567-E89B-42D3-A456-426614174000",
          ...registeredFact,
          registrationSource: "app-ota-release-api",
        },
        idempotent: false,
      };
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      async text() {
        return JSON.stringify({ success: true, data });
      },
    };
  };
  const payload = {
    releaseBatchId: batchId,
    appKey: "mobile",
    environment: "production",
    clientChannel: "production",
    releaseChannel: iosReleaseChannel,
    easBranch: iosReleaseChannel,
    projectName: "hbweb-expo",
    easProjectId: projectId,
    platform: "ios",
    runtimeVersion: "1.0.2",
  };
  await preflightOtaRelease(payload, {
    baseUrl: "https://hotbargain.vip/api",
    accessToken: serviceToken,
    fetchFn,
  });
  const releasePayload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: null,
    },
  );
  await registerOtaRelease(releasePayload, {
    baseUrl: "https://hotbargain.vip/api",
    accessToken: serviceToken,
    fetchFn,
  });
  assert.deepEqual(requests.map((item) => item.url), [
    "https://hotbargain.vip/api/app-ota-releases/preflight",
    "https://hotbargain.vip/api/app-ota-releases/register",
  ]);
  assert.equal(requests[0].config.headers.Authorization, `Bearer ${serviceToken}`);
  assert.equal(JSON.parse(requests[0].config.body).releaseChannel, iosReleaseChannel);

  for (const releaseOverride of [
    { releaseBatchId: "823e4567-e89b-42d3-a456-426614174000" },
    { appKey: "pos-handheld" },
    { environment: "preview" },
    { clientChannel: "preview" },
    { releaseChannel: androidReleaseChannel },
    { easBranch: androidReleaseChannel },
    { projectName: "other-project" },
    { platform: "android" },
    { runtimeVersion: "2.0.0" },
    { updateGroupId: androidGroupId },
    { updateId: androidUpdateId },
    { message: "漂移说明" },
    { gitCommitHash: "different-commit" },
    { dashboardUrl: "https://expo.dev/projects/other/update" },
    { publishedAtUtc: "2026-08-27T10:17:00.000Z" },
    { isRollback: true, rollbackOfReleaseId: null },
    { registrationSource: "legacy-backfill" },
  ]) {
    await assert.rejects(
      () => registerOtaRelease(releasePayload, {
        baseUrl: "https://hotbargain.vip/api",
        accessToken: serviceToken,
        fetchFn: async (_url, config) => {
          const requestBody = JSON.parse(config.body);
          const registeredFact = { ...requestBody };
          delete registeredFact.easProjectId;
          return {
            ok: true,
            status: 200,
            statusText: "OK",
            async text() {
              return JSON.stringify({
                success: true,
                data: {
                  release: {
                    id: "723e4567-e89b-42d3-a456-426614174000",
                    ...registeredFact,
                    registrationSource: "app-ota-release-api",
                    ...releaseOverride,
                  },
                  idempotent: false,
                },
              });
            },
          };
        },
      }),
      /response identity is invalid/i,
    );
  }

  const nullablePayload = buildOtaReleasePayload(
    {
      ...parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
      gitCommitHash: "",
      dashboardUrl: "",
    },
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: null,
    },
  );
  await registerOtaRelease(nullablePayload, {
    baseUrl: "https://hotbargain.vip/api",
    accessToken: serviceToken,
    fetchFn: async () => ({
      ok: true,
      status: 200,
      statusText: "OK",
      async text() {
        const registeredFact = { ...nullablePayload };
        delete registeredFact.easProjectId;
        return JSON.stringify({
          success: true,
          data: {
            release: {
              id: "723e4567-e89b-42d3-a456-426614174000",
              ...registeredFact,
              gitCommitHash: "",
              dashboardUrl: null,
              registrationSource: "app-ota-release-api",
            },
            idempotent: true,
          },
        });
      },
    }),
  });

  const rollbackPayload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      releaseBatchId: batchId,
      environment: "production",
      releaseChannel: iosReleaseChannel,
      runtimeVersion: "1.0.2",
      message: "修复订货流程",
      platform: "ios",
      rollbackOfReleaseId: rollbackSourceId,
    },
  );
  const rollbackResponse = (rollbackOfReleaseId = rollbackSourceId.toUpperCase()) => {
    const registeredFact = { ...rollbackPayload };
    delete registeredFact.easProjectId;
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      async text() {
        return JSON.stringify({
          success: true,
          data: {
            release: {
              id: "723e4567-e89b-42d3-a456-426614174000",
              ...registeredFact,
              rollbackOfReleaseId,
              registrationSource: "app-ota-release-api",
            },
            idempotent: false,
          },
        });
      },
    };
  };
  await registerOtaRelease(rollbackPayload, {
    baseUrl: "https://hotbargain.vip/api",
    accessToken: serviceToken,
    fetchFn: async () => rollbackResponse(),
  });
  await assert.rejects(
    () => registerOtaRelease(rollbackPayload, {
      baseUrl: "https://hotbargain.vip/api",
      accessToken: serviceToken,
      fetchFn: async () => rollbackResponse(
        "923e4567-e89b-42d3-a456-426614174000",
      ),
    }),
    /response identity is invalid/i,
  );
});

test("bootstrap 旧登记只发送显式迁移 DTO，并严格核对后台回显身份", async () => {
  const payload = buildLegacyBootstrapPayload(
    parseEasUpdateOutput(createJsonOutput("ios", "production"), "ios"),
    createOptions({
      releaseBatchId: batchId,
      releaseChannel: "production",
      bootstrapLegacyFixedChannel: true,
    }),
  );
  const requests = [];
  const legacyResponseFact = (overrides = {}) => {
    const fact = { ...payload };
    delete fact.bootstrapLegacyFixedChannel;
    return {
      id: "723E4567-E89B-42D3-A456-426614174000",
      appKey: "mobile",
      ...fact,
      updateGroupId: fact.updateGroupId.toUpperCase(),
      updateId: fact.updateId.toUpperCase(),
      publishedAt: fact.publishedAt.replace(/Z$/, ""),
      createdAt: "2026-08-27T10:17:00.000Z",
      updatedAt: null,
      ...overrides,
    };
  };
  const fetchFn = async (url, config) => {
    requests.push({ url, config });
    return {
      ok: true,
      status: 200,
      statusText: "OK",
      async text() {
        return JSON.stringify({
          success: true,
          data: legacyResponseFact(),
        });
      },
    };
  };
  const registered = await registerLegacyBootstrapUpdate(payload, {
    baseUrl: "https://hotbargain.vip/api",
    accessToken: serviceToken,
    fetchFn,
  });
  assert.equal(
    registered.id.toLowerCase(),
    "723e4567-e89b-42d3-a456-426614174000",
  );
  assert.equal(requests[0].url, "https://hotbargain.vip/api/mobile-app-builds/ota-updates");
  assert.equal(requests[0].config.headers.Authorization, `Bearer ${serviceToken}`);
  assert.deepEqual(JSON.parse(requests[0].config.body), payload);
  assert.equal("releaseBatchId" in JSON.parse(requests[0].config.body), false);

  for (const releaseOverride of [
    { appKey: "pos-handheld" },
    { projectName: "other-project" },
    { updateGroupId: androidGroupId },
    { updateId: androidUpdateId },
    { androidUpdateId: androidUpdateId },
    { channel: "preview" },
    { branch: "preview" },
    { platform: "android" },
    { runtimeVersion: "2.0.0" },
    { message: "漂移说明" },
    { gitCommitHash: "different-commit" },
    { dashboardUrl: "https://expo.dev/projects/other/update" },
    { publishedAt: "2026-08-27T10:17:00.000Z" },
    { isRollback: true, rollbackOfGroupId: null },
    { rollbackOfGroupId: androidGroupId },
  ]) {
    await assert.rejects(
      () => registerLegacyBootstrapUpdate(payload, {
        baseUrl: "https://hotbargain.vip/api",
        accessToken: serviceToken,
        fetchFn: async () => ({
          ok: true,
          status: 200,
          statusText: "OK",
          async text() {
            return JSON.stringify({
              success: true,
              data: legacyResponseFact(releaseOverride),
            });
          },
        }),
      }),
      /response is invalid/i,
    );
  }
});
