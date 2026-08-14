import assert from "node:assert/strict";
import test from "node:test";

import {
  appUpdateCacheScopesEqual,
  normalizeAppUpdateCacheScope,
  normalizePosHandheldOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";

const updateGroupId = "223e4567-e89b-42d3-a456-426614174000";

const optionalPolicy = Object.freeze({
  state: "optional",
  policyVersion: "policy-42",
  appKey: "pos-handheld",
  projectName: "hb-pos-handheld",
  platform: "iOS",
  required: false,
  channel: "pos-handheld-production",
  runtimeVersion: "1.2.3",
  updateId: "ios-update-42",
  updateGroupId,
  releaseMessage: "修复收银问题。",
});

test("OTA 策略精确保留真实 handheld 十一字段与通用 updateId", () => {
  assert.deepEqual(
    normalizePosHandheldOtaUpdatePolicy({
      ...optionalPolicy,
      updateGroupId: updateGroupId.toUpperCase(),
      releaseMessage: "  修复收银问题。  ",
    }),
    optionalPolicy,
  );
});

test("none 保留后端返回的 handheld scope，但不得携带更新身份", () => {
  const none = {
    state: "none",
    policyVersion: "none",
    appKey: "pos-handheld",
    projectName: "hb-pos-handheld",
    platform: "iOS",
    required: false,
    channel: "pos-handheld-production",
    runtimeVersion: "1.2.3",
    updateId: null,
    updateGroupId: null,
    releaseMessage: null,
  } as const;

  assert.deepEqual(normalizePosHandheldOtaUpdatePolicy(none), none);

  for (const malformed of [
    { ...none, policyVersion: "policy-1" },
    { ...none, required: true },
    { ...none, updateId: "unexpected-update" },
    { ...none, updateGroupId },
    { ...none, releaseMessage: "unexpected" },
  ]) {
    assert.throws(
      () => normalizePosHandheldOtaUpdatePolicy(malformed),
      /none policy|required/i,
    );
  }
});

test("OTA 策略拒绝旧 iPad 字段、缺字段、额外字段与错误 state/required", () => {
  for (const malformed of [
    { ...optionalPolicy, iosUpdateId: optionalPolicy.updateId },
    { ...optionalPolicy, releaseMessage: undefined },
    { ...optionalPolicy, bearerToken: "forbidden" },
    { ...optionalPolicy, state: "later" },
    { ...optionalPolicy, required: true },
    {
      ...optionalPolicy,
      state: "required",
      required: false,
    },
    { ...optionalPolicy, appKey: "pos-ipad" },
    { ...optionalPolicy, platform: "iPadOS" },
  ]) {
    assert.throws(() => normalizePosHandheldOtaUpdatePolicy(malformed));
  }
});

test("handheld OTA 更新身份与投放 scope 必须完整且合法", () => {
  for (const malformed of [
    { ...optionalPolicy, projectName: null },
    { ...optionalPolicy, channel: "" },
    { ...optionalPolicy, runtimeVersion: null },
    { ...optionalPolicy, updateId: "bad update id" },
    { ...optionalPolicy, updateGroupId: "not-a-uuid" },
  ]) {
    assert.throws(() => normalizePosHandheldOtaUpdatePolicy(malformed));
  }
});

test("runtime compatibility token 在策略与 OTA cache scope 中统一限制为 1..120", () => {
  const maximum = `r${"a".repeat(119)}`;
  assert.equal(
    normalizePosHandheldOtaUpdatePolicy({
      ...optionalPolicy,
      runtimeVersion: maximum,
    }).runtimeVersion,
    maximum,
  );
  assert.equal(
    normalizeAppUpdateCacheScope({
      kind: "ota",
      apiOrigin: "https://pos.example.test",
      storeCode: "S001",
      appKey: "pos-handheld",
      projectId: "123e4567-e89b-42d3-a456-426614174000",
      projectName: "hb-pos-handheld",
      platform: "iOS",
      configuredChannel: "pos-handheld-production",
      runtimeVersion: maximum,
      currentUpdateId: null,
      currentUpdateGroupId: null,
    }).runtimeVersion,
    maximum,
  );

  const tooLong = `r${"a".repeat(120)}`;
  assert.throws(
    () =>
      normalizePosHandheldOtaUpdatePolicy({
        ...optionalPolicy,
        runtimeVersion: tooLong,
      }),
    /runtimeVersion/,
  );
  assert.throws(
    () =>
      normalizeAppUpdateCacheScope({
        kind: "ota",
        apiOrigin: "https://pos.example.test",
        storeCode: "S001",
        appKey: "pos-handheld",
        projectId: "123e4567-e89b-42d3-a456-426614174000",
        projectName: "hb-pos-handheld",
        platform: "iOS",
        configuredChannel: "pos-handheld-production",
        runtimeVersion: tooLong,
        currentUpdateId: null,
        currentUpdateGroupId: null,
      }),
    /runtimeVersion/,
  );
});

test("native 与 OTA cache scope 使用不可碰撞的判别身份域", () => {
  const native = normalizeAppUpdateCacheScope({
    kind: "native",
    apiOrigin: "https://pos.example.test/path",
    storeCode: " S001 ",
    appKey: "pos-handheld",
    platform: "iOS",
    installedVersion: " 1.2.3 ",
    installedBuild: " 42 ",
  });
  const ota = normalizeAppUpdateCacheScope({
    kind: "ota",
    apiOrigin: "https://pos.example.test/another-path",
    storeCode: " S001 ",
    appKey: "pos-handheld",
    projectId: "123E4567-E89B-42D3-A456-426614174000",
    projectName: " hb-pos-handheld ",
    platform: "iOS",
    configuredChannel: " pos-handheld-production ",
    runtimeVersion: " 1.2.3 ",
    currentUpdateId: null,
    currentUpdateGroupId: null,
  });

  assert.deepEqual(native, {
    kind: "native",
    apiOrigin: "https://pos.example.test",
    storeCode: "S001",
    appKey: "pos-handheld",
    platform: "iOS",
    installedVersion: "1.2.3",
    installedBuild: "42",
  });
  assert.deepEqual(ota, {
    kind: "ota",
    apiOrigin: "https://pos.example.test",
    storeCode: "S001",
    appKey: "pos-handheld",
    projectId: "123e4567-e89b-42d3-a456-426614174000",
    projectName: "hb-pos-handheld",
    platform: "iOS",
    configuredChannel: "pos-handheld-production",
    runtimeVersion: "1.2.3",
    currentUpdateId: null,
    currentUpdateGroupId: null,
  });
  assert.equal(appUpdateCacheScopesEqual(native, ota), false);
});

test("OTA cache scope 对 nullable metadata 使用稳定 null，不接受空串哨兵", () => {
  const nullable = normalizeAppUpdateCacheScope({
    kind: "ota",
    apiOrigin: "https://pos.example.test",
    storeCode: "S001",
    appKey: "pos-handheld",
    projectId: null,
    projectName: null,
    platform: "Android",
    configuredChannel: null,
    runtimeVersion: "1.2.3",
    currentUpdateId: null,
    currentUpdateGroupId: null,
  });
  assert.deepEqual(
    {
      projectId: nullable.projectId,
      projectName: nullable.projectName,
      configuredChannel: nullable.configuredChannel,
      currentUpdateId: nullable.currentUpdateId,
      currentUpdateGroupId: nullable.currentUpdateGroupId,
    },
    {
      projectId: null,
      projectName: null,
      configuredChannel: null,
      currentUpdateId: null,
      currentUpdateGroupId: null,
    },
  );

  for (const field of [
    "projectId",
    "projectName",
    "configuredChannel",
    "currentUpdateId",
    "currentUpdateGroupId",
  ] as const) {
    assert.throws(() =>
      normalizeAppUpdateCacheScope({
        ...nullable,
        [field]: " ",
      }),
    );
  }
});
