import assert from "node:assert/strict";
import test from "node:test";

import { HbposPosHandheldUpdateApi } from "./hbpos-pos-handheld-update-api";

import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "@/core/api/hbpos-api";

class RecordingTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(private readonly payload: unknown) {}

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    return { status: 200, data: this.payload as T };
  }
}

const metadata = Object.freeze({
  version: "1.2.3",
  build: "42",
});

const iosDecision = Object.freeze({
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
  releaseMessage: "请在空闲时更新。",
});

const androidDecision = Object.freeze({
  state: "required",
  policyVersion: "android-policy-200",
  platform: "Android",
  required: true,
  latestVersion: "2.0.0",
  latestBuild: "200",
  minimumSupportedVersion: "1.5.0",
  distribution: "apk",
  downloadUrl:
    "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/mobile-app-builds/pos-handheld/production/200.apk",
  fileSize: 2_048,
  sha256: "a".repeat(64),
  packageName: "com.hbweb.poshandheld",
  signingCertificateSha256: "b".repeat(64),
  bundleIdentifier: null,
  appStoreId: null,
  releaseMessage: "必须更新。",
});

test("iOS GET 使用 pos-handheld 路径并保留完整 TestFlight 决策", async () => {
  const transport = new RecordingTransport({ success: true, data: iosDecision });
  const api = new HbposPosHandheldUpdateApi(transport, "iOS");

  assert.deepEqual(await api.getPolicy(metadata), iosDecision);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/app-updates/pos-handheld",
      params: metadata,
    },
  ]);
});

test("Android GET 从 response 到 policy 不丢失 size/hash/package/signature/build", async () => {
  const transport = new RecordingTransport({
    success: true,
    data: androidDecision,
  });
  const api = new HbposPosHandheldUpdateApi(transport, "Android");

  assert.deepEqual(await api.getPolicy(metadata), androidDecision);
  assert.deepEqual(transport.requests[0], {
    method: "GET",
    url: "/api/v1/app-updates/pos-handheld",
    params: metadata,
  });
});

test("response 平台不匹配、Android 元数据缺失或未知本机平台均 fail closed", async () => {
  await assert.rejects(
    () =>
      new HbposPosHandheldUpdateApi(
        new RecordingTransport({ success: true, data: androidDecision }),
        "iOS",
      ).getPolicy(metadata),
    /platform/i,
  );
  await assert.rejects(
    () =>
      new HbposPosHandheldUpdateApi(
        new RecordingTransport({
          success: true,
          data: { ...androidDecision, signingCertificateSha256: null },
        }),
        "Android",
      ).getPolicy(metadata),
    /Android APK update metadata/i,
  );

  const unknownTransport = new RecordingTransport({
    success: true,
    data: androidDecision,
  });
  await assert.rejects(
    () =>
      new HbposPosHandheldUpdateApi(
        unknownTransport,
        "web" as never,
      ).getPolicy(metadata),
    /platform/i,
  );
  assert.equal(unknownTransport.requests.length, 0);
});

test("请求元数据不合法时 fail closed 且不会触网", async () => {
  const transport = new RecordingTransport({ success: true, data: iosDecision });
  const api = new HbposPosHandheldUpdateApi(transport, "iOS");

  await assert.rejects(
    () => api.getPolicy({ ...metadata, build: "build-42" }),
    /build is invalid/,
  );
  assert.equal(transport.requests.length, 0);
});

test("当前 build 必须是规范 JavaScript 安全正整数，且无效值不会触网", async () => {
  for (const build of [
    "0",
    "00",
    "01",
    "1.2",
    " 1 ",
    "1 ",
    "\t1",
    "9007199254740992",
    "10000000000000000",
  ]) {
    const transport = new RecordingTransport({ success: true, data: iosDecision });
    const api = new HbposPosHandheldUpdateApi(transport, "iOS");

    await assert.rejects(
      () => api.getPolicy({ ...metadata, build }),
      /build is invalid/,
    );
    assert.equal(transport.requests.length, 0, build);
  }
});

test("当前 build 接受规范 JavaScript 安全整数边界", async () => {
  for (const build of ["1", "9007199254740991"]) {
    const transport = new RecordingTransport({ success: true, data: iosDecision });
    const api = new HbposPosHandheldUpdateApi(transport, "iOS");

    await api.getPolicy({ ...metadata, build });

    assert.equal(transport.requests.length, 1, build);
    assert.equal(transport.requests[0]?.params?.build, build);
  }
});

test("原生 GET query 精确等于 controller 的 version/build，不发送 platform/runtimeVersion", async () => {
  const transport = new RecordingTransport({ success: true, data: iosDecision });
  const api = new HbposPosHandheldUpdateApi(transport, "iOS");

  await api.getPolicy({
    ...metadata,
    platform: "Android",
    runtimeVersion: "forbidden-extra",
  } as never);

  assert.deepEqual(transport.requests[0]?.params, metadata);
  assert.deepEqual(Object.keys(transport.requests[0]?.params ?? {}).sort(), [
    "build",
    "version",
  ]);
});
