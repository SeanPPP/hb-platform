import type { MobileOtaUpdateContext } from "./mobile-ota-update";

export const MOBILE_OTA_UPDATE_CENTER_BASE_URL = "https://hotbargain.vip/api";

function normalizedText(value: unknown) {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}
function currentUpdateGroupId(manifest: unknown) {
  if (!manifest || typeof manifest !== "object" || Array.isArray(manifest)) {
    return null;
  }
  const record = manifest as Record<string, unknown>;
  const direct = normalizedText(record.updateGroupId);
  if (direct) return direct;
  const metadata = record.metadata;
  if (!metadata || typeof metadata !== "object" || Array.isArray(metadata)) {
    return null;
  }
  return normalizedText((metadata as Record<string, unknown>).updateGroupId);
}

export function resolveMobileOtaRuntimeContext(input: Readonly<{
  platform: unknown;
  channel: unknown;
  runtimeVersion: unknown;
  updateId: unknown;
  manifest?: unknown;
}>): MobileOtaUpdateContext {
  const platform = input.platform === "ios"
    ? "iOS"
    : input.platform === "android"
      ? "Android"
      : null;
  const clientChannel = input.channel === "production" || input.channel === "preview"
    ? input.channel
    : null;
  const runtimeVersion = normalizedText(input.runtimeVersion);
  if (!platform || !clientChannel || !runtimeVersion) {
    throw new Error("Mobile OTA runtime scope is invalid");
  }

  return Object.freeze({
    apiBaseUrl: MOBILE_OTA_UPDATE_CENTER_BASE_URL,
    appKey: "mobile",
    platform,
    clientChannel,
    runtimeVersion,
    currentUpdateId: normalizedText(input.updateId),
    currentUpdateGroupId: currentUpdateGroupId(input.manifest),
  });
}
