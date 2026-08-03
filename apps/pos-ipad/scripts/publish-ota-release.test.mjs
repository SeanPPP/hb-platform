import assert from "node:assert/strict";
import { Readable } from "node:stream";
import test from "node:test";

import {
  EAS_CLI_VERSION,
  POS_IPAD_PRODUCTION_CHANNEL,
  POS_IPAD_RELEASE_CHANNEL_PREFIX,
  buildEasChannelCreateCommand,
  buildEasUpdateCommand,
  buildOtaReleasePayload,
  buildPreflightUrl,
  buildRegistrationUrl,
  parseEasUpdateOutput,
  parsePublishOtaArgs,
  preflightOtaRelease,
  readAccessTokenFromStdin,
  registerOtaRelease,
  runPublishPosIpadOtaRelease,
} from "./publish-ota-release.mjs";

const projectId = "123e4567-e89b-42d3-a456-426614174000";
const groupId = "223e4567-e89b-42d3-a456-426614174000";
const iosUpdateId = "323e4567-e89b-42d3-a456-426614174000";
const releaseChannel = `${POS_IPAD_RELEASE_CHANNEL_PREFIX}20260730-a`;
const currentRuntimeVersion = "0.2.0";
const administratorAccessToken =
  "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJhZG1pbiJ9.signature";
const liveEnvironment = Object.freeze({
  EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
  HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
  HBPOS_OTA_CENTER_ACCESS_TOKEN: administratorAccessToken,
  HBPOS_OTA_ADMIN_JWT: administratorAccessToken,
});
const jsonOutput = JSON.stringify([
  {
    id: iosUpdateId,
    createdAt: "2026-07-30T01:02:03.000Z",
    group: groupId,
    branch: "release-branch-20260730-a",
    runtimeVersion: currentRuntimeVersion,
    platform: "ios",
    manifestPermalink:
      `https://expo.dev/projects/${projectId}/updates/${groupId}`,
    gitCommitHash: "abcdef1234567890",
  },
]);

test("发布 runtime 必须严格绑定当前 resolved appVersion", () => {
  const currentCommand = buildEasUpdateCommand(
    {
      runtimeVersion: currentRuntimeVersion,
      message: "当前原生版本修复",
      projectId,
      releaseChannel,
    },
    {},
  );
  assert.equal(
    currentCommand.env.EXPO_PUBLIC_HBPOS_RUNTIME_VERSION,
    currentRuntimeVersion,
  );

  assert.throws(
    () =>
      buildEasUpdateCommand(
        {
          runtimeVersion: "0.1.0",
          message: "错误指向旧原生版本",
          projectId,
          releaseChannel,
        },
        {},
      ),
    /runtime-version.*当前.*0\.2\.0/u,
  );
});

test("按 EAS 真实数组结构解析 iOS update/group，branch 不冒充 release channel", () => {
  assert.deepEqual(parseEasUpdateOutput(jsonOutput), {
    updateGroupId: groupId,
    iosUpdateId,
    channel: "",
    runtimeVersion: currentRuntimeVersion,
    gitCommitHash: "abcdef1234567890",
    dashboardUrl:
      `https://expo.dev/projects/${projectId}/updates/${groupId}`,
    publishedAtUtc: "2026-07-30T01:02:03.000Z",
  });

  assert.deepEqual(
    parseEasUpdateOutput(`
      Update group ID ${groupId}
      iOS update ID ${iosUpdateId}
    `),
    {
      updateGroupId: "",
      iosUpdateId: "",
      channel: "",
      runtimeVersion: "",
      gitCommitHash: "",
      dashboardUrl: "",
      publishedAtUtc: "",
    },
  );
});

test("登记 payload 精确使用冻结字段且不携带 rollout 激活参数", () => {
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(jsonOutput),
    { runtimeVersion: currentRuntimeVersion, releaseChannel },
    "2026-07-30T02:00:00.000Z",
  );
  assert.deepEqual(payload, {
    updateGroupId: groupId,
    iosUpdateId,
    channel: releaseChannel,
    runtimeVersion: currentRuntimeVersion,
    gitCommitHash: "abcdef1234567890",
    dashboardUrl:
      `https://expo.dev/projects/${projectId}/updates/${groupId}`,
    publishedAtUtc: "2026-07-30T01:02:03.000Z",
    isRollback: false,
    rollbackOfReleaseId: null,
  });
  assert.equal("state" in payload, false);
  assert.equal("activate" in payload, false);
});

