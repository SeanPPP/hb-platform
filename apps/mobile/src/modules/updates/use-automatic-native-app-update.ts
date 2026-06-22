import { useEffect, useRef } from "react";
import { Alert, AppState, Linking, Platform, type AppStateStatus } from "react-native";
import { i18n } from "@/shared/i18n/i18n";
import {
  checkAndDownloadNativeAppUpdate,
  checkLegacyNativeAppUpdate,
  type NativeAppBuildInfo,
} from "./native-app-update";

const APK_MIME_TYPE = "application/vnd.android.package-archive";
const FLAG_GRANT_READ_URI_PERMISSION = 1;

async function getExpoConstants() {
  return (await import("expo-constants")).default;
}

async function getNativeAppBuildProfile() {
  const Constants = await getExpoConstants();
  const value = Constants.expoConfig?.extra?.nativeAppBuildProfile;
  return typeof value === "string" && value.trim() ? value.trim() : "production";
}

async function getNativeAppInstallerEnabled() {
  const Constants = await getExpoConstants();
  const value = Constants.expoConfig?.extra?.nativeAppInstallerEnabled;
  return value !== false && value !== "false" && value !== "0";
}

async function getLegacyCurrentBuildVersion() {
  const Constants = await getExpoConstants();
  const nativeVersionCode = (Constants as { platform?: { android?: { versionCode?: number | string } } })
    .platform?.android?.versionCode;
  const configVersionCode = Constants.expoConfig?.android?.versionCode;
  const value = nativeVersionCode ?? configVersionCode;
  return value == null ? null : String(value);
}

function buildStableApkDownloadUrl(baseURL: string | undefined, profile: string) {
  if (!baseURL?.trim()) {
    return null;
  }

  try {
    const base = baseURL.endsWith("/") ? baseURL : `${baseURL}/`;
    const query = new URLSearchParams({ profile });
    return new URL(`mobile-app-builds/android-latest/download?${query.toString()}`, base).toString();
  } catch {
    return null;
  }
}

