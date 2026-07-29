import type { ConfigContext, ExpoConfig } from "expo/config";

const supportedInterfaceOrientations = [
  "UIInterfaceOrientationLandscapeLeft",
  "UIInterfaceOrientationLandscapeRight",
];
// Expo 配置在独立 Node 上下文中加载，无法直接解析应用源码的 TS 模块。
const defaultHbposApiBaseUrl = "https://hotbargain.vip/pos-api";
const localHbposApiBaseUrl = "http://192.168.31.246:5159";

function buildHbposApiConfiguration(): Readonly<{
  apiBaseUrl: string;
  trustedApiOrigins: readonly string[];
}> {
  const apiBaseUrl =
    process.env.EXPO_PUBLIC_HBPOS_API_URL?.trim() ||
    defaultHbposApiBaseUrl;
  const candidates = [
    apiBaseUrl,
    localHbposApiBaseUrl,
    ...(process.env.EXPO_PUBLIC_HBPOS_TRUSTED_API_ORIGINS ?? "")
      .split(",")
      .map((value) => value.trim())
      .filter(Boolean),
  ];
  return Object.freeze({
    apiBaseUrl,
    trustedApiOrigins: Object.freeze([
      ...new Set(candidates.map((value) => new URL(value).origin)),
    ]),
  });
}

export default ({ config }: ConfigContext): ExpoConfig => {
  const hbpos = buildHbposApiConfiguration();
  return ({
  ...config,
  name: "HB POS",
  slug: "hb-pos-ipad",
  version: "0.1.0",
  icon: "./assets/icon.png",
  scheme: "hbpos-ipad",
  platforms: ["ios"],
  orientation: "landscape",
  userInterfaceStyle: "light",
  newArchEnabled: true,
  runtimeVersion: {
    policy: "appVersion",
  },
  ios: {
    bundleIdentifier: "com.hbweb.posipad",
    buildNumber: "1",
    supportsTablet: true,
    requireFullScreen: true,
    infoPlist: {
      UIRequiresFullScreen: true,
      UISupportedInterfaceOrientations: supportedInterfaceOrientations,
      "UISupportedInterfaceOrientations~ipad": supportedInterfaceOrientations,
      NSBluetoothAlwaysUsageDescription: "HB POS 使用蓝牙连接门店小票打印机。",
      NSCameraUsageDescription: "HB POS 使用相机作为条码扫描备用方式。",
      NSLocalNetworkUsageDescription: "HB POS 需要访问受支持的门店支付终端。",
    },
  },
  plugins: [
    "expo-router",
    "expo-localization",
    [
      "expo-camera",
      {
        cameraPermission: "允许 HB POS 使用相机扫描商品条码。",
      },
    ],
    [
      "expo-secure-store",
      {
        configureAndroidBackup: false,
        faceIDPermission: "允许 HB POS 使用 Face ID 解锁安全凭据。",
      },
    ],
    [
      "expo-sqlite",
      {
        useSQLCipher: true,
        enableFTS: true,
      },
    ],
    [
      "expo-build-properties",
      {
        ios: {
          buildReactNativeFromSource: true,
          deploymentTarget: "17.0",
        },
      },
    ],
    "./plugins/with-rn-fmt-xcode26",
    [
      "./plugins/with-hb-printer",
      {
        backgroundBle: false,
      },
    ],
    "./plugins/with-hb-external-display",
  ],
  experiments: {
    typedRoutes: true,
  },
  extra: {
    hbpos: {
      apiBaseUrl: hbpos.apiBaseUrl,
      // 只允许签名构建中声明的 origin；持久化设置不能扩张凭据发送边界。
      trustedApiOrigins: hbpos.trustedApiOrigins,
      businessTimeZone:
        process.env.EXPO_PUBLIC_HBPOS_BUSINESS_TIME_ZONE?.trim() ??
        "Australia/Brisbane",
      deviceSystem: "iPadOS",
    },
    payments: {
      // 这里只允许公开的终端选择；provider token/secret 始终保留在 Hbpos.Api。
      provider:
        process.env.EXPO_PUBLIC_HBPOS_CARD_PROVIDER?.trim().toLowerCase() ??
        "",
      square: {
        environment:
          process.env.EXPO_PUBLIC_HBPOS_SQUARE_ENVIRONMENT?.trim() ?? "",
        deviceId:
          process.env.EXPO_PUBLIC_HBPOS_SQUARE_DEVICE_ID?.trim() ?? "",
        locationId:
          process.env.EXPO_PUBLIC_HBPOS_SQUARE_LOCATION_ID?.trim() ?? "",
      },
      linkly: {
        environment:
          process.env.EXPO_PUBLIC_HBPOS_LINKLY_ENVIRONMENT?.trim() ?? "",
      },
      voucher: {
        enabled:
          process.env.EXPO_PUBLIC_HBPOS_VOUCHER_ENABLED?.trim()
            .toLowerCase() === "true",
      },
    },
  },
  });
};
