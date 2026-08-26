#!/usr/bin/env node

import { Buffer } from "node:buffer";
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { buildReleaseEvent, reportReleaseEvent } from "../../../scripts/performance/report-release-event.mjs";
import {
  resolveReleaseCommit,
  selectReleaseEventCommit,
} from "../../../scripts/performance/release-commit.mjs";

const require = createRequire(import.meta.url);
const { getConfig } = require("expo/config");

export const POS_IPAD_PRODUCTION_CHANNEL = "pos-ipad-production";
export const POS_IPAD_RELEASE_CHANNEL_PREFIX = "pos-ipad-release-";
export const EAS_CLI_VERSION = "21.3.0";

const PLATFORM = "ios";
const REGISTRATION_PATH = "/api/pos-ipad/ota-releases";
const PREFLIGHT_PATH = `${REGISTRATION_PATH}/preflight`;
const READ_ONLY_SERVICE_TOKEN_PREFIX = "hbsvc_";
const RUNTIME_VERSION_MAX_LENGTH = 120;
const APP_ROOT = fileURLToPath(new URL("../", import.meta.url));
const EMPTY_EAS_RESULT = Object.freeze({
  updateGroupId: "",
  iosUpdateId: "",
  channel: "",
  runtimeVersion: "",
  gitCommitHash: "",
  dashboardUrl: "",
  publishedAtUtc: "",
});

function requireReleaseReporterConfig(environment) {
  const baseUrl = environment.PERFORMANCE_SERVICE_URL?.trim();
  const token = environment.PERFORMANCE_SERVICE_TOKEN?.trim();
  if (!baseUrl || !token) {
    throw new Error("发布 OTA 前必须配置 PERFORMANCE_SERVICE_URL 和 PERFORMANCE_SERVICE_TOKEN");
  }
  return { baseUrl, token };
}

const HELP_TEXT = `
用法：
  node scripts/publish-ota-release.mjs --runtime-version <version> --release-channel <channel> --message <message>

参数：
  --runtime-version <version>   OTA 目标 runtimeVersion；必须等于当前 resolved appVersion。
  --release-channel <channel>   本次 release 独立 channel，必须以 ${POS_IPAD_RELEASE_CHANNEL_PREFIX} 开头。
  --message <message>           EAS Update 发布说明。
  --project-id <uuid>           专用 EAS projectId；默认读取 EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID。
  --center-base-url <url>       Center 根地址；默认读取 HBPOS_OTA_CENTER_BASE_URL。
  --access-token-stdin          从标准输入读取管理员 JWT；默认读取 HBPOS_OTA_CENTER_ACCESS_TOKEN。
  --dry-run                     只打印 channel:create、update 和待登记 JSON，不执行 EAS、不发送网络写入。
  --mock-output-file <path>     dry-run 时解析保存的 eas update --json 输出。
  --help, -h                    显示帮助。

${POS_IPAD_PRODUCTION_CHANNEL} 仅用于原生 bootstrap，不得发布 release 内容。
每次正式发布必须使用新的独立 release channel，平台固定为 iOS。
本脚本只登记 release，不创建或激活 rollout。
`;

export function parseEasUpdateOutput(output) {
  const parsed = parseJsonSafely(output);
  if (!parsed) return { ...EMPTY_EAS_RESULT };

  const objects = collectObjects(parsed);
  const iosUpdate = objects.find(
    (candidate) =>
      stringField(candidate, ["platform"]).toLowerCase() === PLATFORM,
  );
  const groupObject = objects.find((candidate) =>
    asObject(candidate.group),
  );
  const updateGroupId =
    firstStringFromObjects(objects, ["updateGroupId", "groupId"]) ||
    firstStringFromObjects(objects, ["group"]) ||
    stringField(groupObject?.group, ["id"]);

  return {
    updateGroupId,
    iosUpdateId:
      firstStringFromObjects(objects, ["iosUpdateId"]) ||
      stringField(iosUpdate, ["id", "updateId"]),
    channel: firstStringFromObjects(objects, [
      "channel",
    ]),
    runtimeVersion:
      stringField(iosUpdate, ["runtimeVersion"]) ||
      firstStringFromObjects(objects, ["runtimeVersion"]),
    gitCommitHash: firstStringFromObjects(objects, [
      "gitCommitHash",
      "gitCommit",
      "commit",
    ]),
    dashboardUrl: firstStringFromObjects(objects, [
      "dashboardUrl",
      "dashboardURL",
      "manifestPermalink",
      "url",
    ]),
    publishedAtUtc: firstStringFromObjects(objects, [
      "publishedAtUtc",
      "publishedAt",
      "createdAt",
    ]),
  };
}