test("EAS 先创建独立 channel 再发布 iOS update，两个子进程都移除管理员 token", () => {
  const options = {
    runtimeVersion: currentRuntimeVersion,
    message: "门店修复",
    releaseChannel,
  };
  const createCommand = buildEasChannelCreateCommand(
    options,
    liveEnvironment,
  );
  const updateCommand = buildEasUpdateCommand(options, liveEnvironment);

  assert.deepEqual(createCommand.args, [
    `eas-cli@${EAS_CLI_VERSION}`,
    "channel:create",
    releaseChannel,
    "--json",
    "--non-interactive",
  ]);
  assert.deepEqual(updateCommand.args.slice(0, 8), [
    `eas-cli@${EAS_CLI_VERSION}`,
    "update",
    "--channel",
    releaseChannel,
    "--platform",
    "ios",
    "--message",
    "门店修复",
  ]);
  assert.ok(updateCommand.args.includes("--json"));
  for (const command of [createCommand, updateCommand]) {
    assert.equal(command.env.HBPOS_OTA_CENTER_ACCESS_TOKEN, undefined);
    assert.equal(
      command.env.HBPOS_OTA_ADMIN_JWT,
      undefined,
      "README 使用的管理员 JWT 变量也不得进入 EAS 子进程",
    );
    assert.equal(
      command.env.EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID,
      projectId,
    );
  }

  const fromArgument = buildEasUpdateCommand(
    {
      runtimeVersion: currentRuntimeVersion,
      message: "门店修复",
      projectId,
      releaseChannel,
    },
    {},
  );
  assert.equal(
    fromArgument.env.EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID,
    projectId,
  );
});

test("runtime compatibility token 只接受当前 appVersion", () => {
  const maximum = `r${"a".repeat(119)}`;
  const command = buildEasUpdateCommand(
    {
      runtimeVersion: currentRuntimeVersion,
      message: "边界验证",
      releaseChannel,
    },
    { EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId },
  );
  assert.equal(
    command.env.EXPO_PUBLIC_HBPOS_RUNTIME_VERSION,
    currentRuntimeVersion,
  );

  for (const runtimeVersion of [
    maximum,
    `r${"a".repeat(120)}`,
    "-leading",
    "bad:runtime",
    "bad runtime",
  ]) {
    assert.throws(
      () =>
        buildEasUpdateCommand(
          {
            runtimeVersion,
            message: "非法字符",
            releaseChannel,
          },
          { EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId },
        ),
      /runtime-version/,
    );
  }
});

test("dry-run 零网络零 EAS，只打印两个预期命令和待登记 payload 且不泄露 token", async () => {
  let commands = 0;
  let preflights = 0;
  let registrations = 0;
  const logs = [];
  const result = await runPublishPosIpadOtaRelease(
    {
      dryRun: true,
      runtimeVersion: currentRuntimeVersion,
      message: "门店修复",
      releaseChannel,
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
      readMockOutputFn: async () => jsonOutput,
      runCommandFn: async () => {
        commands += 1;
        return { stdout: jsonOutput, stderr: "" };
      },
      preflightOtaReleaseFn: async () => {
        preflights += 1;
      },
      registerOtaReleaseFn: async () => {
        registrations += 1;
      },
    },
  );
  assert.equal(result.dryRun, true);
  assert.equal(commands, 0);
  assert.equal(preflights, 0);
  assert.equal(registrations, 0);
  assert.equal(result.payload.channel, releaseChannel);
  assert.ok(logs.some((line) => line.includes(groupId)));
  assert.ok(logs.some((line) => line.includes("channel:create")));
  assert.ok(logs.some((line) => line.includes("update --channel")));
  assert.equal(logs.join("\n").includes(administratorAccessToken), false);
});

