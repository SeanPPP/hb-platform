import assert from "node:assert/strict";
import { Readable } from "node:stream";
import test from "node:test";

import {
  EAS_CLI_VERSION,
  POS_HANDHELD_PRODUCTION_CHANNEL,
  buildEasUpdateCommand,
  buildOtaReleasePayload,
  buildRegistrationUrl,
  parseEasUpdateOutput,
  parsePublishOtaArgs,
  readAccessTokenFromStdin,
  registerOtaRelease,
  runPublishPosHandheldOtaRelease,
} from "./publish-ota-release.mjs";

const projectId = "123e4567-e89b-42d3-a456-426614174000";
const projectName = "hb-pos-handheld";
const groupId = "223e4567-e89b-42d3-a456-426614174000";
const iosUpdateId = "323e4567-e89b-42d3-a456-426614174000";
const androidUpdateId = "423e4567-e89b-42d3-a456-426614174000";
const currentRuntimeVersion = "0.1.0";
const administratorAccessToken =
  "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJhZG1pbiJ9.signature";
const liveEnvironment = Object.freeze({
  EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
  HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
  HBPOS_OTA_CENTER_ACCESS_TOKEN: administratorAccessToken,
  HBPOS_OTA_ADMIN_JWT: administratorAccessToken,
});

function createJsonOutput(platform = "ios") {
  const updateId = platform === "android" ? androidUpdateId : iosUpdateId;
  return JSON.stringify([
    {
      id: updateId,
      createdAt: "2026-08-10T01:02:03.000Z",
      group: groupId,
      branch: POS_HANDHELD_PRODUCTION_CHANNEL,
      runtimeVersion: currentRuntimeVersion,
      platform,
      message: `${platform} 门店修复`,
      manifestPermalink:
        `https://expo.dev/projects/${projectId}/updates/${groupId}`,
      gitCommitHash: "abcdef1234567890",
    },
  ]);
}

test("发布 runtime 必须严格绑定当前 resolved appVersion", () => {
  const currentCommand = buildEasUpdateCommand(
    {
      runtimeVersion: currentRuntimeVersion,
      message: "当前原生版本修复",
      platform: "ios",
    },
    { EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId },
  );
  assert.equal(
    currentCommand.env.EXPO_PUBLIC_HBPOS_RUNTIME_VERSION,
    currentRuntimeVersion,
  );

  assert.throws(
    () =>
      buildEasUpdateCommand(
        {
          runtimeVersion: "0.2.0",
          message: "错误指向旧原生版本",
          platform: "ios",
        },
        { EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId },
      ),
    /runtime-version.*当前.*0\.1\.0/u,
  );
});

test("按所选平台解析 EAS JSON 的真实 group、update、branch 与发布时间", () => {
  assert.deepEqual(parseEasUpdateOutput(createJsonOutput("ios"), "ios"), {
    updateGroupId: groupId,
    updateId: iosUpdateId,
    channel: "",
    branch: POS_HANDHELD_PRODUCTION_CHANNEL,
    platform: "ios",
    runtimeVersion: currentRuntimeVersion,
    message: "ios 门店修复",
    gitCommitHash: "abcdef1234567890",
    dashboardUrl:
      `https://expo.dev/projects/${projectId}/updates/${groupId}`,
    publishedAt: "2026-08-10T01:02:03.000Z",
  });
  assert.equal(
    parseEasUpdateOutput(createJsonOutput("android"), "android").updateId,
    androidUpdateId,
  );
  assert.equal(
    parseEasUpdateOutput(createJsonOutput("android"), "ios").updateId,
    "",
  );
  assert.equal(
    parseEasUpdateOutput(`Update group ID ${groupId}`, "ios").updateGroupId,
    "",
  );
});

test("登记 payload 精确符合 MobileAppOtaUpdateUpsertDto 并绑定固定生产频道", () => {
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      runtimeVersion: currentRuntimeVersion,
      message: "参数发布说明",
      platform: "ios",
    },
    "2026-08-10T02:00:00.000Z",
  );

  assert.deepEqual(payload, {
    projectName,
    updateGroupId: groupId,
    updateId: iosUpdateId,
    androidUpdateId: null,
    channel: POS_HANDHELD_PRODUCTION_CHANNEL,
    branch: POS_HANDHELD_PRODUCTION_CHANNEL,
    platform: "ios",
    runtimeVersion: currentRuntimeVersion,
    message: "ios 门店修复",
    gitCommitHash: "abcdef1234567890",
    dashboardUrl:
      `https://expo.dev/projects/${projectId}/updates/${groupId}`,
    publishedAt: "2026-08-10T01:02:03.000Z",
    isRollback: false,
    rollbackOfGroupId: null,
  });
  assert.equal("publishedAtUtc" in payload, false);
  assert.equal("rollbackOfReleaseId" in payload, false);
  assert.equal("state" in payload, false);

  const androidPayload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("android"), "android"),
    {
      runtimeVersion: currentRuntimeVersion,
      message: "Android 修复",
      platform: "android",
    },
  );
  assert.equal(androidPayload.updateId, androidUpdateId);
  assert.equal(androidPayload.androidUpdateId, androidUpdateId);
  assert.equal(androidPayload.platform, "android");
});

