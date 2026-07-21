import assert from "node:assert/strict";
import { unwrapApiEnvelope } from "./api-envelope";

function captureError(payload: unknown): unknown {
  try {
    unwrapApiEnvelope(payload);
  } catch (error) {
    return error;
  }
  return undefined;
}

assert.deepEqual(
  unwrapApiEnvelope({ success: true, data: { value: 1 } }),
  { value: 1 },
  "成功响应应保持现有解包语义",
);

assert.deepEqual(
  unwrapApiEnvelope({ value: 1, message: "普通对象" }),
  { value: 1, message: "普通对象" },
  "普通非 envelope 对象应原样返回",
);

const successFalseWithoutData = captureError({
  success: false,
  message: "请求失败",
  errorCode: "REQUEST_FAILED",
});
assert.equal(successFalseWithoutData instanceof Error ? successFalseWithoutData.message : "", "请求失败");
assert.equal(
  successFalseWithoutData
    && typeof successFalseWithoutData === "object"
    && "code" in successFalseWithoutData
    ? successFalseWithoutData.code
    : undefined,
  "REQUEST_FAILED",
  "无 data 的 success:false 必须保留 errorCode",
);

const isSuccessFalseWithoutData = captureError({
  isSuccess: false,
  message: "业务处理失败",
  errorCode: "BUSINESS_FAILED",
});
assert.equal(
  isSuccessFalseWithoutData instanceof Error ? isSuccessFalseWithoutData.message : "",
  "业务处理失败",
);
assert.equal(
  isSuccessFalseWithoutData
    && typeof isSuccessFalseWithoutData === "object"
    && "code" in isSuccessFalseWithoutData
    ? isSuccessFalseWithoutData.code
    : undefined,
  "BUSINESS_FAILED",
  "无 data 的 isSuccess:false 必须保留 errorCode",
);

const conflictingFailureFlags = captureError({
  success: true,
  isSuccess: false,
  message: "冲突标记仍应失败",
});
assert.equal(
  conflictingFailureFlags instanceof Error ? conflictingFailureFlags.message : "",
  "冲突标记仍应失败",
  "任一成功标记显式为 false 时都必须按失败处理",
);

const businessError = captureError({
  success: false,
  data: null,
  message: "二维码已过期",
  errorCode: "ATTENDANCE_QR_EXPIRED",
});
assert.equal(businessError instanceof Error ? businessError.message : "", "二维码已过期");
assert.equal(
  businessError && typeof businessError === "object" && "code" in businessError
    ? businessError.code
    : undefined,
  "ATTENDANCE_QR_EXPIRED",
  "业务失败解包时必须保留服务端 errorCode",
);

console.log("api-envelope.test.ts: ok");
