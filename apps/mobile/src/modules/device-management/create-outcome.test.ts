import assert from "node:assert/strict";
import test from "node:test";
import { unwrapApiEnvelope } from "../../shared/api/api-envelope";
import { isResultUnknownCreateError } from "./create-outcome";

test("真实 HTTP 200 业务拒绝经过解包后允许修正输入", () => {
  for (const code of ["DEVICE_ACTIVATION_STORE_UNAVAILABLE", "MOBILE_ACTIVATION_ACCOUNT_UNAVAILABLE", "EMERGENCY_GRANT_NO_ENABLED_POS", "EMERGENCY_GRANT_ALREADY_ACTIVE"]) {
    let error: unknown;
    try { unwrapApiEnvelope({ success: false, code, message: "业务拒绝" }); } catch (caught) { error = caught; }
    assert.ok(error instanceof Error);
    assert.equal(isResultUnknownCreateError(error), false, code);
  }
});

test("网络断开、超时和 5xx 保持结果未知，内部失败不能按前缀误放行", () => {
  for (const error of [new Error("Network Error"), { code: "ECONNABORTED" }, { response: { status: 504 } }, { status: 500, code: "DEVICE_ACTIVATION_STORE_REQUIRED" }, { code: "EMERGENCY_GRANT_CREATE_FAILED" }, null]) {
    assert.equal(isResultUnknownCreateError(error), true);
  }
});

test("明确 HTTP 4xx 拒绝不会锁死创建表单", () => {
  assert.equal(isResultUnknownCreateError({ response: { status: 403 } }), false);
  assert.equal(isResultUnknownCreateError({ status: 409 }), false);
});

test("HTTP 408 和代理 499 中断仍可能已经提交", () => {
  for (const status of [408, 499]) {
    assert.equal(isResultUnknownCreateError({ status }), true);
    assert.equal(isResultUnknownCreateError({ response: { status }, code: "DEVICE_ACTIVATION_STORE_REQUIRED" }), true);
  }
});
