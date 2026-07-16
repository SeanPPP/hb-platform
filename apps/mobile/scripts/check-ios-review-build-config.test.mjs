import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const scriptsDirectory = path.dirname(fileURLToPath(import.meta.url));
const projectDirectory = path.resolve(scriptsDirectory, "..");
const checkerPath = path.join(scriptsDirectory, "check-ios-review-build-config.mjs");

function runChecker(overrides = {}) {
  const env = { ...process.env };
  delete env.EAS_BUILD_PLATFORM;
  delete env.EAS_BUILD_PROFILE;
  delete env.EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED;
  delete env.EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256;
  Object.assign(env, overrides);

  return spawnSync(process.execPath, [checkerPath], {
    cwd: projectDirectory,
    env,
    encoding: "utf8",
  });
}

for (const env of [
  {},
  { EAS_BUILD_PLATFORM: "android", EAS_BUILD_PROFILE: "production" },
  { EAS_BUILD_PLATFORM: "ios", EAS_BUILD_PROFILE: "preview" },
]) {
  const result = runChecker(env);
  assert.equal(result.status, 0, `非 iOS production 构建应跳过校验：${result.stderr}`);
}

const validHash = "a".repeat(64);
const validResult = runChecker({
  EAS_BUILD_PLATFORM: "ios",
  EAS_BUILD_PROFILE: "production",
  EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED: "true",
  EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256: validHash,
});
assert.equal(validResult.status, 0, validResult.stderr);

for (const [label, overrides, expectedMessage] of [
  ["缺少审核开关", { EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256: validHash }, "EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED"],
  [
    "审核开关不是 true",
    {
      EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED: "false",
      EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256: validHash,
    },
    "EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED",
  ],
  ["缺少密码哈希", { EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED: "true" }, "EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256"],
  [
    "密码哈希长度错误",
    {
      EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED: "true",
      EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256: "a".repeat(63),
    },
    "64 位十六进制",
  ],
  [
    "密码哈希不是十六进制",
    {
      EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED: "true",
      EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256: "z".repeat(64),
    },
    "64 位十六进制",
  ],
]) {
  const result = runChecker({
    EAS_BUILD_PLATFORM: "ios",
    EAS_BUILD_PROFILE: "production",
    ...overrides,
  });

  assert.notEqual(result.status, 0, `${label}时必须阻止构建`);
  assert.match(result.stderr, new RegExp(expectedMessage));
}

const easConfig = JSON.parse(readFileSync(path.join(projectDirectory, "eas.json"), "utf8"));
const productionProfile = easConfig.build.production;
assert.equal(productionProfile.environment, "production");
assert.equal(productionProfile.env.EXPO_PUBLIC_IOS_REVIEW_MODE_ENABLED, "true");
assert.equal(
  productionProfile.env.EXPO_PUBLIC_IOS_REVIEW_PASSWORD_SHA256,
  "9eb044ae7bf67e2f67b05dd9981524a1bebc80f92baa776b5d6e244cbde80a2c",
  "eas.json 只允许保存不可逆哈希，不能保存审核账号明文密码"
);

const packageJson = JSON.parse(readFileSync(path.join(projectDirectory, "package.json"), "utf8"));
assert.equal(
  packageJson.scripts["eas-build-pre-install"],
  "node scripts/check-ios-review-build-config.mjs"
);
assert.equal(
  packageJson.scripts["test:ios-review-build-config"],
  "node scripts/check-ios-review-build-config.test.mjs"
);

console.log("iOS review build config tests passed.");
