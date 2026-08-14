import assert from "node:assert/strict";
import test from "node:test";

import {
  HbposApiError,
  HbposDeviceApi,
  resolveHbposDeviceSystem,
  unwrapHbposEnvelope,
  type HbposTransport,
  type HbposTransportRequest
} from "./hbpos-api";

test("EXPO_OS 只解析 iOS 与 Android，未知平台 fail-closed", () => {
  assert.equal(resolveHbposDeviceSystem("ios"), "iOS");
  assert.equal(resolveHbposDeviceSystem("android"), "Android");
  assert.throws(
    () => resolveHbposDeviceSystem("web"),
    /unsupported handheld platform/i,
  );
  assert.throws(
    () => resolveHbposDeviceSystem(undefined),
    /unsupported handheld platform/i,
  );
});

test("设备注册与验证按构造时注入的平台发送严格 payload", async () => {
  const calls: { url: string; data?: unknown }[] = [];
  const transport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      calls.push({ url: config.url, data: config.data });
      return {
        status: 200,
        data: {
          success: true,
          data: {
            deviceCode: "POS_1003_1011",
            storeCode: "1003",
            storeName: "Chermside",
            deviceStatus: -1,
            isAllowed: false
          }
        } as T
      };
    }
  };

  for (const deviceSystem of ["iOS", "Android"] as const) {
    const api = new HbposDeviceApi(transport, deviceSystem);
    const response = await api.register({
      storeCode: "1003",
      hardwareId: `INSTALL-${deviceSystem}`
    });
    await api.verify({
      deviceCode: "POS_1003_1011",
      storeCode: "1003",
      hardwareId: `INSTALL-${deviceSystem}`,
    });

    assert.equal(response.deviceCode, "POS_1003_1011");
  }
  assert.deepEqual(calls, [
    {
      url: "/api/v1/devices/register",
      data: {
        storeCode: "1003",
        hardwareId: "INSTALL-iOS",
        deviceSystem: "iOS"
      }
    },
    {
      url: "/api/v1/devices/verify",
      data: {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
        hardwareId: "INSTALL-iOS",
        deviceSystem: "iOS"
      }
    },
    {
      url: "/api/v1/devices/register",
      data: {
        storeCode: "1003",
        hardwareId: "INSTALL-Android",
        deviceSystem: "Android"
      }
    },
    {
      url: "/api/v1/devices/verify",
      data: {
        deviceCode: "POS_1003_1011",
        storeCode: "1003",
        hardwareId: "INSTALL-Android",
        deviceSystem: "Android"
      }
    },
  ]);
});

test("注册分店列表使用匿名 catalog 路径，并只返回有效活动分店", async () => {
  const calls: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      calls.push(config);
      return {
        status: 200,
        data: {
          success: true,
          data: [
            { storeCode: " 1003 ", storeName: " Chermside ", isActive: true },
            { storeCode: "1002", storeName: "Aspley", isActive: true },
            { storeCode: "1001", storeName: "Disabled", isActive: false },
            { storeCode: null, storeName: "Invalid", isActive: true }
          ]
        } as T
      };
    }
  };

  const stores = await new HbposDeviceApi(
    transport,
    "iOS",
  ).listRegistrationStores();

  assert.deepEqual(calls, [{
    method: "GET",
    url: "/api/v1/catalog/stores"
  }]);
  assert.deepEqual(stores, [
    { storeCode: "1002", storeName: "Aspley" },
    { storeCode: "1003", storeName: "Chermside" }
  ]);
});

test("业务 envelope 失败以非传输错误抛出，不能被离线回退吞掉", () => {
  assert.throws(
    () => unwrapHbposEnvelope({ success: false, errorCode: "CASHIER_LOGIN_FAILED", message: "denied" }),
    (error: unknown) => error instanceof HbposApiError
      && error.kind === "envelope"
      && error.code === "CASHIER_LOGIN_FAILED"
  );
});
