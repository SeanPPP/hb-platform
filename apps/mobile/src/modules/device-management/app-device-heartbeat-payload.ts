import type { PersistedDeviceSession } from "@/modules/device/types";
import type { AppUpdateInfo } from "@/modules/updates/app-update-info";
import type { AppDeviceHeartbeatPayload } from "@/modules/device-management/types";

export interface NativeApplicationVersionInfo {
  nativeApplicationVersion?: string | null;
  nativeBuildVersion?: string | null;
}

interface BuildAppDeviceHeartbeatPayloadInput {
  installationId: string;
  platformOS: string;
  session?: PersistedDeviceSession | null;
  updateInfo?: Partial<AppUpdateInfo> | null;
  applicationInfo?: NativeApplicationVersionInfo | null;
}

function normalizeText(value: unknown): string | undefined {
  if (typeof value !== "string") {
    return undefined;
  }

  const trimmed = value.trim();
  return trimmed || undefined;
}

function normalizeDeviceSystem(platformOS: string) {
  return platformOS === "ios" ? "iOS" : platformOS === "android" ? "Android" : platformOS;
}

function normalizePlatform(platformOS: string) {
  return platformOS === "ios" ? "ios" : platformOS === "android" ? "android" : platformOS;
}

function resolveUpdateSource(updateInfo?: Partial<AppUpdateInfo> | null) {
  if (normalizeText(updateInfo?.updateId)) {
    return "ota";
  }

  if (updateInfo?.isEmbeddedLaunch === true) {
    return "embedded";
  }

  return "unknown";
}

export function buildAppDeviceHeartbeatPayload({
  installationId,
  platformOS,
  session,
  updateInfo,
  applicationInfo,
}: BuildAppDeviceHeartbeatPayloadInput): AppDeviceHeartbeatPayload {
  const hardwareId = normalizeText(session?.hardwareId) ?? normalizeText(installationId) ?? "";

  // 关键逻辑：用户信息不进入 payload，后端只从 Bearer token 或设备会话解析最近登录用户。
  return {
    hardwareId,
    systemDeviceNumber: normalizeText(session?.systemDeviceNumber),
    deviceSystem: normalizeDeviceSystem(platformOS),
    platform: normalizePlatform(platformOS),
    storeCode: normalizeText(session?.storeCode),
    appVersion:
      normalizeText(applicationInfo?.nativeApplicationVersion) ??
      normalizeText(updateInfo?.appVersion),
    appBuildVersion: normalizeText(applicationInfo?.nativeBuildVersion),
    runtimeVersion: normalizeText(updateInfo?.runtimeVersion),
    channel: normalizeText(updateInfo?.channel),
    updateId: normalizeText(updateInfo?.updateId),
    updateSource: resolveUpdateSource(updateInfo),
  };
}
