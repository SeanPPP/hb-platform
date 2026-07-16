import type { EmployeeProfileReviewStatus } from "./types";

type ApiErrorRecord = Record<string, unknown>;

function asRecord(value: unknown): ApiErrorRecord {
  return value && typeof value === "object" ? value as ApiErrorRecord : {};
}

function readErrorCode(error: unknown) {
  const response = asRecord(asRecord(error).response);
  const payload = asRecord(response.data);
  const code = payload.errorCode ?? payload.ErrorCode ?? payload.code ?? payload.Code;
  return typeof code === "string" ? code : "";
}

export function maskSensitiveValue(value?: string | null) {
  const normalized = value?.trim();
  if (!normalized) {
    return "--";
  }
  return `••••${normalized.slice(-4)}`;
}

export function isRejectReasonValid(reason?: string | null) {
  return Boolean(reason?.trim());
}

export function shouldDisableReviewActions(
  status: EmployeeProfileReviewStatus | undefined,
  stale: boolean
) {
  return status !== "Pending" || stale;
}

export type ReviewFailureKind = "conflict" | "forbidden" | "other";

export function getReviewFailureKind(error: unknown): ReviewFailureKind {
  const response = asRecord(asRecord(error).response);
  const status = Number(response.status);
  if (status === 403) {
    return "forbidden";
  }
  if (status === 404 && readErrorCode(error) === "REQUEST_NOT_FOUND") {
    // 后端为防枚举会把范围撤销伪装成不存在，客户端必须按权限失效清理。
    return "forbidden";
  }
  if (status === 409) {
    const code = readErrorCode(error);
    if (
      code === "EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT"
      || code === "REQUEST_NOT_PENDING"
    ) {
      return "conflict";
    }
  }
  return "other";
}