export function buildOtaReleasePayload(
  parsed,
  options,
  fallbackPublishedAtUtc = new Date().toISOString(),
) {
  const releaseChannel = requiredReleaseChannel(
    options.releaseChannel,
  );
  return {
    updateGroupId: parsed.updateGroupId || null,
    iosUpdateId: parsed.iosUpdateId || null,
    channel: releaseChannel,
    runtimeVersion: parsed.runtimeVersion || options.runtimeVersion,
    gitCommitHash: parsed.gitCommitHash || null,
    dashboardUrl: parsed.dashboardUrl || null,
    publishedAtUtc:
      parsed.publishedAtUtc || fallbackPublishedAtUtc,
    isRollback: false,
    rollbackOfReleaseId: null,
  };
}

function buildCenterUrl(baseUrl, targetPath) {
  const url = new URL(requiredText(baseUrl, "Center base URL", 2_048));
  const loopbackHttp =
    url.protocol === "http:" &&
    ["localhost", "127.0.0.1", "[::1]", "::1"].includes(url.hostname);
  if (
    (url.protocol !== "https:" && !loopbackHttp) ||
    url.username ||
    url.password
  ) {
    throw new Error(
      "Center base URL 必须使用 HTTPS；HTTP 仅允许无凭据的 loopback 地址",
    );
  }
  url.pathname = `${url.pathname.replace(/\/+$/u, "")}${targetPath}`;
  url.search = "";
  url.hash = "";
  return url.toString();
}

export function buildRegistrationUrl(baseUrl) {
  return buildCenterUrl(baseUrl, REGISTRATION_PATH);
}

export function buildPreflightUrl(baseUrl) {
  return buildCenterUrl(baseUrl, PREFLIGHT_PATH);
}

function buildEasEnvironment(options, environment) {
  const projectId = requiredUuid(
    options.projectId ??
      environment.EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID,
    "EAS projectId",
  );
  const runtimeVersion = requiredToken(
    options.runtimeVersion,
    "--runtime-version",
    RUNTIME_VERSION_MAX_LENGTH,
  );
  const currentRuntimeVersion = resolveCurrentRuntimeVersion();
  if (runtimeVersion !== currentRuntimeVersion) {
    throw new Error(
      `--runtime-version ${runtimeVersion} 与当前 resolved appVersion ` +
        `${currentRuntimeVersion} 不一致`,
    );
  }
  // 管理员凭据绝不能进入 EAS；旧 service token 环境变量也只剔除、不兼容读取。
  const {
    HBPOS_OTA_CENTER_ACCESS_TOKEN: _accessToken,
    HBPOS_OTA_ADMIN_JWT: _documentedAdminJwt,
    HBPOS_OTA_SERVICE_TOKEN: _legacyServiceToken,
    ...childEnvironment
  } = environment;
  return {
    ...childEnvironment,
    EXPO_PUBLIC_HBPOS_BUILD_PROFILE: "production",
    EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID: projectId,
    EXPO_PUBLIC_HBPOS_RUNTIME_VERSION: runtimeVersion,
  };
}

