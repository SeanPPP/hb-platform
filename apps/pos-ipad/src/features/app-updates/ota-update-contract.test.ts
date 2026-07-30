import assert from "node:assert/strict";
import test from "node:test";

import {
  POS_IPAD_OTA_NONE_POLICY,
  normalizeAppUpdateCacheScope,
  normalizePosIpadOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";

const updateId = "123e4567-e89b-42d3-a456-426614174000";
const updateGroupId = "223e4567-e89b-42d3-a456-426614174000";

test("OTA 策略严格保留七字段，并规范化 optional/required 的 EAS 标识", () => {
  assert.deepEqual(
    normalizePosIpadOtaUpdatePolicy({
      state: "required",
      policyVersion: "policy-42",
      channel: "Store-S001",
      runtimeVersion: "1.2.3",
      iosUpdateId: updateId.toUpperCase(),
      updateGroupId: updateGroupId.toUpperCase(),
      releaseMessage: "  修复收银问题。  ",
    }),
    {
      state: "required",
      policyVersion: "policy-42",
      channel: "Store-S001",
      runtimeVersion: "1.2.3",
      iosUpdateId: updateId,
      updateGroupId,
      releaseMessage: "修复收银问题。",
    },
  );
});

test("none 必须使用冻结的完整七字段形状", () => {
  assert.deepEqual(
    normalizePosIpadOtaUpdatePolicy({
      state: "none",
      policyVersion: "none",
      channel: null,
      runtimeVersion: null,
      iosUpdateId: null,
      updateGroupId: null,
      releaseMessage: null,
    }),
    POS_IPAD_OTA_NONE_POLICY,
  );

  for (const malformed of [
    { ...POS_IPAD_OTA_NONE_POLICY, policyVersion: "policy-1" },
    { ...POS_IPAD_OTA_NONE_POLICY, channel: "production" },
    { ...POS_IPAD_OTA_NONE_POLICY, releaseMessage: "unexpected" },
  ]) {
    assert.throws(
      () => normalizePosIpadOtaUpdatePolicy(malformed),
      /none policy/i,
    );
  }
});

test("OTA 策略拒绝缺字段、额外字段、非法 state、空标识和非 UUID", () => {
  const valid = {
    state: "optional",
    policyVersion: "policy-42",
    channel: "store-s001",
    runtimeVersion: "1.2.3",
    iosUpdateId: updateId,
    updateGroupId,
    releaseMessage: null,
  } as const;

  for (const malformed of [
    { ...valid, accessToken: "forbidden" },
    { ...valid, releaseMessage: undefined },
    { ...valid, state: "later" },
    { ...valid, channel: "" },
    { ...valid, iosUpdateId: "not-a-uuid" },
    { ...valid, updateGroupId: null },
  ]) {
    assert.throws(() => normalizePosIpadOtaUpdatePolicy(malformed));
  }
});

test("runtime compatibility token 在策略与 cache scope 中统一限制为 1..120", () => {
  const maximum = `r${"a".repeat(119)}`;
  const policy = {
    state: "optional",
    policyVersion: "policy-42",
    channel: "pos-ipad-release-boundary",
    runtimeVersion: maximum,
    iosUpdateId: updateId,
    updateGroupId,
    releaseMessage: null,
  } as const;
  assert.equal(
    normalizePosIpadOtaUpdatePolicy(policy).runtimeVersion,
    maximum,
  );
  assert.equal(
    normalizeAppUpdateCacheScope({
      apiOrigin: "https://pos.example.test",
      storeCode: "S001",
      runtimeVersion: maximum,
      installedVersion: "1.2.3",
    }).runtimeVersion,
    maximum,
  );

  const tooLong = `r${"a".repeat(120)}`;
  assert.throws(
    () =>
      normalizePosIpadOtaUpdatePolicy({
        ...policy,
        runtimeVersion: tooLong,
      }),
    /runtimeVersion/,
  );
  assert.throws(
    () =>
      normalizeAppUpdateCacheScope({
        apiOrigin: "https://pos.example.test",
        storeCode: "S001",
        runtimeVersion: tooLong,
        installedVersion: "1.2.3",
      }),
    /runtimeVersion/,
  );
});
