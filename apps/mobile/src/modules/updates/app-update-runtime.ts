import * as Application from "expo-application";
import Constants from "expo-constants";
import * as Updates from "expo-updates";
import {
  resolveAppUpdateCheckAvailability,
  type AppUpdateCheckResult,
  type AppUpdateInfo,
} from "./app-update-info";

export function getCurrentAppUpdateInfo(): AppUpdateInfo {
  return {
    appVersion: Application.nativeApplicationVersion ?? Constants.expoConfig?.version ?? null,
    appBuildVersion: Application.nativeBuildVersion ?? null,
    runtimeVersion: Updates.runtimeVersion ?? null,
    channel: Updates.channel ?? null,
    updateId: Updates.updateId ?? null,
    isEmbeddedLaunch: Updates.isEmbeddedLaunch,
  };
}

export async function checkAndDownloadAppUpdate(): Promise<AppUpdateCheckResult> {
  // expo-updates 的手动检查 API 在开发模式会抛错；这里直接兜底成不可用状态。
  const availability = resolveAppUpdateCheckAvailability({
    isDev: __DEV__,
    isEnabled: Updates.isEnabled,
  });
  if (availability !== "available") {
    return { status: availability };
  }

  const update = await Updates.checkForUpdateAsync();
  if (!update.isAvailable) {
    return { status: "not-available" };
  }

  await Updates.fetchUpdateAsync();
  return { status: "downloaded" };
}

export async function reloadAppToApplyUpdate(): Promise<void> {
  // 更新包已下载后由用户确认重启，避免在扫码、保存等操作中突然重载 App。
  await Updates.reloadAsync();
}
