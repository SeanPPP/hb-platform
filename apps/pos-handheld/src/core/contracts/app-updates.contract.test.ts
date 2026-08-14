import assert from "node:assert/strict";
import test from "node:test";

import {
  ANDROID_APK_MAX_SIZE_BYTES,
  normalizePosHandheldUpdatePolicy,
} from "./app-updates";

const androidDecision = Object.freeze({
  enabled: true,
  state: "required",
  policyVersion: "android-policy-200",
  platform: "Android",
  required: true,
  latestVersion: "2.0.0",
  latestBuild: "200",
  minimumSupportedVersion: "1.5.0",
  distribution: "apk",
  downloadUrl: "https://updates.example.test/pos-handheld/200.apk",
  fileSize: 2_048,
  sha256: "a".repeat(64),
  packageName: "com.hbweb.poshandheld",
  signingCertificateSha256: "b".repeat(64),
  bundleIdentifier: null,
  appStoreId: null,
  releaseMessage: "Security update",
});

const iosDecision = Object.freeze({
  enabled: true,
  state: "optional",
  policyVersion: "ios-policy-300",
  platform: "iOS",
  required: false,
  latestVersion: "3.0.0",
  latestBuild: "300",
  minimumSupportedVersion: "2.0.0",
  distribution: "testflight",
  downloadUrl: "https://testflight.apple.com/join/AbCdEf12",
  fileSize: null,
  sha256: null,
  packageName: null,
  signingCertificateSha256: null,
  bundleIdentifier: "com.hbweb.poshandheld",
  appStoreId: "1234567890",
  releaseMessage: null,
});

test("Android policy 保留 backend 完整安装身份并冻结结果", () => {
  const normalized = normalizePosHandheldUpdatePolicy(androidDecision);
  assert.deepEqual(normalized, androidDecision);
  assert.equal(Object.isFrozen(normalized), true);
});

test("iOS TestFlight/App Store 决策必须与 Android 安装字段严格互斥", () => {
  assert.deepEqual(normalizePosHandheldUpdatePolicy(iosDecision), iosDecision);
  assert.equal(
    normalizePosHandheldUpdatePolicy({
      ...iosDecision,
      distribution: "app-store",
      downloadUrl: "https://apps.apple.com/au/app/id1234567890",
    }).distribution,
    "app-store",
  );
  assert.throws(
    () =>
      normalizePosHandheldUpdatePolicy({
        ...iosDecision,
        fileSize: 2_048,
      }),
    /iOS|metadata|field/i,
  );
  assert.throws(
    () =>
      normalizePosHandheldUpdatePolicy({
        ...iosDecision,
        distribution: "apk",
      }),
    /distribution|iOS/i,
  );
});

test("iOS App Store URL 的最终独立 segment 必须精确绑定 App Store ID", () => {
  const appStoreDecision = {
    ...iosDecision,
    distribution: "app-store",
    downloadUrl:
      "https://apps.apple.com/au/app/hb-pos-handheld/id1234567890",
  } as const;

  assert.deepEqual(
    normalizePosHandheldUpdatePolicy(appStoreDecision),
    appStoreDecision,
  );

  for (const downloadUrl of [
    "https://apps.apple.com/au/app/hb-pos-handheld/id1234567891",
    "https://apps.apple.com/au/app/hb-pos-handheld/id01234567890",
    "https://apps.apple.com/au/app/hb-pos-handheld/product-id1234567890",
    "https://apps.apple.com/au/app/hb-pos-handheld",
    "https://apps.apple.com/au/app/id1234567890/reviews",
  ]) {
    assert.throws(
      () =>
        normalizePosHandheldUpdatePolicy({
          ...appStoreDecision,
          downloadUrl,
        }),
      TypeError,
      `${downloadUrl} 不得通过 App Store 身份绑定`,
    );
  }
});

test("iOS bundle 与 App Store ID 必须匹配冻结身份格式", () => {
  assert.throws(
    () =>
      normalizePosHandheldUpdatePolicy({
        ...iosDecision,
        bundleIdentifier: "com.example.other-handheld",
      }),
    TypeError,
  );
  for (const appStoreId of ["12345abc", "1234", "1".repeat(21)]) {
    assert.throws(
      () =>
        normalizePosHandheldUpdatePolicy({
          ...iosDecision,
          appStoreId,
        }),
      TypeError,
    );
  }
});

