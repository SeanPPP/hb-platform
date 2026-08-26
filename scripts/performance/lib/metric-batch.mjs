import {
  ValidationError,
  assertCanonicalUtcTimestamp,
  assertEnum,
  assertExactKeys,
  assertFiniteNumber,
  assertPlainObject,
  assertSafeString,
} from "./validation.mjs";

export const PERFORMANCE_METRIC_NAMES = Object.freeze([
  "api.request.duration",
  "sql.command.duration",
  "hq.sync.duration",
  "hq.sync.backlog",
  "background.job.duration",
  "web.first_screen.bytes",
  "web.largest_initial_chunk.bytes",
  "web.table.react_commit.duration",
  "web.table.render_to_paint.duration",
  "pos.cold_start.duration",
  "pos.scan_to_cart.duration",
  "pos.payment_response.duration",
  "sentry.crash_free_session.ratio",
  "ci.run.duration",
]);

export const PERFORMANCE_METRIC_UNITS = Object.freeze(["ms", "bytes", "count", "ratio"]);

export const PERFORMANCE_METRIC_DIMENSIONS = Object.freeze([
  "metricId",
  "route",
  "method",
  "statusClass",
  "environment",
  "instance",
  "app",
  "version",
  "channel",
  "store",
  "paymentType",
  "outcome",
  "databaseContext",
  "sqlFingerprint",
  "sqlTemplate",
  "taskType",
  "operation",
  "lane",
  "component",
  "source",
  "release",
  "dist",
  "project",
  "action",
]);

const MAX_EVENTS = 200;
const MAX_DIMENSIONS = 10;
const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";
const ALLOWED_DIMENSIONS = new Set(PERFORMANCE_METRIC_DIMENSIONS);

function validateDimensions(dimensions, path) {
  assertPlainObject(dimensions, path);
  const entries = Object.entries(dimensions);
  if (entries.length > MAX_DIMENSIONS) {
    throw new ValidationError(`${path} 最多允许 ${MAX_DIMENSIONS} 个维度`);
  }
  for (const [key, value] of entries) {
    if (!ALLOWED_DIMENSIONS.has(key)) {
      throw new ValidationError(`${path} 的维度 ${key} 不在白名单`);
    }
    assertSafeString(key, `${path} 的维度名`, { maxLength: 64 });
    assertSafeString(value, `${path}.${key}`, { maxLength: 120 });
  }
}

function validateEvent(event, index) {
  const path = `payload.events[${index}]`;
  assertExactKeys(
    event,
    { required: ["eventId", "metric", "observedAt", "value", "unit", "dimensions"] },
    path,
  );
  assertSafeString(event.eventId, `${path}.eventId`, {
    minLength: 36,
    maxLength: 36,
    pattern: GUID_PATTERN,
  });
  if (event.eventId.toLowerCase() === EMPTY_GUID) {
    throw new ValidationError(`${path}.eventId 不能为空 GUID`);
  }
  assertEnum(event.metric, PERFORMANCE_METRIC_NAMES, `${path}.metric`);
  assertCanonicalUtcTimestamp(event.observedAt, `${path}.observedAt`);
  assertFiniteNumber(event.value, `${path}.value`, {
    min: 0,
    max: 1_000_000_000_000_000,
  });
  assertEnum(event.unit, PERFORMANCE_METRIC_UNITS, `${path}.unit`);
  validateDimensions(event.dimensions, `${path}.dimensions`);
}

export function validateMetricBatchV1(payload) {
  assertExactKeys(
    payload,
    { required: ["schemaVersion", "events"] },
    "payload",
  );
  if (payload.schemaVersion !== 1) {
    throw new ValidationError("payload.schemaVersion 仅支持整数 1");
  }
  if (!Array.isArray(payload.events) || payload.events.length < 1 || payload.events.length > MAX_EVENTS) {
    throw new ValidationError(`payload.events 必须包含 1 至 ${MAX_EVENTS} 个事件`);
  }

  const eventIds = new Set();
  payload.events.forEach((event, index) => {
    validateEvent(event, index);
    const normalizedId = event.eventId.toLowerCase();
    if (eventIds.has(normalizedId)) {
      throw new ValidationError(`payload.events 包含重复 eventId ${event.eventId}`);
    }
    eventIds.add(normalizedId);
  });
  return payload;
}
