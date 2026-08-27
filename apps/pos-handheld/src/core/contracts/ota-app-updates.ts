export type PosHandheldOtaUpdateState = "none" | "optional" | "required";

export const POS_HANDHELD_PRODUCTION_CHANNEL = "pos-handheld-production";

export type PosHandheldOtaUpdatePolicy =
  | Readonly<{
      state: "none";
      policyVersion: "none";
      appKey: "pos-handheld";
      projectName: string | null;
      platform: "iOS" | "Android";
      required: false;
      channel: string | null;
      runtimeVersion: string | null;
      updateId: null;
      updateGroupId: null;
      releaseMessage: null;
    }>
  | Readonly<{
      state: "optional" | "required";
      policyVersion: string;
      appKey: "pos-handheld";
      projectName: string;
      platform: "iOS" | "Android";
      required: boolean;
      channel: string;
      runtimeVersion: string;
      updateId: string;
      updateGroupId: string;
      releaseMessage: string | null;
    }>;

type AppUpdateCacheScopeBase = Readonly<{
  apiOrigin: string;
  storeCode: string;
  appKey: "pos-handheld";
  platform: "iOS" | "Android";
}>;

export type NativeAppUpdateCacheScope = AppUpdateCacheScopeBase &
  Readonly<{
    kind: "native";
    installedVersion: string;
    installedBuild: string;
  }>;

export type OtaAppUpdateCacheScope = AppUpdateCacheScopeBase &
  Readonly<{
    kind: "ota";
    projectId: string | null;
    projectName: string | null;
    configuredChannel: string | null;
    runtimeVersion: string;
    currentUpdateId: string | null;
    currentUpdateGroupId: string | null;
  }>;

/** 原生安装身份与 OTA 投放身份是两个显式、不可互换的缓存域。 */
export type AppUpdateCacheScope =
  | NativeAppUpdateCacheScope
  | OtaAppUpdateCacheScope;

export interface PosHandheldOtaUpdatePolicyStorePort {
  get(): Promise<PosHandheldOtaUpdatePolicy | null>;
  save(
    policy: PosHandheldOtaUpdatePolicy,
  ): Promise<PosHandheldOtaUpdatePolicy>;
}

export const POS_HANDHELD_OTA_NONE_POLICY: PosHandheldOtaUpdatePolicy =
  createPosHandheldOtaNonePolicy("iOS");

export function createPosHandheldOtaNonePolicy(
  platform: "iOS" | "Android",
): PosHandheldOtaUpdatePolicy {
  return Object.freeze({
    state: "none",
    policyVersion: "none",
    appKey: "pos-handheld",
    projectName: null,
    platform,
    required: false,
    channel: null,
    runtimeVersion: null,
    updateId: null,
    updateGroupId: null,
    releaseMessage: null,
  });
}

const OTA_POLICY_FIELDS = [
  "state",
  "policyVersion",
  "appKey",
  "projectName",
  "platform",
  "required",
  "channel",
  "runtimeVersion",
  "updateId",
  "updateGroupId",
  "releaseMessage",
] as const;
const RUNTIME_VERSION_MAX_LENGTH = 120;

/**
 * 策略只能选择签名 production 构建的 legacy channel，或由该 channel、真机平台
 * 派生的不可变 release channel；后台返回任意 channel 时必须失败关闭。
 */
export function isTrustedPosHandheldOtaChannel(
  channel: string | null,
  configuredChannel: string | null,
  platform: "iOS" | "Android",
): boolean {
  if (configuredChannel !== POS_HANDHELD_PRODUCTION_CHANNEL) return false;
  if (channel === configuredChannel) return true;
  if (channel === null) return false;
  const platformSegment = platform === "iOS" ? "ios" : "android";
  const prefix = `${configuredChannel}-${platformSegment}-release-`;
  const suffix = channel.startsWith(prefix)
    ? channel.slice(prefix.length)
    : "";
  return /^[a-z0-9][a-z0-9-]{0,63}$/u.test(suffix);
}

/**
 * OTA 协议与原生更新协议完全分离；所有服务端字段都必须显式出现，
 * 防止服务端扩展或 envelope 错位在客户端被静默接受。
 */
