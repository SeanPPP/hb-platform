import assert from "node:assert/strict";
import test from "node:test";

import {
  PaymentFinalCompletionPlanner,
  SafeApprovedPaymentCompletionPlanner,
  type PaymentCompletionProjection,
  type PaymentCompletionSettings,
  type PaymentReceiptRenderInput,
} from "./payment-completion-planner";

import type { PaymentAttempt } from "@/core/contracts";
import type { PaymentAttemptExecutionResult } from "@/features/payments/payment-attempt-service";

test("partial Approved 只规划 tender，不读取打印设置、不渲染、不创建最终履约", async () => {
  const events: string[] = [];
  const approved = approvedPlanner(
    { orderGuid: "order-1", total: aud(2_000), paid: aud(0) },
    events,
  );

  const plan = await approved.plan(
    execution({ amount: aud(500) }),
  );

  assert.equal(events.includes("settings"), false);
  assert.equal(events.includes("render"), false);
  assert.deepEqual(plan.completionAuditEvents, []);
  assert.deepEqual(plan.fulfilment, { print: null, drawer: null });
  assert.equal(plan.outbox.kind, "order-sync");
  assert.equal(plan.outbox.aggregateId, "order-1");
});

test("卡/券 Approved 足额计划只含脱敏 audit/outbox，按设置打印但永不开钱箱", async () => {
  const events: string[] = [];
  const approved = approvedPlanner(
    { orderGuid: "order-1", total: aud(1_250), paid: aud(0) },
    events,
    settings({
      automaticPrint: { cash: false, card: true, voucher: true },
      cashDrawerEnabled: true,
      cashDrawerPermissionAllowed: true,
    }),
  );
  const value = execution({
    receiptText: "SECRET RECEIPT PAN 4111111111111111",
    references: {
      checkoutId: "checkout-secret",
      paymentId: "payment-secret",
      sessionId: null,
      txnRef: null,
      rfn: null,
      voucherReservationToken: null,
    },
  });

  const plan = await approved.plan(value);

  assert.equal(plan.fulfilment.print?.printerId, "printer-1");
  assert.equal(plan.fulfilment.drawer, null);
  assert.deepEqual(events, ["projection", "settings", "render"]);
  const json = JSON.stringify(plan);
  assert.equal(json.includes("SECRET RECEIPT"), false);
  assert.equal(json.includes("checkout-secret"), false);
  assert.equal(json.includes("payment-secret"), false);
  assert.equal(json.includes("4111111111111111"), false);
  assert.deepEqual(JSON.parse(plan.outbox.payloadJson), {
    orderGuid: "order-1",
  });
});

test("final mixed cash 金额必须精确等于 expectedRemaining，按权限冻结 drawer/print", async () => {
  const inputs: PaymentReceiptRenderInput[] = [];
  const planner = finalPlanner([], settings(), inputs);

  const plan = await planner.planFinalCash({
    actionId: "cash-action-1",
    orderGuid: "order-cash",
    amount: aud(500),
    expectedRemaining: aud(500),
  });

  assert.equal(plan.fulfilment.print?.orderGuid, "order-cash");
  assert.equal(plan.fulfilment.drawer?.orderGuid, "order-cash");
  assert.equal(plan.fulfilment.drawer?.printJobId, plan.fulfilment.print?.jobId);
  assert.deepEqual(inputs, [
    {
      orderGuid: "order-cash",
      method: "cash",
      amount: aud(500),
      attemptId: null,
    },
  ]);
  assert.equal(
    JSON.stringify(plan.completionAuditEvents[0]?.payload).includes(
      "cash-action-1",
    ),
    false,
  );

  await assert.rejects(
    () =>
      planner.planFinalCash({
        actionId: "cash-action-mismatch",
        orderGuid: "order-cash",
        amount: aud(499),
        expectedRemaining: aud(500),
      }),
    /MIXED_CASH_FINAL_AMOUNT_MUST_EQUAL_EXPECTED_REMAINING/,
  );
});

test("settings/renderer 故障只降级为无打印；已批准计划仍可原子落账且卡不误开箱", async () => {
  const ids = idFactory();
  const final = new PaymentFinalCompletionPlanner({
    settings: {
      async load() {
        return settings({
          automaticPrint: { cash: false, card: true, voucher: false },
        });
      },
    },
    renderer: {
      async render() {
        throw new Error("printer renderer unavailable");
      },
    },
    createId: ids,
    nowIso: () => "2026-07-28T00:00:00.000Z",
  });

  const plan = await final.planApproved({
    orderGuid: "order-approved",
    attemptId: "attempt-approved",
    provider: "square",
    amount: aud(1_000),
  });

  assert.deepEqual(plan.fulfilment, { print: null, drawer: null });
  assert.equal(plan.completionAuditEvents.length, 1);
  assert.equal(plan.outbox.kind, "order-sync");
});

function approvedPlanner(
  projection: PaymentCompletionProjection,
  events: string[],
  configuredSettings = settings(),
): SafeApprovedPaymentCompletionPlanner {
  const inputs: PaymentReceiptRenderInput[] = [];
  return new SafeApprovedPaymentCompletionPlanner({
    projection: {
      async read() {
        events.push("projection");
        return projection;
      },
    },
    finalPlanner: finalPlanner(events, configuredSettings, inputs),
    createId: idFactory(),
    nowIso: () => "2026-07-28T00:00:00.000Z",
  });
}

function finalPlanner(
  events: string[],
  configuredSettings: PaymentCompletionSettings,
  renderInputs: PaymentReceiptRenderInput[],
): PaymentFinalCompletionPlanner {
  return new PaymentFinalCompletionPlanner({
    settings: {
      async load() {
        events.push("settings");
        return configuredSettings;
      },
    },
    renderer: {
      async render(input) {
        events.push("render");
        renderInputs.push(input);
        return Uint8Array.from([0x1b, 0x40, 0x0a]);
      },
    },
    createId: idFactory(),
    nowIso: () => "2026-07-28T00:00:00.000Z",
  });
}

function settings(
  overrides: Partial<PaymentCompletionSettings> = {},
): PaymentCompletionSettings {
  return {
    printerId: "printer-1",
    automaticPrint: { cash: true, card: true, voucher: true },
    cashDrawerEnabled: true,
    cashDrawerPermissionAllowed: true,
    ...overrides,
  };
}

function execution(
  overrides: Partial<PaymentAttempt> = {},
): PaymentAttemptExecutionResult {
  return {
    attempt: attempt(overrides),
    receiptText:
      overrides.receiptText ?? "SECRET RECEIPT PAN 4111111111111111",
    responseCode: "APPROVED",
  };
}

function attempt(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-1",
    idempotencyKey: "idempotency-1",
    orderGuid: "order-1",
    provider: "square",
    operation: "purchase",
    amount: aud(1_250),
    state: "Approved",
    references: {
      checkoutId: "checkout-1",
      paymentId: "payment-1",
      sessionId: null,
      txnRef: null,
      rfn: null,
      voucherReservationToken: null,
    },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:01:00.000Z",
    lastErrorCode: null,
    ...overrides,
  };
}

function aud(cents: number) {
  return { currency: "AUD", cents } as const;
}

function idFactory(): () => string {
  let id = 0;
  return () => `id-${++id}`;
}
