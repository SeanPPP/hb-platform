import assert from "node:assert/strict";
import test from "node:test";

import { HbposPosIpadUpdateApi } from "./hbpos-pos-ipad-update-api";

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
  runtimeVersion: "1.2.3",
});

const policy = Object.freeze({
  enabled: true,
  minimumSupportedVersion: "1.2.0",
  latestVersion: "1.3.0",
  forceUpdate: false,
  appStoreUrl: "https://apps.apple.com/au/app/hot-bargain/id123456789",
  releaseMessage: "请在空闲时更新。",
});

test("严格生成 GET 只发送经校验的 version、build、runtimeVersion，并规范化响应", async () => {
  const transport = new RecordingTransport({ success: true, data: policy });
  const api = new HbposPosIpadUpdateApi(transport);

  assert.deepEqual(await api.getPolicy(metadata), policy);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/app-updates/pos-ipad",
      params: metadata,
    },
  ]);
});

test("请求元数据不合法或响应缺字段时 fail-closed，且非法请求不会触网", async () => {
  const validTransport = new RecordingTransport({ success: true, data: policy });
  const api = new HbposPosIpadUpdateApi(validTransport);

  await assert.rejects(
    () => api.getPolicy({ ...metadata, build: "build-42" }),
    /build is invalid/,
  );
  assert.equal(validTransport.requests.length, 0);

  const malformedResponse = new HbposPosIpadUpdateApi(
    new RecordingTransport({
      success: true,
      data: { ...policy, forceUpdate: undefined },
    }),
  );
  await assert.rejects(
    () => malformedResponse.getPolicy(metadata),
    /booleans are invalid/,
  );
});

test("原生决策 runtime 使用 exact compatibility token，不误当营销版本解析", async () => {
  const transport = new RecordingTransport({ success: true, data: policy });
  const api = new HbposPosIpadUpdateApi(transport);
  const maximum = `r${"a".repeat(119)}`;

  await api.getPolicy({
    ...metadata,
    runtimeVersion: "ios-sdk54/pos.1_2",
  });
  await api.getPolicy({ ...metadata, runtimeVersion: maximum });
  assert.deepEqual(
    transport.requests.map(
      (request) => request.params?.runtimeVersion,
    ),
    ["ios-sdk54/pos.1_2", maximum],
  );
  await assert.rejects(
    () =>
      api.getPolicy({
        ...metadata,
        runtimeVersion: `r${"a".repeat(120)}`,
      }),
    /runtimeVersion is invalid/,
  );
  assert.equal(transport.requests.length, 2);
});
