import type { ApplicationLogInput } from "./log-center";
import { reportApplicationLog } from "./log-center-runtime";

interface ExternalFetchFailureContext {
  level?: string;
  message: string;
  sourceType: string;
  requestMethod?: string;
  requestUrl?: string | null;
  statusCode?: number;
  error?: unknown;
  fileUri?: string | null;
  properties?: Record<string, unknown>;
}

const SENSITIVE_PROPERTY_PATTERN = /(token|authorization|credential|signature|sig|code)/i;

function sanitizeUrlForLogging(url?: string | null) {
  const candidate = typeof url === "string" ? url.trim() : "";
  if (!candidate) {
    return undefined;
  }

  try {
    const parsed = new URL(candidate);
    if (parsed.origin === "null") {
      return candidate.split("?")[0]?.split("#")[0]?.trim() || undefined;
    }
    return `${parsed.origin}${parsed.pathname}`;
  } catch {
    return candidate.split("?")[0]?.split("#")[0]?.trim() || undefined;
  }
}

function extractFileUriTail(fileUri?: string | null) {
  const sanitizedUri = sanitizeUrlForLogging(fileUri);
  if (!sanitizedUri) {
    return undefined;
  }

  const segments = sanitizedUri.split("/").filter(Boolean);
  return segments[segments.length - 1] || undefined;
}

function sanitizeProperties(properties?: Record<string, unknown>) {
  if (!properties) {
    return undefined;
  }

  const sanitized: Record<string, unknown> = {};

  for (const [key, value] of Object.entries(properties)) {
    if (value == null || SENSITIVE_PROPERTY_PATTERN.test(key)) {
      continue;
    }

    if (/url$/i.test(key) && typeof value === "string") {
      const sanitizedUrl = sanitizeUrlForLogging(value);
      if (sanitizedUrl) {
        sanitized[key] = sanitizedUrl;
      }
      continue;
    }

    if (/uri$/i.test(key) && typeof value === "string") {
      const fileUriTail = extractFileUriTail(value);
      if (fileUriTail) {
        sanitized[`${key}Tail`] = fileUriTail;
      }
      continue;
    }

    sanitized[key] = value;
  }

  return Object.keys(sanitized).length ? sanitized : undefined;
}

export function createExternalFetchFailureLogInput(
  context: ExternalFetchFailureContext
): ApplicationLogInput {
  const normalizedError = context.error instanceof Error
    ? context.error
    : context.error
      ? new Error(String(context.error))
      : undefined;
  const sanitizedRequestUrl = sanitizeUrlForLogging(context.requestUrl);
  const sanitizedProperties = sanitizeProperties(context.properties) ?? {};
  const fileUriTail = extractFileUriTail(context.fileUri);

  if (fileUriTail) {
    sanitizedProperties.fileUriTail = fileUriTail;
  }

  return {
    level: context.level ?? "Error",
    message: context.message,
    sourceType: context.sourceType,
    requestMethod: context.requestMethod,
    requestPath: sanitizedRequestUrl,
    statusCode: context.statusCode,
    exceptionType: normalizedError?.name,
    exceptionMessage: normalizedError?.message,
    stackTrace: normalizedError?.stack,
    properties: Object.keys(sanitizedProperties).length ? sanitizedProperties : undefined,
  };
}

export function reportExternalFetchFailure(context: ExternalFetchFailureContext) {
  try {
    // 外部上传/下载日志必须旁路上报，任何异常都不能反向影响原始业务失败语义。
    reportApplicationLog(createExternalFetchFailureLogInput(context));
  } catch {
    // 这里显式吞掉日志链路异常，确保调用方仍然只处理自己的业务错误。
  }
}