export function resolveCurrentRuntimeVersion() {
  let resolvedConfig;
  try {
    resolvedConfig = getConfig(APP_ROOT, {
      skipSDKVersionRequirement: true,
    }).exp;
  } catch (error) {
    throw new Error(
      `无法解析当前 Expo config：${
        error instanceof Error ? error.message : String(error)
      }`,
      { cause: error },
    );
  }
  const appVersion = requiredToken(
    resolvedConfig.version,
    "当前 Expo appVersion",
    RUNTIME_VERSION_MAX_LENGTH,
  );
  const runtimeVersion = resolvedConfig.runtimeVersion;
  if (
    runtimeVersion &&
    typeof runtimeVersion === "object" &&
    runtimeVersion.policy === "appVersion"
  ) {
    return appVersion;
  }
  if (typeof runtimeVersion === "string" && runtimeVersion === appVersion) {
    return appVersion;
  }
  throw new Error(
    `当前 resolved runtimeVersion 必须使用 appVersion ${appVersion}`,
  );
}

export function buildEasChannelCreateCommand(
  options,
  environment = process.env,
) {
  const releaseChannel = requiredReleaseChannel(options.releaseChannel);
  return {
    command: "npx",
    args: [
      `eas-cli@${EAS_CLI_VERSION}`,
      "channel:create",
      releaseChannel,
      "--json",
      "--non-interactive",
    ],
    env: buildEasEnvironment(options, environment),
  };
}

export function buildEasUpdateCommand(options, environment = process.env) {
  const message = requiredText(options.message, "--message", 1_000);
  const releaseChannel = requiredReleaseChannel(options.releaseChannel);
  return {
    command: "npx",
    args: [
      `eas-cli@${EAS_CLI_VERSION}`,
      "update",
      "--channel",
      releaseChannel,
      "--platform",
      PLATFORM,
      "--message",
      message,
      "--json",
      "--non-interactive",
    ],
    env: buildEasEnvironment(options, environment),
  };
}

export async function preflightOtaRelease(
  releaseChannel,
  config,
  fetchFn = globalThis.fetch,
) {
  const channel = requiredReleaseChannel(releaseChannel);
  const accessToken = requiredAccessToken(config.accessToken);
  const url = buildPreflightUrl(config.baseUrl);
  let response;
  let responseText;
  try {
    response = await fetchFn(url, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${accessToken}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ channel }),
    });
    responseText = await response.text();
  } catch {
    throw new Error("OTA release 预检失败：网络异常");
  }

  if (!response.ok) {
    throw new Error(
      `OTA release 预检失败：HTTP ${response.status} ${response.statusText}${
        responseText ? ` - ${responseText}` : ""
      }`,
    );
  }
  const responsePayload = parseJsonSafely(responseText);
  const success = responseField(responsePayload, "success");
  const data = responseField(responsePayload, "data");
  const responseChannel = responseField(data, "channel");
  const available = responseField(data, "available");
  if (success !== true) {
    const errorCode = responseField(responsePayload, "errorCode");
    throw new Error(
      `OTA release 预检失败${
        typeof errorCode === "string" && errorCode
          ? `：${errorCode}`
          : "：Center 拒绝本次 channel"
      }`,
    );
  }
  let normalizedResponseChannel;
  try {
    normalizedResponseChannel = requiredReleaseChannel(responseChannel);
  } catch {
    throw new Error("OTA release 预检失败：响应 channel 无效");
  }
  if (normalizedResponseChannel !== channel) {
    throw new Error(
      `OTA release 预检失败：响应 channel 不匹配 ${normalizedResponseChannel}`,
    );
  }
  if (available !== true) {
    throw new Error("OTA release 预检失败：channel 不可用");
  }
  return Object.freeze({ url, channel });
}

