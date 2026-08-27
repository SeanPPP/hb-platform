#!/usr/bin/env node

import { Buffer } from "node:buffer";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import {
  buildReleaseEvent,
  reportReleaseEvent,
} from "../../../scripts/performance/report-release-event.mjs";
import {
  resolveReleaseCommit,
  selectReleaseEventCommit,
} from "../../../scripts/performance/release-commit.mjs";

export const EAS_CLI_VERSION = "21.3.0";
export const APP_OTA_PREFLIGHT_PATH = "/api/app-ota-releases/preflight";
export const APP_OTA_REGISTER_PATH = "/api/app-ota-releases/register";
export const LEGACY_MOBILE_OTA_REGISTER_PATH = "/api/mobile-app-builds/ota-updates";

const APP_KEY = "mobile";
const PROJECT_NAME = "hbweb-expo";
const EAS_PROJECT_ID = "3b37541e-6191-460d-9a57-fe6691e206cf";
const VALID_ENVIRONMENTS = new Set(["production", "preview"]);
const VALID_PLATFORMS = new Set(["android", "ios", "all"]);
const MAX_STDIN_TOKEN_BYTES = 4_096;
const EAS_CHANNEL_PAGE_LIMIT = 25;
const MAX_EAS_CHANNEL_PAGES = 100;
const JWT_PATTERN = /^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/;
const SERVICE_TOKEN_PATTERN = /^hbsvc_[A-Za-z0-9_-]{8,}$/;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const RECOVERY_MANIFEST_FIELDS = Object.freeze([
  "appKey",
  "createdAtUtc",
  "environment",
  "releaseBatchId",
  "releases",
  "schemaVersion",
]);
const RECOVERY_RELEASE_FIELDS = Object.freeze([
  "appKey",
  "clientChannel",
  "dashboardUrl",
  "easBranch",
  "easProjectId",
  "environment",
  "gitCommitHash",
  "isRollback",
  "message",
  "platform",
  "projectName",
  "publishedAtUtc",
  "releaseBatchId",
  "releaseChannel",
  "rollbackOfReleaseId",
  "runtimeVersion",
  "updateGroupId",
  "updateId",
]);
const BOOTSTRAP_RECOVERY_MANIFEST_FIELDS = Object.freeze([
  "appKey",
  "bootstrapLegacyFixedChannel",
  "createdAtUtc",
  "environment",
  "releaseBatchId",
  "releases",
  "schemaVersion",
]);
const BOOTSTRAP_RECOVERY_RELEASE_FIELDS = Object.freeze([
  "androidUpdateId",
  "bootstrapLegacyFixedChannel",
  "branch",
  "channel",
  "dashboardUrl",
  "gitCommitHash",
  "isRollback",
  "message",
  "platform",
  "projectName",
  "publishedAt",
  "rollbackOfGroupId",
  "runtimeVersion",
  "updateGroupId",
  "updateId",
]);

const HELP_TEXT = `
用法：
  node scripts/publish-ota-update.mjs --environment production|preview --platform android|ios|all --runtime-version <runtime> --message <message> [--access-token-stdin]
  node scripts/publish-ota-update.mjs --bootstrap-legacy-fixed-channel --environment production|preview --platform android|ios --runtime-version <runtime> --message <message> [--access-token-stdin]
  node scripts/publish-ota-update.mjs --register-only <recovery.json> [--access-token-stdin]

安全行为：
  - 每个平台发布到独立且不可复用的 release channel。
  - 两个平台均在任何 EAS 写入前完成后台 preflight。
  - 发布登记只写不可变事实，不会启用或修改投放策略。
  - 管理员 JWT 只能通过 --access-token-stdin 读取；环境变量只接受 hbsvc_ 服务 token。
  - EAS 已成功但登记失败时写入无凭据 recovery manifest，使用 --register-only 幂等补登记。
  - --bootstrap-legacy-fixed-channel 仅用于迁移窗口中的单平台 fixed-channel bootstrap，绝不写入新策略。
`;

function normalizedText(value) {
  return typeof value === "string" && value.trim() ? value.trim() : "";
}

function requireReleaseReporterConfig(environment) {
  const baseUrl = normalizedText(environment.PERFORMANCE_SERVICE_URL);
  const token = normalizedText(environment.PERFORMANCE_SERVICE_TOKEN);
  if (!baseUrl || !token) {
    throw new Error(
      "发布 OTA 前必须配置 PERFORMANCE_SERVICE_URL 和 PERFORMANCE_SERVICE_TOKEN",
    );
  }
  return { baseUrl, token };
}

async function reportAcceptedOtaRelease(
  payload,
  {
    completedAtUtcFn = () => new Date().toISOString(),
    config,
    logger,
    releaseEnvironment,
    reportReleaseEventFn,
    resolvedCommit,
  },
) {
  if (!reportReleaseEventFn) return;
  const sourceRunId = payload.updateGroupId;
  const startedAtUtc = payload.publishedAtUtc ?? payload.publishedAt;
  const event = buildReleaseEvent({
    action: payload.isRollback ? "rollback" : "deploy",
    conclusion: "accepted",
    component: APP_KEY,
    environment: releaseEnvironment === "production" ? "Production" : "Preview",
    releaseId: sourceRunId,
    commitSha: selectReleaseEventCommit({
      payloadCommit: payload.gitCommitHash,
      resolvedCommit,
    }),
    startedAtUtc,
    completedAtUtc: completedAtUtcFn(),
    healthChecked: true,
    sourceProvider: "expo-ota",
    sourceRunId,
  });
  try {
    await reportReleaseEventFn({ event, config });
  } catch (error) {
    throw new Error(
      `OTA 已发布并登记，不得重新发布，只重试 release event 上报：${redactedError(error)}`,
      { cause: error },
    );
  }
  logger.log(`${payload.platform}: OTA 发布验收已上报。`);
}

function isRecord(value) {
  return value && typeof value === "object" && !Array.isArray(value) ? value : null;
}

function parseJson(output) {
  const text = String(output ?? "").replace(/\x1B\[[0-?]*[ -/]*[@-~]/g, "").trim();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    const starts = [text.indexOf("["), text.indexOf("{")].filter((value) => value >= 0);
    const end = Math.max(text.lastIndexOf("]"), text.lastIndexOf("}"));
    if (!starts.length || end <= Math.min(...starts)) return null;
    try {
      return JSON.parse(text.slice(Math.min(...starts), end + 1));
    } catch {
      return null;
    }
  }
}

function collectRecords(value, records = []) {
  const record = isRecord(value);
  if (record) {
    records.push(record);
    for (const nested of Object.values(record)) collectRecords(nested, records);
  } else if (Array.isArray(value)) {
    for (const nested of value) collectRecords(nested, records);
  }
  return records;
}

function field(record, keys) {
  for (const key of keys) {
    const value = record?.[key];
    if (typeof value === "string" && value.trim()) return value.trim();
  }
  return "";
}

