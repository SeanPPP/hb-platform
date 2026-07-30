import {
  appUpdateCacheScopesEqual,
  normalizeAppUpdateCacheScope,
  type AppUpdateCacheScope,
} from "../contracts/ota-app-updates";

export type StoredAppUpdateCacheScope = AppUpdateCacheScope &
  Readonly<{ policyVersion: string }>;

export function createAppUpdateCacheKey(
  prefix: string,
  scope: AppUpdateCacheScope,
  policyVersion?: string,
): string {
  const normalized = normalizeAppUpdateCacheScope(scope);
  const parts = [
    normalized.apiOrigin,
    normalized.storeCode,
    normalized.runtimeVersion,
    normalized.installedVersion,
    ...(policyVersion ? [policyVersion] : []),
  ];
  return `${prefix}:${parts.map(encodeURIComponent).join(":")}`;
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
  const fields = [
    "apiOrigin",
    "storeCode",
    "runtimeVersion",
    "installedVersion",
    "policyVersion",
  ] as const;
  if (
    Object.keys(input).length !== fields.length ||
    fields.some((field) => !hasOwn(input, field))
  ) {
    return false;
  }
  try {
    const actual = normalizeAppUpdateCacheScope({
      apiOrigin: String(input.apiOrigin),
      storeCode: String(input.storeCode),
      runtimeVersion: String(input.runtimeVersion),
      installedVersion: String(input.installedVersion),
    });
    return (
      appUpdateCacheScopesEqual(actual, expected) &&
      normalizePolicyVersion(input.policyVersion) ===
        expected.policyVersion
    );
  } catch {
    return false;
  }
}

export function normalizePolicyVersion(value: unknown): string {
  if (typeof value !== "string") {
    throw new TypeError("iPad update cache policyVersion is invalid.");
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 128 ||
    !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)
  ) {
    throw new TypeError("iPad update cache policyVersion is invalid.");
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
