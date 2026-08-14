import assert from "node:assert/strict";
import test from "node:test";

import { resolveExpoUpdateRuntimeVersion } from "./expo-update-runtime-version";

test("expo updates disabled 时空 runtimeVersion 回退到 appVersion", () => {
  for (const runtimeVersion of [null, undefined, "", "   "]) {
    assert.equal(
      resolveExpoUpdateRuntimeVersion(runtimeVersion, "0.2.0"),
      "0.2.0",
    );
  }
});

test("合法 runtimeVersion 保持不变", () => {
  assert.equal(
    resolveExpoUpdateRuntimeVersion("ios-sdk54/pos.1_2", "0.2.0"),
    "ios-sdk54/pos.1_2",
  );
});

test("非空非法 runtimeVersion 不得静默回退", () => {
  assert.throws(
    () => resolveExpoUpdateRuntimeVersion("invalid runtime", "0.2.0"),
    /runtimeVersion is invalid/,
  );
  assert.throws(
    () => resolveExpoUpdateRuntimeVersion(`r${"a".repeat(120)}`, "0.2.0"),
    /runtimeVersion is invalid/,
  );
});

test("回退 appVersion 仍必须满足 runtime token 合同", () => {
  assert.throws(
    () => resolveExpoUpdateRuntimeVersion("", "invalid version"),
    /runtimeVersion is invalid/,
  );
});
