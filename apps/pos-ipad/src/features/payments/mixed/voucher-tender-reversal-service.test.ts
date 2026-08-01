import assert from "node:assert/strict";
import test from "node:test";

import {
  VoucherTenderReversalService,
  type VoucherTenderReversalRecord,
  type VoucherTenderReversalStorePort,
} from "./voucher-tender-reversal-service";

import type {
  Money,
  OrderTender,
  PaymentAttempt,
  PaymentProviderResult,
} from "@/core/contracts";
import type { MixedPaymentOrderTruth } from "@/features/payments/mixed";

const aud = (cents: number): Money => ({ currency: "AUD", cents });

test("成功撤券严格按 prepare、Submitted、provider、commit 顺序且不改原 attempt/tender", async () => {
  const events: string[] = [];
  const attempt = approvedVoucherAttempt();
  const attemptBefore = structuredClone(attempt);
  const source = voucherTender();
  const sourceBefore = structuredClone(source);
  const store = new FakeVoucherReversalStore(record({ source }), events);
  const service = createService({
    store,
    attempt,
    events,
    release: async (submitted) => {
      events.push("release");
      assert.equal(store.current.state, "Submitted");
      assert.equal(submitted.state, "Approved");
      return released(submitted);
    },
  });

  const result = await service.reverseTender(command());

  assert.deepEqual(events, [
    "prepare",
    "get-attempt",
    "submitted",
    "release",
    "commit",
  ]);
  assert.equal(result.state, "reversed");
  assert.equal(result.replayed, false);
  assert.equal(result.reversalTenderGuid, "voucher-reversal-1");
  assert.deepEqual(attempt, attemptBefore);
  assert.deepEqual(source, sourceBefore);
  assert.equal(
    result.truth.tenders.find(
      (tender) => tender.tenderGuid === source.tenderGuid,
    )?.amount.cents,
    500,
  );
  assert.deepEqual(store.releaseProofs, [
    { state: "Cancelled", responseCode: "VOUCHER_RELEASED" },
  ]);
  assert.equal(
    JSON.stringify(store.releaseProofs).includes("reservation-secret"),
    false,
  );
});

test("同进程同 action 并发重放共享一次完整外部撤券", async () => {
  const events: string[] = [];
  const store = new FakeVoucherReversalStore(record(), events);
  const pending = deferred<PaymentProviderResult>();
  const attempt = approvedVoucherAttempt();
  const firstService = createService({
    store,
    attempt,
    events,
    release: async () => {
      events.push("release");
      return pending.promise;
    },
  });
  const secondService = createService({
    store,
    attempt,
    events,
    release: async () => {
      events.push("unexpected-release");
      return pending.promise;
    },
  });

  const first = firstService.reverseTender(command());
  const duplicate = secondService.reverseTender(command());
  assert.strictEqual(duplicate, first);
  await waitUntil(() => events.includes("release"));
  pending.resolve(released(attempt));

  const [left, right] = await Promise.all([first, duplicate]);
  assert.deepEqual(right, left);
  assert.equal(events.filter((event) => event === "prepare").length, 1);
  assert.equal(events.filter((event) => event === "submitted").length, 1);
  assert.equal(events.filter((event) => event === "release").length, 1);
  assert.equal(events.filter((event) => event === "commit").length, 1);
  assert.equal(events.includes("unexpected-release"), false);
});

test("Unknown 可由同 action 重试，每次 provider 前都先持久增加 attemptCount", async () => {
  const events: string[] = [];
  const store = new FakeVoucherReversalStore(record(), events);
  const attempt = approvedVoucherAttempt();
  const service = createService({
    store,
    attempt,
    events,
    release: async () => {
      events.push(`release-${store.current.attemptCount}`);
      return {
        state: "Unknown",
        references: attempt.references,
        receiptText: null,
        responseCode: "VOUCHER_RELEASE_RESULT_UNRESOLVED",
      };
    },
  });

  const first = await service.reverseTender(command());
  const retry = await service.reverseTender(command());

  assert.equal(first.state, "unknown");
  assert.equal(retry.state, "unknown");
  assert.equal(store.current.state, "Unknown");
  assert.equal(store.current.attemptCount, 2);
  assert.deepEqual(
    events.filter(
      (event) => event === "submitted" || event.startsWith("release-"),
    ),
    ["submitted", "release-1", "submitted", "release-2"],
  );
  assert.deepEqual(store.unknownCodes, [
    "VOUCHER_RELEASE_RESULT_UNRESOLVED",
    "VOUCHER_RELEASE_RESULT_UNRESOLVED",
  ]);
});

