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
        assignedDeviceCode: null,
        assignmentRevision: 0,
        terminalVersion: null,
      },
    ],
    devices: [],
    lineManagementSupported: false,
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

test("Linkly 线路目录保留分配版本与同店 POS 设备，旧服务则明确标记不支持管理", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Production",
        mode: "Active",
        selectedTerminalId: "terminal-1",
        selectionRevision: 8,
        terminals: [{
          terminalId: "terminal-1",
          laneNo: 1,
          displayName: "Front",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
          assignedDeviceCode: " POS-01 ",
          assignmentRevision: 4,
          terminalVersion: " 0x00000000000007D3 ",
        }],
        devices: [{
          deviceCode: " POS-02 ",
          deviceSystem: " iPadOS ",
          isAvailable: true,
          selectedTerminalId: null,
          selectionRevision: 0,
        }],
      },
    },
    {
      success: true,
      data: {
        environment: "Production",
        mode: "Active",
        selectedTerminalId: null,
        selectionRevision: null,
        terminals: [{
          terminalId: "legacy-terminal",
          laneNo: 2,
          displayName: "Legacy",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
        }],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const current = await subject.readTerminals("Production", signal);
  const legacy = await subject.readTerminals("Production", signal);

  assert.equal(current.lineManagementSupported, true);
  assert.deepEqual(current.terminals[0], {
    terminalId: "terminal-1",
    laneNo: 1,
    displayName: "Front",
    pairingState: "Ready",
    isBusy: false,
    isReady: true,
    lastHealthStatus: null,
    lastHealthAt: null,
    assignedDeviceCode: " POS-01 ",
    assignmentRevision: 4,
    terminalVersion: " 0x00000000000007D3 ",
  });
  assert.deepEqual(current.devices, [{
    deviceCode: " POS-02 ",
    deviceSystem: "iPadOS",
    isAvailable: true,
    selectedTerminalId: null,
    selectionRevision: 0,
  }]);
  assert.equal(legacy.lineManagementSupported, false);
  assert.equal(legacy.terminals[0]?.terminalVersion, null);
  assert.deepEqual(legacy.devices, []);
});

