#!/usr/bin/env node

import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const VALID_CHANNELS = new Set(["preview", "production"]);
const VALID_PROFILES = new Set(["preview", "production"]);
const PLATFORM = "android";
const REGISTRATION_PATH = "/api/mobile-app-builds/ota-updates";
const CURRENT_USER_PATH = "/api/Auth/current";
const SERVICE_TOKEN_CURRENT_PATH = "/api/service-api-tokens/current";
const SERVICE_API_TOKEN_PREFIX = "hbsvc_";
const MANAGE_APP_DOWNLOADS_PERMISSION = "System.ManageAppDownloads";
const SUPER_ADMIN_ROLE_NAMES = new Set(["Admin", "管理员"]);

const HELP_TEXT = `
用法：
  node scripts/publish-ota-update.mjs --channel preview|production --profile preview|production --runtime-version <version> --message <message>

参数：
  --channel <preview|production>       EAS Update channel。
  --profile <preview|production>       写入 OTA 包的 App build profile。
  --runtime-version <version>          OTA 目标 runtimeVersion。
  --message <message>                  EAS Update 发布说明。
  --dry-run                            只打印命令和补录 JSON，不发布 OTA，也不登记后台。
  --mock-output-file <path>            配合 --dry-run，解析已保存的 EAS 输出用于验证。
  --help, -h                           显示帮助。

后台登记环境变量：
  HBWEB_API_BASE_URL                   后台站点根地址或 API base URL，例如 https://<backend-domain> 或 https://<backend-domain>/api。
  HBWEB_API_TOKEN                      后台 Bearer token。
`;

function parseArgs(argv) {
  const options = {
    dryRun: false,
    help: false,
  };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];

    if (arg === "--help" || arg === "-h") {
      options.help = true;
      continue;
    }

    if (arg === "--dry-run") {
      options.dryRun = true;
      continue;
    }

    if (arg.startsWith("--")) {
      const next = argv[index + 1];
      if (!next || next.startsWith("--")) {
        throw new Error(`参数 ${arg} 缺少取值`);
      }

      switch (arg) {
        case "--channel":
          options.channel = next;
          break;
        case "--profile":
          options.profile = next;
          break;
        case "--runtime-version":
          options.runtimeVersion = next;
          break;
        case "--message":
          options.message = next;
          break;
        case "--mock-output-file":
          options.mockOutputFile = next;
          break;
        default:
          throw new Error(`未知参数：${arg}`);
      }

      index += 1;
      continue;
    }

    throw new Error(`未知参数：${arg}`);
  }

  return options;
}

function validateOptions(options) {
  if (!VALID_CHANNELS.has(options.channel)) {
    throw new Error("--channel 必须是 preview 或 production");
  }

  if (!VALID_PROFILES.has(options.profile)) {
    throw new Error("--profile 必须是 preview 或 production");
  }

  if (!options.runtimeVersion?.trim()) {
    throw new Error("--runtime-version 不能为空");
  }

  if (!options.message?.trim()) {
    throw new Error("--message 不能为空");
  }
}

