import assert from "node:assert/strict";
import test from "node:test";

import {
  createAppUpdateCacheKey,
  createStoredAppUpdateCacheScope,
  matchesStoredAppUpdateCacheScope,
} from "./scoped-app-update-cache";

const nativeScope = Object.freeze({
  kind: "native",
  apiOrigin: "https://hotbargain.vip",
  storeCode: "S001",
  appKey: "pos-handheld",
  platform: "iOS",
  installedVersion: "1.2.3",
  installedBuild: "42",
} as const);
const otaScope = Object.freeze({
  kind: "ota",
  apiOrigin: "https://hotbargain.vip",
  storeCode: "S001",
  appKey: "pos-handheld",
  projectId: "123e4567-e89b-42d3-a456-426614174000",
  projectName: "hb-pos-handheld",
  platform: "iOS",
  configuredChannel: "store-s001",
  runtimeVersion: "1.2.3",
  currentUpdateId: null,
  currentUpdateGroupId: null,
} as const);

test("native 与 OTA scope 即使公共字段相同也生成不同 key 域", () => {
  const nativeKey = createAppUpdateCacheKey("cache-vnext", nativeScope);
  const otaKey = createAppUpdateCacheKey("cache-vnext", otaScope);

  assert.notEqual(nativeKey, otaKey);
  assert.match(nativeKey, /:native:/);
  assert.match(otaKey, /:ota:/);
});

test("旧四字段 stored scope 不得被新缓存身份接受", () => {
  const expected = createStoredAppUpdateCacheScope(
    nativeScope,
    "native-v3",
  );

  assert.equal(
    matchesStoredAppUpdateCacheScope(
      {
        apiOrigin: nativeScope.apiOrigin,
        storeCode: nativeScope.storeCode,
        runtimeVersion: "1.2.3",
        installedVersion: nativeScope.installedVersion,
        policyVersion: "native-v3",
      },
      expected,
    ),
    false,
  );
});

test("OTA nullable 字段的 null 与真实字符串值不产生 key 碰撞", () => {
  for (const field of [
    "projectName",
    "configuredChannel",
    "currentUpdateId",
  ] as const) {
    const nullKey = createAppUpdateCacheKey("cache-vnext", {
      ...otaScope,
      [field]: null,
    });
    assert.notEqual(
      createAppUpdateCacheKey("cache-vnext", {
        ...otaScope,
        [field]: "null",
      }),
      nullKey,
    );
  }
});
