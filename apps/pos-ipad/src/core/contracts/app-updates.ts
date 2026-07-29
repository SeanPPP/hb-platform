export type PosIpadUpdatePolicy = Readonly<{
  enabled: boolean;
  minimumSupportedVersion: string | null;
  latestVersion: string | null;
  forceUpdate: boolean;
  appStoreUrl: string | null;
  releaseMessage: string | null;
}>;

export type NewTransactionGate = Readonly<{
  state: "enabled" | "disabled" | "force-update" | "unchecked";
  canStartNewTransaction: boolean;
  /** 同步、审计、支付恢复和支持导出永远不能被新交易门禁截断。 */
  canContinueRecovery: true;
}>;

export interface PosIpadUpdatePolicyStorePort {
  get(): Promise<PosIpadUpdatePolicy | null>;
  save(policy: PosIpadUpdatePolicy): Promise<PosIpadUpdatePolicy>;
}

export function deriveNewTransactionGate(
  policy: PosIpadUpdatePolicy | null,
): NewTransactionGate {
  const state =
    policy === null
      ? "unchecked"
      : policy.forceUpdate
        ? "force-update"
        : policy.enabled
          ? "enabled"
          : "disabled";
  return Object.freeze({
    state,
    canStartNewTransaction: state === "enabled",
    canContinueRecovery: true as const,
  });
}

export function normalizePosIpadUpdatePolicy(
  input: unknown,
): PosIpadUpdatePolicy {
  if (!isRecord(input)) {
    throw new TypeError("iPad update policy must be an object.");
  }
  const requiredFields = [
    "enabled",
    "minimumSupportedVersion",
    "latestVersion",
    "forceUpdate",
    "appStoreUrl",
    "releaseMessage",
  ] as const;
  const allowed = new Set<string>(requiredFields);
  if (Object.keys(input).some((key) => !allowed.has(key))) {
    throw new TypeError("iPad update policy contains an unsupported field.");
  }
  if (requiredFields.some((field) => !hasOwn(input, field))) {
    throw new TypeError("iPad update policy must explicitly contain all fields.");
  }
  if (
    typeof input.enabled !== "boolean" ||
    typeof input.forceUpdate !== "boolean"
  ) {
    throw new TypeError("iPad update policy booleans are invalid.");
  }
  return Object.freeze({
    enabled: input.enabled,
    minimumSupportedVersion: optionalVersion(
      input.minimumSupportedVersion,
      "minimum supported version",
    ),
    latestVersion: optionalVersion(input.latestVersion, "latest version"),
    forceUpdate: input.forceUpdate,
    appStoreUrl: optionalAppleAppStoreUrl(input.appStoreUrl),
    releaseMessage: optionalText(
      input.releaseMessage,
      "release message",
      1_000,
    ),
  });
}

function optionalAppleAppStoreUrl(value: unknown): string | null {
  const normalized = optionalText(value, "Apple App Store URL", 2_048);
  if (normalized === null) return null;
  let parsed: URL;
  try {
    parsed = new URL(normalized);
  } catch {
    throw new TypeError("Apple App Store URL is invalid.");
  }
  const allowedHost =
    parsed.hostname === "apps.apple.com" ||
    parsed.hostname === "itunes.apple.com";
  const allowedProtocol =
    parsed.protocol === "https:" || parsed.protocol === "itms-apps:";
  if (!allowedHost || !allowedProtocol || parsed.username || parsed.password) {
    throw new TypeError("Apple App Store URL is invalid.");
  }
  return parsed.toString();
}

function optionalVersion(value: unknown, label: string): string | null {
  const normalized = optionalText(value, label, 64);
  if (normalized === null) return null;
  if (!/^v?\d+(?:\.\d+){0,3}$/iu.test(normalized)) {
    throw new TypeError(`iPad update policy ${label} is invalid.`);
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
    throw new TypeError(`iPad update policy ${label} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`iPad update policy ${label} is invalid.`);
  }
  return normalized;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOwn(value: object, field: PropertyKey): boolean {
  return Object.prototype.hasOwnProperty.call(value, field);
}