test("缺 projectId、独立 channel 或管理员 access token 在 EAS 发布前 fail-fast", async () => {
  let commands = 0;
  const dependencies = {
    logger: { log() {} },
    runCommandFn: async () => {
      commands += 1;
      return { stdout: jsonOutput, stderr: "" };
    },
  };
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        {
          dryRun: true,
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
          releaseChannel,
        },
        { ...dependencies, environment: {} },
      ),
    /EAS projectId/,
  );
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        {
          dryRun: true,
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
        },
        {
          ...dependencies,
          environment: {
            EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
          },
        },
      ),
    /--release-channel/,
  );
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        {
          dryRun: true,
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
          releaseChannel: POS_IPAD_PRODUCTION_CHANNEL,
        },
        {
          ...dependencies,
          environment: {
            EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
          },
        },
      ),
    /release channel/,
  );
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        {
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
          releaseChannel,
        },
        {
          ...dependencies,
          environment: {
            EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
            HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
          },
        },
      ),
    /access token/i,
  );
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        {
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
          releaseChannel,
        },
        {
          ...dependencies,
          environment: {
            EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
            HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
            HBPOS_OTA_CENTER_ACCESS_TOKEN: "hbsvc_read_only",
          },
        },
      ),
    /service token|hbsvc_/i,
  );
  for (const invalidToken of [
    "not-a-jwt",
    "header.payload",
    "header.payload.signature.extra",
    "header..signature",
    "header.payload.signature ",
    "header.payload.+invalid",
  ]) {
    await assert.rejects(
      () =>
        runPublishPosIpadOtaRelease(
          {
            runtimeVersion: currentRuntimeVersion,
            message: "门店修复",
            releaseChannel,
          },
          {
            ...dependencies,
            environment: {
              EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
              HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
              HBPOS_OTA_CENTER_ACCESS_TOKEN: invalidToken,
            },
          },
        ),
      /JWT|access token/i,
    );
  }
  assert.equal(commands, 0);
});

test("Center 预检使用同一管理员 JWT 并严格验证规范 channel 与 available", async () => {
  const calls = [];
  const result = await preflightOtaRelease(
    releaseChannel,
    {
      baseUrl: "https://center.example",
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
            data: { channel: releaseChannel, available: true },
          }),
      };
    },
  );
  assert.deepEqual(result, {
    url: "https://center.example/api/pos-ipad/ota-releases/preflight",
    channel: releaseChannel,
  });
  assert.equal(calls[0].init.method, "POST");
  assert.equal(
    calls[0].init.headers.Authorization,
    `Bearer ${administratorAccessToken}`,
  );
  assert.deepEqual(JSON.parse(calls[0].init.body), {
    channel: releaseChannel,
  });
  assert.equal(
    buildPreflightUrl("https://center.example"),
    "https://center.example/api/pos-ipad/ota-releases/preflight",
  );

  for (const response of [
    {
      ok: false,
      status: 401,
      statusText: "Unauthorized",
      text: async () => "",
    },
    {
      ok: false,
      status: 403,
      statusText: "Forbidden",
      text: async () => "",
    },
    {
      ok: false,
      status: 409,
      statusText: "Conflict",
      text: async () => "",
    },
    {
      ok: true,
      status: 200,
      statusText: "OK",
      text: async () =>
        JSON.stringify({
          success: false,
          errorCode: "OTA_CHANNEL_ALREADY_REGISTERED",
        }),
    },
    {
      ok: true,
      status: 200,
      statusText: "OK",
      text: async () =>
        JSON.stringify({
          success: true,
          data: { channel: releaseChannel, available: false },
        }),
    },
    {
      ok: true,
      status: 200,
      statusText: "OK",
      text: async () =>
        JSON.stringify({
          success: true,
          data: {
            channel: `${POS_IPAD_RELEASE_CHANNEL_PREFIX}other`,
            available: true,
          },
        }),
    },
  ]) {
    await assert.rejects(
      () =>
        preflightOtaRelease(
          releaseChannel,
          {
            baseUrl: "https://center.example",
            accessToken: administratorAccessToken,
          },
          async () => response,
        ),
      /预检失败|不可用|不匹配/,
    );
  }
  await assert.rejects(
    () =>
      preflightOtaRelease(
        releaseChannel,
        {
          baseUrl: "https://center.example",
          accessToken: administratorAccessToken,
        },
        async () => {
          throw new Error("socket closed");
        },
    ),
    /预检失败.*网络/,
  );
  await assert.rejects(
    () =>
      preflightOtaRelease(
        releaseChannel,
        {
          baseUrl: "https://center.example",
          accessToken: administratorAccessToken,
        },
        async () => ({
          ok: true,
          status: 200,
          statusText: "OK",
          text: async () => {
            throw new Error("response interrupted");
          },
        }),
      ),
    /预检失败.*网络/,
  );
});