test("transport 抛错持久化 Unknown；确定 phase/binding 非法持久化 Blocked", async () => {
  const transportEvents: string[] = [];
  const transportStore = new FakeVoucherReversalStore(
    record(),
    transportEvents,
  );
  const transport = createService({
    store: transportStore,
    attempt: approvedVoucherAttempt(),
    events: transportEvents,
    release: async () => {
      transportEvents.push("release");
      throw new Error("socket closed");
    },
  });

  const unknown = await transport.reverseTender(command());
  assert.equal(unknown.state, "unknown");
  assert.deepEqual(transportStore.unknownCodes, [
    "VOUCHER_RELEASE_TRANSPORT_ERROR",
  ]);

  const phaseEvents: string[] = [];
  const phaseStore = new FakeVoucherReversalStore(record(), phaseEvents);
  const approved = approvedVoucherAttempt();
  const phase = createService({
    store: phaseStore,
    attempt: approved,
    events: phaseEvents,
    release: async () => {
      phaseEvents.push("release");
      return {
        state: "Unknown",
        references: approved.references,
        receiptText: null,
        responseCode: "VOUCHER_RESERVATION_REQUIRED",
      };
    },
  });

  const declined = await phase.reverseTender(command());
  assert.equal(declined.state, "declined");
  assert.deepEqual(phaseStore.blockedCodes, [
    "VOUCHER_RESERVATION_REQUIRED",
  ]);
});

test("缺失或非原 Approved voucher purchase 会 Blocked 且 provider 零调用", async () => {
  for (const [label, attempt] of [
    ["missing", null],
    [
      "wrong-provider",
      approvedVoucherAttempt({ provider: "square" }),
    ],
    [
      "wrong-operation",
      approvedVoucherAttempt({ operation: "refund" }),
    ],
    [
      "wrong-state",
      approvedVoucherAttempt({ state: "Unknown" }),
    ],
    [
      "wrong-order",
      approvedVoucherAttempt({ orderGuid: "order-other" }),
    ],
    [
      "wrong-amount",
      approvedVoucherAttempt({ amount: aud(501) }),
    ],
  ] as const) {
    const events: string[] = [];
    const store = new FakeVoucherReversalStore(record(), events);
    const service = createService({
      store,
      attempt,
      events,
      release: async () => {
        events.push("release");
        return released(approvedVoucherAttempt());
      },
    });

    const result = await service.reverseTender({
      ...command(),
      actionId: `reverse-${label}`,
    });
    assert.equal(result.state, "declined", label);
    assert.equal(events.includes("release"), false, label);
    assert.equal(store.blockedCodes.length, 1, label);
  }
});

test("Reversed 重放和 Blocked 终态都不读取 attempt、不调用 provider", async () => {
  for (const state of ["Reversed", "Blocked"] as const) {
    const events: string[] = [];
    const store = new FakeVoucherReversalStore(
      record({
        state,
        reversalTenderGuid:
          state === "Reversed" ? "voucher-reversal-existing" : null,
      }),
      events,
    );
    const service = createService({
      store,
      attempt: approvedVoucherAttempt(),
      events,
      release: async () => {
        events.push("release");
        return released(approvedVoucherAttempt());
      },
    });

    const result = await service.reverseTender({
      ...command(),
      actionId: `reverse-terminal-${state}`,
    });
    assert.equal(
      result.state,
      state === "Reversed" ? "reversed" : "declined",
    );
    assert.equal(result.replayed, true);
    assert.deepEqual(events, ["prepare"]);
  }
});

function createService(input: Readonly<{
  store: VoucherTenderReversalStorePort;
  attempt: PaymentAttempt | null;
  events: string[];
  release(attempt: PaymentAttempt): Promise<PaymentProviderResult>;
}>): VoucherTenderReversalService {
  return new VoucherTenderReversalService({
    store: input.store,
    paymentAttempts: {
      async getAttempt(attemptId) {
        input.events.push("get-attempt");
        assert.equal(attemptId, "voucher-attempt-1");
        return input.attempt;
      },
    },
    release: {
      status: "available",
      release: input.release,
    },
  });
}

