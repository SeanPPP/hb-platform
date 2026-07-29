const { createRunOncePlugin, withInfoPlist } = require("@expo/config-plugins");

const BLUETOOTH_USAGE = "HB POS 使用蓝牙连接芯烨小票打印机和钱箱。";

/**
 * 本地 Expo Module 已由 autolinking 编译进 iOS；插件只声明系统权限。
 * 后台 BLE 默认关闭，避免收银前台应用在没有明确产品需求时常驻扫描。
 */
function withHbPrinter(config, { backgroundBle = false } = {}) {
  return withInfoPlist(config, (nextConfig) => {
    nextConfig.modResults.NSBluetoothAlwaysUsageDescription = BLUETOOTH_USAGE;
    if (backgroundBle) {
      const modes = new Set(nextConfig.modResults.UIBackgroundModes ?? []);
      modes.add("bluetooth-central");
      nextConfig.modResults.UIBackgroundModes = [...modes];
    }
    return nextConfig;
  });
}

module.exports = createRunOncePlugin(withHbPrinter, "with-hb-printer", "0.1.0");