test("EAS 只发布到固定频道，支持显式 ios 或 android，且子进程移除管理员 token", () => {
  for (const platform of ["ios", "android"]) {
    const command = buildEasUpdateCommand(
      {
        runtimeVersion: currentRuntimeVersion,
        message: `${platform} 门店修复`,
        platform,
      },
      liveEnvironment,
    );

    assert.deepEqual(command.args, [
      `eas-cli@${EAS_CLI_VERSION}`,
      "update",
      "--channel",
      POS_HANDHELD_PRODUCTION_CHANNEL,
      "--platform",
      platform,
      "--message",
      `${platform} 门店修复`,
      "--json",
      "--non-interactive",
    ]);
    assert.equal(command.args.includes("channel:create"), false);
    assert.equal(command.env.HBPOS_OTA_CENTER_ACCESS_TOKEN, undefined);
    assert.equal(command.env.HBPOS_OTA_ADMIN_JWT, undefined);
    assert.equal(
      command.env.EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID,
      projectId,
    );
  }

  assert.throws(
    () =>
      buildEasUpdateCommand(
        {
          runtimeVersion: currentRuntimeVersion,
          message: "非法平台",
          platform: "web",
        },
        { EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId },
      ),
    /platform.*ios.*android/i,
  );
});

test("dry-run 零网络零 EAS，只打印固定频道 update 命令和待登记 payload", async () => {
  let commands = 0;
  let registrations = 0;
  const logs = [];
  const result = await runPublishPosHandheldOtaRelease(
    {
      dryRun: true,
      runtimeVersion: currentRuntimeVersion,
      message: "门店修复",
      platform: "ios",
      mockOutputFile: "mock.json",
      accessTokenStdin: true,
    },
    {
      environment: {
        EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
      },
      logger: { log: (value) => logs.push(String(value)) },
      readAccessTokenStdinFn: async () => {
        throw new Error("dry-run 不得读取标准输入");
      },
      readMockOutputFn: async () => createJsonOutput("ios"),
      runCommandFn: async () => {
        commands += 1;
        return { stdout: createJsonOutput("ios"), stderr: "" };
      },
      registerOtaReleaseFn: async () => {
        registrations += 1;
      },
    },
  );

  assert.equal(result.dryRun, true);
  assert.equal(commands, 0);
  assert.equal(registrations, 0);
  assert.equal(result.payload.channel, POS_HANDHELD_PRODUCTION_CHANNEL);
  assert.equal(result.payload.projectName, projectName);
  assert.ok(logs.some((line) => line.includes("update --channel")));
  assert.equal(logs.some((line) => line.includes("channel:create")), false);
  assert.equal(logs.join("\n").includes(administratorAccessToken), false);
});

test("live 缺 projectId、Center 地址或管理员 JWT 时在 EAS 前 fail-fast", async () => {
  let commands = 0;
  const options = {
    runtimeVersion: currentRuntimeVersion,
    message: "门店修复",
    platform: "ios",
  };
  const dependencies = {
    logger: { log() {} },
    runCommandFn: async () => {
      commands += 1;
      return { stdout: createJsonOutput("ios"), stderr: "" };
    },
  };

  await assert.rejects(
    () =>
      runPublishPosHandheldOtaRelease(options, {
        ...dependencies,
        environment: {},
      }),
    /EAS projectId/,
  );
  await assert.rejects(
    () =>
      runPublishPosHandheldOtaRelease(options, {
        ...dependencies,
        environment: {
          EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
        },
      }),
    /Center base URL/,
  );
  await assert.rejects(
    () =>
      runPublishPosHandheldOtaRelease(options, {
        ...dependencies,
        environment: {
          EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
          HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
        },
      }),
    /access token/i,
  );
  await assert.rejects(
    () =>
      runPublishPosHandheldOtaRelease(options, {
        ...dependencies,
        environment: {
          EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
          HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
          HBPOS_OTA_CENTER_ACCESS_TOKEN: "hbsvc_read_only",
        },
      }),
    /service token|hbsvc_/i,
  );
  assert.equal(commands, 0);
});

