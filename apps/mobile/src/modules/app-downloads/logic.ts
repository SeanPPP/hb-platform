import type {
  HandheldCandidate,
  HandheldPolicy,
  NativePolicyForm,
  OtaPolicyForm,
  TargetScope,
} from "./types";

export interface ConfirmationLabels {
  enabled: string;
  disabled: string;
  release: string;
  noRelease: string;
  required: string;
  optional: string;
  allDevices: string;
  minimumVersion: string;
  minimumBuild: string;
  notes: string;
  scope: string;
}

const INT32_MAX = 2_147_483_647;

export function normalizeText(value: string | null | undefined) {
  return value?.trim() || null;
}

/** 保留当前策略绑定但已退出候选分页的版本，避免保存时误报 releaseRequired。 */
export function mergeHandheldPolicyCandidates(
  candidates: HandheldCandidate[],
  policies: Pick<HandheldPolicy, "lane" | "candidateId" | "candidate">[],
) {
  const merged = new Map(
    candidates.map((candidate) => [
      `${candidate.lane}:${candidate.id}`,
      candidate,
    ]),
  );
  for (const policy of policies) {
    const candidate = policy.candidate;
    if (
      candidate &&
      candidate.id === policy.candidateId &&
      candidate.lane === policy.lane
    ) {
      const key = `${candidate.lane}:${candidate.id}`;
      if (!merged.has(key)) merged.set(key, candidate);
    }
  }
  return [...merged.values()];
}

export function parseBuildNumber(value: string, required = false) {
  const normalized = value.trim();
  if (!normalized && !required) return null;
  if (!/^\d+$/.test(normalized)) return null;
  const parsed = Number(normalized);
  return Number.isInteger(parsed) && parsed >= 0 && parsed <= INT32_MAX
    ? parsed
    : null;
}

export function parseHandheldBuildNumber(value: string) {
  const normalized = value.trim();
  if (!/^[1-9]\d*$/.test(normalized)) return null;
  const parsed = Number(normalized);
  return Number.isInteger(parsed) && parsed > 0 && parsed <= INT32_MAX
    ? parsed
    : null;
}