test("live 的 401、403、冲突或网络预检失败均在零 EAS 命令时停止", async () => {
  for (const reason of [
    "HTTP 401 Unauthorized",
    "HTTP 403 Forbidden",
    "OTA_CHANNEL_ALREADY_REGISTERED",
    "网络错误",
  ]) {
    let commands = 0;
    await assert.rejects(
      () =>
        runPublishPosIpadOtaRelease(
          { runtimeVersion: currentRuntimeVersion, message: "门店修复", releaseChannel },
          {
            environment: liveEnvironment,
            logger: { log() {} },
            preflightOtaReleaseFn: async () => {
              throw new Error(`OTA release 预检失败：${reason}`);
            },
            runCommandFn: async () => {
              commands += 1;
              return { stdout: jsonOutput, stderr: "" };
            },
          },
        ),
      /预检失败/,
    );
    assert.equal(commands, 0);
  }
});

test("live 严格按 Center 预检、创建 channel、发布 update、登记 release 执行", async () => {
  const payloads = [];
  const registrationConfigs = [];
  const events = [];
  await runPublishPosIpadOtaRelease(
    { runtimeVersion: currentRuntimeVersion, message: "门店修复", releaseChannel },
    {
      environment: liveEnvironment,
      logger: { log() {} },
      preflightOtaReleaseFn: async (channel, config) => {
        events.push("preflight");
        assert.equal(channel, releaseChannel);
        assert.deepEqual(config, {
          baseUrl: "https://center.example",
          accessToken: administratorAccessToken,
        });
      },
      runCommandFn: async (command) => {
        const operation = command.args[1];
        events.push(operation);
        return operation === "channel:create"
          ? { stdout: JSON.stringify({ name: releaseChannel }), stderr: "" }
          : { stdout: jsonOutput, stderr: "" };
      },
      registerOtaReleaseFn: async (payload, config) => {
        events.push("register");
        payloads.push(payload);
        registrationConfigs.push(config);
        return { url: buildRegistrationUrl("https://center.example") };
      },
    },
  );
  assert.equal(payloads.length, 1);
  assert.equal(payloads[0].updateGroupId, groupId);
  assert.deepEqual(registrationConfigs, [
    {
      baseUrl: "https://center.example",
      accessToken: administratorAccessToken,
    },
  ]);
  assert.deepEqual(events, [
    "preflight",
    "channel:create",
    "update",
    "register",
  ]);

  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        { runtimeVersion: currentRuntimeVersion, message: "门店修复", releaseChannel },
        {
          environment: liveEnvironment,
          logger: { log() {} },
          preflightOtaReleaseFn: async () => {},
          runCommandFn: async (command) =>
            command.args[1] === "channel:create"
              ? { stdout: "{}", stderr: "" }
              : {
                  stdout: JSON.stringify({ updates: [] }),
                  stderr: "",
                },
          registerOtaReleaseFn: async () => {
            throw new Error("must not register");
          },
        },
      ),
    /缺少.*updateGroupId.*iosUpdateId/s,
  );
});

test("EAS channel 已存在或创建失败时不发布 update、不登记且不自动删除", async () => {
  const commands = [];
  let registrations = 0;
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        { runtimeVersion: currentRuntimeVersion, message: "门店修复", releaseChannel },
        {
          environment: liveEnvironment,
          logger: { log() {} },
          preflightOtaReleaseFn: async () => {},
          runCommandFn: async (command) => {
            commands.push(command.args);
            throw new Error("EAS channel 已存在");
          },
          registerOtaReleaseFn: async () => {
            registrations += 1;
          },
        },
      ),
    /channel 已存在/,
  );
  assert.equal(commands.length, 1);
  assert.equal(commands[0][1], "channel:create");
  assert.equal(
    commands.some((args) => args.includes("channel:delete")),
    false,
  );
  assert.equal(registrations, 0);
});