function groupId(record) {
  const direct = field(record, ["updateGroupId", "groupId", "group"]);
  if (direct) return direct;
  const group = isRecord(record?.group);
  return field(group, ["id"]);
}

export function parsePublishOtaArgs(argv) {
  const options = {
    environment: undefined,
    platform: undefined,
    runtimeVersion: undefined,
    message: undefined,
    accessTokenStdin: false,
    dryRun: false,
    help: false,
    rollbackOfReleaseId: null,
    registerOnlyFile: null,
    bootstrapLegacyFixedChannel: false,
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--help" || arg === "-h") {
      options.help = true;
      continue;
    }
    if (arg === "--access-token-stdin") {
      options.accessTokenStdin = true;
      continue;
    }
    if (arg === "--dry-run") {
      options.dryRun = true;
      continue;
    }
    if (arg === "--bootstrap-legacy-fixed-channel") {
      options.bootstrapLegacyFixedChannel = true;
      continue;
    }
    const value = argv[index + 1];
    if (!arg.startsWith("--") || !value || value.startsWith("--")) {
      throw new Error(`参数 ${arg} 缺少取值`);
    }
    switch (arg) {
      case "--environment": options.environment = value; break;
      case "--platform": options.platform = value; break;
      case "--runtime-version": options.runtimeVersion = value; break;
      case "--message": options.message = value; break;
      case "--rollback-of-release-id": options.rollbackOfReleaseId = value; break;
      case "--register-only": options.registerOnlyFile = value; break;
      default: throw new Error(`未知参数：${arg}`);
    }
    index += 1;
  }

  if (!options.help) validatePublishOptions(options);
  return options;
}

function validatePublishOptions(options) {
  if (options.registerOnlyFile) {
    if (
      options.environment
      || options.platform
      || options.runtimeVersion
      || options.message
      || options.rollbackOfReleaseId
      || options.dryRun
      || options.bootstrapLegacyFixedChannel
    ) {
      throw new Error("--register-only 不能与发布参数组合");
    }
    return;
  }
  if (!VALID_ENVIRONMENTS.has(options.environment)) {
    throw new Error("--environment 必须是 production 或 preview");
  }
  if (!VALID_PLATFORMS.has(options.platform)) {
    throw new Error("--platform 必须是 android、ios 或 all");
  }
  if (
    !isNormalizedBoundedText(options.runtimeVersion, 120)
  ) {
    throw new Error("--runtime-version 必须去除首尾空白且不超过 120 个字符");
  }
  if (!isNormalizedBoundedText(options.message, 1_000)) {
    throw new Error("--message 必须去除首尾空白且不超过 1000 个字符");
  }
  if (
    options.rollbackOfReleaseId
    && !UUID_PATTERN.test(options.rollbackOfReleaseId)
  ) {
    throw new Error("--rollback-of-release-id 必须是 UUID");
  }
  if (options.rollbackOfReleaseId && options.platform === "all") {
    throw new Error("rollback 必须按平台分别指定来源，不能与 --platform all 组合");
  }
  if (options.bootstrapLegacyFixedChannel && options.platform === "all") {
    throw new Error("bootstrap legacy fixed channel 必须按单个平台执行");
  }
  if (options.bootstrapLegacyFixedChannel && options.rollbackOfReleaseId) {
    throw new Error("bootstrap legacy fixed channel 不能与 rollback 组合");
  }
}

export function createReleaseChannel(environment, platform, nowIso, entropy = randomUUID()) {
  if (!VALID_ENVIRONMENTS.has(environment) || !["android", "ios"].includes(platform)) {
    throw new Error("Mobile OTA release channel scope is invalid");
  }
  const timestamp = new Date(nowIso).toISOString().toLowerCase().replace(/[-:.]/g, "");
  const suffix = entropy.toLowerCase().replace(/[^a-z0-9]/g, "").slice(0, 8);
  if (suffix.length < 8) throw new Error("Mobile OTA release channel entropy is invalid");
  return `mobile-${environment}-${platform}-release-${timestamp}-${suffix}`;
}

function assertReleaseChannel(options) {
  if (options.bootstrapLegacyFixedChannel) {
    if (
      !["android", "ios"].includes(options.platform)
      || options.releaseChannel !== options.environment
    ) {
      throw new Error("Mobile OTA bootstrap channel must exactly match environment");
    }
    return;
  }
  const prefix = `mobile-${options.environment}-${options.platform}-release-`;
  if (!normalizedText(options.releaseChannel).startsWith(prefix)) {
    throw new Error("Mobile OTA release channel does not match environment/platform");
  }
}

function sanitizedEasEnvironment(environment) {
  return Object.fromEntries(
    Object.entries(environment).filter(([key]) => {
      const normalized = key.toUpperCase();
      if (key === "EXPO_TOKEN") return true;
      return !(
        normalized.startsWith("HBWEB_")
        || normalized.startsWith("PERFORMANCE_")
        || normalized.includes("APP_OTA")
        || normalized.includes("UPDATE_DECISION")
        || normalized.includes("ADMIN_JWT")
        || normalized.includes("SERVICE_TOKEN")
        || /(?:TOKEN|JWT|SECRET|PASSWORD|CREDENTIAL|ACCESS_KEY|PRIVATE_KEY)/.test(normalized)
      );
    }),
  );
}

export function buildEasUpdateCommand(options, environment = process.env) {
  assertReleaseChannel(options);
  return {
    command: "npx",
    args: [
      `eas-cli@${EAS_CLI_VERSION}`,
      "update",
      "--channel",
      options.releaseChannel,
      "--platform",
      options.platform,
      "--message",
      options.message,
      "--json",
      "--non-interactive",
    ],
    env: {
      ...sanitizedEasEnvironment(environment),
      EXPO_PUBLIC_APP_BUILD_PROFILE: options.environment,
      EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED: "true",
      EXPO_PUBLIC_RUNTIME_VERSION: options.runtimeVersion,
    },
  };
}

export function buildEasChannelViewCommand(options, environment = process.env) {
  assertReleaseChannel(options);
  return {
    command: "npx",
    args: [
      `eas-cli@${EAS_CLI_VERSION}`,
      "channel:view",
      options.releaseChannel,
      "--json",
      "--non-interactive",
    ],
    env: {
      ...sanitizedEasEnvironment(environment),
      EXPO_PUBLIC_APP_BUILD_PROFILE: options.environment,
      EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED: "true",
      EXPO_PUBLIC_RUNTIME_VERSION: options.runtimeVersion,
    },
  };
}

