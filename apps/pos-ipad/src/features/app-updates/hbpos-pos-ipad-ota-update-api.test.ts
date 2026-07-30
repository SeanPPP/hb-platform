import assert from "node:assert/strict";
import test from "node:test";

import { HbposPosIpadOtaUpdateApi } from "./hbpos-pos-ipad-ota-update-api";

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
  runtimeVersion: "1.2.3",
  currentUpdateId: "123e4567-e89b-42d3-a456-426614174000",
  currentUpdateGroupId: "223e4567-e89b-42d3-a456-426614174000",
});

const policy = Object.freeze({
  state: "optional",
  policyVersion: "policy-42",
  channel: "store-s001",
  runtimeVersion: "1.2.3",
  iosUpdateId: "323e4567-e89b-42d3-a456-426614174000",
  updateGroupId: "423e4567-e89b-42d3-a456-426614174000",
  releaseMessage: "发现新版。",
});

test("OTA API 只调用专用 GET，并从 ApiResponse.data 解包严格七字段策略", async () => {
  const transport = new RecordingTransport({ success: true, data: policy });
  const api = new HbposPosIpadOtaUpdateApi(transport);

  assert.deepEqual(await api.getPolicy(metadata), policy);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/app-updates/pos-ipad/ota",
      params: metadata,
    },
  ]);
});

test("OTA API 对空 current IDs 使用省略参数，非法元数据不触网", async () => {
  const transport = new RecordingTransport({
    success: true,
    data: {
      state: "none",
      policyVersion: "none",
      channel: null,
      runtimeVersion: null,
      iosUpdateId: null,
      updateGroupId: null,
      releaseMessage: null,
    },
  });
  const api = new HbposPosIpadOtaUpdateApi(transport);

  await api.getPolicy({
    runtimeVersion: "1.2.3",
    currentUpdateId: null,
    currentUpdateGroupId: null,
  });
  assert.deepEqual(transport.requests[0]?.params, {
    runtimeVersion: "1.2.3",
    currentUpdateId: undefined,
    currentUpdateGroupId: undefined,
  });

  await assert.rejects(
    () => api.getPolicy({ ...metadata, currentUpdateId: "invalid" }),
    /currentUpdateId is invalid/,
  );
  assert.equal(transport.requests.length, 1);
});

test("OTA metadata runtime token 接受 120 字符并拒绝 121 字符且不触网", async () => {
  const transport = new RecordingTransport({
    success: true,
    data: {
      state: "none",
      policyVersion: "none",
      channel: null,
      runtimeVersion: null,
      iosUpdateId: null,
      updateGroupId: null,
      releaseMessage: null,
    },
  });
  const api = new HbposPosIpadOtaUpdateApi(transport);
  const maximum = `r${"a".repeat(119)}`;

  await api.getPolicy({ ...metadata, runtimeVersion: maximum });
  assert.equal(
    transport.requests[0]?.params?.runtimeVersion,
    maximum,
  );
  await assert.rejects(
    () =>
      api.getPolicy({
        ...metadata,
        runtimeVersion: `r${"a".repeat(120)}`,
      }),
    /runtimeVersion is invalid/,
  );
  assert.equal(transport.requests.length, 1);
});

test("OTA API 拒绝失败 envelope 与越界 data", async () => {
  await assert.rejects(
    () =>
      new HbposPosIpadOtaUpdateApi(
        new RecordingTransport({
          success: false,
          data: policy,
          errorCode: "DENIED",
        }),
      ).getPolicy(metadata),
    /rejected/,
  );

  await assert.rejects(
    () =>
      new HbposPosIpadOtaUpdateApi(
        new RecordingTransport({
          success: true,
          data: { ...policy, bearerToken: "forbidden" },
        }),
      ).getPolicy(metadata),
    /unsupported field/,
  );
});
