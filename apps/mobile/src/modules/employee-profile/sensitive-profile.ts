import type {
  EmployeeProfile,
  EmployeeProfileSensitiveChangeRequest,
  SensitiveEmployeeProfilePayload,
  UpdateEmployeeProfilePayload,
} from "./types";

const SENSITIVE_FIELDS = [
  "bankBsb",
  "bankAccountNumber",
  "superannuationCompanyName",
  "superannuationCompanyCode",
  "superannuationAccountNumber",
  "identityType",
  "identityId",
] as const satisfies ReadonlyArray<keyof SensitiveEmployeeProfilePayload>;

function normalizeSensitiveValue(value: string | null | undefined) {
  return value?.trim() ?? "";
}

function toFormalSensitiveDraft(profile: EmployeeProfile): SensitiveEmployeeProfilePayload {
  return {
    bankBsb: profile.bankBsb,
    bankAccountNumber: profile.bankAccountNumber,
    superannuationCompanyName: profile.superannuationCompanyName,
    superannuationCompanyCode: profile.superannuationCompanyCode,
    superannuationAccountNumber: profile.superannuationAccountNumber,
    identityType: profile.identityType,
    identityId: profile.identityId,
  };
}

function toPendingSensitiveDraft(
  request: EmployeeProfileSensitiveChangeRequest
): SensitiveEmployeeProfilePayload {
  return Object.fromEntries(
    SENSITIVE_FIELDS.map((field) => [field, request[field]])
  ) as unknown as SensitiveEmployeeProfilePayload;
}

export function selectSensitiveDraft(
  profile: EmployeeProfile,
  request: EmployeeProfileSensitiveChangeRequest | null | undefined
) {
  // 只有 Pending 才能恢复待审快照，其他状态重新填报时必须以正式资料为准。
  return request?.status === "Pending"
    ? toPendingSensitiveDraft(request)
    : toFormalSensitiveDraft(profile);
}

export function buildNonSensitiveProfilePayload(
  draft: UpdateEmployeeProfilePayload
): UpdateEmployeeProfilePayload {
  return {
    phone: draft.phone.trim(),
    birthday: draft.birthday.trim(),
    gender: draft.gender.trim(),
    employmentType: draft.employmentType.trim(),
    address: draft.address.trim(),
  };
}

export function normalizeSensitiveDraft(
  draft: SensitiveEmployeeProfilePayload
): SensitiveEmployeeProfilePayload {
  return Object.fromEntries(
    SENSITIVE_FIELDS.map((field) => [field, normalizeSensitiveValue(draft[field])])
  ) as unknown as SensitiveEmployeeProfilePayload;
}

export function getChangedSensitiveFields(
  profile: EmployeeProfile,
  draft: SensitiveEmployeeProfilePayload
) {
  const formal = toFormalSensitiveDraft(profile);
  // 变更判断使用完整规范化值；末四位摘要只用于渲染，绝不能参与相等判断。
  return SENSITIVE_FIELDS.filter(
    (field) => normalizeSensitiveValue(formal[field]) !== normalizeSensitiveValue(draft[field])
  );
}

export function getSensitiveAccountSummary(value: string | null | undefined) {
  const compact = normalizeSensitiveValue(value).replace(/[\s-]+/g, "");
  return compact ? `•••• ${compact.slice(-4)}` : "";
}

export function getSensitiveStatusView(
  request: EmployeeProfileSensitiveChangeRequest | null | undefined
) {
  if (!request) {
    return {
      statusKey: "status.empty",
      canRefill: true,
      reviewReason: "",
      submittedAt: "",
      changedFields: [] as string[],
    };
  }
  return {
    statusKey: `status.${request.status.toLowerCase()}`,
    canRefill: request.status !== "Pending",
    reviewReason: request.reviewReason ?? "",
    submittedAt: request.submittedAt,
    changedFields: request.changedFields,
  };
}

export type SensitiveRefreshTrigger = "focus" | "app-active" | "manual";

export function shouldRefreshSensitiveProfile(
  trigger: SensitiveRefreshTrigger,
  isAuthenticated: boolean,
  appState: string
) {
  if (!isAuthenticated) {
    return false;
  }
  return trigger !== "app-active" || appState === "active";
}

export function shouldShowPendingIdentityPhotoRemoval(input: {
  changedFields: string[];
  pendingHasIdentityPhoto: boolean;
  formalHasIdentityPhoto: boolean;
}) {
  return input.formalHasIdentityPhoto
    && !input.pendingHasIdentityPhoto
    && input.changedFields.includes("identityPhotoUrl");
}

export async function submitSensitiveProfileWithCache<TPayload, TRequest>(
  payload: TPayload,
  dependencies: {
    cancelRequestQuery: () => Promise<unknown>;
    submitRequest: (value: TPayload) => Promise<TRequest>;
    setRequestData: (value: TRequest) => void;
    refreshRequestQuery: () => Promise<unknown>;
  }
) {
  // 关键逻辑：先取消旧 GET，防止其在 PUT 成功后把新待审快照覆盖回旧值。
  await dependencies.cancelRequestQuery();
  const request = await dependencies.submitRequest(payload);
  dependencies.setRequestData(request);
  try {
    await dependencies.refreshRequestQuery();
  } catch {
    // PUT 已成功并写入权威响应；刷新失败不得让 mutation 自动重试或误报提交失败。
  }
  return request;
}

export async function refreshEmployeeProfileAfterIdentityMutation(dependencies: {
  refetchSensitive: () => Promise<{ isError: boolean }>;
  refetchFormal: () => Promise<{ isError: boolean }>;
}) {
  const refetchSafely = async (refetch: () => Promise<{ isError: boolean }>) => {
    try {
      return (await refetch()).isError;
    } catch {
      return true;
    }
  };
  // 先刷新待审快照，确保新证件照立即出现在审核状态区；正式资料随后独立校验。
  const sensitiveIsError = await refetchSafely(dependencies.refetchSensitive);
  const formalIsError = await refetchSafely(dependencies.refetchFormal);
  return { isError: sensitiveIsError || formalIsError };
}

export function isSensitiveVersionConflict(error: unknown) {
  const status = (error as { response?: { status?: unknown } } | null)?.response?.status;
  if (status === 409) {
    return true;
  }
  const message = error instanceof Error ? error.message : String(error ?? "");
  return message.includes("EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT")
    || message.includes("正式敏感资料已被管理员更新");
}