export function buildEasChannelListCommand(
  options,
  offset = 0,
  environment = process.env,
) {
  assertReleaseChannel(options);
  if (!Number.isSafeInteger(offset) || offset < 0) {
    throw new Error("EAS channel:list offset is invalid");
  }
  return {
    command: "npx",
    args: [
      `eas-cli@${EAS_CLI_VERSION}`,
      "channel:list",
      "--json",
      "--non-interactive",
      "--limit",
      String(EAS_CHANNEL_PAGE_LIMIT),
      "--offset",
      String(offset),
    ],
    env: {
      ...sanitizedEasEnvironment(environment),
      EXPO_PUBLIC_APP_BUILD_PROFILE: options.environment,
      EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED: "true",
      EXPO_PUBLIC_RUNTIME_VERSION: options.runtimeVersion,
    },
  };
}

export function parseEasUpdateOutput(output, expectedPlatform) {
  const parsed = parseJson(output);
  if (!parsed) return emptyParsedRelease();
  const candidates = collectRecords(parsed).filter(
    (record) => field(record, ["platform"]).toLowerCase() === expectedPlatform,
  );
  const release = candidates.find((record) => field(record, ["id", "updateId"])) ?? candidates[0];
  if (!release) return emptyParsedRelease();
  return {
    updateGroupId: groupId(release),
    updateId: field(release, ["id", "updateId"]),
    channel: field(release, ["channel", "channelName"]),
    branch: field(release, ["branch", "branchName"]),
    platform: field(release, ["platform"]).toLowerCase(),
    runtimeVersion: field(release, ["runtimeVersion"]),
    message: field(release, ["message", "commitMessage"]),
    gitCommitHash: field(release, ["gitCommitHash", "gitCommit", "commit"]),
    dashboardUrl: field(release, ["dashboardUrl", "manifestPermalink", "url"]),
    publishedAt: field(release, ["publishedAt", "createdAt"]),
  };
}

export function parseEasChannelViewOutput(output, expectedChannel, expectedRelease = null) {
  const parsed = isRecord(parseJson(output));
  const channel = isRecord(parsed?.currentPage);
  if (!channel || field(channel, ["name"]) !== expectedChannel) {
    throw new Error("EAS channel:view channel mismatch or missing");
  }
  if (channel.isPaused !== false) {
    throw new Error("EAS channel:view channel must be active");
  }

  const mappingText = field(channel, ["branchMapping"]);
  const mapping = isRecord(parseJson(mappingText));
  const mappingRows = Array.isArray(mapping?.data) ? mapping.data : [];
  if (mapping?.version !== 0 || mappingRows.length !== 1) {
    throw new Error("EAS channel:view branch mapping is invalid");
  }
  const mappingRow = isRecord(mappingRows[0]);
  const branchId = field(mappingRow, ["branchId"]);
  if (!branchId || mappingRow?.branchMappingLogic !== "true") {
    throw new Error("EAS channel:view branch mapping is not an exact fixed mapping");
  }

  const branches = Array.isArray(channel.updateBranches) ? channel.updateBranches : [];
  const matchingBranches = branches.filter((candidate) => {
    const branch = isRecord(candidate);
    return field(branch, ["id"]) === branchId && field(branch, ["name"]) === expectedChannel;
  });
  if (branches.length !== 1 || matchingBranches.length !== 1) {
    throw new Error("EAS channel:view branch identity does not match release channel");
  }

  if (expectedRelease) {
    const branch = matchingBranches[0];
    const groups = Array.isArray(branch.updateGroups) ? branch.updateGroups : [];
    const updates = groups.length === 1 && Array.isArray(groups[0]) ? groups[0] : [];
    if (groups.length !== 1 || updates.length !== 1) {
      throw new Error("EAS channel:view latest update group is missing or ambiguous");
    }
    const update = isRecord(updates[0]);
    const actual = {
      updateId: field(update, ["id", "updateId"]),
      updateGroupId: groupId(update),
      runtimeVersion: field(update, ["runtimeVersion"]),
      platform: field(update, ["platform"]).toLowerCase(),
      message: field(update, ["message", "commitMessage"]),
      gitCommitHash: field(update, ["gitCommitHash", "gitCommit", "commit"]),
      dashboardUrl: field(update, ["dashboardUrl", "manifestPermalink", "url"]),
      publishedAtUtc: field(update, ["publishedAt", "createdAt"]),
    };
    const expected = {
      updateId: expectedRelease.updateId,
      updateGroupId: expectedRelease.updateGroupId,
      runtimeVersion: expectedRelease.runtimeVersion,
      platform: expectedRelease.platform,
      message: expectedRelease.message,
      gitCommitHash: expectedRelease.gitCommitHash ?? "",
      dashboardUrl: expectedRelease.dashboardUrl ?? "",
      publishedAtUtc: expectedRelease.publishedAtUtc,
    };
    for (const [key, value] of Object.entries(expected)) {
      const actualValue = actual[key];
      const matches = key === "updateId" || key === "updateGroupId"
        ? actualValue.toLowerCase() === String(value).toLowerCase()
        : actualValue === value;
      if (!matches) {
        throw new Error(
          `EAS channel:view update ${key} mismatch: expected ${value || "<empty>"}, received ${actualValue || "<empty>"}`,
        );
      }
    }
  }
  return Object.freeze({ channel: expectedChannel, branch: expectedChannel, branchId });
}

export function parseEasChannelListOutput(output) {
  const parsed = isRecord(parseJson(output));
  const page = parsed?.currentPage;
  if (!Array.isArray(page)) {
    throw new Error("EAS channel:list JSON currentPage is invalid");
  }
  const names = page.map((candidate) => {
    const channel = isRecord(candidate);
    const name = field(channel, ["name"]);
    if (!name || name !== name.trim()) {
      throw new Error("EAS channel:list JSON contains an invalid channel");
    }
    return name;
  });
  if (new Set(names).size !== names.length) {
    throw new Error("EAS channel:list JSON contains duplicate channels");
  }
  return Object.freeze(names);
}

export async function assertReleaseChannelsUnused(
  plans,
  environment = process.env,
  runCommandFn = runCommand,
) {
  if (!Array.isArray(plans) || plans.length === 0) {
    throw new Error("EAS channel:list has no release plans to verify");
  }
  const knownChannels = new Set();
  let reachedLastPage = false;
  for (let pageIndex = 0; pageIndex < MAX_EAS_CHANNEL_PAGES; pageIndex += 1) {
    const offset = pageIndex * EAS_CHANNEL_PAGE_LIMIT;
    const command = buildEasChannelListCommand(plans[0], offset, environment);
    const execution = await runCommandFn(command);
    const names = parseEasChannelListOutput(execution.stdout);
    for (const name of names) {
      if (knownChannels.has(name)) {
        throw new Error("EAS channel:list pagination is unstable; cannot prove channel unused");
      }
      knownChannels.add(name);
    }
    if (names.length < EAS_CHANNEL_PAGE_LIMIT) {
      reachedLastPage = true;
      break;
    }
  }
  if (!reachedLastPage) {
    throw new Error("EAS channel:list exceeded the fail-closed pagination limit");
  }
  for (const plan of plans) {
    if (knownChannels.has(plan.releaseChannel)) {
      throw new Error(`EAS release channel already exists: ${plan.releaseChannel}`);
    }
  }
}

