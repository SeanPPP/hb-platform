const {
  createRunOncePlugin,
  withAndroidManifest,
  withInfoPlist,
} = require("@expo/config-plugins");

const BLUETOOTH_USAGE = "HB POS 使用蓝牙连接芯烨小票打印机和钱箱。";
const ANDROID_PERMISSION_MATRIX = [
  {
    name: "android.permission.BLUETOOTH",
    attributes: { "android:maxSdkVersion": "30" },
  },
  {
    name: "android.permission.BLUETOOTH_ADMIN",
    attributes: { "android:maxSdkVersion": "30" },
  },
  {
    name: "android.permission.ACCESS_FINE_LOCATION",
    attributes: { "android:maxSdkVersion": "30" },
  },
  {
    name: "android.permission.BLUETOOTH_SCAN",
    attributes: { "android:usesPermissionFlags": "neverForLocation" },
  },
  {
    name: "android.permission.BLUETOOTH_CONNECT",
    attributes: {},
  },
];

/**
 * 本地 Expo Module 由 autolinking 编译；插件只声明前台扫描/连接权限。
 * Android 30 与 31+ 权限显式分界，不声明广播、后台定位或前台服务。
 */
function withHbPrinter(config) {
  config = withInfoPlist(config, (nextConfig) => {
    nextConfig.modResults.NSBluetoothAlwaysUsageDescription = BLUETOOTH_USAGE;
    return nextConfig;
  });

  config = withAndroidManifest(config, (nextConfig) => {
    const manifest = nextConfig.modResults.manifest;
    const permissions = manifest["uses-permission"] ?? [];
    for (const permission of ANDROID_PERMISSION_MATRIX) {
      const retained = permissions.filter(
        (entry) => entry?.$?.["android:name"] !== permission.name,
      );
      retained.push({
        $: {
          "android:name": permission.name,
          ...permission.attributes,
        },
      });
      permissions.splice(0, permissions.length, ...retained);
    }
    manifest["uses-permission"] = permissions;
    return nextConfig;
  });

  return config;
}

module.exports = createRunOncePlugin(withHbPrinter, "with-hb-printer", "0.2.0");
