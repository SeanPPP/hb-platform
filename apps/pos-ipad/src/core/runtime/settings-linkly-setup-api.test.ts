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

test("Linkly Active health 携带已展示终端与选择 revision，Draft 仍走旧请求", async () => {
  const healthResponse = {
    success: true,
    data: {
      environment: "Sandbox",
      storeCode: "STORE-01",
      deviceCode: "IPAD-01",
      isReady: true,
      checks: [],
    },
  };
  const transport = new QueueTransport([healthResponse, healthResponse]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  await subject.readState("Sandbox", signal, {
    environment: "Sandbox",
    mode: "Active",
    selectedTerminalId: "terminal-2",
    selectionRevision: 9,
    terminals: [],
  });
  await subject.readState("Sandbox", signal, {
    environment: "Sandbox",
    mode: "Draft",
    selectedTerminalId: "terminal-2",
    selectionRevision: 0,
    terminals: [],
  });

  assert.deepEqual(transport.requests.map((request) => request.params), [
    {
      environment: "Sandbox",
      terminalId: "terminal-2",
      selectionRevision: 9,
    },
    { environment: "Sandbox" },
  ]);
});

test("Linkly 终端列表只保留安全摘要并拒绝凭据字段", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Sandbox",
        mode: "Active",
        selectedTerminalId: "terminal-1",
        selectionRevision: 7,
        terminals: [
          {
            terminalId: " terminal-1 ",
            laneNo: 1,
            displayName: " Front counter ",
            pairingState: "Ready",
            isBusy: false,
            isReady: true,
            lastHealthStatus: "ready",
            lastHealthAt: "2026-09-02T01:00:00.000Z",
            username: "must-not-leak",
            password: "must-not-leak",
            secret: "must-not-leak",
          },
        ],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const result = await subject.readTerminals("Sandbox", signal);

  assert.deepEqual(result, {
    environment: "Sandbox",
    mode: "Active",
    selectedTerminalId: "terminal-1",
    selectionRevision: 7,
    terminals: [
      {
        terminalId: "terminal-1",
        laneNo: 1,
        displayName: "Front counter",
        pairingState: "Ready",
        isBusy: false,
        isReady: true,
        lastHealthStatus: "ready",
        lastHealthAt: "2026-09-02T01:00:00.000Z",
      },
    ],
  });
  assert.equal(JSON.stringify(result).includes("must-not-leak"), false);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/linkly/cloud-backend/terminals",
      params: { environment: "Sandbox" },
      signal,
    },
  ]);
});

test("Linkly 终端列表兼容旧服务、Draft 与 Active 未选择的 null revision", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Sandbox",
        selectedTerminalId: null,
        selectionRevision: null,
        terminals: [],
      },
    },
    {
      success: true,
      data: {
        environment: "Sandbox",
        mode: "Draft",
        selectedTerminalId: null,
        selectionRevision: null,
        terminals: [],
      },
    },
    {
      success: true,
      data: {
        environment: "Sandbox",
        mode: "Active",
        selectedTerminalId: null,
        selectionRevision: null,
        terminals: [{
          terminalId: "terminal-1",
          laneNo: 1,
          displayName: "Front",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
        }],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const legacy = await subject.readTerminals("Sandbox", signal);
  const draft = await subject.readTerminals("Sandbox", signal);
  const activeUnselected = await subject.readTerminals("Sandbox", signal);

  assert.deepEqual(
    { mode: legacy.mode, selectionRevision: legacy.selectionRevision },
    { mode: "Legacy", selectionRevision: 0 },
  );
  assert.deepEqual(
    { mode: draft.mode, selectionRevision: draft.selectionRevision },
    { mode: "Draft", selectionRevision: 0 },
  );
  assert.deepEqual(
    {
      mode: activeUnselected.mode,
      selectedTerminalId: activeUnselected.selectedTerminalId,
      selectionRevision: activeUnselected.selectionRevision,
    },
    { mode: "Active", selectedTerminalId: null, selectionRevision: 0 },
  );
});