function emptyParsedRelease() {
  return {
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
  };
}

function assertPublishedRelease(parsed, options) {
  const expected = {
    platform: options.platform,
    runtimeVersion: options.runtimeVersion,
    branch: options.releaseChannel,
    message: options.message,
  };
  for (const [key, value] of Object.entries(expected)) {
    if (parsed[key] !== value) {
      throw new Error(`EAS JSON ${key} mismatch: expected ${value}, received ${parsed[key] || "<empty>"}`);
    }
  }
  for (const key of ["updateGroupId", "updateId"]) {
    if (!UUID_PATTERN.test(parsed[key])) {
      throw new Error(`EAS JSON ${key} is not a UUID`);
    }
  }
  if (!parsed.publishedAt || !Number.isFinite(Date.parse(parsed.publishedAt))) {
    throw new Error("EAS JSON publishedAt is invalid");
  }
  normalizeDashboardUrlFact(parsed.dashboardUrl);
}

function normalizeDashboardUrlFact(value) {
  const dashboardUrl = value == null || value === "" ? null : value;
  if (!isNullableHttpsUrl(dashboardUrl)) {
    throw new Error("EAS JSON dashboardUrl must be null or normalized HTTPS within 2048 characters");
  }
  return dashboardUrl;
}

export function buildOtaReleasePayload(parsed, options) {
  const dashboardUrl = normalizeDashboardUrlFact(parsed.dashboardUrl);
  return Object.freeze({
    releaseBatchId: options.releaseBatchId,
    appKey: APP_KEY,
    environment: options.environment,
    clientChannel: options.environment,
    releaseChannel: options.releaseChannel,
    easBranch: parsed.branch,
    projectName: PROJECT_NAME,
    easProjectId: EAS_PROJECT_ID,
    platform: options.platform,
    runtimeVersion: parsed.runtimeVersion,
    updateGroupId: parsed.updateGroupId,
    updateId: parsed.updateId,
    message: parsed.message,
    gitCommitHash: parsed.gitCommitHash || null,
    dashboardUrl,
    publishedAtUtc: parsed.publishedAt,
    isRollback: Boolean(options.rollbackOfReleaseId),
    rollbackOfReleaseId: options.rollbackOfReleaseId || null,
  });
}

export function buildLegacyBootstrapPayload(parsed, options) {
  if (
    !options.bootstrapLegacyFixedChannel
    || options.releaseChannel !== options.environment
    || !["android", "ios"].includes(options.platform)
  ) {
    throw new Error("Mobile OTA legacy bootstrap identity is invalid");
  }
  const dashboardUrl = normalizeDashboardUrlFact(parsed.dashboardUrl);
  return Object.freeze({
    projectName: PROJECT_NAME,
    updateGroupId: parsed.updateGroupId,
    updateId: parsed.updateId,
    androidUpdateId: options.platform === "android" ? parsed.updateId : null,
    channel: options.environment,
    branch: parsed.branch,
    platform: options.platform,
    runtimeVersion: parsed.runtimeVersion,
    message: parsed.message,
    gitCommitHash: parsed.gitCommitHash || null,
    dashboardUrl,
    publishedAt: parsed.publishedAt,
    isRollback: false,
    rollbackOfGroupId: null,
    bootstrapLegacyFixedChannel: true,
  });
}

function buildPreflightPayload(options) {
  return Object.freeze({
    releaseBatchId: options.releaseBatchId,
    appKey: APP_KEY,
    environment: options.environment,
    clientChannel: options.environment,
    releaseChannel: options.releaseChannel,
    easBranch: options.releaseChannel,
    projectName: PROJECT_NAME,
    easProjectId: EAS_PROJECT_ID,
    platform: options.platform,
    runtimeVersion: options.runtimeVersion,
    ...(options.rollbackOfReleaseId
      ? { rollbackOfReleaseId: options.rollbackOfReleaseId }
      : {}),
    ...(options.bootstrapLegacyFixedChannel
      ? { bootstrapLegacyFixedChannel: true }
      : {}),
  });
}

function buildBackendUrl(baseUrl, endpointPath) {
  const url = new URL(normalizedText(baseUrl));
  const basePath = url.pathname.replace(/\/+$/, "");
  const suffix = basePath.endsWith("/api")
    ? endpointPath.replace(/^\/api/, "")
    : endpointPath;
  url.pathname = `${basePath}${suffix}`;
  url.search = "";
  url.hash = "";
  return url.toString();
}

export const buildPreflightUrl = (baseUrl) => buildBackendUrl(baseUrl, APP_OTA_PREFLIGHT_PATH);
export const buildRegistrationUrl = (baseUrl) => buildBackendUrl(baseUrl, APP_OTA_REGISTER_PATH);
export const buildLegacyRegistrationUrl = (baseUrl) => (
  buildBackendUrl(baseUrl, LEGACY_MOBILE_OTA_REGISTER_PATH)
);

async function responsePayload(response) {
  const text = await response.text();
  const payload = text ? parseJson(text) : null;
  if (!response.ok) {
    const message = normalizedText(payload?.message) || normalizedText(text) || response.statusText;
    throw new Error(`HTTP ${response.status} ${response.statusText}${message ? ` - ${message}` : ""}`);
  }
  if (payload?.success === false || payload?.isSuccess === false) {
    throw new Error(normalizedText(payload.message) || "backend success=false");
  }
  return payload?.data ?? payload;
}

