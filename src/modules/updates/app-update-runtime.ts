import Constants from "expo-constants";
import * as Updates from "expo-updates";
import {
  resolveAppUpdateCheckAvailability,
  type AppUpdateCheckResult,
  type AppUpdateInfo,
} from "./app-update-info";

export function getCurrentAppUpdateInfo(): AppUpdateInfo {
  return {
    appVersion: Constants.expoConfig?.version ?? null,
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
