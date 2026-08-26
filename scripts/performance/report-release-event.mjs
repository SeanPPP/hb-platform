import { createHash } from "node:crypto";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { postServiceJson, redactSensitive } from "./lib/http-reporter.mjs";
import {
  ValidationError,
  assertCanonicalUtcTimestamp,
  assertCommitSha,
  assertEnum,
  assertExactKeys,
  assertSafeString,
  assertUuidV4,
} from "./lib/validation.mjs";

const ENDPOINT_PATH = "/api/system/performance/release-events";
const ACTIONS = ["deploy", "rollback"];
const STATUSES = ["accepted", "failed"];
const SAFE_SLUG = /^[A-Za-z0-9][A-Za-z0-9._:-]*$/u;

export function validateReleaseEventV1(event) {
  assertExactKeys(
    event,
    {
      required: [
        "eventId",
        "action",
        "status",
        "environment",
        "component",
        "commit",
        "version",
        "startedAtUtc",
        "completedAtUtc",
        "source",
      ],
    },
    "event",
  );
  assertUuidV4(event.eventId, "event.eventId");
  assertEnum(event.action, ACTIONS, "event.action");
  assertEnum(event.status, STATUSES, "event.status");
  assertSafeString(event.environment, "event.environment", {
    maxLength: 60,
    pattern: SAFE_SLUG,
  });
  assertSafeString(event.component, "event.component", {
    maxLength: 120,
    pattern: SAFE_SLUG,
  });
  assertCommitSha(event.commit, "event.commit");
  if (event.version !== null) {
    assertSafeString(event.version, "event.version", {
      maxLength: 80,
      pattern: SAFE_SLUG,
    });
  }
  const startedAt = assertCanonicalUtcTimestamp(event.startedAtUtc, "event.startedAtUtc");
  const completedAt = assertCanonicalUtcTimestamp(event.completedAtUtc, "event.completedAtUtc");
  if (completedAt.getTime() < startedAt.getTime()) {
    throw new ValidationError("event.completedAtUtc 不得早于 startedAtUtc");
  }
  assertSafeString(event.source, "event.source", { maxLength: 120 });
  return event;
}

function buildSource({ sourceProvider, sourceRunId, healthCheckReference }, eventId) {
  assertSafeString(sourceProvider, "sourceProvider", {
    maxLength: 40,
    pattern: SAFE_SLUG,
  });
  const reference = healthCheckReference ?? sourceRunId ?? eventId;
  assertSafeString(reference, "release source reference", {
    maxLength: 70,
    pattern: SAFE_SLUG,
  });
  return `${sourceProvider}:${reference}`;
}

