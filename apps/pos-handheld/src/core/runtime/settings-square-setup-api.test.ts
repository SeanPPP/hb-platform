import assert from "node:assert/strict";
import test from "node:test";

import {
  SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES,
  mergeSettingsSquareDevices,
} from "../../features/settings/settings-square-setup";
import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "../api/hbpos-api";

import { HbposSettingsSquareSetupApi } from "./settings-square-setup-api";

class QueueTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(private readonly responses: unknown[]) {}

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    const response = this.responses.shift();
    if (response instanceof Error) throw response;
    return {
      status: 200,
      data: response as T,
    };
  }
}

test("Sandbox 设备合并规范化 device: 前缀并补齐官方 checkout 测试设备", () => {
  const officialDevice = SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES[0];
  const merged = mergeSettingsSquareDevices("Sandbox", " LOC-1 ", [
    {
      id: ` device:${officialDevice.id.toUpperCase()} `,
      code: " API-1 ",
      name: " Paired device ",
      status: " PAIRED ",
      locationId: " LOC-1 ",
      sandboxTest: false,
    },
  ]);

  assert.equal(
    merged.filter((device) => device.id === officialDevice.id).length,
    1,
  );
  assert.equal(
    merged.length,
    SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES.length,
  );
  assert.deepEqual(merged[0], {
    id: officialDevice.id,
    code: "API-1",
    name: "Paired device",
    status: "PAIRED",
    locationId: "LOC-1",
    sandboxTest: true,
  });
  assert.equal(merged.at(-1)?.locationId, "LOC-1");
  assert.equal(merged.at(-1)?.sandboxTest, true);
  assert.deepEqual(
    mergeSettingsSquareDevices("Production", "LOC-1", []),
    [],
  );
});

test("setup API 复用 OpenAPI DTO、公开 token 状态并原样下传 AbortSignal", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        environment: "Sandbox",
        configured: true,
        enabled: true,
        updatedAt: " 2026-08-01T00:00:00Z ",
        accessToken: "must-not-leak",
      },
    },
    {
      success: true,
      data: [
        {
          id: " LOC-1 ",
          name: " Brisbane ",
          status: " ACTIVE ",
          currency: " AUD ",
          country: " AU ",
        },
      ],
    },
  ]);
  const subject = new HbposSettingsSquareSetupApi(transport);
  const signal = new AbortController().signal;

  const token = await subject.getSquareTokenStatus("Sandbox", signal);
  const locations = await subject.listSquareLocations("Sandbox", signal);

  assert.deepEqual(token, {
    environment: "Sandbox",
    configured: true,
    enabled: true,
    updatedAt: "2026-08-01T00:00:00Z",
  });
  assert.equal("accessToken" in token, false);
  assert.deepEqual(locations, [
    {
      id: "LOC-1",
      name: "Brisbane",
      status: "ACTIVE",
      currency: "AUD",
      country: "AU",
    },
  ]);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/square/token",
      params: { environment: "Sandbox" },
      signal,
    },
    {
      method: "GET",
      url: "/api/v1/square/locations",
      params: { environment: "Sandbox" },
      signal,
    },
  ]);
});

test("Sandbox 设备本地生成，配对码列表仍使用 location scope", async () => {
  const officialDevice = SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES[0];
  const transport = new QueueTransport([
    {
      success: true,
      data: [
        {
          id: "DC-1",
          code: "ABC-123",
          status: "PAIRED",
          deviceId: `device:${officialDevice.id}`,
          locationId: "LOC-1",
          name: "iPad Front",
        },
      ],
    },
  ]);
  const subject = new HbposSettingsSquareSetupApi(transport);
  const signal = new AbortController().signal;

  const devices = await subject.listSquareDevices(
    "Sandbox",
    " LOC-1 ",
    signal,
  );
  const deviceCodes = await subject.listSquareDeviceCodes(
    "Sandbox",
    " LOC-1 ",
    signal,
  );

  assert.equal(devices.length, SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES.length);
  assert.equal(
    devices.filter((device) => device.id === officialDevice.id).length,
    1,
  );
  assert.deepEqual(deviceCodes, [
    {
      id: "DC-1",
      code: "ABC-123",
      status: "PAIRED",
      deviceId: officialDevice.id,
      locationId: "LOC-1",
      name: "iPad Front",
    },
  ]);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/square/device-codes",
      params: { environment: "Sandbox", locationId: "LOC-1" },
      signal,
    },
  ]);
});