function stripAnsi(input) {
  return input.replace(/\x1B\[[0-?]*[ -/]*[@-~]/g, "");
}

function asObject(value) {
  return value && typeof value === "object" && !Array.isArray(value) ? value : null;
}

function collectObjects(value, objects = []) {
  const objectValue = asObject(value);
  if (objectValue) {
    objects.push(objectValue);
    for (const nestedValue of Object.values(objectValue)) {
      collectObjects(nestedValue, objects);
    }
    return objects;
  }

  if (Array.isArray(value)) {
    for (const item of value) {
      collectObjects(item, objects);
    }
  }

  return objects;
}

function stringField(source, keys) {
  const objectValue = asObject(source);
  if (!objectValue) {
    return "";
  }

  for (const key of keys) {
    const value = objectValue[key];
    if (typeof value === "string" && value.trim()) {
      return value.trim();
    }
  }

  return "";
}

function firstStringFromObjects(objects, keys) {
  for (const objectValue of objects) {
    const value = stringField(objectValue, keys);
    if (value) {
      return value;
    }
  }

  return "";
}

function parseJsonSafely(output) {
  const cleanOutput = stripAnsi(output).trim();
  if (!cleanOutput) {
    return null;
  }

  try {
    return JSON.parse(cleanOutput);
  } catch {
    const objectStart = cleanOutput.indexOf("{");
    const arrayStart = cleanOutput.indexOf("[");
    const starts = [objectStart, arrayStart].filter((index) => index >= 0);
    if (!starts.length) {
      return null;
    }

    const start = Math.min(...starts);
    const objectEnd = cleanOutput.lastIndexOf("}");
    const arrayEnd = cleanOutput.lastIndexOf("]");
    const end = Math.max(objectEnd, arrayEnd);
    if (end <= start) {
      return null;
    }

    try {
      return JSON.parse(cleanOutput.slice(start, end + 1));
    } catch {
      return null;
    }
  }
}

function parseEasUpdateJsonOutput(output) {
  const parsedJson = parseJsonSafely(output);
  if (!parsedJson) {
    return null;
  }

  const objects = collectObjects(parsedJson);
  const androidUpdate = objects.find(
    (objectValue) => stringField(objectValue, ["platform"]).toLowerCase() === PLATFORM,
  );
  const groupObject = objects.find((objectValue) => asObject(objectValue.group));
  const groupId =
    firstStringFromObjects(objects, ["updateGroupId", "groupId"])
    || firstStringFromObjects(objects, ["group"])
    || stringField(groupObject?.group, ["id"])
    || stringField(objects.find((objectValue) => Array.isArray(objectValue.updates)), ["id"]);

  // JSON 输出是自动补录的主路径；字段兼容不同 EAS CLI 版本和 update/list 形态。
  return {
    branch: firstStringFromObjects(objects, ["branch", "branchName", "channel"]),
    runtimeVersion:
      stringField(androidUpdate, ["runtimeVersion"]) || firstStringFromObjects(objects, ["runtimeVersion"]),
    updateGroupId: groupId,
    androidUpdateId:
      firstStringFromObjects(objects, ["androidUpdateId"])
      || stringField(androidUpdate, ["id", "updateId"]),
    message: firstStringFromObjects(objects, ["message", "commitMessage"]),
    gitCommitHash: firstStringFromObjects(objects, ["gitCommitHash", "gitCommit", "commit"]),
    dashboardUrl: firstStringFromObjects(objects, ["dashboardUrl", "dashboardURL", "url"]),
  };
}

export function parseEasUpdateOutput(output) {
  // 自动登记只信任 eas update --json；文本输出只用于人工排查，不能参与入库。
  return parseEasUpdateJsonOutput(output) ?? {
    branch: "",
    runtimeVersion: "",
    updateGroupId: "",
    androidUpdateId: "",
    message: "",
    gitCommitHash: "",
    dashboardUrl: "",
  };
}

export function buildOtaRegistrationPayload(parsed, options, publishedAt = new Date().toISOString()) {
  // channel/profile/runtime 以脚本入参为准；EAS 输出缺省时使用入参兜底，避免影响手动补录。
  return {
    updateGroupId: parsed.updateGroupId || null,
    androidUpdateId: parsed.androidUpdateId || null,
    channel: options.channel,
    branch: parsed.branch || options.channel,
    platform: PLATFORM,
    runtimeVersion: parsed.runtimeVersion || options.runtimeVersion,
    message: parsed.message || options.message,
    gitCommitHash: parsed.gitCommitHash || null,
    dashboardUrl: parsed.dashboardUrl || null,
    publishedAt,
    isRollback: false,
    rollbackOfGroupId: null,
  };
}

function buildBackendApiUrl(baseUrl, apiPath) {
  const url = new URL(baseUrl.trim());
  const normalizedPath = url.pathname.replace(/\/+$/, "");
  const requestPath = normalizedPath.endsWith("/api")
    ? apiPath.replace(/^\/api/, "")
    : apiPath;

  url.pathname = `${normalizedPath}${requestPath}`;
  url.search = "";
  url.hash = "";
  return url.toString();
}

export function buildRegistrationUrl(baseUrl) {
  return buildBackendApiUrl(baseUrl, REGISTRATION_PATH);
}

function isServiceApiToken(token) {
  return token.startsWith(SERVICE_API_TOKEN_PREFIX);
}

export function buildTokenPreflightUrl(baseUrl, token = "") {
  // hbsvc_ 是后台自动化 token；普通后台登录 token 仍走用户 current 接口。
  const path = isServiceApiToken(token.trim())
    ? SERVICE_TOKEN_CURRENT_PATH
    : CURRENT_USER_PATH;
  return buildBackendApiUrl(baseUrl, path);
}

function buildEasCommand(options) {
  // 旧 APK 过渡 OTA 必须关闭原生安装器，防止旧 runtime 加载新 native module。
  return {
    command: "npx",
    args: [
      "eas-cli@latest",
      "update",
      "--channel",
      options.channel,
      "--platform",
      PLATFORM,
      "--message",
      options.message,
      "--json",
    ],
    env: {
      ...process.env,
      EXPO_PUBLIC_APP_BUILD_PROFILE: options.profile,
      EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED: "false",
      EXPO_PUBLIC_RUNTIME_VERSION: options.runtimeVersion,
    },
  };
}

function shellQuote(value) {
  if (/^[A-Za-z0-9_@%+=:,./-]+$/.test(value)) {
    return value;
  }

  return `'${value.replace(/'/g, "'\\''")}'`;
}

function printManualRegistrationJson(payload, logger = console) {
  logger.log("\n可手动补录的 OTA JSON：");
  logger.log(JSON.stringify(payload, null, 2));
}

function runCommand({ command, args, env }) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: process.cwd(),
      env,
      stdio: ["inherit", "pipe", "pipe"],
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

      const error = new Error(`EAS OTA 发布失败，退出码 ${code}`);
      error.code = code;
      error.stdout = stdout;
      error.stderr = stderr;
      reject(error);
    });
  });
}

