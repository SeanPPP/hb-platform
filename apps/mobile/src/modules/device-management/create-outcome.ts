// 只列出三个创建接口在写入前明确拒绝的错误；内部失败不按前缀放行重试。
const DEFINITE_REJECTIONS = new Set([
  ...["STORE_REQUIRED", "SYSTEM_INVALID", "VALIDITY_INVALID", "REASON_INVALID", "STORE_FORBIDDEN", "STORE_UNAVAILABLE"].map((code) => `DEVICE_ACTIVATION_${code}`),
  ...["STORE_REQUIRED", "SYSTEM_INVALID", "ACCOUNT_REQUIRED", "VALIDITY_INVALID", "REASON_INVALID", "STORE_SCOPE_FORBIDDEN", "STORE_FORBIDDEN", "STORE_UNAVAILABLE", "ACCOUNT_UNAVAILABLE"].map((code) => `MOBILE_ACTIVATION_${code}`),
  ...["STORE_REQUIRED", "REASON_INVALID", "STORE_FORBIDDEN", "NO_ENABLED_POS", "ACTIVE_SIGNING_KEY_UNAVAILABLE", "ALREADY_ACTIVE", "SIGNING_KEY_DECRYPT_FAILED", "SIGNING_KEY_INVALID"].map((code) => `EMERGENCY_GRANT_${code}`),
]);

export function isResultUnknownCreateError(error: unknown): boolean {
  if (!error || typeof error !== "object") return true;
  const candidate = error as { code?: unknown; status?: unknown; response?: { status?: unknown } };
  const status = typeof candidate.status === "number" ? candidate.status
    : typeof candidate.response?.status === "number" ? candidate.response.status : null;
  // 网关超时或服务端错误即使携带 code，也不能证明创建没有提交。
  if (status !== null && status >= 500) return true;
  if (status === 408 || status === 499) return true;
  if (status !== null && status >= 400 && status < 500) return false;
  // HTTP 200 的业务拒绝经 unwrapApiEnvelope 后只有 Error.code。
  return typeof candidate.code !== "string" || !DEFINITE_REJECTIONS.has(candidate.code);
}