test("live 只执行一次 EAS update，再用同一管理员 JWT 登记通用 OTA 记录", async () => {
  const events = [];
  const payloads = [];
  const result = await runPublishPosHandheldOtaRelease(
    {
      runtimeVersion: currentRuntimeVersion,
      message: "门店修复",
      platform: "ios",
    },
    {
      environment: liveEnvironment,
      logger: { log() {} },
      preflightOtaReleaseFn: async () => {
        throw new Error("不得调用已删除的专用 preflight 路由");
      },
      runCommandFn: async (command) => {
        events.push(command.args[1]);
        return { stdout: createJsonOutput("ios"), stderr: "" };
      },
      registerOtaReleaseFn: async (payload, config) => {
        events.push("register");
        payloads.push(payload);
        assert.deepEqual(config, {
          baseUrl: "https://center.example",
          accessToken: administratorAccessToken,
        });
        return { url: buildRegistrationUrl("https://center.example") };
      },
    },
  );

  assert.deepEqual(events, ["update", "register"]);
  assert.equal(payloads[0].projectName, projectName);
  assert.equal(payloads[0].channel, POS_HANDHELD_PRODUCTION_CHANNEL);
  assert.equal(payloads[0].platform, "ios");
  assert.equal(result.payload.updateId, iosUpdateId);
});

test("EAS JSON channel/runtime/platform 不匹配或缺关键字段时不登记", async () => {
  const cases = [
    {
      output: JSON.stringify({
        channel: "pos-handheld-preview",
        updates: JSON.parse(createJsonOutput("ios")),
      }),
      pattern: /channel 不匹配/,
    },
    {
      output: JSON.stringify([
        {
          ...JSON.parse(createJsonOutput("ios"))[0],
          runtimeVersion: "9.9.9",
        },
      ]),
      pattern: /runtimeVersion 不匹配/,
    },
    {
      output: JSON.stringify({ updates: [] }),
      pattern: /缺少.*updateGroupId.*updateId/s,
    },
  ];

  for (const item of cases) {
    let registrations = 0;
    await assert.rejects(
      () =>
        runPublishPosHandheldOtaRelease(
          {
            runtimeVersion: currentRuntimeVersion,
            message: "门店修复",
            platform: "ios",
          },
          {
            environment: liveEnvironment,
            logger: { log() {} },
            runCommandFn: async () => ({ stdout: item.output, stderr: "" }),
            registerOtaReleaseFn: async () => {
              registrations += 1;
            },
          },
        ),
      item.pattern,
    );
    assert.equal(registrations, 0);
  }
});

test("EAS 已发布但登记失败时抛错并打印可重试 payload，不重新发布", async () => {
  let commands = 0;
  let registrations = 0;
  const logs = [];

  await assert.rejects(
    () =>
      runPublishPosHandheldOtaRelease(
        {
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
          platform: "ios",
        },
        {
          environment: liveEnvironment,
          logger: { log: (value) => logs.push(String(value)) },
          runCommandFn: async () => {
            commands += 1;
            return { stdout: createJsonOutput("ios"), stderr: "" };
          },
          registerOtaReleaseFn: async () => {
            registrations += 1;
            throw new Error("Center 暂时不可用");
          },
        },
      ),
    /Center 暂时不可用/,
  );

  assert.equal(commands, 1);
  assert.equal(registrations, 1);
  assert.ok(logs.some((line) => line.includes("可重试登记 payload")));
  assert.ok(logs.some((line) => line.includes(groupId)));
  assert.ok(logs.some((line) => line.includes("不得重新发布")));
});

test("POST 使用已有通用路由、管理员 Bearer JWT 和 ApiResponse.data", async () => {
  const calls = [];
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(createJsonOutput("ios"), "ios"),
    {
      runtimeVersion: currentRuntimeVersion,
      message: "门店修复",
      platform: "ios",
    },
  );
  const result = await registerOtaRelease(
    payload,
    {
      baseUrl: "https://center.example/api",
      accessToken: administratorAccessToken,
    },
    async (url, init) => {
      calls.push({ url, init });
      return {
        ok: true,
        status: 200,
        statusText: "OK",
        text: async () =>
          JSON.stringify({
            success: true,
            data: { updateGroupId: groupId },
          }),
      };
    },
  );

  assert.deepEqual(result, {
    url: "https://center.example/api/mobile-app-builds/ota-updates",
  });
  assert.equal(
    calls[0].init.headers.Authorization,
    `Bearer ${administratorAccessToken}`,
  );
  assert.deepEqual(JSON.parse(calls[0].init.body), payload);
});