test("Linkly 单线路连接测试携带完整并发前置条件并校验回包作用域", async () => {
  const transport = new QueueTransport([{
    success: true,
    data: {
      terminalId: "terminal-2",
      environment: "Sandbox",
      terminalVersion: "v12",
      assignedDeviceCode: null,
      assignmentRevision: 0,
      succeeded: false,
      status: "unreachable",
      checkedAt: "2026-09-10T02:03:04Z",
      message: "Terminal did not answer",
      responseCode: "TIMEOUT",
    },
  }]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const result = await subject.testTerminalConnection(
    "Sandbox",
    {
      terminalId: "terminal-2",
      terminalVersion: "v12",
      assignedDeviceCode: null,
      assignmentRevision: 0,
    },
    signal,
  );

  assert.equal(result.status, "unreachable");
  assert.equal(result.responseCode, "TIMEOUT");
  assert.deepEqual(transport.requests[0], {
    method: "POST",
    url: "/api/v1/linkly/cloud-backend/terminals/terminal-2/connection-test",
    data: {
      environment: "Sandbox",
      expectedTerminalVersion: "v12",
      expectedAssignedDeviceCode: null,
      expectedAssignmentRevision: 0,
    },
    signal,
  });
});

test("Linkly 线路分配 PUT 原样发送目标设备旧选择并直接采用完整权威快照", async () => {
  const response = {
    success: true,
    data: {
      environment: "Production",
      mode: "Active",
      selectedTerminalId: "terminal-3",
      selectionRevision: 11,
      terminals: [
        {
          terminalId: "terminal-3",
          laneNo: 3,
          displayName: "Returns",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
          assignedDeviceCode: "POS-02",
          assignmentRevision: 11,
          terminalVersion: "v9",
        },
        {
          terminalId: "terminal-1",
          laneNo: 1,
          displayName: "Front",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
          assignedDeviceCode: null,
          assignmentRevision: 0,
          terminalVersion: "v3-next",
        },
      ],
      devices: [{
        deviceCode: "POS-02",
        deviceSystem: "iPadOS",
        isAvailable: true,
        selectedTerminalId: "terminal-3",
        selectionRevision: 11,
      }],
    },
  };
  const transport = new QueueTransport([response]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const signal = new AbortController().signal;

  const result = await subject.assignTerminal(
    "Production",
    {
      terminalId: "terminal-3",
      terminalVersion: "v8",
      assignedDeviceCode: null,
      assignmentRevision: 8,
      targetDeviceCode: "POS-02",
      expectedTargetTerminalId: "terminal-1",
      expectedTargetSelectionRevision: 10,
    },
    signal,
  );

  assert.equal(result.terminals[0]?.assignedDeviceCode, "POS-02");
  assert.deepEqual(transport.requests[0], {
    method: "PUT",
    url: "/api/v1/linkly/cloud-backend/terminals/terminal-3/assignment",
    data: {
      environment: "Production",
      expectedTerminalVersion: "v8",
      expectedAssignedDeviceCode: null,
      expectedAssignmentRevision: 8,
      targetDeviceCode: "POS-02",
      expectedTargetTerminalId: "terminal-1",
      expectedTargetSelectionRevision: 10,
    },
    signal,
  });
});

test("Linkly 线路分配保留 opaque CAS token，禁止 trim 或截断", async () => {
  const opaqueVersion = " version with boundary spaces ";
  const transport = new QueueTransport([{
    success: true,
    data: {
      environment: "Production",
      mode: "Active",
      selectedTerminalId: "terminal-3",
      selectionRevision: 7,
      terminals: [{
        terminalId: "terminal-3",
        laneNo: 3,
        displayName: "Returns",
        pairingState: "Ready",
        isBusy: false,
        isReady: true,
        lastHealthStatus: null,
        lastHealthAt: null,
        assignedDeviceCode: "POS-02",
        assignmentRevision: 7,
        terminalVersion: "next-version",
      }],
      devices: [{
        deviceCode: "POS-02",
        deviceSystem: "iPadOS",
        isAvailable: true,
        selectedTerminalId: "terminal-3",
        selectionRevision: 7,
      }],
    },
  }]);

  await new HbposSettingsLinklySetupApi(transport).assignTerminal(
    "Production",
    {
      terminalId: "terminal-3",
      terminalVersion: opaqueVersion,
      assignedDeviceCode: null,
      assignmentRevision: 6,
      targetDeviceCode: "POS-02",
      expectedTargetTerminalId: null,
      expectedTargetSelectionRevision: 6,
    },
    new AbortController().signal,
  );

  assert.equal(
    (transport.requests[0]?.data as { expectedTerminalVersion: string })
      .expectedTerminalVersion,
    opaqueVersion,
  );
  await assert.rejects(
    () => new HbposSettingsLinklySetupApi(new QueueTransport([])).assignTerminal(
      "Production",
      {
        terminalId: "terminal-3",
        terminalVersion: "v".repeat(161),
        assignedDeviceCode: null,
        assignmentRevision: 6,
        targetDeviceCode: null,
        expectedTargetTerminalId: null,
        expectedTargetSelectionRevision: 0,
      },
      new AbortController().signal,
    ),
    /metadata is invalid/u,
  );
});

test("Linkly 实际换线要求来源与被替换线路清除旧健康结果", async () => {
  const response = {
    success: true,
    data: {
      environment: "Production",
      mode: "Active",
      selectedTerminalId: "terminal-2",
      selectionRevision: 5,
      terminals: [
        {
          terminalId: "terminal-2",
          laneNo: 2,
          displayName: "Returns",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
          lastHealthStatus: "Ready",
          lastHealthAt: "2026-09-10T02:03:04Z",
          assignedDeviceCode: "IPAD-01",
          assignmentRevision: 5,
          terminalVersion: "v5",
        },
        {
          terminalId: "terminal-1",
          laneNo: 1,
          displayName: "Front",
          pairingState: "Ready",
          isBusy: false,
          isReady: true,
          lastHealthStatus: null,
          lastHealthAt: null,
          assignedDeviceCode: null,
          assignmentRevision: 0,
          terminalVersion: "v2-next",
        },
      ],
      devices: [{
        deviceCode: "IPAD-01",
        deviceSystem: "iPadOS",
        isAvailable: true,
        selectedTerminalId: "terminal-2",
        selectionRevision: 5,
      }],
    },
  };
  const subject = new HbposSettingsLinklySetupApi(new QueueTransport([response]));

  await assert.rejects(
    () => subject.assignTerminal("Production", {
      terminalId: "terminal-2",
      terminalVersion: "v4",
      assignedDeviceCode: null,
      assignmentRevision: 4,
      targetDeviceCode: "IPAD-01",
      expectedTargetTerminalId: "terminal-1",
      expectedTargetSelectionRevision: 4,
    }, new AbortController().signal),
    /unconfirmed/u,
  );

  const source = response.data.terminals[0] as {
    lastHealthStatus: string | null;
    lastHealthAt: string | null;
  };
  const replaced = response.data.terminals[1] as {
    lastHealthStatus: string | null;
    lastHealthAt: string | null;
  };
  source.lastHealthStatus = null;
  source.lastHealthAt = null;
  replaced.lastHealthStatus = "Ready";
  replaced.lastHealthAt = "2026-09-10T02:03:04Z";
  await assert.rejects(
    () => new HbposSettingsLinklySetupApi(
      new QueueTransport([response]),
    ).assignTerminal("Production", {
      terminalId: "terminal-2",
      terminalVersion: "v4",
      assignedDeviceCode: null,
      assignmentRevision: 4,
      targetDeviceCode: "IPAD-01",
      expectedTargetTerminalId: "terminal-1",
      expectedTargetSelectionRevision: 4,
    }, new AbortController().signal),
    /unconfirmed/u,
  );
});

test("Linkly 幂等重复分配保留版本、revision 与健康结果", async () => {
  const transport = new QueueTransport([{
    success: true,
    data: {
      environment: "Production",
      mode: "Active",
      selectedTerminalId: "terminal-2",
      selectionRevision: 5,
      terminals: [{
        terminalId: "terminal-2",
        laneNo: 2,
        displayName: "Returns",
        pairingState: "Ready",
        isBusy: false,
        isReady: true,
        lastHealthStatus: "Ready",
        lastHealthAt: "2026-09-10T02:03:04Z",
        assignedDeviceCode: "IPAD-01",
        assignmentRevision: 5,
        terminalVersion: "v5",
      }],
      devices: [{
        deviceCode: "IPAD-01",
        deviceSystem: "iPadOS",
        isAvailable: true,
        selectedTerminalId: "terminal-2",
        selectionRevision: 5,
      }],
    },
  }]);

  const result = await new HbposSettingsLinklySetupApi(transport).assignTerminal(
    "Production",
    {
      terminalId: "terminal-2",
      terminalVersion: "v5",
      assignedDeviceCode: "IPAD-01",
      assignmentRevision: 5,
      targetDeviceCode: "IPAD-01",
      expectedTargetTerminalId: "terminal-2",
      expectedTargetSelectionRevision: 5,
    },
    new AbortController().signal,
  );

  assert.equal(result.terminals[0]?.terminalVersion, "v5");
  assert.equal(result.terminals[0]?.lastHealthStatus, "Ready");
});

test("Linkly 转移按目标 selection revision 确认，不与来源旧 revision 比较", async () => {
  const result = await new HbposSettingsLinklySetupApi(new QueueTransport([
    assignmentResponse({
      sourceAssignedDeviceCode: "TARGET-POS",
      sourceRevision: 11,
      targetDeviceCode: "TARGET-POS",
      targetRevision: 11,
      includeDisplacedLine: true,
      includePreviousOwner: true,
    }),
  ])).assignTerminal("Production", {
    terminalId: "source-line",
    terminalVersion: "source-v1",
    assignedDeviceCode: "OLD-POS",
    assignmentRevision: 900_000,
    targetDeviceCode: "TARGET-POS",
    expectedTargetTerminalId: "displaced-line",
    expectedTargetSelectionRevision: 10,
  }, new AbortController().signal);

  assert.equal(result.terminals[0]?.assignmentRevision, 11);
  assert.deepEqual(
    result.devices?.find((device) => device.deviceCode === "OLD-POS"),
    {
      deviceCode: "OLD-POS",
      deviceSystem: "iPadOS",
      isAvailable: true,
      selectedTerminalId: null,
      selectionRevision: 0,
    },
  );
  assert.equal(result.terminals[1]?.assignmentRevision, 0);
});

test("Linkly 解绑确认来源与仍注册旧 owner 的 revision 均归零", async () => {
  const result = await new HbposSettingsLinklySetupApi(new QueueTransport([
    assignmentResponse({
      sourceAssignedDeviceCode: null,
      sourceRevision: 0,
      targetDeviceCode: null,
      targetRevision: 0,
      includeDisplacedLine: false,
      includePreviousOwner: true,
    }),
  ])).assignTerminal("Production", {
    terminalId: "source-line",
    terminalVersion: "source-v1",
    assignedDeviceCode: "OLD-POS",
    assignmentRevision: 900_000,
    targetDeviceCode: null,
    expectedTargetTerminalId: null,
    expectedTargetSelectionRevision: 0,
  }, new AbortController().signal);

  assert.equal(result.terminals[0]?.assignedDeviceCode, null);
  assert.equal(result.terminals[0]?.assignmentRevision, 0);
  assert.equal(result.devices?.[0]?.selectedTerminalId, null);
  assert.equal(result.devices?.[0]?.selectionRevision, 0);
});

test("Linkly 新目标无旧 selection 时接受低于来源旧 revision 的正 revision", async () => {
  const result = await new HbposSettingsLinklySetupApi(new QueueTransport([
    assignmentResponse({
      sourceAssignedDeviceCode: "NEW-POS",
      sourceRevision: 7,
      targetDeviceCode: "NEW-POS",
      targetRevision: 7,
      includeDisplacedLine: false,
      // 历史 owner 的 registration 已删除时不会出现在设备目录中。
      includePreviousOwner: false,
    }),
  ])).assignTerminal("Production", {
    terminalId: "source-line",
    terminalVersion: "source-v1",
    assignedDeviceCode: "OLD-POS",
    assignmentRevision: 900_000,
    targetDeviceCode: "NEW-POS",
    expectedTargetTerminalId: null,
    expectedTargetSelectionRevision: 0,
  }, new AbortController().signal);

  assert.equal(result.terminals[0]?.assignmentRevision, 7);
});

test("Linkly 转移后来源与目标 revision 不一致时拒绝权威回包", async () => {
  const subject = new HbposSettingsLinklySetupApi(new QueueTransport([
    assignmentResponse({
      sourceAssignedDeviceCode: "TARGET-POS",
      sourceRevision: 12,
      targetDeviceCode: "TARGET-POS",
      targetRevision: 11,
      includeDisplacedLine: true,
      includePreviousOwner: true,
    }),
  ]));

  await assert.rejects(
    () => subject.assignTerminal("Production", {
      terminalId: "source-line",
      terminalVersion: "source-v1",
      assignedDeviceCode: "OLD-POS",
      assignmentRevision: 900_000,
      targetDeviceCode: "TARGET-POS",
      expectedTargetTerminalId: "displaced-line",
      expectedTargetSelectionRevision: 10,
    }, new AbortController().signal),
    /unconfirmed/u,
  );
});

test("Linkly 已有目标 selection 从 revision 10 跳到 12 时拒绝权威回包", async () => {
  const subject = new HbposSettingsLinklySetupApi(new QueueTransport([
    assignmentResponse({
      sourceAssignedDeviceCode: "TARGET-POS",
      sourceRevision: 12,
      targetDeviceCode: "TARGET-POS",
      targetRevision: 12,
      includeDisplacedLine: true,
      includePreviousOwner: true,
    }),
  ]));

  await assert.rejects(
    () => subject.assignTerminal("Production", {
      terminalId: "source-line",
      terminalVersion: "source-v1",
      assignedDeviceCode: "OLD-POS",
      assignmentRevision: 900_000,
      targetDeviceCode: "TARGET-POS",
      expectedTargetTerminalId: "displaced-line",
      expectedTargetSelectionRevision: 10,
    }, new AbortController().signal),
    /unconfirmed/u,
  );
});

test("Linkly 分配 PUT 结果不明时只 GET 核对，确认已提交后返回快照且不重放", async () => {
  const transport = new QueueTransport([
    new HbposApiError("timeout", { kind: "transport", code: "REQUEST_TIMEOUT" }),
    {
      success: true,
      data: {
        environment: "Production",
        mode: "Active",
        selectedTerminalId: "terminal-2",
        selectionRevision: 5,
        terminals: [
          {
            terminalId: "terminal-2",
            laneNo: 2,
            displayName: "Returns",
            pairingState: "Ready",
            isBusy: false,
            isReady: true,
            assignedDeviceCode: "IPAD-01",
            assignmentRevision: 5,
            terminalVersion: "v5",
          },
          {
            terminalId: "terminal-1",
            laneNo: 1,
            displayName: "Front",
            pairingState: "Ready",
            isBusy: false,
            isReady: true,
            assignedDeviceCode: null,
            assignmentRevision: 0,
            terminalVersion: "v2-next",
          },
        ],
        devices: [{
          deviceCode: "IPAD-01",
          deviceSystem: "iPadOS",
          isAvailable: true,
          selectedTerminalId: "terminal-2",
          selectionRevision: 5,
        }],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  const result = await subject.assignTerminal("Production", {
    terminalId: "terminal-2",
    terminalVersion: "v4",
    assignedDeviceCode: null,
    assignmentRevision: 4,
    targetDeviceCode: "IPAD-01",
    expectedTargetTerminalId: "terminal-1",
    expectedTargetSelectionRevision: 4,
  }, new AbortController().signal);

  assert.equal(result.terminals[0]?.assignedDeviceCode, "IPAD-01");
  assert.deepEqual(transport.requests.map(({ method, url }) => ({ method, url })), [
    { method: "PUT", url: "/api/v1/linkly/cloud-backend/terminals/terminal-2/assignment" },
    { method: "GET", url: "/api/v1/linkly/cloud-backend/terminals" },
  ]);
});

test("Linkly 连接测试无 HTTP 终态时只 GET 核对且不重放 POST", async () => {
  const transport = new QueueTransport([
    new HbposApiError("timeout", { kind: "http", status: 504 }),
    {
      success: true,
      data: {
        environment: "Sandbox",
        mode: "Active",
        selectedTerminalId: null,
        selectionRevision: null,
        terminals: [],
        devices: [],
      },
    },
  ]);
  const subject = new HbposSettingsLinklySetupApi(transport);
  await assert.rejects(() => subject.testTerminalConnection("Sandbox", {
    terminalId: "terminal-1",
    terminalVersion: "v1",
    assignedDeviceCode: null,
    assignmentRevision: 0,
  }, new AbortController().signal));
  assert.deepEqual(transport.requests.map(({ method, url }) => ({ method, url })), [
    { method: "POST", url: "/api/v1/linkly/cloud-backend/terminals/terminal-1/connection-test" },
    { method: "GET", url: "/api/v1/linkly/cloud-backend/terminals" },
  ]);
});

function assignmentResponse(input: Readonly<{
  sourceAssignedDeviceCode: string | null;
  sourceRevision: number;
  targetDeviceCode: string | null;
  targetRevision: number;
  includeDisplacedLine: boolean;
  includePreviousOwner: boolean;
}>) {
  const terminals = [{
    terminalId: "source-line",
    laneNo: 2,
    displayName: "Source",
    pairingState: "Ready",
    isBusy: false,
    isReady: true,
    lastHealthStatus: null,
    lastHealthAt: null,
    assignedDeviceCode: input.sourceAssignedDeviceCode,
    assignmentRevision: input.sourceRevision,
    terminalVersion: "source-v2",
  }];
  if (input.includeDisplacedLine) {
    terminals.push({
      terminalId: "displaced-line",
      laneNo: 1,
      displayName: "Displaced",
      pairingState: "Ready",
      isBusy: false,
      isReady: true,
      lastHealthStatus: null,
      lastHealthAt: null,
      assignedDeviceCode: null,
      assignmentRevision: 0,
      terminalVersion: "displaced-v2",
    });
  }
  const devices: {
    deviceCode: string;
    deviceSystem: string;
    isAvailable: boolean;
    selectedTerminalId: string | null;
    selectionRevision: number;
  }[] = input.targetDeviceCode === null
    ? []
    : [{
        deviceCode: input.targetDeviceCode,
        deviceSystem: "iPadOS",
        isAvailable: true,
        selectedTerminalId: "source-line",
        selectionRevision: input.targetRevision,
      }];
  if (input.includePreviousOwner) {
    devices.push({
      deviceCode: "OLD-POS",
      deviceSystem: "iPadOS",
      isAvailable: true,
      selectedTerminalId: null,
      selectionRevision: 0,
    });
  }
  return {
    success: true,
    data: {
      environment: "Production",
      mode: "Active",
      selectedTerminalId: input.targetDeviceCode === null
        ? null
        : "source-line",
      selectionRevision: input.sourceRevision,
      terminals,
      devices,
    },
  };
}