test("TestFlight 只接受 optional canonical join URL，required 必须 fail closed", () => {
  assert.deepEqual(normalizePosHandheldUpdatePolicy(iosDecision), iosDecision);
  assert.throws(
    () =>
      normalizePosHandheldUpdatePolicy({
        ...iosDecision,
        state: "required",
        required: true,
      }),
    TypeError,
  );
  for (const downloadUrl of [
    "https://testflight.apple.com/not-join/AbCdEf12",
    "https://testflight.apple.com/join/AbCdEf12/extra",
    "https://testflight.apple.com/join/AbCdEf12?source=other",
  ]) {
    assert.throws(() =>
      normalizePosHandheldUpdatePolicy({ ...iosDecision, downloadUrl }),
    );
  }
});

test("none 决策必须显式给出全部 null 元数据，不能藏安装入口", () => {
  const none = {
    enabled: true,
    state: "none",
    policyVersion: "none",
    platform: "Android",
    required: false,
    latestVersion: null,
    latestBuild: null,
    minimumSupportedVersion: null,
    distribution: null,
    downloadUrl: null,
    fileSize: null,
    sha256: null,
    packageName: null,
    signingCertificateSha256: null,
    bundleIdentifier: null,
    appStoreId: null,
    releaseMessage: null,
  } as const;
  assert.deepEqual(normalizePosHandheldUpdatePolicy(none), none);
  assert.throws(
    () =>
      normalizePosHandheldUpdatePolicy({
        ...none,
        downloadUrl: androidDecision.downloadUrl,
      }),
    /none|metadata|target/i,
  );
});

test("设备交易权限保留 false，旧策略缺字段默认允许，非法值拒绝", () => {
  const disabled = normalizePosHandheldUpdatePolicy({
    ...androidDecision,
    enabled: false,
  });
  assert.equal(disabled.enabled, false);

  const legacy = { ...iosDecision } as Record<string, unknown>;
  delete legacy.enabled;
  assert.equal(normalizePosHandheldUpdatePolicy(legacy).enabled, true);

  assert.throws(
    () => normalizePosHandheldUpdatePolicy({ ...iosDecision, enabled: "false" }),
    /enabled|transaction/i,
  );
});

test("Android 条件式元数据缺失、越界或篡改时 fail closed", () => {
  for (const field of [
    "latestVersion",
    "latestBuild",
    "distribution",
    "downloadUrl",
    "fileSize",
    "sha256",
    "packageName",
    "signingCertificateSha256",
  ] as const) {
    assert.throws(
      () => normalizePosHandheldUpdatePolicy({ ...androidDecision, [field]: null }),
      TypeError,
      `${field} 缺失必须拒绝`,
    );
  }
  for (const patch of [
    { latestBuild: "1.2" },
    { fileSize: 0 },
    { fileSize: ANDROID_APK_MAX_SIZE_BYTES + 1 },
    { sha256: "f".repeat(63) },
    { packageName: "fake-package" },
    { signingCertificateSha256: "g".repeat(64) },
    { downloadUrl: "http://updates.example.test/fake.apk" },
    { bundleIdentifier: "com.hbweb.poshandheld" },
  ]) {
    assert.throws(() =>
      normalizePosHandheldUpdatePolicy({ ...androidDecision, ...patch }),
    );
  }
});

test("state/required 必须一致，且任何缺字段或未知字段都拒绝", () => {
  assert.throws(() =>
    normalizePosHandheldUpdatePolicy({ ...androidDecision, required: false }),
  );
  assert.throws(() =>
    normalizePosHandheldUpdatePolicy({
      ...androidDecision,
      state: "optional",
      required: true,
    }),
  );
  const incomplete = { ...androidDecision } as Record<string, unknown>;
  delete incomplete.signingCertificateSha256;
  assert.throws(() => normalizePosHandheldUpdatePolicy(incomplete));
  assert.throws(() =>
    normalizePosHandheldUpdatePolicy({
      ...androidDecision,
      authorizationToken: "forbidden",
    }),
  );
});
