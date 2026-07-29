import assert from "node:assert/strict";
import test from "node:test";

import {
  DurableVoucherPreparationService,
  type VoucherPreparedAttemptBinding,
  type VoucherPreparedContext,
  type VoucherPreparedContextDraft,
  type VoucherPreparationStorePort,
} from "./voucher-preparation";

import type { PaymentAttempt } from "@/core/contracts";

test("券购买上下文先耐久 prepare，再绑定 immutable attempt；公开结果不回显敏感值", async () => {
  const events: string[] = [];
  const store = new MemoryPreparationStore(events);
  const service = new DurableVoucherPreparationService(
    store,
    {
      async resolve() {
        events.push("identity");
        return { storeCode: "S1", cashierId: "cashier-1" };
      },
    },
    {
      assertActive: () => {
        events.push("session");
      },
    },
  );

  const prepared = await service.preparePurchase({
    actionId: "voucher-action-1",
    orderGuid: "order-1",
    voucherCode: "SECRET-VOUCHER-001",
  });
  events.push("provider-boundary");
  const context = await service.contextForAttempt(attempt());

  assert.deepEqual(prepared, { prepared: true });
  assert.equal(JSON.stringify(prepared).includes("SECRET-VOUCHER-001"), false);
  assert.equal(context.voucherCode, "SECRET-VOUCHER-001");
  assert.ok(events.indexOf("prepare") < events.indexOf("provider-boundary"));
  assert.ok(events.indexOf("prepare") < events.indexOf("bind"));
  assert.equal(store.bound?.attemptId, "attempt-1");
  assert.equal(store.bound?.idempotencyKey, "idempotency-1");
});

test("退款上下文只保存可信身份与原因，不接受券码；绑定到 refund attempt", async () => {
  const store = new MemoryPreparationStore([]);
  const service = new DurableVoucherPreparationService(
    store,
    {
      async resolve() {
        return { storeCode: "S2", cashierId: "cashier-2" };
      },
    },
    { assertActive() {} },
  );

  const result = await service.prepareRefund({
    actionId: "refund-action",
    orderGuid: "return-order",
    refundReason: "approved return",
  });
  const context = await service.contextForAttempt(
    attempt({
      orderGuid: "return-order",
      operation: "refund",
      attemptId: "refund-attempt",
      idempotencyKey: "refund-idempotency",
    }),
  );

  assert.deepEqual(result, { prepared: true });
  assert.equal(context.voucherCode, null);
  assert.equal(context.refundReason, "approved return");
  assert.equal(store.prepared?.operation, "refund");
});

test("旧会话在受保护写入后失效时 fail closed，不返回伪 prepare 成功", async () => {
  let active = true;
  const store = new MemoryPreparationStore([]);
  store.afterPrepare = () => {
    active = false;
  };
  const service = new DurableVoucherPreparationService(
    store,
    {
      async resolve() {
        return { storeCode: "S1", cashierId: "C1" };
      },
    },
    {
      assertActive() {
        if (!active) throw new Error("CURRENT_CASHIER_REQUIRED");
      },
    },
  );

  await assert.rejects(
    () =>
      service.preparePurchase({
        actionId: "action-old-session",
        orderGuid: "order-old-session",
        voucherCode: "SECRET-OLD",
      }),
    /CURRENT_CASHIER_REQUIRED/,
  );
  assert.equal(store.prepared?.voucherCode, "SECRET-OLD");
});

class MemoryPreparationStore implements VoucherPreparationStorePort {
  public prepared: VoucherPreparedContextDraft | null = null;
  public bound: VoucherPreparedAttemptBinding | null = null;
  public afterPrepare: (() => void) | null = null;

  public constructor(private readonly events: string[]) {}

  public async prepare(input: VoucherPreparedContextDraft): Promise<string> {
    this.events.push("prepare");
    if (this.prepared) {
      assert.deepEqual(input, this.prepared);
    } else {
      this.prepared = input;
    }
    this.afterPrepare?.();
    return "vctx-1";
  }

  public async bindToAttempt(
    input: VoucherPreparedAttemptBinding,
  ): Promise<VoucherPreparedContext | null> {
    this.events.push("bind");
    this.bound = input;
    if (
      !this.prepared ||
      this.prepared.orderGuid !== input.orderGuid ||
      this.prepared.operation !== input.operation
    ) {
      return null;
    }
    return {
      ...this.prepared,
      protectedReference: "vctx-1",
      attemptId: input.attemptId,
      idempotencyKey: input.idempotencyKey,
    };
  }
}

function attempt(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-1",
    idempotencyKey: "idempotency-1",
    orderGuid: "order-1",
    provider: "voucher",
    operation: "purchase",
    amount: { currency: "AUD", cents: 500 },
    state: "Submitted",
    references: {
      checkoutId: null,
      paymentId: null,
      sessionId: null,
      txnRef: null,
      rfn: null,
      voucherReservationToken: null,
    },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:00:01.000Z",
    lastErrorCode: null,
    ...overrides,
  };
}
