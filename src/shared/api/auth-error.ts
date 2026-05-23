type ApiRecord = Record<string, unknown>;

function isRecord(value: unknown): value is ApiRecord {
  return typeof value === "object" && value !== null;
}

function asText(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function hasFailedEnvelope(payload: ApiRecord) {
  return payload.success === false || payload.isSuccess === false || payload.Success === false;
}

function hasUnauthenticatedStatus(payload: ApiRecord) {
  const status = payload.status ?? payload.statusCode ?? payload.Status ?? payload.StatusCode;
  return status === 401 || status === "401";
}

function hasUnauthenticatedMessage(payload: ApiRecord) {
  const message = [
    asText(payload.message),
    asText(payload.Message),
    asText(payload.error),
    asText(payload.Error),
    asText(payload.code),
    asText(payload.Code),
  ]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();

  return [
    "未登录",
    "请登录",
    "登录已过期",
    "登陆已过期",
    "登录失效",
    "登陆失效",
    "设备未授权",
    "设备未启用",
    "设备授权失败",
    "unauthorized",
    "unauthenticated",
    "device not authorized",
    "device not approved",
    "device disabled",
    "not logged in",
    "not authenticated",
    "session expired",
    "token expired",
  ].some((keyword) => message.includes(keyword));
}

export function isUnauthenticatedApiPayload(payload: unknown) {
  if (!isRecord(payload) || !hasFailedEnvelope(payload)) {
    return false;
  }

  return hasUnauthenticatedStatus(payload) || hasUnauthenticatedMessage(payload);
}
