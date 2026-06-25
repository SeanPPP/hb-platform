import appJson from "./app.json";

const baseExpoConfig = appJson.expo;
const nativeRuntimeVersion =
  process.env.EXPO_PUBLIC_RUNTIME_VERSION?.trim() || baseExpoConfig.runtimeVersion || baseExpoConfig.version;
const nativeAppBuildProfile =
  process.env.EXPO_PUBLIC_APP_BUILD_PROFILE?.trim() || process.env.EAS_BUILD_PROFILE?.trim() || "production";
const nativeInstallerFlag = process.env.EXPO_PUBLIC_NATIVE_APK_INSTALLER_ENABLED?.trim().toLowerCase();
const nativeAppInstallerEnabled = nativeInstallerFlag !== "0" && nativeInstallerFlag !== "false";

export default {
  expo: {
    ...baseExpoConfig,
    runtimeVersion: nativeRuntimeVersion,
    extra: {
      ...baseExpoConfig.extra,
      // 固化当前安装包的 EAS profile，自更新时按同一轨道检查 APK。
      nativeAppBuildProfile,
      // 旧 runtime 的 OTA 包关闭原生安装器，只用 Linking 提醒下载，避免加载新 native module。
      nativeAppInstallerEnabled,
      logCenter: {
        endpoint: process.env.EXPO_PUBLIC_LOG_CENTER_ENDPOINT?.trim() || "",
        // 只从本地环境变量读取日志中心密钥，示例配置也不要写入真实值。
        key: process.env.HB_LOG_CENTER_KEY?.trim() || "",
        environment:
          process.env.EXPO_PUBLIC_LOG_CENTER_ENVIRONMENT?.trim()
          || process.env.APP_ENV?.trim()
          || process.env.NODE_ENV?.trim()
          || "development",
        projectCode: process.env.EXPO_PUBLIC_LOG_CENTER_PROJECT?.trim() || "HbwebExpo",
        serviceName: process.env.EXPO_PUBLIC_LOG_CENTER_SERVICE?.trim() || "HbwebExpoApp",
        sourceType: "Mobile",
      },
    },
  },
};