export async function preflightOtaRelease(payload, auth) {
  const response = await (auth.fetchFn ?? globalThis.fetch)(buildPreflightUrl(auth.baseUrl), {
    method: "POST",
    headers: {
      Authorization: `Bearer ${auth.accessToken}`,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify(payload),
  });
  const data = await responsePayload(response);
  if (data?.valid !== true) throw new Error("Mobile OTA preflight did not return valid=true");
  return data;
}

export async function registerOtaRelease(payload, auth) {
  const response = await (auth.fetchFn ?? globalThis.fetch)(buildRegistrationUrl(auth.baseUrl), {
    method: "POST",
    headers: {
      Authorization: `Bearer ${auth.accessToken}`,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify(payload),
  });
  const data = await responsePayload(response);
  if (!isRecord(data?.release) || typeof data.idempotent !== "boolean") {
    throw new Error("Mobile OTA register response is invalid");
  }
  assertRegisteredReleaseIdentity(data.release, payload);
  return data;
}

function assertRegisteredReleaseIdentity(release, payload) {
  const actualRollbackOfReleaseId = release.rollbackOfReleaseId ?? null;
  const expectedRollbackOfReleaseId = payload.rollbackOfReleaseId ?? null;
  const identityMatches = (
    typeof release.id === "string"
    && UUID_PATTERN.test(release.id)
    && uuidMatches(release.releaseBatchId, payload.releaseBatchId)
    && requiredTextMatches(release.appKey, payload.appKey)
    && requiredTextMatches(release.environment, payload.environment)
    && requiredTextMatches(release.clientChannel, payload.clientChannel)
    && requiredTextMatches(release.releaseChannel, payload.releaseChannel)
    && requiredTextMatches(release.easBranch, payload.easBranch)
    && requiredTextMatches(release.projectName, payload.projectName)
    && requiredTextMatches(release.platform, payload.platform)
    && requiredTextMatches(release.runtimeVersion, payload.runtimeVersion)
    && uuidMatches(release.updateGroupId, payload.updateGroupId)
    && uuidMatches(release.updateId, payload.updateId)
    && nullableTextMatches(release, "message", payload.message)
    && nullableTextMatches(release, "gitCommitHash", payload.gitCommitHash)
    && nullableTextMatches(release, "dashboardUrl", payload.dashboardUrl)
    && utcTimestampMatches(release.publishedAtUtc, payload.publishedAtUtc)
    && typeof release.isRollback === "boolean"
    && release.isRollback === payload.isRollback
    && release.isRollback === (actualRollbackOfReleaseId !== null)
    && (
      expectedRollbackOfReleaseId === null
        ? actualRollbackOfReleaseId === null
        : typeof actualRollbackOfReleaseId === "string"
          && uuidMatches(actualRollbackOfReleaseId, expectedRollbackOfReleaseId)
    )
    && requiredTextMatches(
      release.registrationSource,
      "app-ota-release-api",
    )
  );
  if (!identityMatches) {
    throw new Error("Mobile OTA register response identity is invalid");
  }
}

export async function registerLegacyBootstrapUpdate(payload, auth) {
  const response = await (auth.fetchFn ?? globalThis.fetch)(buildLegacyRegistrationUrl(auth.baseUrl), {
    method: "POST",
    headers: {
      Authorization: `Bearer ${auth.accessToken}`,
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify(payload),
  });
  const data = await responsePayload(response);
  const registered = isRecord(data);
  const expectedAndroidUpdateId = payload.androidUpdateId ?? null;
  const actualAndroidUpdateId = registered?.androidUpdateId ?? null;
  const actualRollbackOfGroupId = registered?.rollbackOfGroupId ?? null;
  if (
    !registered
    || typeof registered.id !== "string"
    || !UUID_PATTERN.test(registered.id)
    || !requiredTextMatches(registered.appKey, APP_KEY)
    || !requiredTextMatches(registered.projectName, payload.projectName)
    || !uuidMatches(registered.updateGroupId, payload.updateGroupId)
    || !uuidMatches(registered.updateId, payload.updateId)
    || (
      expectedAndroidUpdateId === null
        ? actualAndroidUpdateId !== null && actualAndroidUpdateId !== ""
        : typeof actualAndroidUpdateId !== "string"
          || !uuidMatches(actualAndroidUpdateId, expectedAndroidUpdateId)
    )
    || !requiredTextMatches(registered.channel, payload.channel)
    || !requiredTextMatches(registered.branch, payload.branch)
    || !requiredTextMatches(registered.platform, payload.platform)
    || !requiredTextMatches(registered.runtimeVersion, payload.runtimeVersion)
    || !nullableTextMatches(registered, "message", payload.message)
    || !nullableTextMatches(registered, "gitCommitHash", payload.gitCommitHash)
    || !nullableTextMatches(registered, "dashboardUrl", payload.dashboardUrl)
    || !utcTimestampMatches(registered.publishedAt, payload.publishedAt)
    || registered.isRollback !== payload.isRollback
    || (
      payload.rollbackOfGroupId == null
        ? actualRollbackOfGroupId !== null && actualRollbackOfGroupId !== ""
        : typeof actualRollbackOfGroupId !== "string"
          || !uuidMatches(actualRollbackOfGroupId, payload.rollbackOfGroupId)
    )
  ) {
    throw new Error("Mobile OTA legacy bootstrap register response is invalid");
  }
  return registered;
}

function uuidMatches(actual, expected) {
  return typeof actual === "string"
    && typeof expected === "string"
    && UUID_PATTERN.test(actual)
    && UUID_PATTERN.test(expected)
    && actual.toLowerCase() === expected.toLowerCase();
}

function requiredTextMatches(actual, expected) {
  return typeof actual === "string"
    && typeof expected === "string"
    && actual === actual.trim()
    && actual === expected;
}

function nullableTextMatches(record, key, expected) {
  const expectedValue = expected == null || expected === "" ? null : expected;
  if (!Object.hasOwn(record, key)) return expectedValue === null;
  const actual = record[key];
  const actualValue = actual == null || actual === ""
    ? null
    : typeof actual === "string" && actual === actual.trim()
      ? actual
      : Symbol.for("invalid-mobile-ota-fact");
  return actualValue === expectedValue;
}

function utcTimestampMatches(actual, expected) {
  const toUtcMilliseconds = (value) => {
    if (
      typeof value !== "string"
      || value !== value.trim()
      || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})?$/i.test(value)
    ) {
      return null;
    }
    const zoned = /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : `${value}Z`;
    const milliseconds = Date.parse(zoned);
    return Number.isFinite(milliseconds) ? milliseconds : null;
  };
  const actualMilliseconds = toUtcMilliseconds(actual);
  const expectedMilliseconds = toUtcMilliseconds(expected);
  return actualMilliseconds !== null
    && expectedMilliseconds !== null
    && actualMilliseconds === expectedMilliseconds;
}

export async function readAccessTokenFromStdin(stream = process.stdin) {
  if (stream.isTTY === true) {
    throw new Error("--access-token-stdin 需要非交互标准输入，不能等待 TTY");
  }
  let bytes = 0;
  const chunks = [];
  for await (const chunk of stream) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    bytes += buffer.byteLength;
    if (bytes > MAX_STDIN_TOKEN_BYTES) throw new Error("stdin access token exceeds 4096 bytes");
    chunks.push(buffer);
  }
  const token = Buffer.concat(chunks).toString("utf8").trim();
  if (!token) throw new Error("stdin access token is empty");
  return token;
}

function isAllowedPublishToken(token) {
  return SERVICE_TOKEN_PATTERN.test(token) || JWT_PATTERN.test(token);
}

export async function resolvePublishAccessToken(options, environment = process.env, stdin = process.stdin) {
  if (options.accessTokenStdin) {
    const token = await readAccessTokenFromStdin(stdin);
    if (!isAllowedPublishToken(token)) throw new Error("stdin access token format is invalid");
    return token;
  }
  const rawEnvironmentToken = environment.HBWEB_API_TOKEN;
  const environmentToken = normalizedText(rawEnvironmentToken);
  if (
    environmentToken
    && (
      rawEnvironmentToken !== environmentToken
      || !SERVICE_TOKEN_PATTERN.test(environmentToken)
    )
  ) {
    throw new Error("管理员 JWT 只允许通过 --access-token-stdin 传入");
  }
  if (!environmentToken) {
    throw new Error("缺少后台发布凭据；管理员 JWT 请使用 --access-token-stdin");
  }
  return environmentToken;
}