test("EAS 已发布但登记失败时打印可重试 payload，绝不重新 publish 或激活", async () => {
  const commands = [];
  const logs = [];
  let registrations = 0;
  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        { runtimeVersion: currentRuntimeVersion, message: "门店修复", releaseChannel },
        {
          environment: liveEnvironment,
          logger: { log: (value) => logs.push(String(value)) },
          preflightOtaReleaseFn: async () => {},
          runCommandFn: async (command) => {
            commands.push(command.args);
            return command.args[1] === "channel:create"
              ? { stdout: "{}", stderr: "" }
              : { stdout: jsonOutput, stderr: "" };
          },
          registerOtaReleaseFn: async () => {
            registrations += 1;
            throw new Error("Center 暂时不可用");
          },
        },
      ),
    /Center 暂时不可用/,
  );
  assert.deepEqual(
    commands.map((args) => args[1]),
    ["channel:create", "update"],
  );
  assert.equal(registrations, 1);
  assert.ok(logs.some((line) => line.includes("可重试登记 payload")));
  assert.ok(logs.some((line) => line.includes(groupId)));
  assert.ok(logs.some((line) => line.includes("不得重新发布")));
  assert.equal(logs.some((line) => line.includes("rollout")), false);
});

test("POST 使用专用路径和管理员 Bearer access token，并拒绝只读 service token", async () => {
  const calls = [];
  const payload = buildOtaReleasePayload(
    parseEasUpdateOutput(jsonOutput),
    { runtimeVersion: currentRuntimeVersion, releaseChannel },
  );
  const result = await registerOtaRelease(
    payload,
    {
      baseUrl: "https://center.example",
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
    url: "https://center.example/api/pos-ipad/ota-releases",
  });
  assert.equal(
    calls[0].init.headers.Authorization,
    `Bearer ${administratorAccessToken}`,
  );
  assert.deepEqual(JSON.parse(calls[0].init.body), payload);

  await assert.rejects(
    () =>
      registerOtaRelease(
        payload,
        {
          baseUrl: "https://center.example",
          accessToken: "hbsvc_read_only",
        },
        async () => {
          throw new Error("只读 service token 不得触网");
        },
      ),
    /service token|hbsvc_/i,
  );
});

test("管理员 access token 只允许发往 HTTPS，HTTP 仅放行 loopback 本地联调", () => {
  assert.throws(
    () => buildRegistrationUrl("http://center.example"),
    /HTTPS|loopback/,
  );
  assert.equal(
    buildRegistrationUrl("http://localhost:5002"),
    "http://localhost:5002/api/pos-ipad/ota-releases",
  );
  assert.equal(
    buildRegistrationUrl("http://127.0.0.1:5002"),
    "http://127.0.0.1:5002/api/pos-ipad/ota-releases",
  );
  assert.equal(
    buildRegistrationUrl("http://[::1]:5002"),
    "http://[::1]:5002/api/pos-ipad/ota-releases",
  );
});

test("连续 release 的命令与登记 payload 各自使用独立 channel", () => {
  const channels = [
    `${POS_IPAD_RELEASE_CHANNEL_PREFIX}20260730-a`,
    `${POS_IPAD_RELEASE_CHANNEL_PREFIX}20260730-b`,
  ];
  const commands = channels.map((channel) =>
    buildEasUpdateCommand(
      {
        runtimeVersion: currentRuntimeVersion,
        message: "门店修复",
        releaseChannel: channel,
      },
      { EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId },
    ),
  );
  const payloads = channels.map((channel) =>
    buildOtaReleasePayload(
      parseEasUpdateOutput(jsonOutput),
      { runtimeVersion: currentRuntimeVersion, releaseChannel: channel },
    ),
  );

  assert.deepEqual(
    commands.map((command) =>
      command.args[command.args.indexOf("--channel") + 1]),
    channels,
  );
  assert.deepEqual(payloads.map((payload) => payload.channel), channels);
});

