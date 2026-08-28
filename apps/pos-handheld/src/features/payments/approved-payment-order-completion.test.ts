import assert from "node:assert/strict";
import test from "node:test";

import {
  ApprovedPaymentOrderCompletionRecoveryRequiredError,
  ApprovedPaymentOrderCompletionService,
  type ApprovedPaymentOrderCompletionPlan,
  type ApprovedPaymentOrderCompletionPlannerPort,
} from "./approved-payment-order-completion";
import {
  PaymentAttemptStateError,
  type PaymentAttemptExecutionResult,
} from "@hb/pos-payments-core/features/payments/payment-attempt-service";

import type {
  ApprovedPaymentOrderCommit,
  ApprovedPaymentOrderCommitPort,
  ApprovedPaymentOrderCommitResult,
  PaymentAttempt,
} from "@/core/contracts";

test("重启后的 Approved execution 只以原 attempt.orderGuid 计划并提交订单", async () => {
  const planner = new RecordingPlanner(plan());
  const committer = new RecordingCommitter(result());
  const service = new ApprovedPaymentOrderCompletionService({ planner, committer });
  const approved = execution({
    attemptId: "attempt-restarted",
    orderGuid: "order-before-provider-call",
  });

  const completed = await service.complete(approved, actor());

  assert.equal(planner.inputs.length, 1);
  assert.equal(planner.inputs[0]?.attempt.attemptId, "attempt-restarted");
  assert.equal(planner.inputs[0]?.attempt.orderGuid, "order-before-provider-call");
  assert.equal(planner.inputs[0]?.receiptText, "CARD RECEIPT");
  assert.deepEqual(planner.actors, [actor()]);
  assert.deepEqual(committer.inputs, [
    {
      attemptId: "attempt-restarted",
      orderGuid: "order-before-provider-call",
      ...plan(),
      recalledHoldCompletion: null,
    },
  ]);
  assert.deepEqual(completed, result());
});

test("DB 判定为 replay 时原样返回已存在的 tender，不用 planner 新 ID 宣称新提交", async () => {
  const replayed = result({
    replayed: true,
    tenderGuid: "tender-original",
  });
  const service = new ApprovedPaymentOrderCompletionService({
    planner: new RecordingPlanner(plan({ tenderGuid: "tender-new-after-restart" })),
    committer: new RecordingCommitter(replayed),
  });

  const completed = await service.complete(execution(), actor());

  assert.strictEqual(completed, replayed);
  assert.equal(completed.replayed, true);
  assert.equal(completed.tenderGuid, "tender-original");
  assert.equal(completed.orderGuid, "order-approved");
});

test("混合支付尚未足额时如实返回 partial，不提前宣称订单完成", async () => {
  const partial = result({
    completed: false,
    signedTenderAmountCents: 500,
  });
  const service = new ApprovedPaymentOrderCompletionService({
    planner: new RecordingPlanner(plan()),
    committer: new RecordingCommitter(partial),
  });

  const completed = await service.complete(execution(), actor());

  assert.strictEqual(completed, partial);
  assert.equal(completed.completed, false);
  assert.equal(completed.signedTenderAmountCents, 500);
});

test("committer 异常必须标记 recoveryRequired，不能返回伪成功", async () => {
  const failure = new Error("transaction commit failed");
  const committer = new RecordingCommitter(result());
  committer.failure = failure;
  const service = new ApprovedPaymentOrderCompletionService({
    planner: new RecordingPlanner(plan()),
    committer,
  });

  await assert.rejects(
    () => service.complete(execution(), actor()),
    (error: unknown) => {
      assert.ok(error instanceof ApprovedPaymentOrderCompletionRecoveryRequiredError);
      assert.equal(error.recoveryRequired, true);
      assert.equal(error.attemptId, "attempt-approved");
      assert.equal(error.orderGuid, "order-approved");
      assert.strictEqual(error.cause, failure);
      return true;
    },
  );
});

