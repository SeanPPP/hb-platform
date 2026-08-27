import { ValidationError, assertFiniteNumber, assertSafeString } from "./validation.mjs";

const MAX_REQUEST_BYTES = 256 * 1024;
const MAX_RESPONSE_BYTES = 64 * 1024;
const ALLOWED_ENDPOINTS = new Set([
  "/api/system/performance/automation-batches",
  "/api/system/performance/release-events",
]);

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&");
}

export function redactSensitive(value, explicitSecrets = []) {
  let message = value instanceof Error ? value.message : String(value);
  for (const secret of explicitSecrets) {
    if (typeof secret === "string" && secret.length > 0 && secret.length <= 4096) {
      message = message.replace(new RegExp(escapeRegExp(secret), "gu"), "[REDACTED]");
    }
  }
  message = message
    .replace(/Bearer\s+[^\s,;]+/giu, "Bearer [REDACTED]")
    .replace(/hbsvc_[A-Za-z0-9_-]+/gu, "[REDACTED]")
    .replace(
      /([?&](?:access_?token|api_?key|password|secret)=)[^&#\s]*/giu,
      "$1[REDACTED]",
    )
    .replace(/[\u0000-\u001f\u007f]+/gu, " ")
    .trim();
  return message.length > 500 ? `${message.slice(0, 497)}...` : message;
}

export function validateServiceToken(token) {
  assertSafeString(token, "service token", {
    minLength: 38,
    maxLength: 518,
    pattern: /^hbsvc_[A-Za-z0-9_-]{32,512}$/u,
  });
  return token;
}

export function buildEndpointUrl(baseUrl, endpointPath) {
  assertSafeString(baseUrl, "service URL", { maxLength: 2048 });
  if (!ALLOWED_ENDPOINTS.has(endpointPath)) {
    throw new ValidationError("上报 endpoint 不在固定允许列表中");
  }

  let parsed;
  try {
    parsed = new URL(baseUrl);
  } catch {
    throw new ValidationError("service URL 格式无效");
  }
  if (parsed.protocol !== "https:") {
    throw new ValidationError("service URL 必须使用 HTTPS");
  }
  if (parsed.username || parsed.password || parsed.search || parsed.hash) {
    throw new ValidationError("service URL 不得包含凭据、query 或 fragment");
  }
  if (parsed.pathname !== "/") {
    throw new ValidationError("service URL 必须是 origin，不得附带路径");
  }
  return new URL(endpointPath, parsed.origin).toString();
}

function normalizeTimeout(timeoutMs) {
  const normalized = timeoutMs ?? 10_000;
  assertFiniteNumber(normalized, "timeoutMs", {
    min: 100,
    max: 30_000,
    integer: true,
  });
  return normalized;
}

function safeRequestId(response, token) {
  const value = response?.headers?.get?.("x-request-id") ?? null;
  if (typeof value !== "string" || value.length < 1 || value.length > 128) {
    return null;
  }
  if (
    value !== value.trim() ||
    !/^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u.test(value) ||
    value.includes(token) ||
    /hbsvc_|bearer/i.test(value)
  ) {
    return null;
  }
  return value;
}

async function cancelResponseBody(response) {
  try {
    await response?.body?.cancel?.();
  } catch {
    // 响应正文既不需要也不可信，取消失败不覆盖真实 HTTP 结论。
  }
}

function safeBusinessCode(value) {
  if (
    typeof value === "string" &&
    value.length >= 1 &&
    value.length <= 80 &&
    /^[A-Za-z0-9_.:-]+$/u.test(value)
  ) {
    return value;
  }
  return "UNKNOWN";
}

async function readBoundedResponseText(response) {
  const contentLength = response?.headers?.get?.("content-length");
  if (contentLength && /^\d+$/u.test(contentLength) && Number(contentLength) > MAX_RESPONSE_BYTES) {
    await cancelResponseBody(response);
    throw new Error(`响应正文超过 ${MAX_RESPONSE_BYTES} bytes`);
  }

  if (typeof response?.body?.getReader === "function") {
    const reader = response.body.getReader();
    const chunks = [];
    let totalBytes = 0;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        if (!(value instanceof Uint8Array)) {
          throw new Error("响应正文流格式无效");
        }
        totalBytes += value.byteLength;
        if (totalBytes > MAX_RESPONSE_BYTES) {
          await reader.cancel();
          throw new Error(`响应正文超过 ${MAX_RESPONSE_BYTES} bytes`);
        }
        chunks.push(value);
      }
    } finally {
      reader.releaseLock?.();
    }
    const combined = new Uint8Array(totalBytes);
    let offset = 0;
    for (const chunk of chunks) {
      combined.set(chunk, offset);
      offset += chunk.byteLength;
    }
    return new TextDecoder("utf-8", { fatal: true }).decode(combined);
  }

  if (typeof response?.text === "function") {
    const text = await response.text();
    if (Buffer.byteLength(text, "utf8") > MAX_RESPONSE_BYTES) {
      throw new Error(`响应正文超过 ${MAX_RESPONSE_BYTES} bytes`);
    }
    return text;
  }
  throw new Error("响应缺少可读取的 JSON 正文");
}

