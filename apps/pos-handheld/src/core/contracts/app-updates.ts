import type { DeviceSystem } from "./security";
import posHandheldIosIdentity from "./pos-handheld-ios-identity.json";

export const ANDROID_APK_MAX_SIZE_BYTES = 512 * 1_024 * 1_024;
export const POS_HANDHELD_IOS_BUNDLE_IDENTIFIER =
  posHandheldIosIdentity.bundleIdentifier;

export type PosHandheldUpdateState = "none" | "optional" | "required";
export type PosHandheldUpdateDistribution =
  | "apk"
  | "app-store"
  | "testflight"
  | null;

/**
 * 该结构与 pos-handheld backend 决策一一对应；Android 安装身份不得在
 * transport、缓存或编排层被裁剪成单一 URL。
 */
export type PosHandheldUpdatePolicy = Readonly<{
  state: PosHandheldUpdateState;
  policyVersion: string;
  platform: DeviceSystem;
  required: boolean;
  latestVersion: string | null;
  latestBuild: string | null;
  minimumSupportedVersion: string | null;
  distribution: PosHandheldUpdateDistribution;
  downloadUrl: string | null;
  fileSize: number | null;
  sha256: string | null;
  packageName: string | null;
  signingCertificateSha256: string | null;
  bundleIdentifier: string | null;
  appStoreId: string | null;
  releaseMessage: string | null;
}>;

export type NewTransactionGate = Readonly<{
  state:
    | "enabled"
    | "disabled"
    | "force-update"
    | "ota-update"
    | "unchecked";
  canStartNewTransaction: boolean;
  /** 同步、审计、支付恢复和支持导出永远不能被新交易门禁截断。 */
  canContinueRecovery: true;
}>;

export interface PosHandheldUpdatePolicyStorePort {
  get(): Promise<PosHandheldUpdatePolicy | null>;
  save(policy: PosHandheldUpdatePolicy): Promise<PosHandheldUpdatePolicy>;
}

export function deriveNewTransactionGate(
  policy: PosHandheldUpdatePolicy | null,
): NewTransactionGate {
  // 首次更新策略尚未完成时保持失败关闭；只有 backend 的 required 决策阻止新交易。
  const state =
    policy === null
      ? "unchecked"
      : policy.required
        ? "force-update"
        : "enabled";
  return Object.freeze({
    state,
    canStartNewTransaction: state === "enabled",
    canContinueRecovery: true as const,
  });
}