test("非 Approved 或退款 attempt 在 planner 和 committer 前即被拒绝", async () => {
  const planner = new RecordingPlanner(plan());
  const committer = new RecordingCommitter(result());
  const service = new ApprovedPaymentOrderCompletionService({ planner, committer });

  await assert.rejects(
    () => service.complete(execution({ state: "Pending" }), actor()),
    PaymentAttemptStateError,
  );
  await assert.rejects(
    () => service.complete(execution({ operation: "refund" }), actor()),
    PaymentAttemptStateError,
  );
  assert.equal(planner.inputs.length, 0);
  assert.equal(committer.inputs.length, 0);
});

class RecordingPlanner implements ApprovedPaymentOrderCompletionPlannerPort {
  public readonly inputs: PaymentAttemptExecutionResult[] = [];
  public readonly actors: import("@/core/contracts").AuditActorSnapshot[] = [];

  public constructor(private readonly value: ApprovedPaymentOrderCompletionPlan) {}

  public async plan(
    executionResult: PaymentAttemptExecutionResult,
    frozenActor: import("@/core/contracts").AuditActorSnapshot,
  ): Promise<ApprovedPaymentOrderCompletionPlan> {
    this.inputs.push(executionResult);
    this.actors.push(frozenActor);
    return this.value;
  }
}

class RecordingCommitter implements ApprovedPaymentOrderCommitPort {
  public readonly inputs: ApprovedPaymentOrderCommit[] = [];
  public failure: Error | null = null;

  public constructor(private readonly value: ApprovedPaymentOrderCommitResult) {}

  public async completeApprovedPaymentOrder(
    input: ApprovedPaymentOrderCommit,
  ): Promise<ApprovedPaymentOrderCommitResult> {
    this.inputs.push(input);
    if (this.failure) throw this.failure;
    return this.value;
  }
}

function execution(
  attemptOverrides: Partial<PaymentAttempt> = {},
): PaymentAttemptExecutionResult {
  return {
    attempt: attempt(attemptOverrides),
    receiptText: "CARD RECEIPT",
    responseCode: "APPROVED",
  };
}

function actor() {
  return {
    cashierId: "cashier-1",
    cashierName: "Alice",
    userGuid: "user-guid-1",
  } as const;
}

function attempt(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-approved",
    idempotencyKey: "idempotency-approved",
    orderGuid: "order-approved",
    provider: "square",
    operation: "purchase",
    amount: { currency: "AUD", cents: 1_250 },
    state: "Approved",
    references: {
      checkoutId: "checkout-approved",
      paymentId: "payment-approved",
      sessionId: null,
      txnRef: null,
      rfn: null,
      voucherReservationToken: null,
    },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:01:00.000Z",
    lastErrorCode: null,
    receiptText: "CARD RECEIPT",
    responseCode: "APPROVED",
    ...overrides,
  };
}

function plan(
  overrides: Partial<ApprovedPaymentOrderCompletionPlan> = {},
): ApprovedPaymentOrderCompletionPlan {
  return {
    tenderGuid: "tender-approved",
    completionAuditEvents: [
      {
        eventId: "audit-approved",
        eventType: "PaymentApproved",
        occurredAtIso: "2026-07-28T00:01:00.000Z",
        orderGuid: "order-approved",
        correlationId: "attempt-approved",
        payload: {},
      },
    ],
    outbox: {
      messageId: "outbox-approved",
      aggregateId: "order-approved",
      kind: "order-sync",
      payloadJson: "{}",
      nextAttemptAtIso: "2026-07-28T00:01:00.000Z",
    },
    fulfilment: {
      print: null,
      drawer: null,
    },
    ...overrides,
  };
}

function result(
  overrides: Partial<ApprovedPaymentOrderCommitResult> = {},
): ApprovedPaymentOrderCommitResult {
  return {
    replayed: false,
    orderGuid: "order-approved",
    tenderGuid: "tender-approved",
    completed: true,
    signedTenderAmountCents: 1_250,
    ...overrides,
  };
}