function runCommand(command) {
  return new Promise((resolve, reject) => {
    const child = spawn(command.command, command.args, {
      cwd: process.cwd(),
      env: command.env,
      stdio: ["ignore", "pipe", "pipe"],
    });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString();
      process.stdout.write(chunk);
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
      process.stderr.write(chunk);
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) return resolve({ stdout, stderr });
      reject(Object.assign(new Error(`EAS OTA publish failed with exit code ${code}`), { code, stdout, stderr }));
    });
  });
}

async function readRecoveryManifest(filePath) {
  return JSON.parse(await readFile(path.resolve(process.cwd(), filePath), "utf8"));
}

async function writeRecoveryManifest(manifest) {
  const directory = path.resolve(process.cwd(), ".artifacts/mobile-ota-recovery");
  await mkdir(directory, { recursive: true });
  const target = path.join(directory, `${manifest.releaseBatchId}.json`);
  await writeFile(target, `${JSON.stringify(manifest, null, 2)}\n`, {
    encoding: "utf8",
    flag: "wx",
    mode: 0o600,
  });
  return target;
}

function redactedError(error) {
  return String(error instanceof Error ? error.message : error)
    .replace(/Bearer\s+[^\s]+/gi, "Bearer [REDACTED]")
    .replace(/hbsvc_[A-Za-z0-9_-]+/g, "hbsvc_[REDACTED]")
    .replace(/[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+/g, "[JWT REDACTED]");
}

function hasExactFields(value, expectedFields) {
  const keys = Object.keys(value).sort();
  return keys.length === expectedFields.length
    && keys.every((key, index) => key === expectedFields[index]);
}

function isNormalizedBoundedText(value, maximum) {
  return typeof value === "string" && value === value.trim() && value.length > 0 && value.length <= maximum;
}

function isNullableNormalizedText(value, maximum) {
  return value === null || isNormalizedBoundedText(value, maximum);
}

function isNullableHttpsUrl(value) {
  if (value === null) return true;
  if (!isNormalizedBoundedText(value, 2_048)) return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:" && !url.username && !url.password;
  } catch {
    return false;
  }
}

function validateRecoveryRelease(payload, manifest) {
  const release = isRecord(payload);
  const platform = release?.platform;
  const expectedPrefix = `mobile-${manifest.environment}-${platform}-release-`;
  if (
    !release
    || !hasExactFields(release, RECOVERY_RELEASE_FIELDS)
    || release.releaseBatchId !== manifest.releaseBatchId
    || release.appKey !== APP_KEY
    || release.environment !== manifest.environment
    || release.clientChannel !== manifest.environment
    || !["android", "ios"].includes(platform)
    || !isNormalizedBoundedText(release.releaseChannel, 160)
    || !release.releaseChannel.startsWith(expectedPrefix)
    || release.releaseChannel.length <= expectedPrefix.length
    || release.easBranch !== release.releaseChannel
    || release.projectName !== PROJECT_NAME
    || release.easProjectId !== EAS_PROJECT_ID
    || !isNormalizedBoundedText(release.runtimeVersion, 120)
    || !UUID_PATTERN.test(release.updateGroupId)
    || !UUID_PATTERN.test(release.updateId)
    || !isNormalizedBoundedText(release.message, 1_000)
    || !isNullableNormalizedText(release.gitCommitHash, 120)
    || !isNullableHttpsUrl(release.dashboardUrl)
    || !isNormalizedBoundedText(release.publishedAtUtc, 64)
    || !Number.isFinite(Date.parse(release.publishedAtUtc))
    || typeof release.isRollback !== "boolean"
    || release.isRollback !== (release.rollbackOfReleaseId !== null)
    || release.rollbackOfReleaseId !== null && !UUID_PATTERN.test(release.rollbackOfReleaseId)
  ) {
    throw new Error("Mobile OTA recovery manifest release fact is invalid");
  }
  return release;
}

function validateBootstrapRecoveryRelease(payload, manifest) {
  const release = isRecord(payload);
  const platform = release?.platform;
  if (
    !release
    || !hasExactFields(release, BOOTSTRAP_RECOVERY_RELEASE_FIELDS)
    || release.projectName !== PROJECT_NAME
    || !["android", "ios"].includes(platform)
    || release.channel !== manifest.environment
    || release.branch !== manifest.environment
    || !isNormalizedBoundedText(release.runtimeVersion, 120)
    || !UUID_PATTERN.test(release.updateGroupId)
    || !UUID_PATTERN.test(release.updateId)
    || release.androidUpdateId !== (platform === "android" ? release.updateId : null)
    || !isNormalizedBoundedText(release.message, 1_000)
    || !isNullableNormalizedText(release.gitCommitHash, 120)
    || !isNullableHttpsUrl(release.dashboardUrl)
    || !isNormalizedBoundedText(release.publishedAt, 64)
    || !Number.isFinite(Date.parse(release.publishedAt))
    || release.isRollback !== false
    || release.rollbackOfGroupId !== null
    || release.bootstrapLegacyFixedChannel !== true
  ) {
    throw new Error("Mobile OTA bootstrap recovery manifest release fact is invalid");
  }
  return release;
}

function authorityReleaseFromLegacyBootstrap(payload) {
  return Object.freeze({
    updateId: payload.updateId,
    updateGroupId: payload.updateGroupId,
    runtimeVersion: payload.runtimeVersion,
    platform: payload.platform,
    message: payload.message,
    gitCommitHash: payload.gitCommitHash,
    dashboardUrl: payload.dashboardUrl,
    publishedAtUtc: payload.publishedAt,
  });
}

function validateRecoveryManifest(manifest) {
  const bootstrapLegacyFixedChannel = manifest?.bootstrapLegacyFixedChannel === true;
  const manifestFields = bootstrapLegacyFixedChannel
    ? BOOTSTRAP_RECOVERY_MANIFEST_FIELDS
    : RECOVERY_MANIFEST_FIELDS;
  if (
    !isRecord(manifest)
    || !hasExactFields(manifest, manifestFields)
    || manifest.schemaVersion !== 1
    || manifest.appKey !== APP_KEY
    || !VALID_ENVIRONMENTS.has(manifest.environment)
    || !normalizedText(manifest.releaseBatchId)
    || !UUID_PATTERN.test(manifest.releaseBatchId)
    || !isNormalizedBoundedText(manifest.createdAtUtc, 64)
    || !Number.isFinite(Date.parse(manifest.createdAtUtc))
    || !Array.isArray(manifest.releases)
    || manifest.releases.length === 0
    || manifest.releases.length > (bootstrapLegacyFixedChannel ? 1 : 2)
  ) {
    throw new Error("Mobile OTA recovery manifest is invalid");
  }
  const releases = manifest.releases.map((release) => (
    bootstrapLegacyFixedChannel
      ? validateBootstrapRecoveryRelease(release, manifest)
      : validateRecoveryRelease(release, manifest)
  ));
  if (new Set(releases.map((release) => release.platform)).size !== releases.length) {
    throw new Error("Mobile OTA recovery manifest contains duplicate platform releases");
  }
  return { ...manifest, releases, bootstrapLegacyFixedChannel };
}

