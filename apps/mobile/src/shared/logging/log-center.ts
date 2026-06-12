export const LOG_CENTER_INGEST_PATH = "/system/logs/ingest";
const DEFAULT_PROJECT_CODE = "HbwebExpo";
const DEFAULT_SERVICE_NAME = "HbwebExpoApp";
const DEFAULT_SOURCE_TYPE = "Mobile";
const DEFAULT_BATCH_SIZE = 10;
const DEFAULT_MAX_QUEUE_SIZE = 30;
const DEFAULT_RETRY_LIMIT = 1;

export interface ApplicationLogIngestItem {
  level: string;
  message: string;
  timestampUtc: string;
  projectCode: string;
  environment: string;
  sourceType: string;
  serviceName?: string;
  category?: string;
  traceId?: string;
  requestPath?: string;
  requestMethod?: string;
  statusCode?: number;
  userId?: string;
  userName?: string;
  exceptionType?: string;
  exceptionMessage?: string;
  stackTrace?: string;
  properties?: Record<string, unknown>;
}

export interface LogCenterConfig {
  enabled: boolean;
  endpoint: string;
  projectCode: string;
  key: string;
  environment: string;
  serviceName: string;
  sourceType: string;
  batchSize: number;
  maxQueueSize: number;
  retryLimit: number;
}

export interface ApplicationLogInput
  extends Partial<Omit<ApplicationLogIngestItem, "timestampUtc" | "projectCode" | "environment" | "sourceType">> {
  level: string;
  message: string;
  sourceType?: string;
}

function readString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function readPositiveInteger(value: unknown, fallback: number) {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    return fallback;
  }

  return Math.floor(value);
}

function sanitizeLogProperties(properties?: Record<string, unknown>) {
  if (!properties) {
    return undefined;
  }

  const sanitized: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(properties)) {
    if (value === undefined) {
      continue;
    }

    if (value instanceof Error) {
      sanitized[key] = {
        name: value.name,
        message: value.message,
      };
      continue;
    }

    sanitized[key] = value;
  }

  return Object.keys(sanitized).length ? sanitized : undefined;
}

export function buildLogCenterEndpoint(apiBaseUrl: string) {
  const trimmedBaseUrl = apiBaseUrl.trim().replace(/\/+$/, "");
  if (!trimmedBaseUrl) {
    return LOG_CENTER_INGEST_PATH;
  }

  return `${trimmedBaseUrl}${LOG_CENTER_INGEST_PATH}`;
}

export function isLogCenterIngestUrl(url?: string | null) {
  const candidate = url?.trim();
  if (!candidate) {
    return false;
  }

  try {
    return new URL(candidate).pathname.endsWith(LOG_CENTER_INGEST_PATH);
  } catch {
    return candidate.split("?")[0].endsWith(LOG_CENTER_INGEST_PATH);
  }
}

export function normalizeLogCenterConfig(rawConfig: unknown, fallbackApiBaseUrl: string): LogCenterConfig {
  const config = (rawConfig && typeof rawConfig === "object" ? rawConfig : {}) as Record<string, unknown>;
  const endpoint = readString(config.endpoint) || buildLogCenterEndpoint(fallbackApiBaseUrl);
  const key = readString(config.key);
  const environment = readString(config.environment);
  const projectCode = readString(config.projectCode) || DEFAULT_PROJECT_CODE;
  const serviceName = readString(config.serviceName) || DEFAULT_SERVICE_NAME;
  const sourceType = readString(config.sourceType) || DEFAULT_SOURCE_TYPE;
  const batchSize = readPositiveInteger(config.batchSize, DEFAULT_BATCH_SIZE);
  const maxQueueSize = readPositiveInteger(config.maxQueueSize, DEFAULT_MAX_QUEUE_SIZE);
  const retryLimit = readPositiveInteger(config.retryLimit, DEFAULT_RETRY_LIMIT);

  return {
    enabled: Boolean(endpoint && key && environment),
    endpoint,
    key,
    environment,
    projectCode,
    serviceName,
    sourceType,
    batchSize,
    maxQueueSize,
    retryLimit,
  };
}

export function createApplicationLogItem(
  input: ApplicationLogInput,
  config: Pick<LogCenterConfig, "projectCode" | "environment" | "serviceName" | "sourceType">
): ApplicationLogIngestItem {
  return {
    level: input.level,
    message: input.message,
    timestampUtc: new Date().toISOString(),
    projectCode: config.projectCode,
    environment: config.environment,
    sourceType: config.sourceType,
    serviceName: input.serviceName?.trim() || config.serviceName,
    category: input.category?.trim() || input.sourceType?.trim() || undefined,
    traceId: input.traceId?.trim() || undefined,
    requestPath: input.requestPath?.trim() || undefined,
    requestMethod: input.requestMethod?.trim().toUpperCase() || undefined,
    statusCode: input.statusCode,
    userId: input.userId?.trim() || undefined,
    userName: input.userName?.trim() || undefined,
    exceptionType: input.exceptionType?.trim() || undefined,
    exceptionMessage: input.exceptionMessage?.trim() || undefined,
    stackTrace: input.stackTrace?.trim() || undefined,
    properties: sanitizeLogProperties(input.properties),
  };
}
