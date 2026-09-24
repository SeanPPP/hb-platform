import assert from "node:assert/strict";
import test from "node:test";
import { AxiosError, AxiosHeaders } from "axios";
import { isNetworkUnavailableError } from "./network-error";

function axiosErrorWith(options: { status?: number; code?: string; request?: unknown; message?: string }) {
  const config = { headers: new AxiosHeaders() };
  const response = options.status
    ? { status: options.status, statusText: "", headers: {}, config, data: {} }
    : undefined;
  return new AxiosError(
    options.message ?? "boom",
    options.code,
    config as never,
    options.request,
    response as never,
  );
}

test("无响应的 axios 错误视为网络不可用", () => {
  assert.equal(isNetworkUnavailableError(axiosErrorWith({ code: "ECONNABORTED" })), true);
  assert.equal(isNetworkUnavailableError(axiosErrorWith({ code: "ERR_NETWORK" })), true);
  assert.equal(isNetworkUnavailableError(axiosErrorWith({ request: {} })), true);
  assert.equal(
    isNetworkUnavailableError(axiosErrorWith({ message: "Network Error" })),
    true,
  );
});

test("有响应的 axios 错误（4xx/5xx）不是网络不可用", () => {
  assert.equal(isNetworkUnavailableError(axiosErrorWith({ status: 500, code: "ERR_BAD_RESPONSE" })), false);
  assert.equal(isNetworkUnavailableError(axiosErrorWith({ status: 404 })), false);
  assert.equal(isNetworkUnavailableError(axiosErrorWith({ status: 401 })), false);
});

test("React Native fetch 断网 TypeError 与 AbortError 视为网络不可用", () => {
  assert.equal(isNetworkUnavailableError(new TypeError("Network request failed")), true);
  const abort = new Error("aborted");
  abort.name = "AbortError";
  assert.equal(isNetworkUnavailableError(abort), true);
});

test("普通业务错误与非对象值不是网络不可用", () => {
  assert.equal(isNetworkUnavailableError(new Error("商品不存在")), false);
  assert.equal(isNetworkUnavailableError("Network request failed"), false);
  assert.equal(isNetworkUnavailableError(null), false);
  assert.equal(isNetworkUnavailableError(undefined), false);
});

test("主动取消不算服务器不可达", () => {
  // 切店抢占、离开页面中止在途请求都会抛这两种错误；算成不可达会让页面
  // 在网络完全正常时切进离线模式并禁用全部编辑。
  assert.equal(isNetworkUnavailableError({ code: "ERR_CANCELED", message: "canceled" }), false);
  assert.equal(isNetworkUnavailableError({ name: "CanceledError", message: "canceled" }), false);
});