export async function registerOtaRelease(
  payload,
  config,
  fetchFn = globalThis.fetch,
) {
  const accessToken = requiredAccessToken(config.accessToken);
  const url = buildRegistrationUrl(config.baseUrl);
  const response = await fetchFn(url, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });
  const responseText = await response.text();
  if (!response.ok) {
    throw new Error(
      `OTA release 登记失败：HTTP ${response.status} ${response.statusText}${
        responseText ? ` - ${responseText}` : ""
      }`,
    );
  }
  const responsePayload = parseJsonSafely(responseText);
  const success = responseField(responsePayload, "success");
  const data = responseField(responsePayload, "data");
  const savedGroupId = responseField(data, "updateGroupId");
  if (
    success !== true ||
    typeof savedGroupId !== "string" ||
    savedGroupId.toLowerCase() !==
      String(payload.updateGroupId).toLowerCase()
  ) {
    throw new Error(
      "OTA release 登记失败：响应缺少匹配的 ApiResponse.data.updateGroupId",
    );
  }
  return { url };
}

export async function runPublishPosIpadOtaRelease(
  options,
  {
    environment = process.env,
    logger = console,
    preflightOtaReleaseFn = preflightOtaRelease,
    readAccessTokenStdinFn = readAccessTokenFromStdin,
    readMockOutputFn = readMockOutput,
    registerOtaReleaseFn = registerOtaRelease,
    reportReleaseEventFn,
    resolveReleaseCommitFn = resolveReleaseCommit,
    runCommandFn = runCommand,
  } = {},
) {
  validateOptions(options);
  const configuration = await resolveConfiguration(
    options,
    environment,
    options.dryRun !== true,
    readAccessTokenStdinFn,
  );
  const channelCreateCommand = buildEasChannelCreateCommand(
    options,
    environment,
  );
  const updateCommand = buildEasUpdateCommand(options, environment);
  logger.log(
    `预期命令 1：${[
      channelCreateCommand.command,
      ...channelCreateCommand.args,
    ]
      .map(shellQuote)
      .join(" ")}`,
  );
  logger.log(
    `预期命令 2：${[updateCommand.command, ...updateCommand.args]
      .map(shellQuote)
      .join(" ")}`,
  );
  logger.log(
    `本次 release channel：${options.releaseChannel}；平台：${PLATFORM}`,
  );

  if (options.dryRun === true) {
    const output = options.mockOutputFile
      ? await readMockOutputFn(options.mockOutputFile)
      : "";
    const payload = buildOtaReleasePayload(
      parseEasUpdateOutput(output),
      options,
    );
    logger.log("dry-run：未执行 EAS，未发送 Center 网络写入。");
    logger.log(JSON.stringify(payload, null, 2));
    return Object.freeze({ dryRun: true, payload });
  }

  const centerAccess = {
    baseUrl: configuration.centerBaseUrl,
    accessToken: configuration.accessToken,
  };
  const releaseReporterConfig = reportReleaseEventFn
    ? requireReleaseReporterConfig(environment)
    : null;
  // 在 EAS 或 Center 写入前冻结 commit，避免本地运行到副作用后才发现无法登记验收。
  const resolvedCommit = resolveReleaseCommitFn({ environment });
  await preflightOtaReleaseFn(options.releaseChannel, centerAccess);
  logger.log("Center release channel 预检通过。");

  await runCommandFn(channelCreateCommand);
  logger.log(`EAS channel 已创建：${options.releaseChannel}`);

  const result = await runCommandFn(updateCommand);
  let parsed = parseEasUpdateOutput(result.stdout);
  if (!parsed.updateGroupId && result.stderr) {
    parsed = parseEasUpdateOutput(result.stderr);
  }
  if (
    parsed.channel &&
    parsed.channel !== options.releaseChannel
  ) {
    throw new Error(
      `EAS JSON channel 不匹配：${parsed.channel}`,
    );
  }
  if (
    parsed.runtimeVersion &&
    parsed.runtimeVersion !== options.runtimeVersion
  ) {
    throw new Error(
      `EAS JSON runtimeVersion 不匹配：${parsed.runtimeVersion}`,
    );
  }
  const payload = buildOtaReleasePayload(parsed, options);
  const gaps = requiredRegistrationGaps(payload);
  if (gaps.length > 0) {
    logger.log(JSON.stringify(payload, null, 2));
    throw new Error(
      `EAS JSON 缺少自动登记字段：${gaps.join(", ")}`,
    );
  }
  validateReleasePayload(payload, options.releaseChannel);
  let registration;
  try {
    registration = await registerOtaReleaseFn(payload, centerAccess);
  } catch (error) {
    logger.log(
      "EAS update 已发布但 Center 登记失败；不得重新发布。可重试登记 payload：",
    );
    logger.log(JSON.stringify(payload, null, 2));
    throw error;
  }
  logger.log(`OTA release 已登记：${registration.url}`);
  if (reportReleaseEventFn) {
    // 只有 EAS 发布和 Center 登记均成功后，才允许记为 accepted deploy。
    const event = buildReleaseEvent({
      action: "deploy",
      conclusion: "accepted",
      component: "pos-ipad",
      environment: "Production",
      releaseId: payload.updateGroupId,
      commitSha: selectReleaseEventCommit({
        payloadCommit: payload.gitCommitHash,
        resolvedCommit,
      }),
      startedAtUtc: payload.publishedAtUtc,
      completedAtUtc: new Date().toISOString(),
      healthChecked: true,
      sourceProvider: "expo-ota",
      sourceRunId: payload.updateGroupId,
    });
    try {
      await reportReleaseEventFn({ event, config: releaseReporterConfig });
    } catch (error) {
      throw new Error(
        `OTA 已发布并登记，不得重新发布，只重试 release event 上报：${error.message}`,
      );
    }
    logger.log("OTA release 验收已上报。");
  }
  logger.log("未创建或激活 rollout。");
  return Object.freeze({
    dryRun: false,
    payload,
    registration,
  });
}