export function useAutomaticNativeAppUpdate(options: { enabled: boolean }) {
  const optionsRef = useRef(options);
  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const inFlightRef = useRef(false);
  const promptedBuildIdRef = useRef<string | null>(null);

  useEffect(() => {
    optionsRef.current = options;
  }, [options.enabled]);

  async function openDownloadedApk(fileUri: string) {
    try {
      const FileSystem = await import("expo-file-system/legacy");
      const IntentLauncher = await import("expo-intent-launcher");
      const contentUri = await FileSystem.getContentUriAsync(fileUri);

      await IntentLauncher.startActivityAsync("android.intent.action.VIEW", {
        data: contentUri,
        type: APK_MIME_TYPE,
        flags: FLAG_GRANT_READ_URI_PERMISSION,
      });
    } catch (error) {
      promptedBuildIdRef.current = null;
      console.warn("[updates] open APK installer failed", error);
      Alert.alert(
        i18n.t("settings:dialogs.nativeUpdateInstallFailedTitle"),
        i18n.t("settings:dialogs.nativeUpdateInstallFailedMessage"),
        [
          {
            text: i18n.t("settings:dialogs.nativeUpdateLaterAction"),
            style: "cancel",
          },
          {
            text: i18n.t("settings:dialogs.nativeUpdateOpenSettingsAction"),
            onPress: () => {
              void openUnknownSourceSettings().catch((settingsError) => {
                console.warn("[updates] open unknown app source settings failed", settingsError);
              });
            },
          },
        ]
      );
    }
  }

  async function openUnknownSourceSettings() {
    const [IntentLauncher, Application] = await Promise.all([
      import("expo-intent-launcher"),
      import("expo-application"),
    ]);
    const packageName = Application.applicationId ? `package:${Application.applicationId}` : undefined;
    await IntentLauncher.startActivityAsync(IntentLauncher.ActivityAction.MANAGE_UNKNOWN_APP_SOURCES, {
      data: packageName,
    });
  }

  function promptInstall(build: NativeAppBuildInfo, fileUri: string) {
    const versionText = [build.appVersion, build.appBuildVersion ? `(${build.appBuildVersion})` : null]
      .filter(Boolean)
      .join(" ");

    Alert.alert(
      i18n.t("settings:dialogs.nativeUpdateReadyTitle"),
      i18n.t("settings:dialogs.nativeUpdateReadyMessage", { version: versionText || build.easBuildId }),
      [
        {
          text: i18n.t("settings:dialogs.nativeUpdateLaterAction"),
          style: "cancel",
        },
        {
          text: i18n.t("settings:dialogs.nativeUpdateInstallAction"),
          onPress: () => {
            void openDownloadedApk(fileUri);
          },
        },
      ]
    );
  }

  function promptLegacyDownload(build: NativeAppBuildInfo, url: string) {
    const versionText = [build.appVersion, build.appBuildVersion ? `(${build.appBuildVersion})` : null]
      .filter(Boolean)
      .join(" ");

    Alert.alert(
      i18n.t("settings:dialogs.legacyNativeUpdateReadyTitle"),
      i18n.t("settings:dialogs.legacyNativeUpdateReadyMessage", { version: versionText || build.easBuildId }),
      [
        {
          text: i18n.t("settings:dialogs.nativeUpdateLaterAction"),
          style: "cancel",
        },
        {
          text: i18n.t("settings:dialogs.legacyNativeUpdateDownloadAction"),
          onPress: () => {
            void Linking.openURL(url).catch((error) => {
              promptedBuildIdRef.current = null;
              console.warn("[updates] open APK download link failed", error);
            });
          },
        },
      ]
    );
  }

  async function check(options: { enabled: boolean }) {
    if (!options.enabled || inFlightRef.current) {
      return;
    }

    inFlightRef.current = true;
    try {
      const { apiClient } = await import("@/shared/api/client");
      const buildProfile = await getNativeAppBuildProfile();
      const nativeInstallerEnabled = await getNativeAppInstallerEnabled();

      if (!nativeInstallerEnabled) {
        const currentBuildVersion = await getLegacyCurrentBuildVersion();
        const result = await checkLegacyNativeAppUpdate({
          apiClient,
          platform: Platform.OS,
          getCurrentBuildVersion: () => currentBuildVersion,
          getBuildProfile: () => buildProfile,
          getDownloadUrl: (build) => buildStableApkDownloadUrl(apiClient.defaults.baseURL, build.buildProfile || buildProfile),
        });

        if (result.status !== "available" || promptedBuildIdRef.current === result.build.easBuildId) {
          return;
        }

        // 旧 runtime 没有新原生模块，只提示用户跳转浏览器下载最新 APK。
        promptedBuildIdRef.current = result.build.easBuildId;
        promptLegacyDownload(result.build, result.url);
        return;
      }

      const [FileSystem, Application] = await Promise.all([
        import("expo-file-system/legacy"),
        import("expo-application"),
      ]);
      const result = await checkAndDownloadNativeAppUpdate({
        apiClient,
        platform: Platform.OS,
        getCurrentBuildVersion: () => Application.nativeBuildVersion,
        getBuildProfile: () => buildProfile,
        getDownloadDirectory: () => FileSystem.cacheDirectory ?? FileSystem.documentDirectory ?? null,
        getFileInfo: FileSystem.getInfoAsync,
        downloadFile: FileSystem.downloadAsync,
        deleteFile: (fileUri) => FileSystem.deleteAsync(fileUri, { idempotent: true }),
      });

      if (result.status !== "downloaded" || promptedBuildIdRef.current === result.build.easBuildId) {
        return;
      }

      // 同一个安装包一次运行只提示一次；下次打开 App 仍会继续提醒未安装的新包。
      promptedBuildIdRef.current = result.build.easBuildId;
      promptInstall(result.build, result.fileUri);
    } catch (error) {
      console.warn("[updates] automatic APK update check failed", error);
    } finally {
      inFlightRef.current = false;
    }
  }

  useEffect(() => {
    if (!options.enabled) {
      return;
    }

    // 登录态恢复之前也执行检查，保证未登录设备能收到重新下载安装提示。
    void check(options);
  }, [options.enabled]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      const previousState = appStateRef.current;
      appStateRef.current = nextState;
      if (previousState === "active" || nextState !== "active") {
        return;
      }

      void check(optionsRef.current);
    });

    return () => {
      subscription.remove();
    };
  }, []);
}
