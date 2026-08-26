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

export const POS_HANDHELD_PRODUCTION_CHANNEL = "pos-handheld-production";
export const EAS_CLI_VERSION = "21.3.0";

const POS_HANDHELD_PROJECT_NAME = "hb-pos-handheld";
const DEFAULT_PLATFORM = "ios";
const VALID_PLATFORMS = new Set(["ios", "android"]);
const REGISTRATION_PATH = "/api/mobile-app-builds/ota-updates";
const READ_ONLY_SERVICE_TOKEN_PREFIX = "hbsvc_";
const RUNTIME_VERSION_MAX_LENGTH = 120;
const APP_ROOT = fileURLToPath(new URL("../", import.meta.url));
const EMPTY_EAS_RESULT = Object.freeze({
  updateGroupId: "",
  updateId: "",
  channel: "",
  branch: "",
  platform: "",
  runtimeVersion: "",
  message: "",
  gitCommitHash: "",
  dashboardUrl: "",
  publishedAt: "",
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
  node scripts/publish-ota-release.mjs --runtime-version <version> --message <message> [--platform ios|android]

参数：
  --runtime-version <version>   OTA 目标 runtimeVersion；必须等于当前 resolved appVersion。
  --message <message>           EAS Update 发布说明。
  --platform <ios|android>      发布平台；默认 ios。
  --project-id <uuid>           专用 EAS projectId；默认读取 EXPO_PUBLIC_HBPOS_EAS_PROJECT_ID。
  --center-base-url <url>       Center 根地址；默认读取 HBPOS_OTA_CENTER_BASE_URL。
  --access-token-stdin          从标准输入读取管理员 JWT；默认读取 HBPOS_OTA_CENTER_ACCESS_TOKEN。
  --dry-run                     只打印 update 和待登记 JSON，不执行 EAS、不发送网络写入。
  --mock-output-file <path>     dry-run 时解析保存的 eas update --json 输出。
  --help, -h                    显示帮助。

所有 OTA 直接发布到固定频道 ${POS_HANDHELD_PRODUCTION_CHANNEL}，并登记到已有通用 OTA 接口。
`;

export function parseEasUpdateOutput(output, platform = DEFAULT_PLATFORM) {
  const selectedPlatform = requiredPlatform(platform);
  const parsed = parseJsonSafely(output);
  if (!parsed) {
    return { ...EMPTY_EAS_RESULT, platform: selectedPlatform };
  }

  const objects = collectObjects(parsed);
  const selectedUpdate = objects.find(
    (candidate) =>
      stringField(candidate, ["platform"]).toLowerCase() === selectedPlatform,
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
    updateId:
      stringField(selectedUpdate, ["id", "updateId"]) ||
      firstStringFromObjects(objects, [
        selectedPlatform === "android" ? "androidUpdateId" : "iosUpdateId",
      ]),
    channel: firstStringFromObjects(objects, ["channel"]),
    branch:
      stringField(selectedUpdate, ["branch", "branchName"]) ||
      firstStringFromObjects(objects, ["branch", "branchName"]),
    platform: selectedPlatform,
    runtimeVersion:
      stringField(selectedUpdate, ["runtimeVersion"]) ||
      firstStringFromObjects(objects, ["runtimeVersion"]),
    message:
      stringField(selectedUpdate, ["message", "commitMessage"]) ||
      firstStringFromObjects(objects, ["message", "commitMessage"]),
    gitCommitHash:
      stringField(selectedUpdate, ["gitCommitHash", "gitCommit", "commit"]) ||
      firstStringFromObjects(objects, [
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
    publishedAt: firstStringFromObjects(objects, [
      "publishedAt",
      "createdAt",
    ]),
  };
}

export function buildOtaReleasePayload(
  parsed,
  options,
  fallbackPublishedAt = new Date().toISOString(),
) {
  const platform = requiredPlatform(options.platform);
  const updateId = parsed.updateId || null;
  return {
    projectName: POS_HANDHELD_PROJECT_NAME,
    updateGroupId: parsed.updateGroupId || null,
    updateId,
    androidUpdateId: platform === "android" ? updateId : null,
    channel: POS_HANDHELD_PRODUCTION_CHANNEL,
    branch: parsed.branch || POS_HANDHELD_PRODUCTION_CHANNEL,
    platform,
    runtimeVersion: parsed.runtimeVersion || options.runtimeVersion,
    message: parsed.message || requiredText(options.message, "--message", 1_000),
    gitCommitHash: parsed.gitCommitHash || null,
    dashboardUrl: parsed.dashboardUrl || null,
    publishedAt: parsed.publishedAt || fallbackPublishedAt,
    isRollback: false,
    rollbackOfGroupId: null,
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
  const normalizedBasePath = url.pathname.replace(/\/+$/u, "");
  const requestPath = normalizedBasePath.endsWith("/api")
    ? targetPath.replace(/^\/api/u, "")
    : targetPath;
  url.pathname = `${normalizedBasePath}${requestPath}`;
  url.search = "";
  url.hash = "";
  return url.toString();
}

export function buildRegistrationUrl(baseUrl) {
  return buildCenterUrl(baseUrl, REGISTRATION_PATH);
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

export function buildEasUpdateCommand(options, environment = process.env) {
  const message = requiredText(options.message, "--message", 1_000);
  const platform = requiredPlatform(options.platform);
  return {
    command: "npx",
    args: [
      `eas-cli@${EAS_CLI_VERSION}`,
      "update",
      "--channel",
      POS_HANDHELD_PRODUCTION_CHANNEL,
      "--platform",
      platform,
      "--message",
      message,
      "--json",
      "--non-interactive",
    ],
    env: buildEasEnvironment(options, environment),
  };
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

export async function runPublishPosHandheldOtaRelease(
  options,
  {
    environment = process.env,
    logger = console,
    readAccessTokenStdinFn = readAccessTokenFromStdin,
    readMockOutputFn = readMockOutput,
    registerOtaReleaseFn = registerOtaRelease,
    reportReleaseEventFn,
    resolveReleaseCommitFn = resolveReleaseCommit,
    runCommandFn = runCommand,
  } = {},
) {
  validateOptions(options);
  const platform = requiredPlatform(options.platform);
  const configuration = await resolveConfiguration(
    options,
    environment,
    options.dryRun !== true,
    readAccessTokenStdinFn,
  );
  const updateCommand = buildEasUpdateCommand(options, environment);
  logger.log(
    `预期命令：${[updateCommand.command, ...updateCommand.args]
      .map(shellQuote)
      .join(" ")}`,
  );
  logger.log(
    `本次 OTA channel：${POS_HANDHELD_PRODUCTION_CHANNEL}；平台：${platform}`,
  );

  if (options.dryRun === true) {
    const output = options.mockOutputFile
      ? await readMockOutputFn(options.mockOutputFile)
      : "";
    const payload = buildOtaReleasePayload(
      parseEasUpdateOutput(output, platform),
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
  // 在 EAS 发起远端发布前解析 commit，避免本地环境在登记完成后才因缺 SHA 失败。
  const resolvedCommit = resolveReleaseCommitFn({ environment });
  const result = await runCommandFn(updateCommand);
  let parsed = parseEasUpdateOutput(result.stdout, platform);
  if (!parsed.updateGroupId && result.stderr) {
    parsed = parseEasUpdateOutput(result.stderr, platform);
  }
  if (
    parsed.channel &&
    parsed.channel !== POS_HANDHELD_PRODUCTION_CHANNEL
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
  validateReleasePayload(payload);
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
  logger.log(`OTA 数据库记录已登记：${registration.url}`);
  if (reportReleaseEventFn) {
    // accepted deploy 的前提是 EAS 发布和 MobileAppOtaUpdate 登记都已成功。
    const event = buildReleaseEvent({
      action: "deploy",
      conclusion: "accepted",
      component: "pos-handheld",
      environment: "Production",
      releaseId: payload.updateGroupId,
      commitSha: selectReleaseEventCommit({
        payloadCommit: payload.gitCommitHash,
        resolvedCommit,
      }),
      startedAtUtc: payload.publishedAt,
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
  return Object.freeze({
    dryRun: false,
    payload,
    registration,
  });
}

export function parsePublishOtaArgs(argv) {
  const options = {
    dryRun: false,
    help: false,
    platform: DEFAULT_PLATFORM,
  };
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
      case "--platform":
        options.platform = value;
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
  requiredPlatform(options.platform);
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
  return [
    "projectName",
    "updateGroupId",
    "updateId",
    "channel",
    "branch",
    "platform",
    "runtimeVersion",
    "message",
    "publishedAt",
  ].filter((field) => !payload[field]);
}

function validateReleasePayload(payload) {
  if (payload.projectName !== POS_HANDHELD_PROJECT_NAME) {
    throw new Error("OTA ProjectName 不匹配");
  }
  requiredUuid(payload.updateGroupId, "updateGroupId");
  requiredUuid(payload.updateId, "updateId");
  const platform = requiredPlatform(payload.platform);
  if (platform === "android") {
    requiredUuid(payload.androidUpdateId, "androidUpdateId");
  } else if (payload.androidUpdateId !== null) {
    throw new Error("iOS OTA 不得携带 androidUpdateId");
  }
  requiredToken(
    payload.runtimeVersion,
    "runtimeVersion",
    RUNTIME_VERSION_MAX_LENGTH,
  );
  requiredToken(payload.branch, "branch", 120);
  requiredText(payload.message, "message", 1_000);
  if (payload.channel !== POS_HANDHELD_PRODUCTION_CHANNEL) {
    throw new Error("OTA channel 不匹配");
  }
  if (
    typeof payload.publishedAt !== "string" ||
    !Number.isFinite(Date.parse(payload.publishedAt))
  ) {
    throw new Error("publishedAt 无效");
  }
}

function requiredPlatform(value) {
  const platform = requiredText(
    value ?? DEFAULT_PLATFORM,
    "--platform",
    20,
  ).toLowerCase();
  if (!VALID_PLATFORMS.has(platform)) {
    throw new Error("--platform 必须是 ios 或 android");
  }
  return platform;
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
  await runPublishPosHandheldOtaRelease(options, {
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
