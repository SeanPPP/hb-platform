import {
  appUpdateCacheScopesEqual,
  normalizeAppUpdateCacheScope,
  type AppUpdateCacheScope,
} from "../contracts/ota-app-updates";

export type StoredAppUpdateCacheScope = AppUpdateCacheScope &
  Readonly<{ policyVersion: string }>;

const CACHE_IDENTITY_PATTERN_VERSION = "scope-v2";

export function createAppUpdateCacheKey(
  prefix: string,
  scope: AppUpdateCacheScope,
  policyVersion?: string,
): string {
  const normalized = normalizeAppUpdateCacheScope(scope);
  const parts = normalized.kind === "native"
    ? [
        CACHE_IDENTITY_PATTERN_VERSION,
        normalized.kind,
        normalized.apiOrigin,
        normalized.storeCode,
        normalized.appKey,
        normalized.platform,
        normalized.installedVersion,
        normalized.installedBuild,
      ]
    : [
        CACHE_IDENTITY_PATTERN_VERSION,
        normalized.kind,
        normalized.apiOrigin,
        normalized.storeCode,
        normalized.appKey,
        nullableKeyPart(normalized.projectId),
        nullableKeyPart(normalized.projectName),
        normalized.platform,
        nullableKeyPart(normalized.configuredChannel),
        normalized.runtimeVersion,
        nullableKeyPart(normalized.currentUpdateId),
        nullableKeyPart(normalized.currentUpdateGroupId),
      ];
  if (policyVersion !== undefined) {
    parts.push(normalizePolicyVersion(policyVersion));
  }
  return `${prefix}:${parts.map(encodeURIComponent).join(":")}`;
}

function nullableKeyPart(value: string | null): string {
  return value === null ? "nullable:null" : `nullable:value:${value}`;
}

export function createStoredAppUpdateCacheScope(
  scope: AppUpdateCacheScope,
  policyVersion: string,
): StoredAppUpdateCacheScope {
  return Object.freeze({
    ...normalizeAppUpdateCacheScope(scope),
    policyVersion: normalizePolicyVersion(policyVersion),
  });
}

export function matchesStoredAppUpdateCacheScope(
  input: unknown,
  expected: StoredAppUpdateCacheScope,
): boolean {
  if (!isRecord(input)) return false;
  const scopeFields = expected.kind === "native"
    ? [
        "kind",
        "apiOrigin",
        "storeCode",
        "appKey",
        "platform",
        "installedVersion",
        "installedBuild",
      ] as const
    : [
        "kind",
        "apiOrigin",
        "storeCode",
        "appKey",
        "projectId",
        "projectName",
        "platform",
        "configuredChannel",
        "runtimeVersion",
        "currentUpdateId",
        "currentUpdateGroupId",
      ] as const;
  const fields = [...scopeFields, "policyVersion"];
  if (
    Object.keys(input).length !== fields.length ||
    fields.some((field) => !hasOwn(input, field))
  ) {
    return false;
  }
  try {
    const { policyVersion, ...scopeInput } = input;
    const actual = normalizeAppUpdateCacheScope(scopeInput);
    return (
      appUpdateCacheScopesEqual(actual, expected) &&
      normalizePolicyVersion(policyVersion) ===
        expected.policyVersion
    );
  } catch {
    return false;
  }
}

export function normalizePolicyVersion(value: unknown): string {
  if (typeof value !== "string") {
    throw new TypeError("Handheld update cache policyVersion is invalid.");
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 128 ||
    !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)
  ) {
    throw new TypeError("Handheld update cache policyVersion is invalid.");
  }
  return normalized;
}

export function isExactRecord(
  input: unknown,
  fields: readonly string[],
): input is Record<string, unknown> {
  if (!isRecord(input) || Object.keys(input).length !== fields.length) {
    return false;
  }
  return fields.every((field) => hasOwn(input, field));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOwn(value: object, field: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, field);
}
