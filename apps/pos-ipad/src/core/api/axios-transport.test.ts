import assert from "node:assert/strict";
import test from "node:test";

import { AxiosError, create, type AxiosRequestConfig } from "axios";

import { createAxiosHbposTransport } from "./axios-transport";
import { HbposCashierApi } from "./hbpos-api";

test("Axios middleware 仅从安全凭据提供者附加设备和收银员认证头", async () => {
  let request: AxiosRequestConfig | undefined;
  const instance = create({
    adapter: async (config) => {
      request = config;
      return { config, status: 200, statusText: "OK", headers: {}, data: { success: true } };
    }
  });
  const transport = createAxiosHbposTransport("https://hbpos.example", {
    async getCredentials() {
      return {
        device: {
          authorizationCode: "device-secret",
          deviceCode: "POS-001",
          storeCode: "1003",
          hardwareId: "INSTALL-001"
        },
        cashierAuthorization: "cashier-secret"
      };
    }
  }, instance);

  await transport.request({ method: "GET", url: "/api/v1/cashiers/session" });

  assert.equal(request?.headers?.Authorization, "Bearer device-secret");
  assert.equal(request?.headers?.["X-HBPOS-Device-Code"], "POS-001");
  assert.equal(request?.headers?.["X-HBPOS-Store-Code"], "1003");
  assert.equal(request?.headers?.["X-HBPOS-Hardware-Id"], "INSTALL-001");
  assert.equal(request?.headers?.["X-HBPOS-Cashier-Authorization"], "cashier-secret");
});

test("请求最终 origin 偏离已选 API 时在读取凭据前失败关闭", async () => {
  let credentialReads = 0;
  let adapterCalls = 0;
  const instance = create({
    adapter: async (config) => {
      adapterCalls += 1;
      return {
        config,
        status: 200,
        statusText: "OK",
        headers: {},
        data: { success: true },
      };
    },
  });
  const transport = createAxiosHbposTransport(
    "https://hbpos.example/api",
    {
      async getCredentials() {
        credentialReads += 1;
        return {
          device: {
            authorizationCode: "device-secret",
            deviceCode: "POS-001",
            storeCode: "1003",
            hardwareId: "INSTALL-001",
          },
        };
      },
    },
    instance,
  );

  await assert.rejects(
    () =>
      transport.request({
        method: "GET",
        url: "https://attacker.example/collect",
      }),
    (error: unknown) =>
      error instanceof Error &&
      /origin|trusted/i.test(error.message),
  );
  assert.equal(credentialReads, 0);
  assert.equal(adapterCalls, 0);
});

test("扩展请求只允许显式的 PUT、条件头和非 2xx 恢复状态", async () => {
  let request: AxiosRequestConfig | undefined;
  const instance = create({
    adapter: async (config) => {
      request = config;
      return {
        config,
        status: 304,
        statusText: "Not Modified",
        headers: {},
        data: null,
      };
    },
  });
  const transport = createAxiosHbposTransport(
    "https://hbpos.example",
    {
      async getCredentials() {
        return {};
      },
    },
    instance,
  );

  const response = await transport.request({
    method: "PUT",
    url: "/api/v1/attendance/signing-key",
    headers: { "If-None-Match": "\"keys-v3\"" },
    acceptedStatuses: [304, 409],
  });

  assert.equal(response.status, 304);
  assert.equal(request?.method, "put");
  assert.equal(request?.headers?.["If-None-Match"], "\"keys-v3\"");
  assert.equal(request?.validateStatus?.(200), true);
  assert.equal(request?.validateStatus?.(304), true);
  assert.equal(request?.validateStatus?.(409), true);
  assert.equal(request?.validateStatus?.(500), false);
});

test("明确设备撤销码无论由 401 或 403 返回都必须锁定设备", async () => {
  const calls: string[] = [];
  let status = 401;
  const instance = create({
    adapter: async (config) => {
      const error = new AxiosError("forbidden", "ERR_BAD_RESPONSE", config, undefined, {
        config,
        data: { errorCode: "DEVICE_DISABLED", message: "disabled" },
        headers: {},
        status,
        statusText: status === 401 ? "Unauthorized" : "Forbidden",
      });
      throw error;
    },
  });
  const transport = createAxiosHbposTransport("https://hbpos.example", {
    async getCredentials() {
      return {};
    },
  }, instance, {
    async onUnauthorized() {
      calls.push("401");
    },
    async onForbidden() {
      calls.push("403");
    },
  });

  await assert.rejects(
    () => transport.request({ method: "GET", url: "/api/v1/orders" }),
    (error: unknown) => error instanceof Error && error.message === "disabled",
  );
  assert.deepEqual(calls, ["403"]);

  status = 403;
  await assert.rejects(
    () => transport.request({ method: "GET", url: "/api/v1/orders" }),
    (error: unknown) => error instanceof Error && error.message === "disabled",
  );
  assert.deepEqual(calls, ["403", "403"]);
});

