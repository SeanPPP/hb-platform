export type PosIpadOtaUpdateState = "none" | "optional" | "required";

export type PosIpadOtaUpdatePolicy =
  | Readonly<{
      state: "none";
      policyVersion: "none";
      channel: null;
      runtimeVersion: null;
      iosUpdateId: null;
      updateGroupId: null;
      releaseMessage: null;
    }>
  | Readonly<{
      state: "optional" | "required";
      policyVersion: string;
      channel: string;
      runtimeVersion: string;
      iosUpdateId: string;
      updateGroupId: string;
      releaseMessage: string | null;
    }>;

export type AppUpdateCacheScope = Readonly<{
  apiOrigin: string;
  storeCode: string;
  runtimeVersion: string;
  installedVersion: string;
}>;

export interface PosIpadOtaUpdatePolicyStorePort {
  get(): Promise<PosIpadOtaUpdatePolicy | null>;
  save(
    policy: PosIpadOtaUpdatePolicy,
  ): Promise<PosIpadOtaUpdatePolicy>;
}

export const POS_IPAD_OTA_NONE_POLICY: PosIpadOtaUpdatePolicy =
  Object.freeze({
    state: "none",
    policyVersion: "none",
    channel: null,
    runtimeVersion: null,
    iosUpdateId: null,
    updateGroupId: null,
    releaseMessage: null,
  });

const OTA_POLICY_FIELDS = [
  "state",
  "policyVersion",
  "channel",
  "runtimeVersion",
  "iosUpdateId",
  "updateGroupId",
  "releaseMessage",
] as const;
const RUNTIME_VERSION_MAX_LENGTH = 120;

/**
 * OTA 协议与原生 App Store 六字段协议完全分离；所有字段都必须显式出现，
 * 防止服务端扩展或 envelope 错位在客户端被静默接受。
 */
export function normalizePosIpadOtaUpdatePolicy(
  input: unknown,
): PosIpadOtaUpdatePolicy {
  if (!isRecord(input)) {
    throw new TypeError("iPad OTA update policy must be an object.");
  }
  const allowed = new Set<string>(OTA_POLICY_FIELDS);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new TypeError(
      "iPad OTA update policy contains an unsupported field.",
    );
  }
  if (OTA_POLICY_FIELDS.some((field) => !hasOwn(input, field))) {
    throw new TypeError(
      "iPad OTA update policy must explicitly contain all fields.",
    );
  }
  if (
    input.state !== "none" &&
    input.state !== "optional" &&
    input.state !== "required"
  ) {
    throw new TypeError("iPad OTA update policy state is invalid.");
  }

  if (input.state === "none") {
    if (
      input.policyVersion !== "none" ||
      input.channel !== null ||
      input.runtimeVersion !== null ||
      input.iosUpdateId !== null ||
      input.updateGroupId !== null ||
      input.releaseMessage !== null
    ) {
      throw new TypeError("iPad OTA none policy shape is invalid.");
    }
    return POS_IPAD_OTA_NONE_POLICY;
  }

  return Object.freeze({
    state: input.state,
    policyVersion: requiredToken(
      input.policyVersion,
      "policyVersion",
      128,
    ),
    channel: requiredToken(input.channel, "channel", 128),
    runtimeVersion: requiredToken(
      input.runtimeVersion,
      "runtimeVersion",
      RUNTIME_VERSION_MAX_LENGTH,
    ),
    iosUpdateId: requiredUuid(input.iosUpdateId, "iosUpdateId"),
    updateGroupId: requiredUuid(
      input.updateGroupId,
      "updateGroupId",
    ),
    releaseMessage: optionalText(
      input.releaseMessage,
      "releaseMessage",
      1_000,
    ),
  });
}

export function normalizeAppUpdateCacheScope(
  input: AppUpdateCacheScope,
): AppUpdateCacheScope {
  if (!isRecord(input)) {
    throw new TypeError("iPad update cache scope is invalid.");
  }
  const requiredFields = [
    "apiOrigin",
    "storeCode",
    "runtimeVersion",
    "installedVersion",
  ] as const;
  const allowed = new Set<string>(requiredFields);
  if (
    Object.keys(input).some((key) => !allowed.has(key)) ||
    requiredFields.some((field) => !hasOwn(input, field))
  ) {
    throw new TypeError("iPad update cache scope is invalid.");
  }
  let origin: URL;
  try {
    origin = new URL(requiredText(input.apiOrigin, "apiOrigin", 2_048));
  } catch {
    throw new TypeError("iPad update cache apiOrigin is invalid.");
  }
  if (
    !["https:", "http:"].includes(origin.protocol) ||
    origin.username ||
    origin.password
  ) {
    throw new TypeError("iPad update cache apiOrigin is invalid.");
  }
  return Object.freeze({
    apiOrigin: origin.origin,
    storeCode: requiredToken(input.storeCode, "storeCode", 64),
    runtimeVersion: requiredToken(
      input.runtimeVersion,
      "runtimeVersion",
      RUNTIME_VERSION_MAX_LENGTH,
    ),
    installedVersion: requiredToken(
      input.installedVersion,
      "installedVersion",
      64,
    ),
  });
}

export function appUpdateCacheScopesEqual(
  left: AppUpdateCacheScope,
  right: AppUpdateCacheScope,
): boolean {
  return (
    left.apiOrigin === right.apiOrigin &&
    left.storeCode === right.storeCode &&
    left.runtimeVersion === right.runtimeVersion &&
    left.installedVersion === right.installedVersion
  );
}

function requiredUuid(value: unknown, field: string): string {
  const normalized = requiredText(value, field, 36).toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  return normalized;
}

function requiredToken(
  value: unknown,
  field: string,
  maximum: number,
): string {
  const normalized = requiredText(value, field, maximum);
  if (!/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)) {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  return normalized;
}

function optionalText(
  value: unknown,
  field: string,
  maximum: number,
): string | null {
  if (value === null) return null;
  return requiredText(value, field, maximum);
}

function requiredText(
  value: unknown,
  field: string,
  maximum: number,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  return normalized;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOwn(value: object, field: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, field);
}