async function readApiEnvelope(response) {
  if (response.status === 204) return { data: null };
  const contentType = response?.headers?.get?.("content-type");
  if (contentType && !/^application\/(?:[a-z0-9.+-]*\+)?json(?:\s*;|$)/iu.test(contentType)) {
    await cancelResponseBody(response);
    throw new Error("响应 Content-Type 不是 JSON");
  }
  const text = await readBoundedResponseText(response);
  let envelope;
  try {
    envelope = JSON.parse(text);
  } catch {
    throw new Error("响应正文不是有效 JSON");
  }
  if (envelope === null || typeof envelope !== "object" || Array.isArray(envelope)) {
    throw new Error("响应正文不是 ApiResponse 对象");
  }
  const success = envelope.success ?? envelope.isSuccess;
  if (success !== true) {
    const code = safeBusinessCode(envelope.errorCode ?? envelope.code);
    throw new Error(`服务端业务拒绝（${code}）`);
  }
  if (
    Object.hasOwn(envelope, "success") &&
    Object.hasOwn(envelope, "isSuccess") &&
    envelope.success !== envelope.isSuccess
  ) {
    throw new Error("服务端 ApiResponse success/isSuccess 不一致");
  }
  return { data: envelope.data ?? null };
}

export async function postServiceJson({
  baseUrl,
  token,
  endpointPath,
  payload,
  timeoutMs,
  fetchImpl = globalThis.fetch,
}) {
  validateServiceToken(token);
  const url = buildEndpointUrl(baseUrl, endpointPath);
  const normalizedTimeout = normalizeTimeout(timeoutMs);
  if (typeof fetchImpl !== "function") {
    throw new ValidationError("当前 Node 运行时不支持 fetch");
  }

  let body;
  try {
    body = JSON.stringify(payload);
  } catch {
    throw new ValidationError("上报 payload 无法序列化为 JSON");
  }
  if (Buffer.byteLength(body, "utf8") > MAX_REQUEST_BYTES) {
    throw new ValidationError(`上报 payload 不得超过 ${MAX_REQUEST_BYTES} bytes`);
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), normalizedTimeout);
  try {
    const response = await fetchImpl(url, {
      method: "POST",
      headers: {
        accept: "application/json",
        authorization: `Bearer ${token}`,
        "content-type": "application/json",
      },
      body,
      redirect: "error",
      credentials: "omit",
      cache: "no-store",
      signal: controller.signal,
    });
    const requestId = safeRequestId(response, token);
    if (!response || response.ok !== true || response.status < 200 || response.status > 299) {
      await cancelResponseBody(response);
      const status = Number.isInteger(response?.status) ? response.status : "unknown";
      const requestSuffix = requestId ? `，request-id ${requestId}` : "";
      throw new Error(`上报失败（HTTP ${status}${requestSuffix}）`);
    }
    const envelope = await readApiEnvelope(response);
    return { status: response.status, requestId, data: envelope.data };
  } catch (error) {
    if (controller.signal.aborted) {
      throw new Error(`上报请求超时（${normalizedTimeout}ms）`);
    }
    throw new Error(`上报请求失败：${redactSensitive(error, [token, baseUrl])}`);
  } finally {
    clearTimeout(timer);
  }
}