export function normalizePosHandheldUpdatePolicy(
  input: unknown,
): PosHandheldUpdatePolicy {
  if (!isRecord(input)) {
    throw new TypeError("Handheld update policy must be an object.");
  }
  const requiredFields = [
    "state",
    "policyVersion",
    "platform",
    "required",
    "latestVersion",
    "latestBuild",
    "minimumSupportedVersion",
    "distribution",
    "downloadUrl",
    "fileSize",
    "sha256",
    "packageName",
    "signingCertificateSha256",
    "bundleIdentifier",
    "appStoreId",
    "releaseMessage",
  ] as const;
  const allowed = new Set<string>(requiredFields);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new TypeError("Handheld update policy contains an unsupported field.");
  }
  if (requiredFields.some((field) => !hasOwn(input, field))) {
    throw new TypeError("Handheld update policy must explicitly contain all fields.");
  }

  const state = requiredState(input.state);
  const policyVersion = requiredPolicyVersion(input.policyVersion, state);
  const platform = requiredPlatform(input.platform);
  if (typeof input.required !== "boolean") {
    throw new TypeError("Handheld update policy required flag is invalid.");
  }
  const required = input.required;
  if (required !== (state === "required")) {
    throw new TypeError("Handheld update state and required flag do not match.");
  }

  const latestVersion = optionalVersion(input.latestVersion, "latest version");
  const latestBuild = optionalBuild(input.latestBuild);
  const minimumSupportedVersion = optionalVersion(
    input.minimumSupportedVersion,
    "minimum supported version",
  );
  const distribution = optionalDistribution(input.distribution);
  const downloadUrl = optionalText(input.downloadUrl, "download URL", 2_048);
  const fileSize = optionalFileSize(input.fileSize);
  const sha256 = optionalSha256(input.sha256, "APK SHA-256");
  const packageName = optionalPackageName(input.packageName);
  const signingCertificateSha256 = optionalSha256(
    input.signingCertificateSha256,
    "APK signing certificate SHA-256",
  );
  const bundleIdentifier = optionalBundleIdentifier(input.bundleIdentifier);
  const appStoreId = optionalAppStoreId(input.appStoreId);
  const releaseMessage = optionalText(
    input.releaseMessage,
    "release message",
    1_000,
  );

  if (state === "none") {
    if (
      policyVersion !== "none" ||
      latestVersion !== null ||
      latestBuild !== null ||
      minimumSupportedVersion !== null ||
      distribution !== null ||
      downloadUrl !== null ||
      fileSize !== null ||
      sha256 !== null ||
      packageName !== null ||
      signingCertificateSha256 !== null ||
      bundleIdentifier !== null ||
      appStoreId !== null ||
      releaseMessage !== null
    ) {
      throw new TypeError("Handheld none decision must not contain update metadata.");
    }
  } else if (platform === "Android") {
    if (
      latestVersion === null ||
      latestBuild === null ||
      distribution !== "apk" ||
      !isTrustedHttpsUrl(downloadUrl) ||
      fileSize === null ||
      sha256 === null ||
      packageName === null ||
      signingCertificateSha256 === null ||
      bundleIdentifier !== null ||
      appStoreId !== null
    ) {
      throw new TypeError("Android APK update metadata is incomplete or invalid.");
    }
  } else if (
    latestVersion === null ||
    latestBuild === null ||
    (distribution !== "app-store" && distribution !== "testflight") ||
    fileSize !== null ||
    sha256 !== null ||
    packageName !== null ||
    signingCertificateSha256 !== null ||
    bundleIdentifier !== POS_HANDHELD_IOS_BUNDLE_IDENTIFIER ||
    appStoreId === null ||
    !isTrustedIosUrl(downloadUrl, distribution, appStoreId) ||
    (distribution === "testflight" && required)
  ) {
    throw new TypeError("iOS update metadata is incomplete or invalid.");
  }

  return Object.freeze({
    state,
    policyVersion,
    platform,
    required,
    latestVersion,
    latestBuild,
    minimumSupportedVersion,
    distribution,
    downloadUrl: downloadUrl === null ? null : new URL(downloadUrl).toString(),
    fileSize,
    sha256,
    packageName,
    signingCertificateSha256,
    bundleIdentifier,
    appStoreId,
    releaseMessage,
  });
}

function requiredState(value: unknown): PosHandheldUpdateState {
  if (value !== "none" && value !== "optional" && value !== "required") {
    throw new TypeError("Handheld update state is invalid.");
  }
  return value;
}

function requiredPolicyVersion(
  value: unknown,
  state: PosHandheldUpdateState,
): string {
  const normalized = requiredToken(value, "policy version", 120);
  if ((state === "none") !== (normalized === "none")) {
    throw new TypeError("Handheld update policy version does not match state.");
  }
  return normalized;
}

function requiredPlatform(value: unknown): DeviceSystem {
  if (value !== "iOS" && value !== "Android") {
    throw new TypeError("Handheld update platform is invalid.");
  }
  return value;
}

function optionalDistribution(value: unknown): PosHandheldUpdateDistribution {
  if (value === null) return null;
  if (value !== "apk" && value !== "app-store" && value !== "testflight") {
    throw new TypeError("Handheld update distribution is invalid.");
  }
  return value;
}

function optionalFileSize(value: unknown): number | null {
  if (value === null) return null;
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value <= 0 ||
    value > ANDROID_APK_MAX_SIZE_BYTES
  ) {
    throw new TypeError("Android APK file size is invalid.");
  }
  return value;
}