function parseHandheldRegistrationBuildNumber(value: string) {
  const normalized = value.trim();
  if (!/^[1-9]\d*$/.test(normalized)) return null;
  const parsed = Number(normalized);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

export type PolicyValidationError =
  | "releaseRequired"
  | "minimumVersionRequired"
  | "minimumBuildInvalid"
  | "storesRequired"
  | "candidateBlocked";

export type RegistrationValidationError =
  | "appStoreIdInvalid"
  | "buildNumberInvalid"
  | "storefrontInvalid";

export function validateRegistration(
  app: "mobile-ios" | "pos-ipad" | "pos-handheld",
  value: { appStoreId: string; buildNumber: string; storefront: string },
): RegistrationValidationError | null {
  if (!/^\d{6,20}$/.test(value.appStoreId.trim())) return "appStoreIdInvalid";
  const build =
    app === "pos-handheld"
      ? parseHandheldRegistrationBuildNumber(value.buildNumber)
      : parseBuildNumber(value.buildNumber, true);
  if (build === null) return "buildNumberInvalid";
  if (!/^[A-Za-z]{2}$/.test(value.storefront.trim()))
    return "storefrontInvalid";
  return null;
}

export function validateNativePolicy(
  form: NativePolicyForm,
  targeted = false,
): PolicyValidationError | null {
  if (!form.enabled) return null;
  if (!form.releaseId.trim()) return "releaseRequired";
  if (
    form.minimumSupportedBuildNumber.trim() &&
    !form.minimumSupportedVersion.trim()
  )
    return "minimumVersionRequired";
  if (
    form.minimumSupportedBuildNumber.trim() &&
    parseBuildNumber(form.minimumSupportedBuildNumber) === null
  )
    return "minimumBuildInvalid";
  if (
    targeted &&
    form.targetScope === "stores" &&
    form.targetStoreGuids.length === 0
  )
    return "storesRequired";
  return null;
}

export function validateOtaPolicy(
  form: OtaPolicyForm,
): PolicyValidationError | null {
  if (!form.enabled) return null;
  if (!form.targetReleaseId.trim()) return "releaseRequired";
  return null;
}

export function validateTargetScope(
  targetScope: TargetScope | undefined,
  targetStoreGuids: string[] | undefined,
): PolicyValidationError | null {
  return targetScope === "stores" && (targetStoreGuids?.length ?? 0) === 0
    ? "storesRequired"
    : null;
}

export function validateHandheldCandidate(
  candidate: {
    id: string;
    activatable: boolean;
    blockedReason: string | null;
  } | null,
  candidateValid: boolean,
  policyBlockedReason: string | null = null,
): PolicyValidationError | null {
  if (!candidate || !candidate.id.trim()) return "releaseRequired";
  const blockedReason = policyBlockedReason ?? candidate.blockedReason;
  if (
    !candidateValid &&
    blockedReason !== "POS_HANDHELD_UPDATE_CANDIDATE_FINGERPRINT_MISMATCH"
  )
    return "candidateBlocked";
  if (!candidate.activatable) return "candidateBlocked";
  return null;
}

export function validateHandheldPolicy(
  form: NativePolicyForm,
  policyLane: string,
  candidate: {
    id: string;
    activatable: boolean;
    blockedReason: string | null;
  } | null,
  candidateValid: boolean,
  policyBlockedReason: string | null = null,
): PolicyValidationError | null {
  if (!form.enabled) return null;
  const nativeError = validateNativePolicy(form);
  if (nativeError) return nativeError;
  const candidateError = validateHandheldCandidate(
    candidate,
    candidateValid,
    policyBlockedReason,
  );
  if (candidateError) return candidateError;
  if (
    policyLane.endsWith("native") &&
    form.minimumSupportedBuildNumber.trim() &&
    parseHandheldBuildNumber(form.minimumSupportedBuildNumber) === null
  )
    return "minimumBuildInvalid";
  return null;
}

export function buildNativePolicyPayload(
  form: NativePolicyForm,
  expectedPolicyVersion: number,
  targeted = false,
) {
  if (!form.enabled) {
    return {
      expectedPolicyVersion,
      enabled: false,
      releaseId: null,
      minimumSupportedVersion: null,
      minimumSupportedBuildNumber: null,
      releaseMessage: null,
      ...(targeted
        ? { targetScope: "all" as const, targetStoreGuids: [] }
        : {}),
    };
  }
  const targetScope: TargetScope =
    targeted && form.targetScope === "stores" ? "stores" : "all";
  return {
    expectedPolicyVersion,
    enabled: true,
    releaseId: normalizeText(form.releaseId),
    minimumSupportedVersion: normalizeText(form.minimumSupportedVersion),
    minimumSupportedBuildNumber: parseBuildNumber(
      form.minimumSupportedBuildNumber,
    ),
    releaseMessage: normalizeText(form.releaseMessage),
    ...(targeted
      ? {
          targetScope,
          targetStoreGuids:
            targetScope === "stores" ? form.targetStoreGuids : [],
        }
      : {}),
  };
}

export function buildOtaPolicyPayload(
  form: OtaPolicyForm,
  expectedPolicyVersion: number,
) {
  return {
    expectedPolicyVersion,
    enabled: form.enabled,
    required: form.enabled && form.required,
    targetReleaseId: form.enabled ? normalizeText(form.targetReleaseId) : null,
    releaseMessage: form.enabled ? normalizeText(form.releaseMessage) : null,
  };
}

export function isPolicyConflict(error: unknown) {
  if (!error || typeof error !== "object") return false;
  const raw = error as {
    response?: { status?: number; data?: unknown };
    status?: number;
    code?: string;
    errorCode?: string;
  };
  const data = raw.response?.data as
    | { code?: unknown; errorCode?: unknown }
    | undefined;
  const code = String(
    raw.code ?? raw.errorCode ?? data?.code ?? data?.errorCode ?? "",
  );
  return (
    raw.response?.status === 409 ||
    raw.status === 409 ||
    code.includes("VERSION_CONFLICT")
  );
}

export function buildConfirmationSummary(
  form: NativePolicyForm | OtaPolicyForm,
  details: { releaseLabel: string; targetLabel?: string },
  labels: ConfirmationLabels,
) {
  if ("targetReleaseId" in form) {
    return [
      `${labels.enabled}：${form.enabled ? labels.enabled : labels.disabled}`,
      `${labels.release}：${form.enabled ? details.releaseLabel : labels.noRelease}`,
      `${labels.required}：${form.enabled && form.required ? labels.required : labels.optional}`,
      `${labels.notes}：${form.releaseMessage.trim() || labels.noRelease}`,
    ];
  }
  return [
    `${labels.enabled}：${form.enabled ? labels.enabled : labels.disabled}`,
    `${labels.release}：${form.enabled ? details.releaseLabel : labels.noRelease}`,
    `${labels.scope}：${details.targetLabel ?? labels.allDevices}`,
    `${labels.minimumVersion}：${form.minimumSupportedVersion.trim() || labels.noRelease}`,
    `${labels.minimumBuild}：${form.minimumSupportedBuildNumber.trim() || labels.noRelease}`,
    `${labels.notes}：${form.releaseMessage.trim() || labels.noRelease}`,
  ];
}
