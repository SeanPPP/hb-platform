const AUTOMATIC_UPDATE_DISABLED_PROFILES = new Set(["development", "preview", "test", "testing"]);

export function normalizeAppBuildProfile(value: unknown) {
  return typeof value === "string" && value.trim() ? value.trim().toLowerCase() : "production";
}

export function shouldRunAutomaticAppUpdatesForProfile(profile: unknown) {
  // 测试包用于验收固定安装快照，自动 OTA/APK 更新只留给正式包。
  return !AUTOMATIC_UPDATE_DISABLED_PROFILES.has(normalizeAppBuildProfile(profile));
}