test("Sandbox 设备选项直接使用官方 checkout 测试终端，不调用 Devices API", async () => {
  const transport = new QueueTransport([
    new Error("Square Sandbox Devices API is unsupported"),
  ]);
  const subject = new HbposSettingsSquareSetupApi(transport);

  const devices = await subject.listSquareDevices(
    "Sandbox",
    "LOC-1",
    new AbortController().signal,
  );

  assert.equal(devices.length, SETTINGS_SQUARE_SANDBOX_CHECKOUT_DEVICES.length);
  assert.equal(devices.every((device) => device.sandboxTest), true);
  assert.deepEqual(transport.requests, []);
});

test("Production 设备选项仍由后端 Devices API 提供", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: [
        {
          id: "device:SQ-PROD-1",
          code: "SQ-1",
          name: "Counter",
          status: "PAIRED",
          locationId: "LOC-1",
        },
      ],
    },
  ]);
  const subject = new HbposSettingsSquareSetupApi(transport);
  const signal = new AbortController().signal;

  const devices = await subject.listSquareDevices(
    "Production",
    " LOC-1 ",
    signal,
  );

  assert.deepEqual(devices, [
    {
      id: "SQ-PROD-1",
      code: "SQ-1",
      name: "Counter",
      status: "PAIRED",
      locationId: "LOC-1",
      sandboxTest: false,
    },
  ]);
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/square/devices",
      params: { environment: "Production", locationId: "LOC-1" },
      signal,
    },
  ]);
});

test("创建配对码只发送上层提供的一次幂等键且 get 编码路径", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        id: "DC-NEW",
        code: "PAIR-1",
        status: "UNPAIRED",
        locationId: "LOC-1",
        name: "Front iPad",
      },
    },
    {
      success: true,
      data: {
        id: "DC /NEW",
        code: "PAIR-1",
        status: "PAIRED",
        deviceId: "device:SQ-1",
        locationId: "LOC-1",
        name: "Front iPad",
      },
    },
  ]);
  const subject = new HbposSettingsSquareSetupApi(transport);
  const signal = new AbortController().signal;

  await subject.createSquareDeviceCode(
    {
      environment: "Production",
      idempotencyKey: " setup-id-1 ",
      locationId: " LOC-1 ",
      name: " Front iPad ",
      productType: " TERMINAL_API ",
    },
    signal,
  );
  const refreshed = await subject.getSquareDeviceCode(
    "Production",
    " DC /NEW ",
    signal,
  );

  assert.equal(transport.requests.length, 2);
  assert.deepEqual(transport.requests[0], {
    method: "POST",
    url: "/api/v1/square/device-codes",
    data: {
      environment: "Production",
      idempotencyKey: "setup-id-1",
      locationId: "LOC-1",
      name: "Front iPad",
      productType: "TERMINAL_API",
    },
    signal,
  });
  assert.deepEqual(transport.requests[1], {
    method: "GET",
    url: "/api/v1/square/device-codes/DC%20%2FNEW",
    params: { environment: "Production" },
    signal,
  });
  assert.deepEqual(refreshed, {
    id: "DC /NEW",
    code: "PAIR-1",
    status: "PAIRED",
    deviceId: "SQ-1",
    locationId: "LOC-1",
    name: "Front iPad",
  });
  assert.equal(JSON.stringify(transport.requests).includes("token"), false);
});

test("创建配对码传输失败时不以新键或同一键自动重试", async () => {
  const transport = new QueueTransport([new Error("network lost")]);
  const subject = new HbposSettingsSquareSetupApi(transport);

  await assert.rejects(
    () =>
      subject.createSquareDeviceCode(
        {
          environment: "Production",
          idempotencyKey: "setup-id-once",
          locationId: "LOC-1",
          name: "Front iPad",
        },
        new AbortController().signal,
      ),
    /network lost/,
  );

  assert.equal(transport.requests.length, 1);
  assert.deepEqual(transport.requests[0]?.data, {
    environment: "Production",
    idempotencyKey: "setup-id-once",
    locationId: "LOC-1",
    name: "Front iPad",
  });
});