test("管理员 JWT 只发往 HTTPS，HTTP 仅放行 loopback 本地联调", () => {
  assert.throws(
    () => buildRegistrationUrl("http://center.example"),
    /HTTPS|loopback/,
  );
  assert.equal(
    buildRegistrationUrl("https://center.example"),
    "https://center.example/api/mobile-app-builds/ota-updates",
  );
  assert.equal(
    buildRegistrationUrl("https://center.example/api"),
    "https://center.example/api/mobile-app-builds/ota-updates",
  );
  assert.equal(
    buildRegistrationUrl("http://localhost:5002"),
    "http://localhost:5002/api/mobile-app-builds/ota-updates",
  );
});

test("CLI 默认 iOS、可显式 Android，并废弃 --release-channel 与 argv token", () => {
  assert.equal(
    parsePublishOtaArgs([
      "--runtime-version",
      currentRuntimeVersion,
      "--message",
      "门店修复",
      "--access-token-stdin",
    ]).platform,
    "ios",
  );
  assert.equal(
    parsePublishOtaArgs([
      "--runtime-version",
      currentRuntimeVersion,
      "--platform",
      "android",
      "--message",
      "门店修复",
    ]).platform,
    "android",
  );
  assert.throws(
    () =>
      parsePublishOtaArgs([
        "--runtime-version",
        currentRuntimeVersion,
        "--release-channel",
        "pos-handheld-release-old",
        "--message",
        "门店修复",
      ]),
    /未知参数.*--release-channel/,
  );
  assert.throws(
    () =>
      parsePublishOtaArgs([
        "--runtime-version",
        currentRuntimeVersion,
        "--message",
        "门店修复",
        "--access-token",
        administratorAccessToken,
      ]),
    /禁止使用 --access-token/,
  );
  assert.throws(
    () =>
      parsePublishOtaArgs([
        "--runtime-version",
        currentRuntimeVersion,
        "--message",
        "门店修复",
        `--access-token=${administratorAccessToken}`,
      ]),
    (error) =>
      error instanceof Error &&
      /禁止使用 --access-token/u.test(error.message) &&
      !error.message.includes(administratorAccessToken),
  );
});

test("stdin token 仅允许单行有限 JWT，TTY、超长与环境变量冲突均在 EAS 前失败", async () => {
  const baseOptions = {
    accessTokenStdin: true,
    runtimeVersion: currentRuntimeVersion,
    message: "门店修复",
    platform: "ios",
  };
  const baseEnvironment = {
    EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
    HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
  };
  let commands = 0;
  const dependencies = {
    environment: baseEnvironment,
    logger: { log() {} },
    registerOtaReleaseFn: async () => ({
      url: "https://center.example/api/mobile-app-builds/ota-updates",
    }),
    runCommandFn: async () => {
      commands += 1;
      return { stdout: createJsonOutput("ios"), stderr: "" };
    },
  };

  await runPublishPosHandheldOtaRelease(baseOptions, {
    ...dependencies,
    readAccessTokenStdinFn: async () => administratorAccessToken,
  });
  assert.equal(commands, 1);

  for (const token of [
    "",
    "header.payload.signature\nsecond.token.value",
    `header.payload.${"x".repeat(4_100)}`,
  ]) {
    commands = 0;
    await assert.rejects(
      () =>
        runPublishPosHandheldOtaRelease(baseOptions, {
          ...dependencies,
          readAccessTokenStdinFn: async () => token,
        }),
      /JWT|access token|4096/i,
    );
    assert.equal(commands, 0);
  }

  commands = 0;
  await assert.rejects(
    () =>
      runPublishPosHandheldOtaRelease(baseOptions, {
        ...dependencies,
        environment: {
          ...baseEnvironment,
          HBPOS_OTA_CENTER_ACCESS_TOKEN: administratorAccessToken,
        },
        readAccessTokenStdinFn: async () => {
          throw new Error("冲突时不得读取 stdin");
        },
      }),
    /不能同时使用/,
  );
  assert.equal(commands, 0);

  const tty = Readable.from([]);
  tty.isTTY = true;
  await assert.rejects(() => readAccessTokenFromStdin(tty), /TTY/);
  assert.equal(
    await readAccessTokenFromStdin(
      Readable.from([`${administratorAccessToken}\n`]),
    ),
    administratorAccessToken,
  );
  await assert.rejects(
    () => readAccessTokenFromStdin(Readable.from(["x".repeat(4_097)])),
    /4096/,
  );
});
