import type {
  MobileOtaClientChannel,
  MobileOtaUpdateContext,
} from "./mobile-ota-update";

export const MOBILE_OTA_UPDATE_CENTER_BASE_URL = "https://hotbargain.vip/api";

export type MobileOtaRuntimeContext = MobileOtaUpdateContext & Readonly<{
  updateChannel: string;
}>;

const IMMUTABLE_RELEASE_CHANNEL_PATTERN =
  /^mobile-(production|preview)-(android|ios)-release-[a-z0-9][a-z0-9-]*$/;

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

function resolveClientChannel(
  platform: "android" | "ios" | null,
  updateChannel: string | null,
): MobileOtaClientChannel | null {
  if (!platform || !updateChannel) return null;
  if (updateChannel === "production" || updateChannel === "preview") {
    return updateChannel;
  }
  const match = IMMUTABLE_RELEASE_CHANNEL_PATTERN.exec(updateChannel);
  if (!match || match[2] !== platform) return null;
  return match[1] as MobileOtaClientChannel;
}

export function resolveMobileOtaRuntimeContext(input: Readonly<{
  platform: unknown;
  channel: unknown;
  runtimeVersion: unknown;
  updateId: unknown;
  manifest?: unknown;
}>): MobileOtaRuntimeContext {
  const nativePlatform = input.platform === "ios"
    ? "ios"
    : input.platform === "android"
      ? "android"
      : null;
  const platform = nativePlatform === "ios"
    ? "iOS"
    : nativePlatform === "android"
      ? "Android"
      : null;
  const updateChannel = normalizedText(input.channel);
  const clientChannel = resolveClientChannel(nativePlatform, updateChannel);
  const runtimeVersion = normalizedText(input.runtimeVersion);
  if (!platform || !clientChannel || !updateChannel || !runtimeVersion) {
    throw new Error("Mobile OTA runtime scope is invalid");
  }

  return Object.freeze({
    apiBaseUrl: MOBILE_OTA_UPDATE_CENTER_BASE_URL,
    appKey: "mobile",
    platform,
    clientChannel,
    updateChannel,
    runtimeVersion,
    currentUpdateId: normalizedText(input.updateId),
    currentUpdateGroupId: currentUpdateGroupId(input.manifest),
  });
}