function optionalBuild(value: unknown): string | null {
  const normalized = optionalText(value, "latest build", 16);
  if (normalized === null) return null;
  if (!/^[1-9]\d{0,15}$/u.test(normalized)) {
    throw new TypeError("Handheld update latest build is invalid.");
  }
  const numeric = Number(normalized);
  if (!Number.isSafeInteger(numeric)) {
    throw new TypeError("Handheld update latest build is invalid.");
  }
  return normalized;
}

function optionalSha256(value: unknown, label: string): string | null {
  const normalized = optionalText(value, label, 64);
  if (normalized === null) return null;
  const lowercase = normalized.toLowerCase();
  if (!/^[a-f0-9]{64}$/u.test(lowercase)) {
    throw new TypeError(`Handheld update ${label} is invalid.`);
  }
  return lowercase;
}

function optionalPackageName(value: unknown): string | null {
  const normalized = optionalText(value, "Android package name", 255);
  if (normalized === null) return null;
  if (!/^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$/u.test(normalized)) {
    throw new TypeError("Handheld update Android package name is invalid.");
  }
  return normalized;
}

function optionalBundleIdentifier(value: unknown): string | null {
  const normalized = optionalText(value, "iOS bundle identifier", 255);
  if (normalized === null) return null;
  if (!/^[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$/u.test(normalized)) {
    throw new TypeError("Handheld update iOS bundle identifier is invalid.");
  }
  return normalized;
}

function optionalAppStoreId(value: unknown): string | null {
  const normalized = optionalText(value, "App Store ID", 20);
  if (normalized === null) return null;
  if (!/^\d{5,20}$/u.test(normalized)) {
    throw new TypeError("Handheld update App Store ID is invalid.");
  }
  return normalized;
}

function isTrustedHttpsUrl(value: string | null): value is string {
  if (value === null) return false;
  try {
    const parsed = new URL(value);
    return (
      parsed.protocol === "https:" &&
      !parsed.username &&
      !parsed.password &&
      !parsed.hash
    );
  } catch {
    return false;
  }
}

function isTrustedIosUrl(
  value: string | null,
  distribution: "app-store" | "testflight",
  appStoreId: string,
): value is string {
  if (!isTrustedHttpsUrl(value)) return false;
  const parsed = new URL(value);
  if (parsed.port) return false;

  const hostname = parsed.hostname.toLowerCase();
  const pathSegments = parsed.pathname.split("/").filter(Boolean);
  if (distribution === "testflight") {
    const joinCode = pathSegments[1];
    return (
      hostname === "testflight.apple.com" &&
      !parsed.search &&
      pathSegments.length === 2 &&
      pathSegments[0] === "join" &&
      joinCode !== undefined &&
      /^[A-Za-z0-9]{4,64}$/u.test(joinCode) &&
      parsed.pathname === `/join/${joinCode}`
    );
  }

  return (
    (hostname === "apps.apple.com" || hostname === "itunes.apple.com") &&
    pathSegments[pathSegments.length - 1] === `id${appStoreId}`
  );
}

function optionalVersion(value: unknown, label: string): string | null {
  const normalized = optionalText(value, label, 64);
  if (normalized === null) return null;
  if (!/^v?\d+(?:\.\d+){0,3}$/iu.test(normalized)) {
    throw new TypeError(`Handheld update policy ${label} is invalid.`);
  }
  return normalized;
}

function requiredToken(value: unknown, label: string, maximum: number): string {
  const normalized = optionalText(value, label, maximum);
  if (normalized === null || !/^[A-Za-z0-9][A-Za-z0-9._:-]*$/u.test(normalized)) {
    throw new TypeError(`Handheld update ${label} is invalid.`);
  }
  return normalized;
}

function optionalText(
  value: unknown,
  label: string,
  maximum: number,
): string | null {
  if (value === null) return null;
  if (typeof value !== "string") {
    throw new TypeError(`Handheld update policy ${label} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`Handheld update policy ${label} is invalid.`);
  }
  return normalized;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOwn(value: object, field: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, field);
}
