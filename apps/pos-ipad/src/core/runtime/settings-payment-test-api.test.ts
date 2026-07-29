import assert from "node:assert/strict";
import test from "node:test";

import type {
  HbposTransport,
  HbposTransportRequest,
  HbposTransportResponse,
} from "../api/hbpos-api";

import { HbposSettingsPaymentTestApi } from "./settings-payment-test-api";

class QueueTransport implements HbposTransport {
  public readonly requests: HbposTransportRequest[] = [];

  public constructor(private readonly responses: unknown[]) {}

  public async request<T>(
    request: HbposTransportRequest,
  ): Promise<HbposTransportResponse<T>> {
    this.requests.push(request);
    return {
      status: 200,
      data: this.responses.shift() as T,
    };
  }
}

test("Square 测试读取候选环境/门店的设备列表并匹配公开 device id", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: [
        {
          id: "SQ-1",
          name: "Counter",
          status: "PAIRED",
          locationId: "LOC-1",
        },
      ],
    },
  ]);
  const subject = new HbposSettingsPaymentTestApi(transport);

  await subject.test("square", {
    provider: "square",
    square: {
      environment: "Sandbox",
      deviceId: "SQ-1",
      locationId: "LOC-1",
    },
    linkly: null,
  });

  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/square/devices",
      params: {
        environment: "Sandbox",
        locationId: "LOC-1",
      },
    },
  ]);
});

test("Square 候选设备不存在或门店不符时失败关闭", async () => {
  const subject = new HbposSettingsPaymentTestApi(
    new QueueTransport([
      {
        success: true,
        data: [
          {
            id: "SQ-1",
            status: "PAIRED",
            locationId: "OTHER",
          },
        ],
      },
    ]),
  );

  await assert.rejects(
    () =>
      subject.test("square", {
        provider: "square",
        square: {
          environment: "Production",
          deviceId: "SQ-1",
          locationId: "LOC-1",
        },
        linkly: null,
      }),
    /not available/i,
  );
});

test("Linkly 测试调用 Backend Async logon-test 且只接受 succeeded", async () => {
  const transport = new QueueTransport([
    {
      success: true,
      data: {
        succeeded: true,
        responseCode: "00",
      },
    },
  ]);
  const subject = new HbposSettingsPaymentTestApi(transport);

  await subject.test("linkly", {
    provider: "linkly",
    square: null,
    linkly: { environment: "Production" },
  });
  assert.deepEqual(transport.requests, [
    {
      method: "POST",
      url: "/api/v1/linkly/cloud-backend/logon-test",
      params: { environment: "Production" },
    },
  ]);
});

test("支付测试不接受未提供的 provider 配置", async () => {
  const subject = new HbposSettingsPaymentTestApi(
    new QueueTransport([]),
  );
  await assert.rejects(
    () =>
      subject.test("square", {
        provider: "linkly",
        square: null,
        linkly: { environment: "Production" },
      }),
    /configuration/i,
  );
});