export class OtaPublishBatchError extends Error {
  constructor(message, results, recoveryPath = null) {
    super(message);
    this.name = "OtaPublishBatchError";
    this.results = results;
    this.recoveryPath = recoveryPath;
    this.exitCode = 2;
  }
}

async function resolveAuth(options, dependencies) {
  const environment = dependencies.environment ?? process.env;
  const baseUrl = normalizedText(environment.HBWEB_API_BASE_URL);
  if (!baseUrl) throw new Error("缺少 HBWEB_API_BASE_URL");
  const accessToken = dependencies.accessToken
    ?? await resolvePublishAccessToken(options, environment, dependencies.stdin ?? process.stdin);
  return { baseUrl, accessToken, fetchFn: dependencies.fetchFn };
}

async function runRegisterOnly(options, dependencies, auth) {
  const manifest = validateRecoveryManifest(
    await (dependencies.readRecoveryManifestFn ?? readRecoveryManifest)(options.registerOnlyFile),
  );
  const results = [];
  for (const payload of manifest.releases) {
    // release channel 已被 EAS 使用；恢复只允许以完整 fingerprint 幂等 register，不能重跑“未使用”preflight。
    // 但必须再次用固定 EAS CLI 做只读权威回读，不能把可编辑的本地 manifest 当作发布证明。
    const authorityOptions = manifest.bootstrapLegacyFixedChannel
      ? {
        environment: manifest.environment,
        platform: payload.platform,
        runtimeVersion: payload.runtimeVersion,
        message: payload.message,
        releaseChannel: payload.channel,
        bootstrapLegacyFixedChannel: true,
      }
      : payload;
    const authorityRelease = manifest.bootstrapLegacyFixedChannel
      ? authorityReleaseFromLegacyBootstrap(payload)
      : payload;
    const channelViewCommand = buildEasChannelViewCommand(
      authorityOptions,
      dependencies.environment ?? process.env,
    );
    const channelViewExecution = await (dependencies.runCommandFn ?? runCommand)(channelViewCommand);
    parseEasChannelViewOutput(
      channelViewExecution.stdout,
      authorityOptions.releaseChannel,
      authorityRelease,
    );
    let registeredResult;
    if (manifest.bootstrapLegacyFixedChannel) {
      const registered = await (
        dependencies.registerLegacyBootstrapUpdateFn ?? registerLegacyBootstrapUpdate
      )(payload, auth);
      registeredResult = {
        platform: payload.platform,
        releaseChannel: payload.channel,
        status: "registered",
        releaseId: registered.id ?? null,
      };
    } else {
      const registered = await (dependencies.registerOtaReleaseFn ?? registerOtaRelease)(payload, auth);
      registeredResult = {
        platform: payload.platform,
        releaseChannel: payload.releaseChannel,
        status: "registered",
        idempotent: registered.idempotent,
        releaseId: registered.release?.id ?? null,
      };
    }
    try {
      await reportAcceptedOtaRelease(payload, {
        completedAtUtcFn: dependencies.completedAtUtcFn,
        config: dependencies.releaseReporterConfig,
        logger: dependencies.logger ?? console,
        releaseEnvironment: manifest.environment,
        reportReleaseEventFn: dependencies.reportReleaseEventFn,
        resolvedCommit: dependencies.resolvedCommit,
      });
      results.push(registeredResult);
    } catch (error) {
      results.push({
        ...registeredResult,
        releaseEventStatus: "failed",
        payload,
        error: redactedError(error),
      });
    }
  }
  if (results.some((result) => result.releaseEventStatus === "failed")) {
    throw new OtaPublishBatchError(
      "register-only 已完成幂等登记，但 release event 上报失败；未执行 EAS 发布，可安全重试同一 recovery manifest。",
      results,
      options.registerOnlyFile,
    );
  }
  return { releaseBatchId: manifest.releaseBatchId, results };
}

