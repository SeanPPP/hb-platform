import { appendFileSync, lstatSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { postServiceJson, redactSensitive } from "./lib/http-reporter.mjs";
import { validateMetricBatchV1 } from "./lib/metric-batch.mjs";
import { ValidationError } from "./lib/validation.mjs";

const ENDPOINT_PATH = "/api/system/performance/automation-batches";
const MAX_INPUT_BYTES = 256 * 1024;

function parseTimeout(value, name) {
  if (value === undefined || value === "") return undefined;
  if (!/^\d+$/u.test(value)) {
    throw new ValidationError(`${name} 必须是整数毫秒`);
  }
  return Number(value);
}

export async function reportMetricBatch({
  payload,
  baseUrl,
  token,
  timeoutMs,
  fetchImpl,
}) {
  validateMetricBatchV1(payload);
  const response = await postServiceJson({
    baseUrl,
    token,
    endpointPath: ENDPOINT_PATH,
    payload,
    timeoutMs,
    fetchImpl,
  });
  const data = response.data;
  if (data === null || typeof data !== "object" || Array.isArray(data)) {
    throw new Error("automation batch 响应缺少 ingest 计数");
  }
  const allowedKeys = new Set(["acceptedCount", "duplicateCount", "rejectedCount"]);
  for (const key of Object.keys(data)) {
    if (!allowedKeys.has(key)) throw new Error(`automation batch 响应包含未知计数字段 ${key}`);
  }
  for (const key of allowedKeys) {
    if (!Number.isInteger(data[key]) || data[key] < 0) {
      throw new Error(`automation batch 响应计数 ${key} 无效`);
    }
  }
  if (data.rejectedCount !== 0 || data.acceptedCount + data.duplicateCount !== payload.events.length) {
    throw new Error("automation batch ingest 计数与发送事件数不一致或包含 rejected 事件");
  }
  return {
    status: response.status,
    requestId: response.requestId,
    acceptedCount: data.acceptedCount,
    duplicateCount: data.duplicateCount,
  };
}

export async function reportMetricBatchFromEnvironment({
  payload,
  optional = false,
  env = process.env,
  fetchImpl,
}) {
  // optional 只控制“无凭据时是否触网”，不能绕过 payload 契约校验。
  validateMetricBatchV1(payload);
  const baseUrl = env.QUALITY_BASELINE_SERVICE_URL;
  const token = env.QUALITY_BASELINE_SERVICE_TOKEN;
  const hasBaseUrl = typeof baseUrl === "string" && baseUrl.trim().length > 0;
  const hasToken = typeof token === "string" && token.trim().length > 0;

  if (!hasBaseUrl && !hasToken) {
    if (optional) return { skipped: true, reason: "credentials_missing" };
    throw new ValidationError("QUALITY_BASELINE_SERVICE_URL/TOKEN 均未配置");
  }
  if (hasBaseUrl !== hasToken) {
    throw new ValidationError("QUALITY_BASELINE_SERVICE_URL/TOKEN 必须同时成对配置");
  }

  return reportMetricBatch({
    payload,
    baseUrl,
    token,
    timeoutMs: parseTimeout(env.QUALITY_BASELINE_TIMEOUT_MS, "QUALITY_BASELINE_TIMEOUT_MS"),
    fetchImpl,
  });
}

function readPayloadFile(filePath) {
  const stat = lstatSync(filePath);
  if (!stat.isFile() || stat.isSymbolicLink()) {
    throw new ValidationError("--file 必须指向普通文件，不能是符号链接");
  }
  if (stat.size > MAX_INPUT_BYTES) {
    throw new ValidationError(`MetricBatchV1 文件不得超过 ${MAX_INPUT_BYTES} bytes`);
  }
  try {
    return JSON.parse(readFileSync(filePath, "utf8"));
  } catch {
    throw new ValidationError("MetricBatchV1 文件不是有效 JSON");
  }
}

function parseArgs(argv) {
  const options = { optional: false };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--optional") {
      options.optional = true;
    } else if (argument === "--file") {
      options.file = argv[++index];
    } else if (argument === "--help") {
      options.help = true;
    } else {
      throw new ValidationError(`未知参数 ${argument}`);
    }
  }
  if (!options.help && (!options.file || options.file.startsWith("--"))) {
    throw new ValidationError("必须提供 --file <MetricBatchV1.json>");
  }
  return options;
}

function appendReporterSummary(message, env) {
  if (env.GITHUB_STEP_SUMMARY) {
    appendFileSync(env.GITHUB_STEP_SUMMARY, `\n${message}\n`, "utf8");
  }
}

async function main(argv = process.argv.slice(2), env = process.env) {
  const options = parseArgs(argv);
  if (options.help) {
    console.log(
      "用法: node scripts/performance/report-metric-batch.mjs --file <batch.json> [--optional]",
    );
    return;
  }
  const payload = readPayloadFile(resolve(options.file));
  const result = await reportMetricBatchFromEnvironment({
    payload,
    optional: options.optional,
    env,
  });
  if (result.skipped) {
    const message =
      "- MetricBatchV1 上报：未配置 QUALITY_BASELINE_SERVICE_URL/TOKEN，已安全跳过；结果保留在 artifact。";
    console.log(message.slice(2));
    appendReporterSummary(message, env);
    return;
  }
  const requestSuffix = result.requestId ? `，request-id ${result.requestId}` : "";
  const countSuffix = `，accepted ${result.acceptedCount}，duplicate ${result.duplicateCount}`;
  appendReporterSummary(
    `- MetricBatchV1 上报：HTTP ${result.status}${requestSuffix}${countSuffix}。`,
    env,
  );
  console.log(`MetricBatchV1 上报成功：HTTP ${result.status}${requestSuffix}${countSuffix}`);
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  main().catch((error) => {
    const secret = process.env.QUALITY_BASELINE_SERVICE_TOKEN;
    console.error(`MetricBatchV1 上报失败：${redactSensitive(error, [secret])}`);
    process.exitCode = 1;
  });
}
