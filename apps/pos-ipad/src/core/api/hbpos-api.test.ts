import assert from "node:assert/strict";
import test from "node:test";

import {
  HbposApiError,
  HbposDeviceApi,
  unwrapHbposEnvelope,
  type HbposTransport,
  type HbposTransportRequest
} from "./hbpos-api";

test("设备注册固定发送 iPadOS，且只消费 Hbpos API envelope", async () => {
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

  const api = new HbposDeviceApi(transport);
  const response = await api.registerAppReview({
    storeCode: "1003",
    hardwareId: "INSTALL-001",
    provisioningCode: "OPEN-REVIEW-DEVICE"
  });

  assert.equal(response.deviceCode, "POS_1003_1011");
  assert.deepEqual(calls, [{
    url: "/api/v1/devices/app-review-register",
    data: {
      storeCode: "1003",
      hardwareId: "INSTALL-001",
      provisioningCode: "OPEN-REVIEW-DEVICE",
      deviceSystem: "iPadOS"
    }
  }]);
});

test("普通注册端点与 App Review 兼容入口保持显式分离", async () => {
  const calls: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      calls.push(config);
      return { status: 200, data: { success: true, data: { deviceStatus: -1, isAllowed: false } } as T };
    }
  };

  await new HbposDeviceApi(transport).register({ storeCode: "1003", hardwareId: "INSTALL-001" });

  assert.equal(calls[0]?.url, "/api/v1/devices/register");
});

test("设备开通码预览、兑换和换店使用固定合同且不提交客户端分店", async () => {
  const calls: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      calls.push(config);
      return {
        status: 200,
        data: {
          success: true,
          data: config.url.endsWith("/preview")
            ? {
                isAllowed: true,
                storeCode: "1042",
                storeName: "Sunnybank",
                deviceSystem: "iPadOS",
                expiresAtUtc: "2026-08-27T12:00:00.000Z",
              }
            : {
                isAllowed: true,
                deviceCode: "IPAD-1042-01",
                storeCode: "1042",
                storeName: "Sunnybank",
                deviceStatus: 1,
                authorizationCode: "device-secret",
              },
        } as T,
      };
    },
  };
  const api = new HbposDeviceApi(transport);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";

  const preview = await api.previewActivationCode({ activationCode });
  const redeemed = await api.redeemActivationCode({
    activationCode,
    hardwareId: "INSTALL-001",
  });
  const rebound = await api.rebindActivationCode({
    activationCode,
    terminalName: "Front iPad",
  });

  assert.equal(preview.storeCode, "1042");
  assert.equal(redeemed.deviceCode, "IPAD-1042-01");
  assert.equal(rebound.storeCode, "1042");
  assert.deepEqual(calls, [
    {
      method: "POST",
      url: "/api/v1/devices/activation-code/preview",
      data: { activationCode, deviceSystem: "iPadOS" },
    },
    {
      method: "POST",
      url: "/api/v1/devices/activation-code/redeem",
      data: {
        activationCode,
        hardwareId: "INSTALL-001",
        deviceSystem: "iPadOS",
      },
    },
    {
      method: "POST",
      url: "/api/v1/devices/activation-code/rebind",
      data: {
        activationCode,
        terminalName: "Front iPad",
      },
    },
  ]);
});

test("兑换与预览固定走匿名 transport，换店只走当前设备认证 transport", async () => {
  const authenticated: string[] = [];
  const anonymous: string[] = [];
  const response = {
    status: 200,
    data: { success: true, data: { isAllowed: false, deviceStatus: 1 } },
  } as const;
  const authenticatedTransport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      authenticated.push(config.url);
      return response as unknown as { status: number; data: T };
    },
  };
  const anonymousTransport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      anonymous.push(config.url);
      return response as unknown as { status: number; data: T };
    },
  };
  const api = new HbposDeviceApi(authenticatedTransport, anonymousTransport);
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";

  await api.previewActivationCode({ activationCode });
  await api.redeemActivationCode({ activationCode, hardwareId: "INSTALL-001" });
  await api.rebindActivationCode({ activationCode });

  assert.deepEqual(anonymous, [
    "/api/v1/devices/activation-code/preview",
    "/api/v1/devices/activation-code/redeem",
  ]);
  assert.deepEqual(authenticated, [
    "/api/v1/devices/activation-code/rebind",
  ]);
});

test("重绑丢响应恢复沿用匿名兑换 body，但必须发送 recovery-only header", async () => {
  const calls: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      calls.push(config);
      return {
        status: 200,
        data: {
          success: true,
          data: { isAllowed: false, reasonCode: "ACTIVATION_CODE_NOT_AVAILABLE" },
        } as T,
      };
    },
  };
  const activationCode =
    "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";

  await new HbposDeviceApi(transport).redeemActivationCode(
    { activationCode, hardwareId: "INSTALL-001" },
    { recoveryOnly: true },
  );

  assert.deepEqual(calls, [{
    method: "POST",
    url: "/api/v1/devices/activation-code/redeem",
    headers: { "X-HBPOS-Activation-Recovery-Only": "true" },
    data: { activationCode, hardwareId: "INSTALL-001", deviceSystem: "iPadOS" },
  }]);
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

  const stores = await new HbposDeviceApi(transport).listRegistrationStores();

  assert.deepEqual(calls, [{
    method: "GET",
    url: "/api/v1/catalog/stores"
  }]);
  assert.deepEqual(stores, [
    { storeCode: "1002", storeName: "Aspley" },
    { storeCode: "1003", storeName: "Chermside" }
  ]);
});

test("设备注册重置只发送 operationId 并使用本次在线员工票据", async () => {
  const calls: HbposTransportRequest[] = [];
  const transport: HbposTransport = {
    async request<T>(config: HbposTransportRequest) {
      calls.push(config);
      return {
        status: 200,
        data: {
          success: true,
          data: {
            operationId: "10000000-0000-4000-8000-000000000001",
            deviceCode: "IPAD-1042-01",
            storeCode: "1042",
            disabledAtUtc: "2026-08-18T02:00:00.000Z",
          },
        } as T,
      };
    },
  };

  const result = await new HbposDeviceApi(transport).resetRegistration(
    { operationId: "10000000-0000-4000-8000-000000000001" },
    "fresh-online-ticket",
  );

  assert.equal(result.storeCode, "1042");
  assert.deepEqual(calls, [
    {
      method: "POST",
      url: "/api/v1/devices/reset-registration",
      data: { operationId: "10000000-0000-4000-8000-000000000001" },
      headers: {
        "X-HBPOS-Cashier-Authorization": "fresh-online-ticket",
      },
    },
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