export async function runPublishMobileOtaRelease(options, dependencies = {}) {
  validatePublishOptions(options);
  const logger = dependencies.logger ?? console;
  const environment = dependencies.environment ?? process.env;
  const releaseReporterConfig =
    dependencies.reportReleaseEventFn && !options.dryRun
      ? requireReleaseReporterConfig(environment)
      : null;
  // 在任何 EAS 写入前解析 commit；发布成功后不能再因本地 SHA 缺失而失败。
  const resolvedCommit =
    dependencies.reportReleaseEventFn && !options.dryRun
      ? (dependencies.resolveReleaseCommitFn ?? resolveReleaseCommit)({ environment })
      : null;
  const auth = options.dryRun ? null : await resolveAuth(options, dependencies);
  if (options.registerOnlyFile) {
    return runRegisterOnly(
      options,
      {
        ...dependencies,
        environment,
        releaseReporterConfig,
        resolvedCommit,
      },
      auth,
    );
  }

  const nowIso = (dependencies.nowIsoFn ?? (() => new Date().toISOString()))();
  const releaseBatchId = (dependencies.createReleaseBatchIdFn ?? randomUUID)();
  const platforms = options.platform === "all" ? ["android", "ios"] : [options.platform];
  const plans = platforms.map((platform) => {
    const releaseChannel = options.bootstrapLegacyFixedChannel
      ? options.environment
      : (dependencies.createReleaseChannelFn ?? createReleaseChannel)(
        options.environment,
        platform,
        nowIso,
        randomUUID(),
      );
    return {
      ...options,
      platform,
      releaseBatchId,
      releaseChannel,
    };
  });

  if (options.dryRun) {
    const results = plans.map((plan) => {
      const command = buildEasUpdateCommand(plan, dependencies.environment ?? process.env);
      logger.log(`${plan.platform}: ${command.command} ${command.args.join(" ")}`);
      return { platform: plan.platform, releaseChannel: plan.releaseChannel, status: "dry-run" };
    });
    return { releaseBatchId, results };
  }

  // all 的两个平台必须全部 preflight 成功后，才允许第一次 EAS 写入。
  for (const plan of plans) {
    await (dependencies.preflightOtaReleaseFn ?? preflightOtaRelease)(
      buildPreflightPayload(plan),
      auth,
    );
  }

  if (options.bootstrapLegacyFixedChannel) {
    // fixed channel 已存在，不能做 unused 检查；但写入前必须先证明它仍精确映射同名 branch。
    for (const plan of plans) {
      const channelViewCommand = buildEasChannelViewCommand(
        plan,
        dependencies.environment ?? process.env,
      );
      const channelViewExecution = await (dependencies.runCommandFn ?? runCommand)(
        channelViewCommand,
      );
      parseEasChannelViewOutput(
        channelViewExecution.stdout,
        plan.releaseChannel,
      );
    }
  } else {
    // 后端只证明数据库未登记；还必须在 Expo 权威侧穷尽 channel 分页并证明所有目标均未使用。
    await (dependencies.assertReleaseChannelsUnusedFn ?? (
      (releasePlans) => assertReleaseChannelsUnused(
        releasePlans,
        dependencies.environment ?? process.env,
        dependencies.runCommandFn ?? runCommand,
      )
    ))(plans);
  }

  const results = [];
  const recoveryReleases = [];
  for (const plan of plans) {
    let easCompleted = false;
    let publishedPayload = null;
    try {
      const command = buildEasUpdateCommand(plan, dependencies.environment ?? process.env);
      const execution = await (dependencies.runCommandFn ?? runCommand)(command);
      easCompleted = true;
      const parsed = parseEasUpdateOutput(execution.stdout, plan.platform);
      assertPublishedRelease(parsed, plan);
      const payload = options.bootstrapLegacyFixedChannel
        ? buildLegacyBootstrapPayload(parsed, plan)
        : buildOtaReleasePayload(parsed, plan);
      publishedPayload = payload;
      const channelViewCommand = buildEasChannelViewCommand(
        plan,
        dependencies.environment ?? process.env,
      );
      const channelViewExecution = await (dependencies.runCommandFn ?? runCommand)(channelViewCommand);
      parseEasChannelViewOutput(
        channelViewExecution.stdout,
        plan.releaseChannel,
        options.bootstrapLegacyFixedChannel
          ? authorityReleaseFromLegacyBootstrap(payload)
          : payload,
      );
      try {
        let registeredResult;
        if (options.bootstrapLegacyFixedChannel) {
          const registered = await (
            dependencies.registerLegacyBootstrapUpdateFn ?? registerLegacyBootstrapUpdate
          )(payload, auth);
          registeredResult = {
            platform: plan.platform,
            releaseChannel: plan.releaseChannel,
            status: "registered",
            releaseId: registered.id ?? null,
          };
        } else {
          const registered = await (dependencies.registerOtaReleaseFn ?? registerOtaRelease)(payload, auth);
          registeredResult = {
            platform: plan.platform,
            releaseChannel: plan.releaseChannel,
            status: "registered",
            idempotent: registered.idempotent,
            releaseId: registered.release?.id ?? null,
          };
        }
        try {
          await reportAcceptedOtaRelease(payload, {
            completedAtUtcFn: dependencies.completedAtUtcFn,
            config: releaseReporterConfig,
            logger,
            releaseEnvironment: options.environment,
            reportReleaseEventFn: dependencies.reportReleaseEventFn,
            resolvedCommit,
          });
          results.push(registeredResult);
        } catch (error) {
          // 登记已成功；保留恢复 manifest 只供 --register-only 幂等回读并重试验收上报。
          recoveryReleases.push(payload);
          results.push({
            ...registeredResult,
            releaseEventStatus: "failed",
            payload,
            error: redactedError(error),
          });
        }
      } catch (error) {
        recoveryReleases.push(payload);
        results.push({
          platform: plan.platform,
          releaseChannel: plan.releaseChannel,
          status: "registration-failed",
          payload,
          error: redactedError(error),
        });
      }
    } catch (error) {
      if (easCompleted && publishedPayload) recoveryReleases.push(publishedPayload);
      results.push({
        platform: plan.platform,
        releaseChannel: plan.releaseChannel,
        status: easCompleted ? "published-unverified" : "publish-failed",
        ...(publishedPayload ? { payload: publishedPayload } : {}),
        error: redactedError(error),
      });
    }
  }

  let recoveryPath = null;
  if (recoveryReleases.length) {
    const recoveryManifest = {
      schemaVersion: 1,
      appKey: APP_KEY,
      environment: options.environment,
      releaseBatchId,
      createdAtUtc: nowIso,
      ...(options.bootstrapLegacyFixedChannel
        ? { bootstrapLegacyFixedChannel: true }
        : {}),
      releases: recoveryReleases,
    };
    try {
      recoveryPath = await (dependencies.writeRecoveryManifestFn ?? writeRecoveryManifest)(recoveryManifest);
    } catch (error) {
      logger.warn(`恢复 manifest 写入失败（${redactedError(error)}）；以下 JSON 不含凭据，请安全保存，禁止重发。`);
      logger.log(JSON.stringify(recoveryManifest));
      throw new OtaPublishBatchError(
        "EAS 已发布但恢复 manifest 写入失败；请安全保存无凭据 JSON 后再做权威回读，禁止重发。",
        results,
        null,
      );
    }
    const hasReleaseReportFailure = results.some(
      (result) => result.releaseEventStatus === "failed",
    );
    logger.warn(
      hasReleaseReportFailure
        ? `EAS 已发布并登记但验收上报失败；禁止重新发布。仅使用 --register-only ${recoveryPath} 幂等重试上报。`
        : `EAS 已发布但尚未完成可信登记；核对事实后仅使用 --register-only ${recoveryPath} 补登记，禁止重发。`,
    );
  }

  for (const result of results) {
    logger.log(`${result.platform}: ${result.status} (${result.releaseChannel})`);
  }
  const hasReleaseReportFailure = results.some(
    (result) => result.releaseEventStatus === "failed",
  );
  if (
    hasReleaseReportFailure ||
    results.some((result) => result.status !== "registered")
  ) {
    const hasUnverifiedPublish = results.some((result) => result.status === "published-unverified");
    throw new OtaPublishBatchError(
      hasUnverifiedPublish
        ? "EAS 已成功，但发布事实或 channel 映射未通过权威验证；禁止自动重发。"
        : hasReleaseReportFailure
          ? "OTA 已发布并登记，但 release event 上报失败；禁止重发，只能用 recovery manifest 幂等重试验收上报。"
          : "Mobile OTA batch completed partially or failed",
      results,
      recoveryPath,
    );
  }
  return { releaseBatchId, results };
}

async function main() {
  const options = parsePublishOtaArgs(process.argv.slice(2));
  if (options.help) {
    console.log(HELP_TEXT.trim());
    return;
  }
  await runPublishMobileOtaRelease(options, {
    reportReleaseEventFn: ({ event, config }) =>
      reportReleaseEvent({
        event,
        baseUrl: config.baseUrl,
        token: config.token,
      }),
  });
}

function isCliEntry() {
  return Boolean(process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]));
}

if (isCliEntry()) {
  main().catch((error) => {
    console.error(redactedError(error));
    process.exitCode = error instanceof OtaPublishBatchError ? error.exitCode : 1;
  });
}