function getResponseField(source, camelKey, pascalKey = camelKey[0].toUpperCase() + camelKey.slice(1)) {
  const objectValue = asObject(source);
  if (!objectValue) {
    return undefined;
  }

  return objectValue[camelKey] ?? objectValue[pascalKey];
}

function describeBackendFailure(payload) {
  const message = getResponseField(payload, "message");
  const errorCode = getResponseField(payload, "errorCode") ?? getResponseField(payload, "code");
  const details = getResponseField(payload, "details");
  return [message, errorCode, details ? JSON.stringify(details) : ""]
    .filter(Boolean)
    .join(" / ");
}

function extractRoleNames(data) {
  const roleNames = getResponseField(data, "roleNames");
  if (Array.isArray(roleNames)) {
    return roleNames;
  }

  const roles = getResponseField(data, "roles");
  if (!Array.isArray(roles)) {
    return [];
  }

  return roles
    .map((role) => getResponseField(role, "roleName"))
    .filter((roleName) => typeof roleName === "string");
}

function readRegistrationConfig() {
  return {
    baseUrl: process.env.HBWEB_API_BASE_URL?.trim() || "",
    token: process.env.HBWEB_API_TOKEN?.trim() || "",
  };
}

function requireRegistrationConfig() {
  const config = readRegistrationConfig();
  if (!config.baseUrl || !config.token) {
    throw new Error("发布 OTA 前必须配置 HBWEB_API_BASE_URL 和 HBWEB_API_TOKEN");
  }

  return config;
}

export async function preflightOtaRegistration(fetchFn = globalThis.fetch) {
  const { baseUrl, token } = requireRegistrationConfig();
  const url = buildTokenPreflightUrl(baseUrl, token);
  const serviceToken = isServiceApiToken(token);
  let response;

  try {
    // 非 dry-run 必须先验证后台 token 和权限，避免 OTA 已发布后才发现无权登记。
    response = await fetchFn(url, {
      method: "GET",
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });
  } catch (error) {
    throw new Error(`后台 token 验证失败：${error.message}`);
  }

  if (!response.ok) {
    const responseText = await response.text();
    throw new Error(`后台 token 验证失败：HTTP ${response.status} ${response.statusText}${responseText ? ` - ${responseText}` : ""}`);
  }

  const responseText = await response.text();
  const responsePayload = responseText ? parseJsonSafely(responseText) : null;
  const success = getResponseField(responsePayload, "success") ?? getResponseField(responsePayload, "isSuccess");
  const data = getResponseField(responsePayload, "data");
  if (success !== true) {
    const failureReason = describeBackendFailure(responsePayload) || "响应 success=false";
    throw new Error(`后台 token 验证失败：${failureReason}`);
  }

  if (serviceToken) {
    const scopes = getResponseField(data, "scopes");
    if (!Array.isArray(scopes) || !scopes.includes(MANAGE_APP_DOWNLOADS_PERMISSION)) {
      throw new Error(`后台 token 验证失败：缺少 ${MANAGE_APP_DOWNLOADS_PERMISSION} scope`);
    }

    return { url };
  }

  const permissions = getResponseField(data, "permissions");
  const roleNames = extractRoleNames(data);
  const hasManagePermission = Array.isArray(permissions)
    && permissions.includes(MANAGE_APP_DOWNLOADS_PERMISSION);
  const isSuperAdmin = roleNames.some((roleName) => SUPER_ADMIN_ROLE_NAMES.has(roleName));
  if (!hasManagePermission && !isSuperAdmin) {
    throw new Error(`后台 token 验证失败：缺少 ${MANAGE_APP_DOWNLOADS_PERMISSION} 权限`);
  }

  return { url };
}