export function parsePublishOtaArgs(argv) {
  const options = { dryRun: false, help: false };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help" || argument === "-h") {
      options.help = true;
      continue;
    }
    if (argument === "--dry-run") {
      options.dryRun = true;
      continue;
    }
    if (argument === "--access-token-stdin") {
      if (options.accessTokenStdin === true) {
        throw new Error("参数 --access-token-stdin 不能重复");
      }
      options.accessTokenStdin = true;
      continue;
    }
    if (
      argument === "--access-token" ||
      argument.startsWith("--access-token=")
    ) {
      throw new Error(
        "禁止使用 --access-token 传递凭据；请改用 --access-token-stdin 或 HBPOS_OTA_CENTER_ACCESS_TOKEN",
      );
    }
    if (!argument.startsWith("--")) {
      throw new Error(`未知参数：${argument}`);
    }
    const value = argv[index + 1];
    if (!value || value.startsWith("--")) {
      throw new Error(`参数 ${argument} 缺少取值`);
    }
    switch (argument) {
      case "--runtime-version":
        options.runtimeVersion = value;
        break;
      case "--release-channel":
        options.releaseChannel = value;
        break;
      case "--message":
        options.message = value;
        break;
      case "--project-id":
        options.projectId = value;
        break;
      case "--center-base-url":
        options.centerBaseUrl = value;
        break;
      case "--mock-output-file":
        options.mockOutputFile = value;
        break;
      default:
        throw new Error(`未知参数：${argument}`);
    }
    index += 1;
  }
  return options;
}

function validateOptions(options) {
  requiredToken(
    options.runtimeVersion,
    "--runtime-version",
    RUNTIME_VERSION_MAX_LENGTH,
  );
  requiredReleaseChannel(options.releaseChannel);
  requiredText(options.message, "--message", 1_000);
  if (options.accessToken !== undefined) {
    throw new Error(
      "禁止通过 options.accessToken 传递凭据；请使用标准输入或环境变量",
    );
  }
  if (options.mockOutputFile && options.dryRun !== true) {
    throw new Error("--mock-output-file 只能与 --dry-run 一起使用");
  }
}

