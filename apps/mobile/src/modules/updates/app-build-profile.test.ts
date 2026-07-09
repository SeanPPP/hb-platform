import assert from "node:assert/strict";
import {
  normalizeAppBuildProfile,
  shouldRunAutomaticAppUpdatesForProfile,
} from "./app-build-profile";

function run() {
  assert.equal(normalizeAppBuildProfile(" Preview "), "preview", "构建 profile 应归一化大小写和空白");
  assert.equal(normalizeAppBuildProfile(null), "production", "缺省 profile 按生产包处理");

  assert.equal(
    shouldRunAutomaticAppUpdatesForProfile("production"),
    true,
    "正式包应保留自动更新"
  );
  assert.equal(
    shouldRunAutomaticAppUpdatesForProfile("preview"),
    false,
    "测试 preview 包不应自动更新"
  );
  assert.equal(
    shouldRunAutomaticAppUpdatesForProfile("development"),
    false,
    "开发包不应自动更新"
  );
  assert.equal(
    shouldRunAutomaticAppUpdatesForProfile("test"),
    false,
    "显式 test profile 不应自动更新"
  );

  console.log("app-build-profile.test.ts: ok");
}

run();