test("Linkly 终端切换发送 revision 并以随后重读为权威", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        mode: "Active",
        selectedTerminalId: "terminal-2",
        selectionRevision: 4,
      },
    },
    {
      success: true,
      data: {
        environment: "Production",
        selectedTerminalId: "terminal-2",
        selectionRevision: 4,
        terminals: [
          {
            terminalId: "terminal-2",
            laneNo: 2,
            displayName: "Returns",
            pairingState: "Ready",
            isBusy: false,
            isReady: true,
          },
        ],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const result = await subject.selectTerminal(
    "Production",
    "terminal-2",
    3,
    signal,
  );

  assert.equal(result.selectedTerminalId, "terminal-2");
  assert.equal(result.selectionRevision, 4);
  assert.deepEqual(
    transport.requests.map(({ method, url, data, params }) => ({
      method,
      url,
      data,
      params,
    })),
    [
      {
        method: "PUT",
        url: "/api/v1/linkly/cloud-backend/terminal-selection",
        data: {
          environment: "Production",
          terminalId: "terminal-2",
          expectedRevision: 3,
        },
        params: undefined,
      },
      {
        method: "GET",
        url: "/api/v1/linkly/cloud-backend/terminals",
        data: undefined,
        params: { environment: "Production" },
      },
    ],
  );
});

test("Linkly 首次选择以 revision 0 提交并接受服务端 revision 1", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        mode: "Active",
        selectedTerminalId: "terminal-1",
        selectionRevision: 1,
      },
    },
    {
      success: true,
      data: {
        environment: "Production",
        mode: "Active",
        selectedTerminalId: "terminal-1",
        selectionRevision: 1,
        terminals: [{
          terminalId: "terminal-1",
          laneNo: 1,
          displayName: "Front",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
        }],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const result = await subject.selectTerminal(
    "Production",
    "terminal-1",
    0,
    signal,
  );

  assert.equal(result.selectedTerminalId, "terminal-1");
  assert.equal(result.selectionRevision, 1);
  assert.equal(transport.requests[0]?.method, "PUT");
  assert.deepEqual(transport.requests[0]?.data, {
    environment: "Production",
    terminalId: "terminal-1",
    expectedRevision: 0,
  });
});

test("Linkly 配对 POST 只发送 TerminalId、Environment 与六位数字 PairCode", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        terminalId: "terminal-2",
        environment: "Production",
        displayName: "Returns",
        pairingState: "Ready",
        isReady: true,
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  assert.deepEqual(
    await subject.pair("Production", "terminal-2", "123456", signal),
    { status: "completed" },
  );
  assert.deepEqual(transport.requests, [
    {
      method: "POST",
      url: "/api/v1/linkly/cloud-backend/terminals/terminal-2/pair",
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
      () => subject.pair("Sandbox", "terminal-1", pairCode, signal),
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
      "terminal-1",
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
        terminalId: "terminal-1",
        environment: "Production",
        displayName: "Front",
        pairingState: "Unknown",
        isReady: false,
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Production",
      "terminal-1",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});

test("Linkly 配对成功响应的 terminalId 不匹配时按 unknown 处理", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        terminalId: "terminal-other",
        environment: "Production",
        displayName: "Other",
        pairingState: "Ready",
        isReady: true,
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Production",
      "terminal-1",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});

test("Linkly 配对 408 与全部 5xx 结果不确定时刷新路径不重放 PairCode", async () => {
  for (const status of [408, 500, 502, 503, 504]) {
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
        "terminal-1",
        "123456",
        new AbortController().signal,
      ),
      { status: "unknown" },
    );
    assert.equal(transport.requests.length, 1);
  }
});

test("Linkly 配对发送前已取消时零请求并传播 AbortError", async () => {
  const transport = new QueueTransport([]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const controller = new AbortController();
  controller.abort();

  await assert.rejects(
    () => subject.pair("Sandbox", "terminal-1", "123456", controller.signal),
    (error: unknown) => error instanceof Error && error.name === "AbortError",
  );
  assert.equal(transport.requests.length, 0);
});

test("Linkly 配对 POST 已交给 transport 后 REQUEST_ABORTED 按 unknown 处理", async () => {
  const transport = new QueueTransport([
    new HbposApiError("cancelled", {
      kind: "transport",
      code: "REQUEST_ABORTED",
    }),
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);

  assert.deepEqual(
    await subject.pair(
      "Sandbox",
      "terminal-1",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});

test("Linkly 配对的确定性 4xx 失败原样传播且不自动重试", async () => {
  for (const status of [409, 422]) {
    const transport = new QueueTransport([
      new HbposApiError("pair rejected", {
        kind: "http",
        status,
        code: "LINKLY_PAIR_DEFINITE_FAILURE",
      }),
    ]);
    const subject = new HbposSettingsLinklySetupApi(transport);

    await assert.rejects(
      () =>
        subject.pair(
          "Sandbox",
          "terminal-1",
          "123456",
          new AbortController().signal,
        ),
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
      "terminal-1",
      "123456",
      new AbortController().signal,
    ),
    { status: "unknown" },
  );
  assert.equal(transport.requests.length, 1);
});