function createDeterministicEventId(input) {
  const identity = JSON.stringify([
    input.sourceProvider,
    input.sourceRunId ?? input.healthCheckReference ?? "manual",
    input.environment,
    input.component,
    input.action,
    input.releaseId,
    input.commitSha,
    input.startedAtUtc,
  ]);
  const bytes = createHash("sha256").update(identity, "utf8").digest().subarray(0, 16);
  // 使用稳定哈希生成符合现有 v4 形状校验的 UUID；同一部署验收重试会复用同一幂等键。
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString("hex");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function buildReleaseEvent(
  {
    action,
    conclusion,
    component,
    environment,
    releaseId,
    commitSha,
    startedAtUtc,
    completedAtUtc,
    healthChecked,
    healthCheckReference = null,
    sourceProvider = "manual",
    sourceRunId,
  },
  { eventId } = {},
) {
  if (healthChecked !== true) {
    throw new ValidationError("必须显式确认健康验收已完成（--health-checked）");
  }
  // 先校验枚举，让错误不会被后续字段缺失掩盖。
  assertEnum(action, ACTIONS, "action");
  assertEnum(conclusion, STATUSES, "conclusion");
  const startedAt = assertCanonicalUtcTimestamp(startedAtUtc, "startedAtUtc");
  const completedAt = assertCanonicalUtcTimestamp(completedAtUtc, "completedAtUtc");
  if (completedAt.getTime() < startedAt.getTime()) {
    throw new ValidationError("completedAtUtc 不得早于 startedAtUtc");
  }
  const stableEventId = eventId ?? createDeterministicEventId({
    action,
    component,
    environment,
    releaseId,
    commitSha,
    startedAtUtc,
    healthCheckReference,
    sourceProvider,
    sourceRunId,
  });
  const event = {
    eventId: stableEventId,
    action,
    status: conclusion,
    environment,
    component,
    commit: commitSha,
    version: releaseId,
    startedAtUtc,
    completedAtUtc,
    source: buildSource(
      { sourceProvider, sourceRunId, healthCheckReference },
      stableEventId,
    ),
  };
  return validateReleaseEventV1(event);
}

export async function reportReleaseEvent({
  event,
  baseUrl,
  token,
  timeoutMs,
  fetchImpl,
}) {
  validateReleaseEventV1(event);
  const response = await postServiceJson({
    baseUrl,
    token,
    endpointPath: ENDPOINT_PATH,
    payload: event,
    timeoutMs,
    fetchImpl,
  });
  return { status: response.status, requestId: response.requestId };
}

function parsePositiveInteger(value, name) {
  if (value === undefined || value === "") return undefined;
  if (!/^\d+$/u.test(value)) throw new ValidationError(`${name} 必须是正整数`);
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 1) {
    throw new ValidationError(`${name} 必须是正整数`);
  }
  return parsed;
}

function parseArgs(argv) {
  const options = { healthChecked: false };
  const valueOptions = new Map([
    ["--action", "action"],
    ["--conclusion", "conclusion"],
    ["--component", "component"],
    ["--environment", "environment"],
    ["--release-id", "releaseId"],
    ["--commit-sha", "commitSha"],
    ["--started-at-utc", "startedAtUtc"],
    ["--completed-at-utc", "completedAtUtc"],
    ["--health-check-reference", "healthCheckReference"],
  ]);
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--health-checked") {
      options.healthChecked = true;
    } else if (argument === "--help") {
      options.help = true;
    } else if (valueOptions.has(argument)) {
      const value = argv[++index];
      if (!value || value.startsWith("--")) {
        throw new ValidationError(`${argument} 缺少值`);
      }
      options[valueOptions.get(argument)] = value;
    } else {
      throw new ValidationError(`未知参数 ${argument}`);
    }
  }
  return options;
}

async function main(argv = process.argv.slice(2), env = process.env) {
  const options = parseArgs(argv);
  if (options.help) {
    console.log(
      "用法: node scripts/performance/report-release-event.mjs --health-checked --action <deploy|rollback> --conclusion <accepted|failed> --component <name> --environment <name> --release-id <id> --commit-sha <sha> --started-at-utc <ISO UTC> --completed-at-utc <ISO UTC> [--health-check-reference <id>]",
    );
    return;
  }
  const event = buildReleaseEvent({
    ...options,
    sourceProvider: env.GITHUB_ACTIONS === "true" ? "github-actions" : "manual",
    sourceRunId: env.GITHUB_RUN_ID,
  });
  const baseUrl = env.PERFORMANCE_SERVICE_URL;
  const token = env.PERFORMANCE_SERVICE_TOKEN;
  if (!baseUrl || !token) {
    throw new ValidationError("PERFORMANCE_SERVICE_URL/TOKEN 必须同时配置");
  }
  const result = await reportReleaseEvent({
    event,
    baseUrl,
    token,
    timeoutMs: parsePositiveInteger(env.PERFORMANCE_SERVICE_TIMEOUT_MS, "超时毫秒"),
  });
  const requestSuffix = result.requestId ? `，request-id ${result.requestId}` : "";
  console.log(`release event 上报成功：HTTP ${result.status}${requestSuffix}`);
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  main().catch((error) => {
    console.error(
      `release event 上报失败：${redactSensitive(error, [process.env.PERFORMANCE_SERVICE_TOKEN])}`,
    );
    process.exitCode = 1;
  });
}
