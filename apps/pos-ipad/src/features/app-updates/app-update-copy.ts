export const appUpdateEnglishCopy = {
  "eyebrow.optional": "NEW VERSION",
  "eyebrow.required": "HB POS UPDATE",
  "optional.nativeTitle": "A new App Store version is available",
  "optional.otaTitle": "A new HB POS update is available",
  "required.nativeTitle": "Update HB POS to continue",
  "required.otaTitle": "Finish this HB POS update to continue",
  "optional.body":
    "You can update now or continue working and choose Later.",
  "required.body":
    "This terminal is safe to update. Sales pages stay locked until the update finishes.",
  "action.openStore": "Open App Store",
  "action.installOta": "Install update",
  "action.working": "Updating…",
  "action.later": "Later",
  "action.settings": "Settings",
  "action.support": "Update support",
  "action.registration": "Device registration",
  "error.notSafe":
    "Finish the current sale or payment recovery before updating.",
  "error.unavailable":
    "The verified update is not available yet. Check the network and try again.",
} as const;

export type AppUpdateCopyKey = keyof typeof appUpdateEnglishCopy;

export const appUpdateChineseCopy: Record<
  AppUpdateCopyKey,
  string
> = {
  "eyebrow.optional": "发现新版",
  "eyebrow.required": "应用更新",
  "optional.nativeTitle": "发现 App Store 新版本",
  "optional.otaTitle": "发现 HB POS 新版本",
  "required.nativeTitle": "更新 HB POS 后才能继续",
  "required.otaTitle": "完成 HB POS 更新后才能继续",
  "optional.body": "可以立即更新，也可选择稍后并继续当前工作。",
  "required.body": "当前交易已安全收口，升级完成前业务页面保持锁定。",
  "action.openStore": "打开 App Store",
  "action.installOta": "安装更新",
  "action.working": "正在更新…",
  "action.later": "稍后",
  "action.settings": "设置",
  "action.support": "更新支持",
  "action.registration": "设备注册",
  "error.notSafe": "请先完成当前交易或支付恢复，再执行更新。",
  "error.unavailable": "已验证的更新暂不可用，请检查网络后重试。",
};

export function resolveAppUpdateCopy(
  language?: string,
): Readonly<Record<AppUpdateCopyKey, string>> {
  return language?.toLowerCase().startsWith("zh")
    ? appUpdateChineseCopy
    : appUpdateEnglishCopy;
}
