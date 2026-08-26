import { sanitizeText } from "../logging/application-log";

export type SentryRuntimeConfiguration = Readonly<{
  enabled: boolean;
  options: Readonly<{
    dsn: string | undefined;
    enabled: boolean;
    release: string;
    dist: string;
    environment: string;
    sendDefaultPii: false;
  }>;
}>;

const SENSITIVE_SENTRY_KEY =
  /authorization|token|password|passcode|pin|secret|apikey|credential|card|pan|cvv|voucher|cookie|header|barcode|lookupcode|orderid|cashier|userid|username|email|phone|address|ip(?:a)ddress|request|user|deviceid/iu;
const MAX_SENTRY_SCRUB_DEPTH = 12;

export function resolveSentryConfiguration(input: Readonly<{
  dsn: string | null | undefined;
  appIdentifier: string;
  appVersion: string;
  buildNumber: string | null | undefined;
  environment: string | null | undefined;
}>): SentryRuntimeConfiguration {
  const dsn = safeSentryDsn(input.dsn);
  const appIdentifier = safeReleaseToken(input.appIdentifier, "hbpos");
  const appVersion = safeReleaseToken(input.appVersion, "0.0.0");
  const dist = safeReleaseToken(input.buildNumber ?? "", "0");
  const environment = safeReleaseToken(
    input.environment ?? "",
    "development",
  );
  return Object.freeze({
    enabled: dsn !== undefined,
    options: Object.freeze({
      dsn,
      enabled: dsn !== undefined,
      release: appIdentifier + "@" + appVersion,
      dist,
      environment,
      sendDefaultPii: false,
    }),
  });
}

/**
 * Sentry SDK 的 sendDefaultPii=false 是第一道门；beforeSend 再按中心日志同一
 * sanitizeText 模式清洗自由文本，并 fail-closed 移除身份、请求和业务标识键。
 */
export function sanitizeSentryEvent<T>(event: T): T {
  return sanitizeSentryValue(event, "", 0, new WeakSet<object>()) as T;
}

function sanitizeSentryValue(
  value: unknown,
  key: string,
  depth: number,
  seen: WeakSet<object>,
): unknown {
  if (SENSITIVE_SENTRY_KEY.test(normalizeKey(key))) return "[REDACTED]";
  if (value === null || typeof value === "number" || typeof value === "boolean") {
    return value;
  }
  if (typeof value === "string") return sanitizeText(value, 8_000);
  if (typeof value !== "object") return undefined;
  if (depth >= MAX_SENTRY_SCRUB_DEPTH || seen.has(value)) {
    return "[REDACTED]";
  }
  seen.add(value);
  if (Array.isArray(value)) {
    const result = value.map((item) =>
      sanitizeSentryValue(item, "", depth + 1, seen),
    );
    seen.delete(value);
    return result;
  }

  const result: Record<string, unknown> = {};
  for (const [childKey, childValue] of Object.entries(value)) {
    const sanitized = sanitizeSentryValue(
      childValue,
      childKey,
      depth + 1,
      seen,
    );
    if (sanitized !== undefined) result[childKey] = sanitized;
  }
  seen.delete(value);
  return result;
}

function normalizeKey(value: string): string {
  return value.normalize("NFKC").replaceAll(/[^A-Za-z0-9]/gu, "");
}

function safeSentryDsn(
  value: string | null | undefined,
): string | undefined {
  if (!value?.trim()) return undefined;
  try {
    const url = new URL(value.trim());
    if (
      url.protocol !== "https:" ||
      !url.username ||
      url.password ||
      !url.hostname ||
      !url.pathname
    ) {
      return undefined;
    }
    return url.toString().replace(/\/$/u, "");
  } catch {
    return undefined;
  }
}

function safeReleaseToken(value: string, fallback: string): string {
  const trimmed = value.trim();
  return trimmed && /^[A-Za-z0-9][A-Za-z0-9._@/-]{0,119}$/u.test(trimmed)
    ? trimmed
    : fallback;
}
