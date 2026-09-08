import type {
  WpfPolicySummary,
  WpfRelease,
  WpfReleasePolicyRequest,
  WpfTargetScope,
} from "./types";

export const WPF_RELEASE_CHANNELS = ["production", "preview"] as const;

export function normalizeTargetScope(
  value: string | null | undefined,
): WpfTargetScope {
  const normalized = value?.trim().toLowerCase();
  return normalized === "stores" || normalized === "devices"
    ? normalized
    : "all";
}

export function normalizeVersion(value: string) {
  return value.trim().replace(/^v/i, "");
}

function parseVersion(value: string) {
  const match = normalizeVersion(value).match(
    /^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$/,
  );
  return match ? match.slice(1).map((part) => Number(part ?? 0)) : null;
}

export function compareVersions(left: string, right: string) {
  const a = parseVersion(left);
  const b = parseVersion(right);
  if (!a || !b) return null;
  for (let index = 0; index < Math.max(a.length, b.length); index += 1) {
    const difference = (a[index] ?? 0) - (b[index] ?? 0);
    if (difference !== 0) return difference;
  }
  return 0;
}

export function getPolicyValidationError(
  input: Pick<
    WpfReleasePolicyRequest,
    | "targetVersion"
    | "minimumSupportedVersion"
    | "targetScope"
    | "targetStoreGuids"
    | "targetDeviceRegistrationIds"
  > & { activeVersions?: string[] },
) {
  const targetVersion = normalizeVersion(input.targetVersion);
  const minimumVersion = normalizeVersion(input.minimumSupportedVersion);
  if (!targetVersion || !minimumVersion) return "requiredVersions" as const;
  if (!parseVersion(targetVersion) || !parseVersion(minimumVersion))
    return "invalidVersion" as const;
  if (input.activeVersions && !input.activeVersions.includes(targetVersion))
    return "targetVersionUnavailable" as const;
  if (input.activeVersions && !input.activeVersions.includes(minimumVersion))
    return "minimumVersionUnavailable" as const;
  const range = compareVersions(minimumVersion, targetVersion);
  if (range !== null && range > 0) return "minimumAboveTarget" as const;
  const targetScope = normalizeTargetScope(input.targetScope);
  if (
    targetScope === "stores" &&
    input.targetStoreGuids.filter((value) => value.trim()).length === 0
  )
    return "storesRequired" as const;
  if (
    targetScope === "devices" &&
    input.targetDeviceRegistrationIds.filter(
      (value) => Number.isInteger(value) && value > 0,
    ).length === 0
  )
    return "devicesRequired" as const;
  return null;
}

export function canSavePolicy(
  input: Pick<
    WpfReleasePolicyRequest,
    | "targetVersion"
    | "minimumSupportedVersion"
    | "targetScope"
    | "targetStoreGuids"
    | "targetDeviceRegistrationIds"
  > & { activeVersions?: string[] },
) {
  return getPolicyValidationError(input) === null;
}

export function policySummaryMatchesRequest(
  request: WpfReleasePolicyRequest,
  summary: WpfPolicySummary | null,
) {
  if (!summary) return false;
  const requestStores =
    request.targetScope === "stores"
      ? [...new Set(request.targetStoreGuids)].sort()
      : [];
  const summaryStores =
    summary.targetScope === "stores"
      ? [...new Set(summary.targetStoreGuids)].sort()
      : [];
  const requestDevices =
    request.targetScope === "devices"
      ? [...new Set(request.targetDeviceRegistrationIds)].sort((a, b) => a - b)
      : [];
  const summaryDevices =
    summary.targetScope === "devices"
      ? [...new Set(summary.targetDeviceRegistrationIds)].sort((a, b) => a - b)
      : [];
  return (
    request.channel.trim().toLowerCase() ===
      summary.channel.trim().toLowerCase() &&
    normalizeVersion(request.targetVersion) ===
      normalizeVersion(summary.targetVersion) &&
    normalizeVersion(request.minimumSupportedVersion) ===
      normalizeVersion(summary.minimumSupportedVersion) &&
    Boolean(request.forceUpdate) === Boolean(summary.forceUpdate) &&
    normalizeTargetScope(request.targetScope) ===
      normalizeTargetScope(summary.targetScope) &&
    JSON.stringify(requestStores) === JSON.stringify(summaryStores) &&
    JSON.stringify(requestDevices) === JSON.stringify(summaryDevices)
  );
}

export function inferRollback(
  targetVersion: string,
  currentTargetVersion: string | null | undefined,
) {
  if (!currentTargetVersion) return false;
  const comparison = compareVersions(
    normalizeVersion(targetVersion),
    normalizeVersion(currentTargetVersion),
  );
  return comparison !== null && comparison < 0;
}

export function getPolicySummary(
  releases: WpfRelease[],
): WpfPolicySummary | null {
  const carrier =
    releases.find((item) => item.targetVersion?.trim()) ??
    releases.find((item) => item.isCurrent);
  if (!carrier) return null;
  const targetVersion = carrier.targetVersion?.trim() || carrier.version.trim();
  if (!targetVersion) return null;
  const scope = normalizeTargetScope(carrier.targetScope);
  const targetStoreGuids = scope === "stores" ? carrier.targetStoreGuids : [];
  const targetDeviceRegistrationIds =
    scope === "devices" ? carrier.targetDeviceRegistrationIds : [];
  const currentRelease = releases.find(
    (item) => item.isCurrent || item.version.trim() === targetVersion,
  );
  return {
    channel: currentRelease?.channel ?? carrier.channel,
    targetVersion,
    minimumSupportedVersion:
      carrier.minimumSupportedVersion?.trim() || targetVersion,
    forceUpdate: Boolean(carrier.forceUpdate || currentRelease?.forceUpdate),
    targetScope: scope,
    targetStoreGuids,
    targetDeviceRegistrationIds,
    targetStoreSummaries:
      scope === "stores" ? carrier.targetStoreSummaries : [],
    targetDeviceSummaries:
      scope === "devices" ? carrier.targetDeviceSummaries : [],
    policyUpdatedAt: carrier.policyUpdatedAt,
    policyUpdatedBy: carrier.policyUpdatedBy,
  };
}

export function createLatestRequestGuard() {
  let latest = 0;
  return {
    next() {
      latest += 1;
      return latest;
    },
    invalidate() {
      latest += 1;
    },
    isCurrent(requestId: number) {
      return requestId === latest;
    },
  };
}

export function maskSha256(value: string | null) {
  if (!value) return "-";
  const trimmed = value.trim();
  return trimmed.length > 18
    ? `${trimmed.slice(0, 12)}…${trimmed.slice(-6)}`
    : trimmed;
}

export function formatFileSize(value: number | null) {
  if (!value || value <= 0) return "-";
  if (value >= 1024 * 1024 * 1024)
    return `${(value / 1024 / 1024 / 1024).toFixed(2)} GB`;
  if (value >= 1024 * 1024) return `${(value / 1024 / 1024).toFixed(2)} MB`;
  return `${(value / 1024).toFixed(1)} KB`;
}

export function getErrorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message.trim()
    ? error.message
    : fallback;
}
