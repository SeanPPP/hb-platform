const {
  EAS_BUILD_PLATFORM,
  EAS_BUILD_PROFILE,
  EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED,
  EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256,
} = process.env;

// 仅约束提交 App Store 的正式 iOS 构建，不影响 Android、预览包和本地开发。
const isIosProductionBuild =
  EAS_BUILD_PLATFORM === "ios" && EAS_BUILD_PROFILE === "production";

if (!isIosProductionBuild) {
  console.log("跳过 iOS 审核模式构建配置校验。");
  process.exit(0);
}

const failures = [];

if (EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED !== "true") {
  failures.push("EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED 必须严格设置为 true");
}

// 只校验不可逆 SHA-256 哈希，任何错误信息都不得输出变量值。
if (!/^[0-9a-fA-F]{64}$/.test(EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256 ?? "")) {
  failures.push("EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256 必须是 64 位十六进制 SHA-256 哈希");
}

if (failures.length > 0) {
  console.error("iOS production 审核模式配置校验失败：");
  failures.forEach((failure) => console.error(`- ${failure}`));
  process.exit(1);
}

console.log("iOS production 审核模式配置校验通过。");