async function resolveConfiguration(
  options,
  environment,
  requireRegistration,
  readAccessTokenStdinFn,
) {
  const projectId = requiredUuid(
    options.projectId ??
      environment.EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID,
    "EAS projectId",
  );
  if (!requireRegistration) {
    return {
      projectId,
      centerBaseUrl: "",
      accessToken: "",
    };
  }
  const centerBaseUrl = requiredText(
    options.centerBaseUrl ??
      environment.HBPOS_OTA_CENTER_BASE_URL,
    "Center base URL",
    2_048,
  );
  buildRegistrationUrl(centerBaseUrl);
  const environmentAccessToken =
    environment.HBPOS_OTA_CENTER_ACCESS_TOKEN;
  if (
    options.accessTokenStdin === true &&
    environmentAccessToken !== undefined
  ) {
    throw new Error(
      "--access-token-stdin 与 HBPOS_OTA_CENTER_ACCESS_TOKEN 不能同时使用",
    );
  }
  const accessToken = requiredAccessToken(
    options.accessTokenStdin === true
      ? await readAccessTokenStdinFn()
      : environmentAccessToken,
  );
  return { projectId, centerBaseUrl, accessToken };
}

export async function readAccessTokenFromStdin(
  input = process.stdin,
) {
  if (input.isTTY === true) {
    throw new Error(
      "--access-token-stdin 需要非交互标准输入，不能等待 TTY",
    );
  }
  let byteLength = 0;
  let content = "";
  for await (const chunk of input) {
    const buffer = Buffer.isBuffer(chunk)
      ? chunk
      : Buffer.from(String(chunk));
    byteLength += buffer.byteLength;
    if (byteLength > 4_096) {
      throw new Error("管理员 access token 超过 4096 bytes");
    }
    content += buffer.toString("utf8");
  }
  // 管道工具通常会追加一个换行；只剥离一个行尾，其他空白仍由 JWT 校验拒绝。
  return content.replace(/\r?\n$/u, "");
}

function requiredRegistrationGaps(payload) {
  return ["updateGroupId", "iosUpdateId", "runtimeVersion"].filter(
    (field) => !payload[field],
  );
}

function validateReleasePayload(payload, expectedChannel) {
  requiredUuid(payload.updateGroupId, "updateGroupId");
  requiredUuid(payload.iosUpdateId, "iosUpdateId");
  requiredToken(
    payload.runtimeVersion,
    "runtimeVersion",
    RUNTIME_VERSION_MAX_LENGTH,
  );
  if (
    payload.channel !==
    requiredReleaseChannel(expectedChannel)
  ) {
    throw new Error("OTA release channel 不匹配");
  }
  if (
    typeof payload.publishedAtUtc !== "string" ||
    !Number.isFinite(Date.parse(payload.publishedAtUtc))
  ) {
    throw new Error("publishedAtUtc 无效");
  }
}

function requiredReleaseChannel(value) {
  const channel = requiredText(value, "--release-channel", 120);
  if (
    value !== channel ||
    channel === POS_IPAD_PRODUCTION_CHANNEL ||
    !channel.startsWith(POS_IPAD_RELEASE_CHANNEL_PREFIX) ||
    !/^pos-ipad-release-[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u.test(
      channel,
    )
  ) {
    throw new Error(
      `--release-channel 必须是以 ${POS_IPAD_RELEASE_CHANNEL_PREFIX} 开头且未使用过的 release channel`,
    );
  }
  return channel;
}

function requiredAccessToken(value) {
  const token = requiredText(value, "管理员 access token", 4_096);
  if (
    token
      .toLowerCase()
      .startsWith(READ_ONLY_SERVICE_TOKEN_PREFIX)
  ) {
    throw new Error(
      "只读 hbsvc_ service token 不能登记 OTA release；请使用具备更新管理权限的管理员 access token",
    );
  }
  if (value !== token || /\s/u.test(token)) {
    throw new Error("管理员 access token 无效");
  }
  if (!/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/u.test(token)) {
    throw new Error(
      "管理员 access token 必须是三段 base64url JWT",
    );
  }
  return token;
}

function requiredUuid(value, field) {
  const normalized = requiredText(value, field, 36).toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new Error(`${field} 必须是 UUID`);
  }
  return normalized;
}

