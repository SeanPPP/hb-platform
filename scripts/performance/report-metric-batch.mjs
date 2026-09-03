import { appendFileSync, lstatSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { postServiceJson, redactSensitive } from "./lib/http-reporter.mjs";
import { validateMetricBatchV1 } from "./lib/metric-batch.mjs";
import {
  ValidationError,
  assertEnum,
  assertExactKeys,
  assertFiniteNumber,
  assertSafeString,
} from "./lib/validation.mjs";

const ENDPOINT_PATH = "/api/system/performance/automation-batches";
const MAX_INPUT_BYTES = 256 * 1024;
const MAX_SAMPLING_POLICIES = 200;
const INGEST_COUNT_KEYS = ["acceptedCount", "duplicateCount", "rejectedCount"];
const SAMPLING_KEYS = ["baselineState", "defaultSampleRate", "policies"];
const BASELINE_STATES = ["not_started", "observing", "frozen"];
const AUTOMATION_METRIC_NAMES = [
  "ci.run.duration",
  "web.first_screen.bytes",
  "web.largest_initial_chunk.bytes",
];

function parseTimeout(value, name) {
  if (value === undefined || value === "") return undefined;
  if (!/^\d+$/u.test(value)) {
    throw new ValidationError(`${name} 必须是整数毫秒`);
  }
  return Number(value);
}

function validateSamplingPolicyResponse(data) {
  const presentSamplingKeys = SAMPLING_KEYS.filter((key) => Object.hasOwn(data, key));
  if (presentSamplingKeys.length === 0) return;
  if (presentSamplingKeys.length !== SAMPLING_KEYS.length) {
    throw new ValidationError("automation batch 响应采样策略字段必须同时返回");
  }

  assertEnum(data.baselineState, BASELINE_STATES, "automation batch 响应 baselineState");
  assertFiniteNumber(
    data.defaultSampleRate,
    "automation batch 响应 defaultSampleRate",
    { min: 0, max: 1 },
  );
  if (!Array.isArray(data.policies) || data.policies.length > MAX_SAMPLING_POLICIES) {
    throw new ValidationError(
      `automation batch 响应 policies 最多允许 ${MAX_SAMPLING_POLICIES} 项`,
    );
  }

  const policyKeys = new Set();
  data.policies.forEach((policy, index) => {
    const path = `automation batch 响应 policies[${index}]`;
    assertExactKeys(
      policy,
      {
        required: ["metric", "selector", "sampleRate"],
        optional: ["slowThreshold"],
      },
      path,
    );
    assertEnum(policy.metric, AUTOMATION_METRIC_NAMES, `${path}.metric`);
    assertSafeString(policy.selector, `${path}.selector`, { maxLength: 120 });
    assertFiniteNumber(policy.sampleRate, `${path}.sampleRate`, { min: 0, max: 1 });
    if (Object.hasOwn(policy, "slowThreshold") && policy.slowThreshold !== null) {
      // 后端阈值会在合法 P95 上继续放大；这里只约束 DTO 的实际协议：非负有限数。
      assertFiniteNumber(policy.slowThreshold, `${path}.slowThreshold`, { min: 0 });
    }

    const policyKey = `${policy.metric}\u0000${policy.selector}`;
    if (policyKeys.has(policyKey)) {
      throw new ValidationError(`${path} 与已有 metric/selector 重复`);
    }
    policyKeys.add(policyKey);
  });
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
  assertExactKeys(
    data,
    { required: INGEST_COUNT_KEYS, optional: SAMPLING_KEYS },
    "automation batch 响应 data",
  );
  for (const key of INGEST_COUNT_KEYS) {
    if (!Number.isInteger(data[key]) || data[key] < 0) {
      throw new Error(`automation batch 响应计数 ${key} 无效`);
    }
  }
  validateSamplingPolicyResponse(data);
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