test("权限、门店范围、新交易门禁和无错误码 403 都不得持久锁设备", async () => {
  const calls: string[] = [];
  let errorCode: string | undefined;
  const instance = create({
    adapter: async (config) => {
      throw new AxiosError("forbidden", "ERR_BAD_RESPONSE", config, undefined, {
        config,
        data: {
          ...(errorCode ? { errorCode } : {}),
          message: "access denied",
        },
        headers: {},
        status: 403,
        statusText: "Forbidden",
      });
    },
  });
  const transport = createAxiosHbposTransport("https://hbpos.example", {
    async getCredentials() {
      return {};
    },
  }, instance, {
    async onUnauthorized() {
      calls.push("401");
    },
    async onForbidden() {
      calls.push("403-device-revoked");
    },
  });

  for (const code of [
    undefined,
    "CASHIER_PERMISSION_FORBIDDEN",
    "DEVICE_SCOPE_FORBIDDEN",
    "POS_IPAD_NEW_TRANSACTIONS_DISABLED",
  ]) {
    errorCode = code;
    await assert.rejects(
      () => transport.request({ method: "GET", url: "/api/v1/orders" }),
      /access denied/,
    );
  }

  assert.deepEqual(calls, []);
});

test("认证失效反馈自身失败也不能掩盖原始 HTTP 拒绝", async () => {
  const instance = create({
    adapter: async (config) => {
      throw new AxiosError("unauthorized", "ERR_BAD_RESPONSE", config, undefined, {
        config,
        data: { errorCode: "AUTH_EXPIRED", message: "login again" },
        headers: {},
        status: 401,
        statusText: "Unauthorized",
      });
    },
  });
  const transport = createAxiosHbposTransport("https://hbpos.example", {
    async getCredentials() {
      return {};
    },
  }, instance, {
    async onUnauthorized() {
      throw new Error("secure storage temporarily unavailable");
    },
    async onForbidden() {},
  });

  await assert.rejects(
    () => transport.request({ method: "GET", url: "/api/v1/orders" }),
    (error: unknown) => error instanceof Error && error.message === "login again",
  );
});

test("条码登录业务拒绝不清理认证，只有明确设备撤销 403 才锁设备", async () => {
  const calls: string[] = [];
  let status = 401;
  let errorCode: string | undefined = "CASHIER_LOGIN_FAILED";
  const instance = create({
    adapter: async (config) => {
      throw new AxiosError("login rejected", "ERR_BAD_RESPONSE", config, undefined, {
        config,
        data: { ...(errorCode ? { errorCode } : {}), message: "invalid barcode" },
        headers: {},
        status,
        statusText: status === 401 ? "Unauthorized" : "Forbidden",
      });
    },
  });
  const transport = createAxiosHbposTransport("https://hbpos.example", {
    async getCredentials() {
      return {};
    },
  }, instance, {
    async onUnauthorized() {
      calls.push("401");
    },
    async onForbidden() {
      calls.push("403");
    },
  });
  const cashiers = new HbposCashierApi(transport);

  await assert.rejects(
    () => cashiers.barcodeLogin({ storeCode: "1003", deviceCode: "POS-1", userBarcode: "INVALID" }),
    /invalid barcode/,
  );
  assert.deepEqual(calls, []);

  status = 403;
  await assert.rejects(
    () => cashiers.barcodeLogin({ storeCode: "1003", deviceCode: "POS-1", userBarcode: "INVALID" }),
    /invalid barcode/,
  );
  assert.deepEqual(calls, []);

  errorCode = "DEVICE_DISABLED";
  await assert.rejects(
    () => cashiers.barcodeLogin({ storeCode: "1003", deviceCode: "POS-1", userBarcode: "INVALID" }),
    /invalid barcode/,
  );
  assert.deepEqual(calls, ["403"]);

  status = 401;
  errorCode = "AUTH_EXPIRED";
  await assert.rejects(
    () => cashiers.barcodeLogin({ storeCode: "1003", deviceCode: "POS-1", userBarcode: "INVALID" }),
    /invalid barcode/,
  );
  assert.deepEqual(calls, ["403", "401"]);

  errorCode = undefined;
  await assert.rejects(
    () => cashiers.barcodeLogin({ storeCode: "1003", deviceCode: "POS-1", userBarcode: "INVALID" }),
    /invalid barcode/,
  );
  assert.deepEqual(calls, ["403", "401", "401"]);
});

test("非条码登录请求的 401 仍触发默认全局认证失效处理", async () => {
  const calls: string[] = [];
  const instance = create({
    adapter: async (config) => {
      throw new AxiosError("expired", "ERR_BAD_RESPONSE", config, undefined, {
        config,
        data: { errorCode: "AUTH_EXPIRED", message: "login again" },
        headers: {},
        status: 401,
        statusText: "Unauthorized",
      });
    },
  });
  const transport = createAxiosHbposTransport("https://hbpos.example", {
    async getCredentials() {
      return {};
    },
  }, instance, {
    async onUnauthorized() {
      calls.push("401");
    },
    async onForbidden() {},
  });

  await assert.rejects(() => transport.request({ method: "GET", url: "/api/v1/orders" }), /login again/);
  assert.deepEqual(calls, ["401"]);
});