test("EAS JSON 显式返回 channel 时必须与命令及 payload 一致", async () => {
  const mismatchedOutput = JSON.stringify({
    channel: `${POS_IPAD_RELEASE_CHANNEL_PREFIX}other`,
    updates: JSON.parse(jsonOutput),
  });
  let registrations = 0;

  await assert.rejects(
    () =>
      runPublishPosIpadOtaRelease(
        {
          runtimeVersion: currentRuntimeVersion,
          message: "门店修复",
          releaseChannel,
        },
        {
          environment: liveEnvironment,
          logger: { log() {} },
          preflightOtaReleaseFn: async () => {},
          runCommandFn: async (command) =>
            command.args[1] === "channel:create"
              ? { stdout: "{}", stderr: "" }
              : {
                  stdout: mismatchedOutput,
                  stderr: "",
                },
          registerOtaReleaseFn: async () => {
            registrations += 1;
          },
        },
      ),
    /channel 不匹配/,
  );
  assert.equal(registrations, 0);
});

test("CLI 禁止 argv token，只接受无值的 --access-token-stdin", () => {
  assert.equal(
    parsePublishOtaArgs([
      "--runtime-version",
      currentRuntimeVersion,
      "--release-channel",
      releaseChannel,
      "--message",
      "门店修复",
      "--access-token-stdin",
    ]).accessTokenStdin,
    true,
  );
  assert.throws(
    () =>
      parsePublishOtaArgs([
        "--runtime-version",
        currentRuntimeVersion,
        "--release-channel",
        releaseChannel,
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
        "--release-channel",
        releaseChannel,
        "--message",
        "门店修复",
        `--access-token=${administratorAccessToken}`,
      ]),
    (error) =>
      error instanceof Error &&
      /禁止使用 --access-token/u.test(error.message) &&
      !error.message.includes(administratorAccessToken),
  );
  assert.throws(
    () =>
      parsePublishOtaArgs([
        "--runtime-version",
        currentRuntimeVersion,
        "--release-channel",
        releaseChannel,
        "--message",
        "门店修复",
        "--service-token",
        "hbsvc_read_only",
      ]),
    /未知参数.*--service-token/,
  );
});

test("stdin token 仅允许单行有限 JWT，TTY、超长与环境变量冲突均在 EAS 前失败", async () => {
  const baseOptions = {
    accessTokenStdin: true,
    runtimeVersion: currentRuntimeVersion,
    message: "门店修复",
    releaseChannel,
  };
  const baseEnvironment = {
    EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
    HBPOS_OTA_CENTER_BASE_URL: "https://center.example",
  };
  let commands = 0;
  const dependencies = {
    environment: baseEnvironment,
    logger: { log() {} },
    preflightOtaReleaseFn: async () => {},
    registerOtaReleaseFn: async () => ({
      url: "https://center.example/api/pos-ipad/ota-releases",
    }),
    runCommandFn: async () => {
      commands += 1;
      return { stdout: jsonOutput, stderr: "" };
    },
  };

  await runPublishPosIpadOtaRelease(baseOptions, {
    ...dependencies,
    readAccessTokenStdinFn: async () => administratorAccessToken,
  });
  assert.equal(commands, 2);

  for (const token of [
    "",
    "header.payload.signature\nsecond.token.value",
    `header.payload.${"x".repeat(4_100)}`,
  ]) {
    commands = 0;
    await assert.rejects(
      () =>
        runPublishPosIpadOtaRelease(baseOptions, {
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
      runPublishPosIpadOtaRelease(baseOptions, {
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
  await assert.rejects(
    () => readAccessTokenFromStdin(tty),
    /TTY/,
  );
  assert.equal(
    await readAccessTokenFromStdin(
      Readable.from([`${administratorAccessToken}\n`]),
    ),
    administratorAccessToken,
  );
  await assert.rejects(
    () =>
      readAccessTokenFromStdin(
        Readable.from(["x".repeat(4_097)]),
      ),
    /4096/,
  );
});
