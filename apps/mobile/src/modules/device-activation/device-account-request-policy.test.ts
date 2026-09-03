import assert from "node:assert/strict";
import test from "node:test";

import {
  deriveEffectiveAuthSessionKind,
  isRelativeApiClientUrl,
  removeRequestHeader,
  resolveDeviceAccountRequestPolicy,
} from "./device-account-request-policy";

test("设备账号的普通请求始终固定到绑定服务器", () => {
  assert.deepEqual(
    resolveDeviceAccountRequestPolicy({
      requestedApiHost: "staging.example.com",
      bindingApiHost: "api.example.com",
      sessionKind: "deviceAccount",
      skipAuthentication: false,
    }),
    {
      apiHost: "api.example.com",
      allowDeviceHeaders: true,
      allowBearerToken: true,
    },
  );
});

test("当前服务器与绑定服务器不同时拒绝任何设备认证头", () => {
  assert.deepEqual(
    resolveDeviceAccountRequestPolicy({
      requestedApiHost: "staging.example.com",
      bindingApiHost: "api.example.com",
      sessionKind: "account",
      skipAuthentication: false,
    }),
    {
      apiHost: "staging.example.com",
      allowDeviceHeaders: false,
      allowBearerToken: true,
    },
  );
});

test("同一绑定服务器允许兼容旧接口附加设备认证头", () => {
  assert.deepEqual(
    resolveDeviceAccountRequestPolicy({
      requestedApiHost: "API.EXAMPLE.COM",
      bindingApiHost: "api.example.com",
      sessionKind: null,
      skipAuthentication: false,
    }),
    {
      apiHost: "API.EXAMPLE.COM",
      allowDeviceHeaders: true,
      allowBearerToken: true,
    },
  );
});

test("匿名预览和登录保留显式服务器且绝不携带设备认证头", () => {
  assert.deepEqual(
    resolveDeviceAccountRequestPolicy({
      requestedApiHost: "preview.example.com",
      bindingApiHost: "api.example.com",
      sessionKind: "deviceAccount",
      skipAuthentication: true,
    }),
    {
      apiHost: "preview.example.com",
      allowDeviceHeaders: false,
      allowBearerToken: false,
    },
  );
});

test("设备账号 marker 缺失安全绑定时 fail closed", () => {
  assert.deepEqual(
    resolveDeviceAccountRequestPolicy({
      requestedApiHost: "staging.example.com",
      bindingApiHost: null,
      sessionKind: "deviceAccount",
      skipAuthentication: false,
    }),
    {
      apiHost: "staging.example.com",
      allowDeviceHeaders: false,
      allowBearerToken: false,
    },
  );
});

test("共享 API client 只接受相对 URL，避免绝对 URL 绕过 binding baseURL", () => {
  assert.equal(isRelativeApiClientUrl("/mobile/v1/device-session/exchange"), true);
  assert.equal(isRelativeApiClientUrl("auth/current"), true);
  assert.equal(isRelativeApiClientUrl("https://evil.example/api"), false);
  assert.equal(isRelativeApiClientUrl("//evil.example/api"), false);
  assert.equal(isRelativeApiClientUrl("data:text/plain,secret"), false);
});

test("token 已写但 marker 未写的崩溃窗口按设备账号恢复", () => {
  assert.equal(
    deriveEffectiveAuthSessionKind({
      persistedKind: null,
      hasAccessToken: true,
      hasRefreshToken: false,
      hasBinding: true,
    }),
    "deviceAccount",
  );
  assert.equal(
    deriveEffectiveAuthSessionKind({
      persistedKind: "account",
      hasAccessToken: true,
      hasRefreshToken: false,
      hasBinding: true,
    }),
    "deviceAccount",
  );
});

test("有 refresh token 的普通账号不被本机 binding 劫持", () => {
  assert.equal(
    deriveEffectiveAuthSessionKind({
      persistedKind: "account",
      hasAccessToken: true,
      hasRefreshToken: true,
      hasBinding: true,
    }),
    "account",
  );
});

test("host mismatch 会按大小写无关方式删除调用方设备认证头", () => {
  const headers: Record<string, unknown> = {
    "x-device-id": "hardware-secret",
    "X-AUTH-CODE": "credential-secret",
    Accept: "application/json",
  };

  removeRequestHeader(headers, "X-Device-Id");
  removeRequestHeader(headers, "X-Auth-Code");

  assert.deepEqual(headers, { Accept: "application/json" });
});
