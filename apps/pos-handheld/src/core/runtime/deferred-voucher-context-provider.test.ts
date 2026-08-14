import assert from "node:assert/strict";
import test from "node:test";

import type { PaymentAttempt } from "../contracts";

import { DeferredVoucherContextProvider } from "./deferred-voucher-context-provider";

const attempt = {
  attemptId: "attempt-1",
} as PaymentAttempt;

test("Voucher context bridge 在绑定前失败关闭，且只能绑定一次", async () => {
  const bridge = new DeferredVoucherContextProvider();

  await assert.rejects(
    () => bridge.provide(attempt),
    (error: unknown) =>
      error instanceof Error &&
      (error as Error & { code?: string }).code ===
        "VOUCHER_CONTEXT_NOT_PREPARED",
  );

  bridge.bind(async (value) => ({
    storeCode: "S001",
    cashierId: "cashier-1",
    voucherCode: value.attemptId,
    refundReason: null,
  }));
  assert.deepEqual(await bridge.provide(attempt), {
    storeCode: "S001",
    cashierId: "cashier-1",
    voucherCode: "attempt-1",
    refundReason: null,
  });
  assert.throws(
    () => bridge.bind(async () => {
      throw new Error("not used");
    }),
    /already bound/i,
  );
});
