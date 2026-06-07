import type { ApplicationLogIngestItem } from '../types/centerLog'

const importMetaEnv = (import.meta as ImportMeta & { env?: ImportMetaEnv }).env ?? {}
const API_BASE_URL = (importMetaEnv.VITE_API_BASE_URL || '').trim()
const CENTER_LOG_INGEST_PATH = '/api/system/logs/ingest'
const CENTER_LOG_PROJECT = (importMetaEnv.VITE_CENTER_LOG_PROJECT || 'hbweb_rv').trim()
const CENTER_LOG_KEY = (importMetaEnv.VITE_CENTER_LOG_KEY || '').trim()
const CENTER_LOG_ENVIRONMENT =
  (importMetaEnv.VITE_CENTER_LOG_ENVIRONMENT || importMetaEnv.MODE || 'development').trim()
const CENTER_LOG_SERVICE_NAME = (importMetaEnv.VITE_CENTER_LOG_SERVICE_NAME || 'hbweb_rv-web').trim()
const CENTER_LOG_SOURCE_TYPE = 'Web'

const MAX_MESSAGE_LENGTH = 2000
const MAX_STACK_LENGTH = 12000
const MAX_PROPERTY_LENGTH = 1000

function trimText(value: string | undefined, maxLength: number) {
  if (!value) {
    return undefined
  }

  const normalized = value.trim()
  if (!normalized) {
    return undefined
  }

  return normalized.length > maxLength
    ? `${normalized.slice(0, maxLength - 3)}...`
    : normalized
}

function buildApiUrl(path: string) {
  return `${API_BASE_URL}${path}`.replace(/([^:]\/)\/+/g, '$1')
}

function getRequestPath(url?: string) {
  if (!url) {
    return undefined
  }

  try {
    const resolved = new URL(url, typeof window !== 'undefined' ? window.location.origin : 'http://localhost')
    return `${resolved.pathname}${resolved.search}`
  } catch {
    return url
  }
}

function sanitizeProperties(properties?: Record<string, unknown>) {
  if (!properties) {
    return undefined
  }

  const sanitizedEntries: Array<[string, unknown]> = []

  Object.entries(properties).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') {
      return
    }

    if (typeof value === 'string') {
      const trimmedValue = trimText(value, MAX_PROPERTY_LENGTH)
      if (trimmedValue) {
        sanitizedEntries.push([key, trimmedValue])
      }
      return
    }

    sanitizedEntries.push([key, value])
  })

  return sanitizedEntries.length ? Object.fromEntries(sanitizedEntries) : undefined
}

export function isCenterLogIngestRequest(url: string) {
  const requestPath = getRequestPath(url) || ''
  return requestPath.startsWith(CENTER_LOG_INGEST_PATH)
}

export function isCenterLogConfigured() {
  return Boolean(CENTER_LOG_KEY)
}

export interface CenterLogPayload extends Omit<ApplicationLogIngestItem, 'projectCode' | 'environment' | 'serviceName' | 'timestampUtc'> {
  timestampUtc?: string
}

export function sendCenterLog(payload: CenterLogPayload) {
  if (!isCenterLogConfigured()) {
    return
  }

  // 前端统一在这里补齐中心日志要求的基础字段，业务侧只传本次事件本身。
  const item: ApplicationLogIngestItem = {
    ...payload,
    message: trimText(payload.message, MAX_MESSAGE_LENGTH) || '未知错误',
    timestampUtc: payload.timestampUtc || new Date().toISOString(),
    projectCode: CENTER_LOG_PROJECT,
    environment: CENTER_LOG_ENVIRONMENT,
    sourceType: CENTER_LOG_SOURCE_TYPE,
    serviceName: CENTER_LOG_SERVICE_NAME || undefined,
    exceptionMessage: trimText(payload.exceptionMessage, MAX_MESSAGE_LENGTH),
    stackTrace: trimText(payload.stackTrace, MAX_STACK_LENGTH),
    requestPath: trimText(payload.requestPath, MAX_PROPERTY_LENGTH),
    traceId: trimText(payload.traceId, MAX_PROPERTY_LENGTH),
    category: trimText(payload.category || payload.sourceType, MAX_PROPERTY_LENGTH),
    userId: trimText(payload.userId, MAX_PROPERTY_LENGTH),
    userName: trimText(payload.userName, MAX_PROPERTY_LENGTH),
    properties: sanitizeProperties(payload.properties),
  }

  void fetch(buildApiUrl(CENTER_LOG_INGEST_PATH), {
    method: 'POST',
    credentials: 'include',
    keepalive: true,
    headers: {
      'Content-Type': 'application/json',
      'X-Log-Project': CENTER_LOG_PROJECT,
      'X-Log-Key': CENTER_LOG_KEY,
    },
    body: JSON.stringify({ logs: [item] }),
  }).catch(() => {
    // 日志上报必须静默失败，不能反向影响业务链路。
  })
}

function normalizeUnknownError(error: unknown) {
  if (error instanceof Error) {
    return {
      message: error.message,
      exceptionType: error.name,
      stackTrace: error.stack,
    }
  }

  return {
    message: typeof error === 'string' ? error : '未知异常',
    exceptionType: typeof error,
    stackTrace: undefined,
  }
}

export interface RequestErrorReportInput {
  url: string
  method: string
  statusCode?: number
  error: unknown
  responsePayload?: unknown
  traceId?: string
}

export function reportRequestError(input: RequestErrorReportInput) {
  // 日志写入接口自身失败时必须短路，避免 request -> log -> request 的递归放大。
  if (isCenterLogIngestRequest(input.url)) {
    return
  }

  const normalizedError = normalizeUnknownError(input.error)
  sendCenterLog({
    level: input.statusCode && input.statusCode < 500 ? 'Warning' : 'Error',
    sourceType: 'frontend-request',
    message: normalizedError.message,
    exceptionType: normalizedError.exceptionType,
    exceptionMessage: normalizedError.message,
    stackTrace: normalizedError.stackTrace,
    requestPath: getRequestPath(input.url),
    requestMethod: input.method,
    statusCode: input.statusCode,
    traceId: input.traceId,
    properties: {
      responsePayload: input.responsePayload,
    },
  })
}

export function reportRuntimeError(
  sourceType: 'window-error' | 'unhandledrejection' | 'react-error-boundary',
  error: unknown,
  properties?: Record<string, unknown>,
) {
  const normalizedError = normalizeUnknownError(error)
  sendCenterLog({
    level: 'Error',
    sourceType,
    message: normalizedError.message,
    exceptionType: normalizedError.exceptionType,
    exceptionMessage: normalizedError.message,
    stackTrace: normalizedError.stackTrace,
    properties,
  })
}
