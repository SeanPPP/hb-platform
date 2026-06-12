import appJson from "./app.json";

const baseExpoConfig = appJson.expo;

export default {
  expo: {
    ...baseExpoConfig,
    extra: {
      ...baseExpoConfig.extra,
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