function requiredToken(value, field, maximum) {
  const normalized = requiredText(value, field, maximum);
  if (!/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)) {
    throw new Error(`${field} 无效`);
  }
  return normalized;
}

function requiredText(value, field, maximum) {
  if (typeof value !== "string") {
    throw new Error(`${field} 不能为空`);
  }
  const normalized = value.trim();
  if (
    !normalized ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new Error(`${field} 无效`);
  }
  return normalized;
}

function parseJsonSafely(output) {
  const clean = stripAnsi(String(output ?? "")).trim();
  if (!clean) return null;
  try {
    return JSON.parse(clean);
  } catch {
    const starts = [clean.indexOf("{"), clean.indexOf("[")].filter(
      (index) => index >= 0,
    );
    if (!starts.length) return null;
    const start = Math.min(...starts);
    const end = Math.max(clean.lastIndexOf("}"), clean.lastIndexOf("]"));
    if (end <= start) return null;
    try {
      return JSON.parse(clean.slice(start, end + 1));
    } catch {
      return null;
    }
  }
}

function stripAnsi(input) {
  return input.replace(/\x1B\[[0-?]*[ -/]*[@-~]/gu, "");
}

function collectObjects(value, output = []) {
  const object = asObject(value);
  if (object) {
    output.push(object);
    for (const nested of Object.values(object)) {
      collectObjects(nested, output);
    }
  } else if (Array.isArray(value)) {
    for (const nested of value) collectObjects(nested, output);
  }
  return output;
}

function asObject(value) {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value
    : null;
}

function stringField(source, keys) {
  const object = asObject(source);
  if (!object) return "";
  for (const key of keys) {
    const value = object[key];
    if (typeof value === "string" && value.trim()) return value.trim();
  }
  return "";
}

function firstStringFromObjects(objects, keys) {
  for (const object of objects) {
    const value = stringField(object, keys);
    if (value) return value;
  }
  return "";
}

function responseField(source, camelKey) {
  const object = asObject(source);
  if (!object) return undefined;
  const pascalKey = `${camelKey[0].toUpperCase()}${camelKey.slice(1)}`;
  return object[camelKey] ?? object[pascalKey];
}

function shellQuote(value) {
  return /^[A-Za-z0-9_@%+=:,./-]+$/u.test(value)
    ? value
    : `'${value.replace(/'/gu, "'\\''")}'`;
}

function runCommand({ command, args, env }) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: process.cwd(),
      env,
      stdio: ["ignore", "pipe", "pipe"],
    });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => {
      const text = chunk.toString();
      stdout += text;
      process.stdout.write(text);
    });
    child.stderr.on("data", (chunk) => {
      const text = chunk.toString();
      stderr += text;
      process.stderr.write(text);
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve({ stdout, stderr });
        return;
      }
      reject(new Error(`EAS OTA 发布失败，退出码 ${code}`));
    });
  });
}

async function readMockOutput(file) {
  return readFile(path.resolve(process.cwd(), file), "utf8");
}

async function main() {
  const options = parsePublishOtaArgs(process.argv.slice(2));
  if (options.help) {
    console.log(HELP_TEXT.trim());
    return;
  }
  await runPublishPosIpadOtaRelease(options, {
    reportReleaseEventFn: ({ event, config }) =>
      reportReleaseEvent({
        event,
        baseUrl: config.baseUrl,
        token: config.token,
      }),
  });
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))
) {
  main().catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