export async function registerOtaUpdate(payload) {
  const { baseUrl, token } = readRegistrationConfig();

  if (!baseUrl || !token) {
    return {
      skipped: true,
      reason: "未配置 HBWEB_API_BASE_URL 或 HBWEB_API_TOKEN",
    };
  }

  // 只有发布脚本持有后台 token；Expo 控制台或裸 eas update 不会触发这一步。
  const url = buildRegistrationUrl(baseUrl);
  const response = await fetch(url, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });

  if (!response.ok) {
    const responseText = await response.text();
    throw new Error(`后台登记失败：HTTP ${response.status} ${response.statusText}${responseText ? ` - ${responseText}` : ""}`);
  }

  const responseText = await response.text();
  const responsePayload = responseText ? parseJsonSafely(responseText) : null;
  const success = getResponseField(responsePayload, "success") ?? getResponseField(responsePayload, "isSuccess");
  const data = getResponseField(responsePayload, "data");
  const savedGroupId = getResponseField(data, "updateGroupId");

  // 后端业务校验失败也可能返回 HTTP 200；必须看 ApiResponse.Success 和回写数据。
  if (success !== true || !savedGroupId) {
    const failureReason = describeBackendFailure(responsePayload) || "响应缺少 success=true 或 data.updateGroupId";
    throw new Error(`后台登记失败：${failureReason}`);
  }

  return {
    skipped: false,
    url,
  };
}

export function getRequiredRegistrationGaps(payload) {
  // EAS 某些 JSON 形态只返回 update group；后端以 group+platform 幂等，Android update ID 允许为空。
  const requiredFields = ["updateGroupId"];
  return requiredFields.filter((field) => !payload[field]);
}

async function createDryRunOutput(options) {
  if (!options.mockOutputFile) {
    return "";
  }

  const mockPath = path.resolve(process.cwd(), options.mockOutputFile);
  return readFile(mockPath, "utf8");
}

async function main() {
  const options = parseArgs(process.argv.slice(2));

  if (options.help) {
    console.log(HELP_TEXT.trim());
    return;
  }

  await runPublishOtaUpdate(options);
}

export async function runPublishOtaUpdate(
  options,
  {
    createDryRunOutputFn = createDryRunOutput,
    logger = console,
    preflightOtaRegistrationFn = preflightOtaRegistration,
    registerOtaUpdateFn = registerOtaUpdate,
    runCommandFn = runCommand,
  } = {},
) {
  validateOptions(options);

  const easCommand = buildEasCommand(options);
  const printableCommand = [easCommand.command, ...easCommand.args].map(shellQuote).join(" ");

  if (!options.dryRun) {
    await preflightOtaRegistrationFn();
  }

  logger.log(`OTA 平台固定为 ${PLATFORM}`);
  logger.log(`执行命令：${printableCommand}`);
  logger.log("本次 OTA 环境变量：");
  logger.log(`- EXPO_PUBLIC_APP_BUILD_PROFILE=${options.profile}`);
  logger.log("- EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED=false");
  logger.log(`- EXPO_PUBLIC_RUNTIME_VERSION=${options.runtimeVersion}`);

  const stdout = options.dryRun
    ? await createDryRunOutputFn(options)
    : (await runCommandFn(easCommand)).stdout;

  const parsed = parseEasUpdateOutput(stdout);
  const payload = buildOtaRegistrationPayload(parsed, options);

  if (options.dryRun) {
    logger.log("\n--dry-run 已启用：未发布 OTA，也不会登记后台。");
    printManualRegistrationJson(payload, logger);
    return;
  }

  const gaps = getRequiredRegistrationGaps(payload);
  if (gaps.length) {
    logger.warn(`\nWARNING: OTA 已发布，但 EAS 输出缺少字段：${gaps.join(", ")}；已跳过自动登记。`);
    printManualRegistrationJson(payload, logger);
    return;
  }

  try {
    const result = await registerOtaUpdateFn(payload);
    if (result.skipped) {
      logger.warn(`\nWARNING: OTA 已发布，但${result.reason}；已跳过自动登记。`);
      printManualRegistrationJson(payload, logger);
      return;
    }

    logger.log(`\nOTA 数据库记录已登记：${result.url}`);
  } catch (error) {
    logger.warn(`\nWARNING: OTA 已发布，但自动登记失败：${error.message}`);
    printManualRegistrationJson(payload, logger);
  }
}

function isCliEntry() {
  return process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);
}

if (isCliEntry()) {
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = typeof error.code === "number" ? error.code : 1;
  });
}
