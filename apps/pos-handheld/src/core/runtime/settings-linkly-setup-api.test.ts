import assert from "node:assert/strict";
import test from "node:test";

import { HbposApiError } from "../api/hbpos-api";
import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "../api/hbpos-api";

import {
  HbposSettingsLinklySetupApi,
  type SettingsLinklyHealthSnapshot,
} from "./settings-linkly-setup-api";

class QueueTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(private readonly responses: readonly unknown[]) {}

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    const response = this.responses[this.requests.length - 1];
    if (response instanceof Error) throw response;
    return {
      status: 200,
      data: response as T,
    };
  }
}

test("Linkly setup health 只读取公开门店/设备标识和后端就绪状态", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Sandbox",
        storeCode: " STORE-01 ",
        deviceCode: " IPAD-01 ",
        isReady: true,
        checks: [
          {
            code: "STORE_CREDENTIAL",
            isReady: true,
            message: "ready",
          },
        ],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const result = await subject.readState("Sandbox", signal);

  assert.deepEqual(result, {
    environment: "Sandbox",
    storeCode: "STORE-01",
    deviceCode: "IPAD-01",
    isReady: true,
    checks: [
      {
        code: "STORE_CREDENTIAL",
        isReady: true,
        message: "ready",
      },
    ],
  } satisfies SettingsLinklyHealthSnapshot);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/linkly/cloud-backend/health",
      params: { environment: "Sandbox" },
      signal,
    },
  ]);
});

test("Linkly 配对 POST 只发送 Environment 与六位数字 PairCode", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        storeCode: "STORE-01",
        deviceCode: "IPAD-01",
        hasSecret: true,
        posId: "550e8400-e29b-41d4-a716-446655440000",
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  assert.deepEqual(
    await subject.pair("Production", "123456", signal),
    { status: "completed" },
  );
  assert.deepEqual(transport.requests, [
    {
      method: "POST",
      url: "/api/v1/linkly/cloud-backend/pair",
      data: {
        environment: "Production",
        pairCode: "123456",
      },
      signal,
      timeoutMs: 270_000,
    },
  ]);
  assert.equal(
    JSON.stringify(transport.requests[0]).includes("password"),
    false,
  );
  assert.equal(
    JSON.stringify(transport.requests[0]).includes("username"),
    false,
  );
});

test("Linkly 配对码必须是六位数字且不发送非法请求", async () => {
  const transport = new QueueTransport([]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  for (const pairCode of ["12345", "1234567", "12A456", ""]) {
    await assert.rejects(
      () => subject.pair("Sandbox", pairCode, signal),
      /six digits/i,
    );
  }
  assert.deepEqual(transport.requests, []);
});

test("Linkly 配对无 HTTP 终态时返回 unknown，由上层刷新而不是重试 POST", async () => {
  const transport = new QueueTransport([
    new HbposApiError("network unavailable", {
      kind: "transport",
      code: "NO_HTTP_RESPONSE",
    }),
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Sandbox",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});

test("Linkly health environment 不匹配时拒绝响应，不回退为请求环境", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        storeCode: "STORE-01",
        deviceCode: "IPAD-01",
        isReady: false,
        checks: [],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  await assert.rejects(
    () => subject.readState("Sandbox", new AbortController().signal),
    /environment mismatch/i,
  );
});

test("Linkly 配对成功响应不完整时按 unknown 处理，避免重放已消费 PairCode", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        storeCode: "STORE-01",
        deviceCode: "IPAD-01",
        hasSecret: false,
        posId: "",
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Production",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});

test("Linkly 配对成功响应的 posId 不是 UUID v4 时按 unknown 处理", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        storeCode: "STORE-01",
        deviceCode: "IPAD-01",
        hasSecret: true,
        posId: "POS-01",
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Production",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});

test("Linkly 配对 502/504 结果不确定时刷新路径不重放 PairCode", async () => {
  for (const status of [502, 504]) {
    const transport = new QueueTransport([
      new HbposApiError("pair result uncertain", {
        kind: "http",
        status,
        code: status === 504 ? "LINKLY_PAIR_TIMEOUT" : "LINKLY_PAIR_FAILED",
      }),
    ]);
    const subject = new HbposSettingsLinklySetupApi(transport);

    assert.deepEqual(
      await subject.pair(
        "Sandbox",
        "123456",
        new AbortController().signal,
      ),
      { status: "unknown" },
    );
    assert.equal(transport.requests.length, 1);
  }
});

test("Linkly 配对 REQUEST_ABORTED 继续传播取消，不转成 unknown", async () => {
  const transport = new QueueTransport([
    new HbposApiError("cancelled", {
      kind: "transport",
      code: "REQUEST_ABORTED",
    }),
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  await assert.rejects(
    () => subject.pair("Sandbox", "123456", new AbortController().signal),
    (error: unknown) =>
      error instanceof HbposApiError && error.code === "REQUEST_ABORTED",
  );
});

test("Linkly 配对的确定性 HTTP 失败原样传播且不自动重试", async () => {
  for (const status of [409, 422, 500]) {
    const transport = new QueueTransport([
      new HbposApiError("pair rejected", {
        kind: "http",
        status,
        code: "LINKLY_PAIR_DEFINITE_FAILURE",
      }),
    ]);
    const subject = new HbposSettingsLinklySetupApi(transport);

    await assert.rejects(
      () => subject.pair("Sandbox", "123456", new AbortController().signal),
      (error: unknown) =>
        error instanceof HbposApiError && error.status === status,
    );
    assert.equal(transport.requests.length, 1);
  }
});

test("Linkly 上游成功但后端持久化失败时按 unknown 处理并禁止重放", async () => {
  const transport = new QueueTransport([
    new HbposApiError("terminal credential was not saved", {
      kind: "http",
      status: 500,
      code: "LINKLY_CLOUD_BACKEND_PAIR_PERSISTENCE_FAILED",
    }),
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Sandbox",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});
