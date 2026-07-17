import assert from "node:assert/strict";
import { unwrapApiEnvelope } from "./api-envelope";

assert.deepEqual(
  unwrapApiEnvelope({ success: true, data: { value: 1 } }),
  { value: 1 },
  "成功响应应保持现有解包语义",
);

let businessError: unknown;
try {
  unwrapApiEnvelope({
    success: false,
    data: null,
    message: "二维码已过期",
    errorCode: "ATTENDANCE_QR_EXPIRED",
  });
} catch (error) {
  businessError = error;
}
assert.equal(businessError instanceof Error ? businessError.message : "", "二维码已过期");
assert.equal(
  businessError && typeof businessError === "object" && "code" in businessError
    ? businessError.code
    : undefined,
  "ATTENDANCE_QR_EXPIRED",
  "业务失败解包时必须保留服务端 errorCode",
);

console.log("api-envelope.test.ts: ok");
