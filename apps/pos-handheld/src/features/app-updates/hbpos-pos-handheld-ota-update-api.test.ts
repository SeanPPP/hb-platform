import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { HbposPosHandheldOtaUpdateApi } from "./hbpos-pos-handheld-ota-update-api";

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

function readApiResponseFixture(fileName: string): unknown {
  return JSON.parse(
    readFileSync(
      join(
        dirname(fileURLToPath(import.meta.url)),
        "fixtures",
        fileName,
      ),
      "utf8",
    ),
  );
}

const apiResponseFixture = readApiResponseFixture(
  "pos-handheld-ota-api-response.json",
);
const androidApiResponseFixture = readApiResponseFixture(
  "pos-handheld-android-ota-api-response.json",
);

const metadata = Object.freeze({
  runtimeVersion: "1.2.3",
  currentUpdateId: "ios-update-8",
  currentUpdateGroupId: "223e4567-e89b-42d3-a456-426614174000",
});

const expectedPolicy = Object.freeze({
  state: "optional",
  policyVersion: "9",
  appKey: "pos-handheld",
  projectName: "hb-pos-handheld",
  platform: "iOS",
  required: false,
  channel: "pos-handheld-production",
  runtimeVersion: "1.2.3",
  updateId: "ios-update-9",
  updateGroupId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  releaseMessage: null,
});

function createApi(payload: unknown, platform: "iOS" | "Android" = "iOS") {
  const transport = new RecordingTransport(payload);
  return {
    api: new HbposPosHandheldOtaUpdateApi(
      transport,
      platform,
      "pos-handheld-production",
    ),
    transport,
  };
}

test("POS API response fixture 端到端解包为通用 handheld 精确合同", async () => {
  const { api, transport } = createApi(apiResponseFixture);

  assert.deepEqual(await api.getPolicy(metadata), expectedPolicy);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/app-updates/pos-handheld/ota",
      params: metadata,
    },
  ]);
});

test("Android POS API fixture 使用同一 OTA path，并精确绑定 Android platform", async () => {
  const { api, transport } = createApi(
    androidApiResponseFixture,
    "Android",
  );

  const policy = await api.getPolicy(metadata);
  assert.equal(policy.platform, "Android");
  assert.equal(policy.updateId, "android-update-10");
  assert.deepEqual(transport.requests[0], {
    method: "GET",
    url: "/api/v1/app-updates/pos-handheld/ota",
    params: metadata,
  });
});

test("未知本机平台在触网前 fail closed", async () => {
  const transport = new RecordingTransport(apiResponseFixture);
  const api = new HbposPosHandheldOtaUpdateApi(
    transport,
    "web" as never,
    "pos-handheld-production",
  );
  await assert.rejects(() => api.getPolicy(metadata), /platform/i);
  assert.equal(transport.requests.length, 0);
});

test("OTA API 对空 current IDs 使用省略参数，并接受通用 updateId", async () => {
  const noneResponse = {
    success: true,
    data: {
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
    },
  };
  const { api, transport } = createApi(noneResponse);

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
    () =>
      api.getPolicy({
        ...metadata,
        currentUpdateId: "invalid update id",
      }),
    /currentUpdateId is invalid/,
  );
  assert.equal(transport.requests.length, 1);
});

test("OTA response 必须匹配本机平台、请求 runtime 与配置 channel", async (t) => {
  const cases = [
    ["platform", "Android"],
    ["runtimeVersion", "9.9.9"],
    ["channel", "pos-handheld-preview"],
  ] as const;

  for (const [field, value] of cases) {
    await t.test(field, async () => {
      const payload = {
        success: true,
        data: { ...expectedPolicy, [field]: value },
      };
      const { api } = createApi(payload);
      await assert.rejects(() => api.getPolicy(metadata), new RegExp(field, "i"));
    });
  }
});

test("production 客户端仅接受本平台 legacy 或唯一 release channel", async () => {
  for (const [platform, channel] of [
    ["iOS", "pos-handheld-production"],
    ["iOS", "pos-handheld-production-ios-release-20260827t101500z-a1b2c3"],
    ["Android", "pos-handheld-production-android-release-20260827t101500z-d4e5f6"],
  ] as const) {
    const fixture = platform === "iOS"
      ? expectedPolicy
      : {
          ...expectedPolicy,
          platform: "Android",
          updateId: "android-update-10",
        };
    const { api } = createApi(
      { success: true, data: { ...fixture, channel } },
      platform,
    );
    assert.equal((await api.getPolicy(metadata)).channel, channel);
  }
});

test("release channel 必须由 production 原生 channel 与真机平台派生", async (t) => {
  const rejected = [
    "pos-handheld-production-android-release-20260827t101500z-a1b2c3",
    "pos-handheld-preview-ios-release-20260827t101500z-a1b2c3",
    "pos-handheld-production-ios-release-",
    "attacker-production-ios-release-20260827t101500z-a1b2c3",
  ];

  for (const channel of rejected) {
    await t.test(channel, async () => {
      const { api } = createApi({
        success: true,
        data: { ...expectedPolicy, channel },
      });
      await assert.rejects(() => api.getPolicy(metadata), /channel/i);
    });
  }

  const transport = new RecordingTransport(apiResponseFixture);
  const previewApi = new HbposPosHandheldOtaUpdateApi(
    transport,
    "iOS",
    "pos-handheld-preview",
  );
  await assert.rejects(() => previewApi.getPolicy(metadata), /production.*channel/i);
  assert.equal(transport.requests.length, 0);
});

test("OTA response 对 appKey、required/state、更新身份与额外字段 fail closed", async (t) => {
  const malformed = [
    { ...expectedPolicy, appKey: "pos-ipad" },
    { ...expectedPolicy, required: true },
    { ...expectedPolicy, updateId: "bad update id" },
    { ...expectedPolicy, updateGroupId: "not-a-uuid" },
    { ...expectedPolicy, accessToken: "forbidden" },
  ];

  for (const [index, data] of malformed.entries()) {
    await t.test(String(index), async () => {
      const { api } = createApi({ success: true, data });
      await assert.rejects(() => api.getPolicy(metadata));
    });
  }
});

test("OTA API 拒绝失败 envelope", async () => {
  const { api } = createApi({
    success: false,
    data: expectedPolicy,
    errorCode: "DENIED",
  });
  await assert.rejects(() => api.getPolicy(metadata), /rejected/);
});