export function normalizePosHandheldOtaUpdatePolicy(
  input: unknown,
): PosHandheldOtaUpdatePolicy {
  if (!isRecord(input)) {
    throw new TypeError("Handheld OTA update policy must be an object.");
  }
  const allowed = new Set<string>(OTA_POLICY_FIELDS);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new TypeError(
      "Handheld OTA update policy contains an unsupported field.",
    );
  }
  if (OTA_POLICY_FIELDS.some((field) => !hasOwn(input, field))) {
    throw new TypeError(
      "Handheld OTA update policy must explicitly contain all fields.",
    );
  }
  if (
    input.state !== "none" &&
    input.state !== "optional" &&
    input.state !== "required"
  ) {
    throw new TypeError("Handheld OTA update policy state is invalid.");
  }
  if (input.appKey !== "pos-handheld") {
    throw new TypeError("Handheld OTA update appKey is invalid.");
  }
  if (input.platform !== "iOS" && input.platform !== "Android") {
    throw new TypeError("Handheld OTA update platform is invalid.");
  }
  const required = input.state === "required";
  if (input.required !== required) {
    throw new TypeError(
      "Handheld OTA update required flag does not match state.",
    );
  }

  if (input.state === "none") {
    if (
      input.policyVersion !== "none" ||
      input.updateId !== null ||
      input.updateGroupId !== null ||
      input.releaseMessage !== null
    ) {
      throw new TypeError("Handheld OTA none policy shape is invalid.");
    }
    return Object.freeze({
      state: "none",
      policyVersion: "none",
      appKey: "pos-handheld",
      projectName: optionalToken(input.projectName, "projectName", 128),
      platform: input.platform,
      required: false,
      channel: optionalToken(input.channel, "channel", 128),
      runtimeVersion: optionalToken(
        input.runtimeVersion,
        "runtimeVersion",
        RUNTIME_VERSION_MAX_LENGTH,
      ),
      updateId: null,
      updateGroupId: null,
      releaseMessage: null,
    });
  }

  const policyVersion = requiredToken(
    input.policyVersion,
    "policyVersion",
    128,
  );
  if (policyVersion === "none") {
    throw new TypeError(
      "Handheld OTA update policyVersion is invalid.",
    );
  }
  return Object.freeze({
    state: input.state,
    policyVersion,
    appKey: "pos-handheld",
    projectName: requiredToken(input.projectName, "projectName", 128),
    platform: input.platform,
    required,
    channel: requiredToken(input.channel, "channel", 128),
    runtimeVersion: requiredToken(
      input.runtimeVersion,
      "runtimeVersion",
      RUNTIME_VERSION_MAX_LENGTH,
    ),
    updateId: requiredToken(input.updateId, "updateId", 256),
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
  input: NativeAppUpdateCacheScope,
): NativeAppUpdateCacheScope;
export function normalizeAppUpdateCacheScope(
  input: OtaAppUpdateCacheScope,
): OtaAppUpdateCacheScope;
export function normalizeAppUpdateCacheScope(
  input: unknown,
): AppUpdateCacheScope;
export function normalizeAppUpdateCacheScope(
  input: unknown,
): AppUpdateCacheScope {
  if (!isRecord(input)) {
    throw new TypeError("Handheld update cache scope is invalid.");
  }
  if (input.kind === "native") {
    return normalizeNativeAppUpdateCacheScope(input);
  }
  if (input.kind === "ota") {
    return normalizeOtaAppUpdateCacheScope(input);
  }
  throw new TypeError("Handheld update cache scope is invalid.");
}

export function normalizeNativeAppUpdateCacheScope(
  input: unknown,
): NativeAppUpdateCacheScope {
  if (!isRecord(input)) {
    throw new TypeError("Handheld native update cache scope is invalid.");
  }
  const requiredFields = [
    "kind",
    "apiOrigin",
    "storeCode",
    "appKey",
    "platform",
    "installedVersion",
    "installedBuild",
  ] as const;
  const allowed = new Set<string>(requiredFields);
  if (
    Object.keys(input).some((key) => !allowed.has(key)) ||
    requiredFields.some((field) => !hasOwn(input, field))
  ) {
    throw new TypeError("Handheld native update cache scope is invalid.");
  }
  if (input.kind !== "native") {
    throw new TypeError("Handheld native update cache scope is invalid.");
  }
  return Object.freeze({
    kind: "native",
    ...normalizeCacheScopeBase(input),
    installedVersion: requiredToken(
      input.installedVersion,
      "installedVersion",
      64,
    ),
    installedBuild: requiredToken(
      input.installedBuild,
      "installedBuild",
      64,
    ),
  });
}

export function normalizeOtaAppUpdateCacheScope(
  input: unknown,
): OtaAppUpdateCacheScope {
  if (!isRecord(input)) {
    throw new TypeError("Handheld OTA update cache scope is invalid.");
  }
  const requiredFields = [
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
  const allowed = new Set<string>(requiredFields);
  if (
    Object.keys(input).some((key) => !allowed.has(key)) ||
    requiredFields.some((field) => !hasOwn(input, field)) ||
    input.kind !== "ota"
  ) {
    throw new TypeError("Handheld OTA update cache scope is invalid.");
  }
  return Object.freeze({
    kind: "ota",
    ...normalizeCacheScopeBase(input),
    projectId: optionalUuid(input.projectId, "projectId"),
    projectName: optionalToken(input.projectName, "projectName", 128),
    configuredChannel: optionalToken(
      input.configuredChannel,
      "configuredChannel",
      128,
    ),
    runtimeVersion: requiredToken(
      input.runtimeVersion,
      "runtimeVersion",
      RUNTIME_VERSION_MAX_LENGTH,
    ),
    currentUpdateId: optionalStableIdentifier(
      input.currentUpdateId,
      "currentUpdateId",
      256,
    ),
    currentUpdateGroupId: optionalUuid(
      input.currentUpdateGroupId,
      "currentUpdateGroupId",
    ),
  });
}

export function appUpdateCacheScopesEqual(
  left: AppUpdateCacheScope,
  right: AppUpdateCacheScope,
): boolean {
  if (
    left.kind !== right.kind ||
    left.apiOrigin !== right.apiOrigin ||
    left.storeCode !== right.storeCode ||
    left.appKey !== right.appKey ||
    left.platform !== right.platform
  ) {
    return false;
  }
  if (left.kind === "native" && right.kind === "native") {
    return (
      left.installedVersion === right.installedVersion &&
      left.installedBuild === right.installedBuild
    );
  }
  if (left.kind === "ota" && right.kind === "ota") {
    return (
      left.projectId === right.projectId &&
      left.projectName === right.projectName &&
      left.configuredChannel === right.configuredChannel &&
      left.runtimeVersion === right.runtimeVersion &&
      left.currentUpdateId === right.currentUpdateId &&
      left.currentUpdateGroupId === right.currentUpdateGroupId
    );
  }
  return false;
}

function normalizeCacheScopeBase(
  input: Record<string, unknown>,
): AppUpdateCacheScopeBase {
  let origin: URL;
  try {
    origin = new URL(requiredText(input.apiOrigin, "apiOrigin", 2_048));
  } catch {
    throw new TypeError("Handheld update cache apiOrigin is invalid.");
  }
  if (
    !["https:", "http:"].includes(origin.protocol) ||
    origin.username ||
    origin.password
  ) {
    throw new TypeError("Handheld update cache apiOrigin is invalid.");
  }
  if (input.appKey !== "pos-handheld") {
    throw new TypeError("Handheld update cache appKey is invalid.");
  }
  if (input.platform !== "iOS" && input.platform !== "Android") {
    throw new TypeError("Handheld update cache platform is invalid.");
  }
  return Object.freeze({
    apiOrigin: origin.origin,
    storeCode: requiredToken(input.storeCode, "storeCode", 64),
    appKey: "pos-handheld",
    platform: input.platform,
  });
}

function requiredUuid(value: unknown, field: string): string {
  const normalized = requiredText(value, field, 36).toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  return normalized;
}

function optionalUuid(value: unknown, field: string): string | null {
  return value === null ? null : requiredUuid(value, field);
}

function optionalStableIdentifier(
  value: unknown,
  field: string,
  maximum: number,
): string | null {
  const normalized = optionalToken(value, field, maximum);
  if (normalized === null) return null;
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
    normalized,
  )
    ? normalized.toLowerCase()
    : normalized;
}

function requiredToken(
  value: unknown,
  field: string,
  maximum: number,
): string {
  const normalized = requiredText(value, field, maximum);
  if (!/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)) {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
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

function optionalToken(
  value: unknown,
  field: string,
  maximum: number,
): string | null {
  if (value === null) return null;
  return requiredToken(value, field, maximum);
}

function requiredText(
  value: unknown,
  field: string,
  maximum: number,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  return normalized;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOwn(value: object, field: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, field);
}
