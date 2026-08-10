const AUTOMATIC_UPDATE_DISABLED_PROFILES = new Set(["development", "test", "testing"]);

export function normalizeAppBuildProfile(value: unknown) {
  return typeof value === "string" && value.trim() ? value.trim().toLowerCase() : "production";
}

export function shouldRunAutomaticAppUpdatesForProfile(profile: unknown) {
  // preview 包需要跟随 OTA/APK 更新；development/test 包继续保留固定安装快照。
  return !AUTOMATIC_UPDATE_DISABLED_PROFILES.has(normalizeAppBuildProfile(profile));
}