class FakeVoucherReversalStore
implements VoucherTenderReversalStorePort {
  public current: VoucherTenderReversalRecord;
  public readonly unknownCodes: string[] = [];
  public readonly blockedCodes: string[] = [];
  public readonly releaseProofs: Readonly<{
    state: "Cancelled";
    responseCode: "VOUCHER_RELEASED";
  }>[] = [];

  public constructor(
    initial: VoucherTenderReversalRecord,
    private readonly events: string[],
  ) {
    this.current = initial;
  }

  public async prepareOrLoad(
    input: Parameters<VoucherTenderReversalStorePort["prepareOrLoad"]>[0],
  ): Promise<VoucherTenderReversalRecord> {
    this.events.push("prepare");
    assert.deepEqual(input, {
      actionId: input.actionId,
      orderGuid: "order-1",
      sourceTenderGuid: "voucher-tender-1",
      reason: "SALE",
      actor: paymentActor(),
    });
    return this.current;
  }

  public async markSubmitted(
    record: VoucherTenderReversalRecord,
  ): Promise<VoucherTenderReversalRecord> {
    this.events.push("submitted");
    this.current = {
      ...record,
      state: "Submitted",
      attemptCount: record.attemptCount + 1,
      lastErrorCode: null,
    };
    return this.current;
  }

  public async markUnknown(
    record: VoucherTenderReversalRecord,
    errorCode: string,
  ): Promise<VoucherTenderReversalRecord> {
    this.events.push("unknown");
    this.unknownCodes.push(errorCode);
    this.current = {
      ...record,
      state: "Unknown",
      lastErrorCode: errorCode,
    };
    return this.current;
  }

  public async markBlocked(
    record: VoucherTenderReversalRecord,
    errorCode: string,
  ): Promise<VoucherTenderReversalRecord> {
    this.events.push("blocked");
    this.blockedCodes.push(errorCode);
    this.current = {
      ...record,
      state: "Blocked",
      lastErrorCode: errorCode,
    };
    return this.current;
  }

  public async commitReleased(
    record: VoucherTenderReversalRecord,
    proof: Readonly<{
      state: "Cancelled";
      responseCode: "VOUCHER_RELEASED";
    }>,
  ): Promise<VoucherTenderReversalRecord> {
    this.events.push("commit");
    this.releaseProofs.push(proof);
    const reversalTenderGuid = "voucher-reversal-1";
    const source = record.truth.tenders.find(
      (tender) => tender.tenderGuid === record.sourceTenderGuid,
    );
    assert.ok(source);
    this.current = {
      ...record,
      state: "Reversed",
      lastErrorCode: null,
      reversalTenderGuid,
      truth: {
        ...record.truth,
        tenders: [
          ...record.truth.tenders,
          {
            ...source,
            tenderGuid: reversalTenderGuid,
            amount: aud(-record.amount.cents),
            reference: null,
            reservationToken: null,
          },
        ],
        reversalLinks: [
          ...record.truth.reversalLinks,
          {
            actionId: record.actionId,
            sourceTenderGuid: record.sourceTenderGuid,
            reversalTenderGuid,
          },
        ],
      },
    };
    return this.current;
  }
}

function command() {
  return {
    actionId: "voucher-reversal-action-1",
    orderGuid: "order-1",
    tenderGuid: "voucher-tender-1",
    actor: paymentActor(),
  };
}

function record(
  overrides: Partial<VoucherTenderReversalRecord> & Readonly<{
    source?: OrderTender;
  }> = {},
): VoucherTenderReversalRecord {
  const source = overrides.source ?? voucherTender();
  return {
    actionId: "voucher-reversal-action-1",
    orderGuid: "order-1",
    sourceTenderGuid: source.tenderGuid,
    sourceAttemptId: "voucher-attempt-1",
    amount: aud(500),
    reason: "SALE",
    state: "Prepared",
    attemptCount: 0,
    lastErrorCode: null,
    reversalTenderGuid: null,
    actor: paymentActor(),
    truth: orderTruth(source),
    ...overrides,
  };
}

function paymentActor() {
  return {
    cashierId: "cashier-1",
    cashierName: "Alice",
    userGuid: "user-guid-1",
  } as const;
}

function orderTruth(source: OrderTender): MixedPaymentOrderTruth {
  return {
    orderGuid: "order-1",
    state: "Completing",
    actualAmount: aud(1_000),
    tenders: [source],
    reversalLinks: [],
  };
}

function voucherTender(): OrderTender {
  return {
    tenderGuid: "voucher-tender-1",
    method: "voucher",
    amount: aud(500),
    reference: null,
    reservationToken: null,
  };
}

function approvedVoucherAttempt(
  overrides: Partial<PaymentAttempt> = {},
): PaymentAttempt {
  return {
    attemptId: "voucher-attempt-1",
    idempotencyKey: "voucher-idempotency-1",
    orderGuid: "order-1",
    provider: "voucher",
    operation: "purchase",
    amount: aud(500),
    state: "Approved",
    references: {
      checkoutId: null,
      paymentId: null,
      sessionId: null,
      txnRef: null,
      rfn: null,
      voucherReservationToken: "reservation-secret",
    },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:01:00.000Z",
    lastErrorCode: null,
    ...overrides,
  };
}

function released(attempt: PaymentAttempt): PaymentProviderResult {
  return {
    state: "Cancelled",
    references: attempt.references,
    receiptText: null,
    responseCode: "VOUCHER_RELEASED",
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((onResolve) => {
    resolve = onResolve;
  });
  return { promise, resolve };
}

async function waitUntil(
  predicate: () => boolean,
  attempts = 50,
): Promise<void> {
  for (let index = 0; index < attempts; index += 1) {
    if (predicate()) return;
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
  throw new Error("Condition was not met.");
}
