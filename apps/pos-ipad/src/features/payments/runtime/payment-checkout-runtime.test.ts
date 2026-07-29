import assert from "node:assert/strict";
import test from "node:test";

import {
  PAYMENT_PERMISSION,
  PaymentCheckoutRuntime,
  PaymentCheckoutRuntimeError,
  type PaymentCartLease,
  type PaymentCartLeasePort,
  type PaymentCheckoutAttemptPort,
  type PaymentCheckoutDraft,
  type PaymentCheckoutDraftPort,
  type PaymentCheckoutMixedCoordinatorPort,
  type PaymentPermissionCode,
} from "./payment-checkout-runtime";

import type { PaymentAttempt, PaymentProvider } from "@/core/contracts";
import type {
  PaymentProviderAvailability,
  PaymentProviderAvailabilityPort,
} from "@/features/payments/runtime/payment-provider-registry";

test("重复点击共享同一 action，provider/completion/clear 各执行一次且公开 JSON 无秘密", async () => {
  const harness = createHarness();
  const pending = deferred<void>();
  let calls = 0;
  harness.mixed.addOnlineTender = async () => {
    calls += 1;
    await pending.promise;
    const approved = attempt({ state: "Approved" });
    harness.attempts.put(approved);
    harness.drafts.current = draft({
      state: "PendingSync",
      remaining: aud(0),
      tenders: [
        {
          tenderGuid: "tender-card",
          method: "card",
          amount: aud(1_000),
          reversible: true,
        },
      ],
    });
    return mixed("completed", {
      attemptId: approved.attemptId,
      tenderGuid: "tender-card",
      remaining: aud(0),
    });
  };
  const runtime = harness.runtime();
  const input = startInput();

  const first = runtime.start(input);
  const duplicate = runtime.start(input);
  assert.strictEqual(first, duplicate);
  pending.resolve();
  const result = await first;

  assert.equal(calls, 1);
  assert.equal(harness.drafts.createCalls, 1);
  assert.equal(harness.lease.clearCalls, 1);
  assert.equal(result.status, "completed");
  const json = JSON.stringify(result);
  assert.equal(json.includes("checkout-secret"), false);
  assert.equal(json.includes("payment-secret"), false);
  assert.equal(json.includes("session-secret"), false);
  assert.equal(json.includes("voucher-secret"), false);
  assert.deepEqual(result.tenders, [
    {
      tenderGuid: "tender-card",
      method: "card",
      amount: aud(1_000),
      reversible: true,
    },
  ]);
});

test("配置缺失在 draft/lease/provider 边界前 fail closed，保留稳定 blocker", async () => {
  const harness = createHarness();
  harness.providers.block("square", "SQUARE_CONFIGURATION_MISSING");
  const runtime = harness.runtime();

  await assert.rejects(
    () => runtime.start(startInput()),
    (error: unknown) => {
      assert.ok(error instanceof PaymentCheckoutRuntimeError);
      assert.equal(error.code, "SQUARE_CONFIGURATION_MISSING");
      return true;
    },
  );
  assert.equal(harness.lease.acquireCalls, 0);
  assert.equal(harness.drafts.createCalls, 0);
  assert.equal(harness.mixed.onlineCalls, 0);
});

test("已知离线不创建伪 attempt，可安全改现金；create 响应丢失 Unknown 则冻结全部新 tender", async () => {
  const offline = createHarness();
  offline.mixed.onlineResult = mixed("recovery-required", {
    errorCode: "ONLINE_REQUIRED",
  });
  const offlineResult = await offline.runtime().start(startInput());
  assert.equal(offlineResult.attemptId, null);
  assert.equal(offlineResult.errorCode, "ONLINE_REQUIRED");
  assert.equal(offlineResult.allowedActions.addCash, true);
  assert.equal(offline.lease.clearCalls, 0);

  const ambiguous = createHarness();
  const unknown = attempt({ state: "Unknown" });
  ambiguous.attempts.put(unknown);
  ambiguous.mixed.onlineResult = mixed("unknown", {
    attemptId: unknown.attemptId,
    errorCode: "PAYMENT_STATUS_UNKNOWN",
  });
  const unknownResult = await ambiguous.runtime().start(startInput());
  assert.equal(unknownResult.status, "unknown");
  assert.deepEqual(unknownResult.allowedActions, {
    start: false,
    changeProvider: false,
    recover: true,
    cancel: false,
    addCash: false,
    removeTender: false,
  });
  assert.equal(ambiguous.lease.clearCalls, 0);
  assert.equal(ambiguous.lease.releaseCalls, 0);
});

test("Approved 后 completion 崩溃保持同一 OrderGuid/attempt/cart lease 等待恢复", async () => {
  const harness = createHarness();
  const approved = attempt({ state: "Approved" });
  harness.attempts.put(approved);
  harness.mixed.onlineResult = mixed("recovery-required", {
    attemptId: approved.attemptId,
    errorCode: "APPROVED_COMPLETION_FAILED",
  });

  const result = await harness.runtime().start(startInput());

  assert.equal(result.orderGuid, "order-1");
  assert.equal(result.attemptId, "attempt-1");
  assert.equal(result.errorCode, "APPROVED_COMPLETION_FAILED");
  assert.equal(result.allowedActions.recover, true);
  assert.equal(harness.lease.clearCalls, 0);
  assert.equal(harness.lease.releaseCalls, 0);
});

test("Cancelled 先以原 action 耐久 close，严格成功后才释放 lease；Unknown/Approved 不 close", async () => {
  const safe = createHarness();
  const submitted = attempt({ state: "Submitted" });
  safe.attempts.put(submitted);
  safe.drafts.recovery = cancellationRecovery(submitted);

  const cancelled = await safe.runtime().cancel({
    orderGuid: submitted.orderGuid,
    attemptId: submitted.attemptId,
  });

  assert.equal(cancelled.status, "cancelled");
  assert.equal(safe.attempts.cancelCalls, 1);
  assert.equal(safe.drafts.closeCalls, 1);
  assert.deepEqual(safe.drafts.closeInputs, [
    { orderGuid: "order-1", actionId: "card-action-1" },
  ]);
  assert.equal(safe.lease.releaseCalls, 1);
  assert.equal(safe.lease.clearCalls, 0);
  assert.deepEqual(safe.events, [
    "attempt:cancel",
    "draft:close",
    "lease:release",
  ]);

  const ambiguous = createHarness();
  const unknown = attempt({ state: "Unknown" });
  ambiguous.attempts.put(unknown);

  const blocked = await ambiguous.runtime().cancel({
    orderGuid: unknown.orderGuid,
    attemptId: unknown.attemptId,
  });

  assert.equal(blocked.status, "unknown");
  assert.equal(blocked.allowedActions.cancel, false);
  assert.equal(ambiguous.attempts.cancelCalls, 0);
  assert.equal(ambiguous.drafts.closeCalls, 0);
  assert.equal(ambiguous.lease.releaseCalls, 0);
  assert.equal(ambiguous.lease.clearCalls, 0);

  const approvedHarness = createHarness();
  const approved = attempt({ state: "Approved" });
  approvedHarness.attempts.put(approved);
  approvedHarness.drafts.recovery = cancellationRecovery(approved);

  const approvedResult = await approvedHarness.runtime().cancel({
    orderGuid: approved.orderGuid,
    attemptId: approved.attemptId,
  });

  assert.equal(approvedResult.status, "recovery-required");
  assert.equal(approvedHarness.attempts.cancelCalls, 0);
  assert.equal(approvedHarness.drafts.closeCalls, 0);
  assert.equal(approvedHarness.lease.releaseCalls, 0);
});

test("Cancelled close 幂等重放仍验证投影后释放，且沿用同一 immutable actionId", async () => {
  const harness = createHarness();
  const cancelledAttempt = attempt({ state: "Cancelled" });
  harness.attempts.put(cancelledAttempt);
  harness.drafts.recovery = cancellationRecovery(cancelledAttempt);
  harness.drafts.closeReplayed = true;

  const result = await harness.runtime().cancel({
    orderGuid: cancelledAttempt.orderGuid,
    attemptId: cancelledAttempt.attemptId,
  });

  assert.equal(result.status, "cancelled");
  assert.equal(harness.drafts.closeCalls, 1);
  assert.equal(harness.drafts.closeInputs[0]?.actionId, "card-action-1");
  assert.equal(harness.lease.releaseCalls, 1);
});

test("Cancelled close 抛错时不 release，返回冻结的 recovery-required/PAYMENT_CANCEL_FAILED", async () => {
  const harness = createHarness();
  const submitted = attempt({ state: "Submitted" });
  harness.attempts.put(submitted);
  harness.drafts.recovery = cancellationRecovery(submitted);
  harness.drafts.closeError = new Error("sqlite close failed");

  const result = await harness.runtime().cancel({
    orderGuid: submitted.orderGuid,
    attemptId: submitted.attemptId,
  });

  assert.equal(result.status, "recovery-required");
  assert.equal(result.errorCode, "PAYMENT_CANCEL_FAILED");
  assert.deepEqual(result.allowedActions, {
    start: false,
    changeProvider: false,
    recover: false,
    cancel: true,
    addCash: false,
    removeTender: false,
  });
  assert.equal(harness.drafts.closeCalls, 1);
  assert.equal(harness.lease.releaseCalls, 0);
  assert.deepEqual(harness.events, ["attempt:cancel", "draft:close"]);
});

test("缺失或不匹配的 immutable payment action 时不调用 cancel/provider/close", async () => {
  const harness = createHarness();
  const submitted = attempt({ state: "Submitted" });
  harness.attempts.put(submitted);
  harness.drafts.recovery = {
    ...cancellationRecovery(submitted),
    preparedAction: {
      ...cancellationRecovery(submitted).preparedAction,
      amount: aud(999),
    },
  };

  const result = await harness.runtime().cancel({
    orderGuid: submitted.orderGuid,
    attemptId: submitted.attemptId,
  });

  assert.equal(result.status, "recovery-required");
  assert.equal(result.errorCode, "PAYMENT_CANCEL_FAILED");
  assert.equal(harness.attempts.cancelCalls, 0);
  assert.equal(harness.drafts.closeCalls, 0);
  assert.equal(harness.lease.releaseCalls, 0);
});

test("close 返回不同 order/intent/revision 或活动 tender 投影时均 fail closed", async () => {
  const invalidDrafts: PaymentCheckoutDraft[] = [
    draft({ orderGuid: "other-order" }),
    draft({ checkoutIntentId: "other-intent" }),
    draft({ cartRevision: 8 }),
    draft({
      remaining: aud(500),
      tenders: [
        {
          tenderGuid: "unexpected-tender",
          method: "cash",
          amount: aud(500),
          reversible: true,
        },
      ],
    }),
  ];

  for (const invalid of invalidDrafts) {
    const harness = createHarness();
    const submitted = attempt({ state: "Submitted" });
    harness.attempts.put(submitted);
    harness.drafts.recovery = cancellationRecovery(submitted);
    harness.drafts.closeDraft = invalid;

    const result = await harness.runtime().cancel({
      orderGuid: submitted.orderGuid,
      attemptId: submitted.attemptId,
    });

    assert.equal(result.status, "recovery-required");
    assert.equal(result.errorCode, "PAYMENT_CANCEL_FAILED");
    assert.equal(harness.lease.releaseCalls, 0);
  }
});

test("Approved 已耐久结算为 partial 后解除 attempt 阻塞，可追加其他 method 但不能再加活动 card", async () => {
  const harness = createHarness();
  harness.mixed.addOnlineTender = async () => {
    const approved = attempt({ state: "Approved", amount: aud(500) });
    harness.attempts.put(approved);
    harness.drafts.current = draft({
      state: "Completing",
      remaining: aud(500),
      tenders: [
        {
          tenderGuid: "card-partial",
          method: "card",
          amount: aud(500),
          reversible: true,
        },
      ],
    });
    return mixed("partial", {
      attemptId: approved.attemptId,
      tenderGuid: "card-partial",
      remaining: aud(500),
    });
  };
  const runtime = harness.runtime();

  const result = await runtime.start({
    ...startInput(),
    amount: aud(500),
  });

  assert.equal(result.status, "partial");
  assert.equal(result.allowedActions.start, true);
  assert.equal(result.allowedActions.changeProvider, true);
  assert.equal(result.allowedActions.addCash, true);
  assert.equal(result.allowedActions.removeTender, true);
  assert.equal(result.allowedActions.recover, false);
  assert.equal(result.allowedActions.cancel, false);
});

test("冷启动可发现 DraftPrepared(null attempt)，保留 promotion/asOf/手工折扣并安全继续同一订单", async () => {
  const harness = createHarness();
  harness.drafts.recovery = {
    draft: harness.drafts.current,
    attemptId: null,
    preparedAction: null,
  };
  harness.mixed.onlineResult = mixed("recovery-required", {
    errorCode: "ONLINE_REQUIRED",
  });
  const runtime = harness.runtime();

  const found = await runtime.findRecoveryRequired();
  assert.equal(found?.status, "draft-prepared");
  assert.equal(found?.attemptId, null);
  const resumed = await runtime.resumeCurrent({
    actionId: "resume-action",
    provider: "square",
    amount: aud(1_000),
  });

  assert.equal(resumed?.orderGuid, "order-1");
  assert.equal(harness.drafts.createCalls, 0);
  assert.equal(harness.lease.value.pricingState.asOfIso, "2026-07-28T00:00:00.000Z");
  assert.equal(harness.lease.value.pricingState.promotions[0]?.id, "promo-1");
  assert.deepEqual(
    harness.lease.value.pricingState.lines[0]?.discountState,
    { kind: "manual-percent", basisPoints: 1_000 },
  );
});

test("binding 已落库但 attempt 未插入时自动复用原 action；Voucher 不再索取券码", async () => {
  const harness = createHarness();
  harness.drafts.recovery = {
    draft: harness.drafts.current,
    attemptId: null,
    preparedAction: {
      actionId: "bound-voucher-action",
      provider: "voucher",
      operation: "purchase",
      amount: aud(1_000),
    },
  };
  let prepareCalls = 0;
  harness.voucherPreparation = {
    async preparePurchase() {
      prepareCalls += 1;
      throw new Error("prepared voucher context must be reused");
    },
  };
  harness.mixed.onlineResult = mixed("recovery-required", {
    errorCode: "ONLINE_REQUIRED",
  });
  const runtime = harness.runtime();

  const found = await runtime.findRecoveryRequired();
  assert.equal(found?.attemptId, null);
  assert.equal(found?.provider, "voucher");
  assert.equal(found?.status, "recovery-required");
  assert.equal(
    found?.errorCode,
    "PAYMENT_PREPARED_ACTION_RECOVERY_REQUIRED",
  );
  assert.equal(found?.allowedActions.start, false);
  assert.equal(found?.allowedActions.recover, true);

  const resumed = await runtime.resumeCurrent();
  assert.equal(resumed?.orderGuid, "order-1");
  assert.equal(prepareCalls, 0);
  assert.deepEqual(harness.mixed.lastOnlineInput, {
    actionId: "bound-voucher-action",
    orderGuid: "order-1",
    provider: "voucher",
    amount: aud(1_000),
  });
});

test("纯 DraftPrepared 可显式 CAS 放弃并安全释放 lease；已有 binding 时禁止", async () => {
  const pure = createHarness();
  pure.drafts.recovery = {
    draft: pure.drafts.current,
    attemptId: null,
    preparedAction: null,
  };
  const runtime = pure.runtime();
  const found = await runtime.findRecoveryRequired();
  assert.equal(found?.allowedActions.cancel, true);

  const abandoned = await runtime.abandonPrepared({
    orderGuid: "order-1",
    actionId: "abandon-action-1",
  });
  assert.equal(abandoned.status, "cancelled");
  assert.deepEqual(abandoned.allowedActions, {
    start: false,
    changeProvider: false,
    recover: false,
    cancel: false,
    addCash: false,
    removeTender: false,
  });
  assert.equal(pure.drafts.abandonCalls, 1);
  assert.equal(pure.lease.releaseCalls, 1);

  const bound = createHarness();
  bound.drafts.recovery = {
    draft: bound.drafts.current,
    attemptId: null,
    preparedAction: {
      actionId: "bound-action",
      provider: "square",
      operation: "purchase",
      amount: aud(1_000),
    },
  };
  await assert.rejects(
    () =>
      bound.runtime().abandonPrepared({
        orderGuid: "order-1",
        actionId: "unsafe-abandon",
      }),
    (error: unknown) => {
      assert.ok(error instanceof PaymentCheckoutRuntimeError);
      assert.equal(error.code, "PAYMENT_DRAFT_ABANDON_FORBIDDEN");
      return true;
    },
  );
  assert.equal(bound.drafts.abandonCalls, 0);
  assert.equal(bound.lease.releaseCalls, 0);
});

test("Unknown 冷恢复只复用同一 attempt；恢复 Approved 完成后才清购物车", async () => {
  const harness = createHarness();
  const unknown = attempt({ state: "Unknown" });
  harness.attempts.put(unknown);
  harness.drafts.recovery = {
    draft: harness.drafts.current,
    attemptId: unknown.attemptId,
    preparedAction: null,
  };
  harness.mixed.recoverOnlineAttempt = async (input) => {
    assert.deepEqual(input, {
      orderGuid: "order-1",
      attemptId: "attempt-1",
    });
    const approved = attempt({ state: "Approved" });
    harness.attempts.put(approved);
    harness.drafts.current = draft({
      state: "PendingSync",
      remaining: aud(0),
      tenders: [
        {
          tenderGuid: "tender-card",
          method: "card",
          amount: aud(1_000),
          reversible: true,
        },
      ],
    });
    return mixed("completed", {
      attemptId: approved.attemptId,
      tenderGuid: "tender-card",
      remaining: aud(0),
    });
  };

  const result = await harness.runtime().resumeCurrent();

  assert.equal(result?.orderGuid, "order-1");
  assert.equal(result?.status, "completed");
  assert.equal(harness.lease.clearCalls, 1);
  assert.equal(harness.lease.releaseCalls, 0);
});

test("provider await 后旧可信会话失效会拒绝伪成功，且不清理 cart lease", async () => {
  const harness = createHarness();
  harness.mixed.addOnlineTender = async () => {
    harness.session.active = false;
    return mixed("completed", { remaining: aud(0) });
  };

  await assert.rejects(
    () => harness.runtime().start(startInput()),
    /CURRENT_CASHIER_REQUIRED/,
  );
  assert.equal(harness.lease.clearCalls, 0);
});

test("Voucher 上下文必在 provider 前耐久准备，且六项精确权限都经 guard", async () => {
  const permissions = new Set<PaymentPermissionCode>();
  const voucher = createHarness();
  voucher.permissions.onAssert = (code) => permissions.add(code);
  voucher.providers.unblock("voucher");
  voucher.events.length = 0;
  voucher.voucherPreparation = {
    async preparePurchase() {
      voucher.events.push("voucher-prepared");
      return { prepared: true };
    },
  };
  voucher.mixed.addOnlineTender = async () => {
    voucher.events.push("provider");
    return mixed("declined", { attemptId: null });
  };
  await voucher.runtime().start({
    ...startInput(),
    provider: "voucher",
    voucherCode: "SECRET-VOUCHER-CODE",
  });
  assert.ok(
    voucher.events.indexOf("voucher-prepared") <
      voucher.events.indexOf("provider"),
  );

  const cash = createHarness();
  cash.permissions.onAssert = (code) => permissions.add(code);
  cash.mixed.cashResult = mixed("partial", {
    tenderGuid: "cash-tender",
    remaining: aud(500),
  });
  cash.drafts.afterCash = draft({
    state: "Completing",
    remaining: aud(500),
    tenders: [
      {
        tenderGuid: "cash-tender",
        method: "cash",
        amount: aud(500),
        reversible: true,
      },
    ],
  });
  await cash.runtime().addCash({
    orderGuid: "order-1",
    actionId: "cash-action",
    amount: aud(500),
  });

  const remove = createHarness(
    draft({
      state: "Completing",
      remaining: aud(500),
      tenders: [
        {
          tenderGuid: "cash-tender",
          method: "cash",
          amount: aud(500),
          reversible: true,
        },
      ],
    }),
  );
  remove.permissions.onAssert = (code) => permissions.add(code);
  remove.mixed.removeResult = mixed("partial", {
    tenderGuid: "cash-reversal",
    remaining: aud(1_000),
  });
  remove.drafts.afterRemove = draft();
  await remove.runtime().removeTender({
    orderGuid: "order-1",
    actionId: "remove-action",
    tenderGuid: "cash-tender",
  });

  const card = createHarness();
  card.permissions.onAssert = (code) => permissions.add(code);
  card.mixed.onlineResult = mixed("recovery-required", {
    errorCode: "ONLINE_REQUIRED",
  });
  await card.runtime().start(startInput());

  assert.deepEqual(
    new Set(permissions),
    new Set(Object.values(PAYMENT_PERMISSION)),
  );
});

test("每种活动 tender 最多一个；reversal 后 draft 移除活动行才允许重加", async () => {
  const activeCard = createHarness(
    draft({
      state: "Completing",
      remaining: aud(500),
      tenders: [
        {
          tenderGuid: "card-existing",
          method: "card",
          amount: aud(500),
          reversible: true,
        },
      ],
    }),
  );
  await assert.rejects(
    () =>
      activeCard.runtime().start({
        ...startInput(),
        amount: aud(500),
      }),
    (error: unknown) => {
      assert.ok(error instanceof PaymentCheckoutRuntimeError);
      assert.equal(error.code, "PAYMENT_TENDER_METHOD_ALREADY_ACTIVE");
      return true;
    },
  );
  assert.equal(activeCard.mixed.onlineCalls, 0);

  const activeCash = createHarness(
    draft({
      state: "Completing",
      remaining: aud(500),
      tenders: [
        {
          tenderGuid: "cash-existing",
          method: "cash",
          amount: aud(500),
          reversible: true,
        },
      ],
    }),
  );
  await assert.rejects(
    () =>
      activeCash.runtime().addCash({
        orderGuid: "order-1",
        actionId: "cash-duplicate",
        amount: aud(500),
      }),
    (error: unknown) => {
      assert.ok(error instanceof PaymentCheckoutRuntimeError);
      assert.equal(error.code, "PAYMENT_TENDER_METHOD_ALREADY_ACTIVE");
      return true;
    },
  );
  assert.equal(activeCash.mixed.cashCalls, 0);
});

function createHarness(initialDraft = draft()) {
  const events: string[] = [];
  const session = new SessionGuard();
  const permissions = new PermissionRecorder();
  const drafts = new MemoryDrafts(initialDraft, events);
  const lease = new MemoryLease(events);
  const attempts = new MemoryAttempts(events);
  const providers = new ProviderAvailability();
  const mixed = new MemoryMixed(drafts);
  let voucherPreparation:
    | {
        preparePurchase(input: {
          actionId: string;
          orderGuid: string;
          voucherCode: string;
        }): Promise<{ prepared: true }>;
      }
    | undefined;
  return {
    events,
    session,
    permissions,
    drafts,
    lease,
    attempts,
    providers,
    mixed,
    get voucherPreparation() {
      return voucherPreparation;
    },
    set voucherPreparation(value) {
      voucherPreparation = value;
    },
    runtime() {
      const options = {
        mixed,
        attempts,
        drafts,
        cartLease: lease,
        providers,
        trustedSession: session,
        permissions,
      };
      return new PaymentCheckoutRuntime(
        voucherPreparation
          ? { ...options, voucherPreparation }
          : options,
      );
    },
  };
}

class SessionGuard {
  public active = true;

  public assertActive(): void {
    if (!this.active) throw new Error("CURRENT_CASHIER_REQUIRED");
  }
}

class PermissionRecorder {
  public onAssert: (code: PaymentPermissionCode) => void = () => {};

  public assert(code: PaymentPermissionCode): void {
    this.onAssert(code);
  }
}

class MemoryLease implements PaymentCartLeasePort {
  public readonly value = lease();
  public acquireCalls = 0;
  public readCalls = 0;
  public clearCalls = 0;
  public releaseCalls = 0;

  public constructor(private readonly events: string[]) {}

  public async acquireExact(input: {
    checkoutIntentId: string;
    expectedRevision: number;
  }): Promise<PaymentCartLease> {
    this.acquireCalls += 1;
    assert.equal(input.checkoutIntentId, this.value.checkoutIntentId);
    assert.equal(input.expectedRevision, this.value.revision);
    return this.value;
  }

  public async readExact(value: PaymentCartLease): Promise<PaymentCartLease> {
    this.readCalls += 1;
    assert.strictEqual(value, this.value);
    return this.value;
  }

  public async clearAfterCompleted(
    value: PaymentCartLease,
    orderGuid: string,
  ): Promise<void> {
    assert.strictEqual(value, this.value);
    assert.equal(orderGuid, "order-1");
    this.clearCalls += 1;
  }

  public async releaseAfterSafeCancel(
    value: PaymentCartLease,
    orderGuid: string,
  ): Promise<void> {
    assert.strictEqual(value, this.value);
    assert.equal(orderGuid, "order-1");
    this.releaseCalls += 1;
    this.events.push("lease:release");
  }
}

class MemoryDrafts implements PaymentCheckoutDraftPort {
  public createCalls = 0;
  public abandonCalls = 0;
  public closeCalls = 0;
  public closeReplayed = false;
  public closeError: unknown = null;
  public closeDraft: PaymentCheckoutDraft | null = null;
  public readonly closeInputs: Readonly<{
    orderGuid: string;
    actionId: string;
  }>[] = [];
  public recovery: {
    draft: PaymentCheckoutDraft;
    attemptId: string | null;
    preparedAction: {
      actionId: string;
      provider: PaymentProvider;
      operation: "purchase";
      amount: ReturnType<typeof aud>;
    } | null;
  } | null = null;
  public afterCash: PaymentCheckoutDraft | null = null;
  public afterRemove: PaymentCheckoutDraft | null = null;

  public constructor(
    public current: PaymentCheckoutDraft,
    private readonly events: string[],
  ) {}

  public async createOrReuse(input: {
    checkoutIntentId: string;
    lease: PaymentCartLease;
  }): Promise<PaymentCheckoutDraft> {
    this.createCalls += 1;
    assert.equal(input.checkoutIntentId, this.current.checkoutIntentId);
    assert.equal(input.lease.pricingState.promotions[0]?.id, "promo-1");
    assert.deepEqual(
      input.lease.pricingState.lines[0]?.discountState,
      { kind: "manual-percent", basisPoints: 1_000 },
    );
    return this.current;
  }

  public async read(orderGuid: string): Promise<PaymentCheckoutDraft | null> {
    assert.equal(orderGuid, this.current.orderGuid);
    return this.current;
  }

  public async findBlockingRecovery() {
    return this.recovery;
  }

  public async abandonPrepared(input: {
    orderGuid: string;
    actionId: string;
  }) {
    assert.equal(input.orderGuid, this.current.orderGuid);
    assert.ok(input.actionId.trim());
    this.abandonCalls += 1;
    return {
      draft: this.current,
      replayed: false,
    };
  }

  public async closeCancelled(input: {
    orderGuid: string;
    actionId: string;
  }) {
    this.closeCalls += 1;
    this.closeInputs.push(input);
    this.events.push("draft:close");
    if (this.closeError) throw this.closeError;
    return {
      draft: this.closeDraft ?? this.current,
      replayed: this.closeReplayed,
    };
  }
}

class MemoryAttempts implements PaymentCheckoutAttemptPort {
  private readonly attempts = new Map<string, PaymentAttempt>();
  public cancelCalls = 0;

  public constructor(private readonly events: string[]) {}

  public put(value: PaymentAttempt): void {
    this.attempts.set(value.attemptId, value);
  }

  public async getAttempt(attemptId: string): Promise<PaymentAttempt | null> {
    return this.attempts.get(attemptId) ?? null;
  }

  public async getBlockingAttempt(
    orderGuid: string,
  ): Promise<PaymentAttempt | null> {
    return (
      [...this.attempts.values()].find(
        (value) =>
          value.orderGuid === orderGuid &&
          ["Created", "Submitted", "Pending", "Unknown", "Approved"].includes(
            value.state,
          ),
      ) ?? null
    );
  }

  public async cancelAttempt(attemptId: string) {
    this.cancelCalls += 1;
    this.events.push("attempt:cancel");
    const current = this.attempts.get(attemptId);
    if (!current) throw new Error("attempt missing");
    const cancelled = { ...current, state: "Cancelled" as const };
    this.put(cancelled);
    return {
      attempt: cancelled,
      receiptText: null,
      responseCode: "CANCELLED",
    };
  }
}

class ProviderAvailability implements PaymentProviderAvailabilityPort {
  private readonly entries = new Map<
    PaymentProvider,
    PaymentProviderAvailability
  >([
    [
      "square",
      { provider: "square", available: true, blocker: null },
    ],
    [
      "linkly-cloud",
      { provider: "linkly-cloud", available: true, blocker: null },
    ],
    [
      "voucher",
      { provider: "voucher", available: true, blocker: null },
    ],
  ]);

  public block(
    provider: PaymentProvider,
    blocker: NonNullable<PaymentProviderAvailability["blocker"]>,
  ): void {
    this.entries.set(provider, { provider, available: false, blocker });
  }

  public unblock(provider: PaymentProvider): void {
    this.entries.set(provider, { provider, available: true, blocker: null });
  }

  public getAvailability(provider: PaymentProvider): PaymentProviderAvailability {
    return (
      this.entries.get(provider) ?? {
        provider,
        available: false,
        blocker: "PAYMENT_PROVIDER_UNKNOWN",
      }
    );
  }

  public listAvailability(): readonly PaymentProviderAvailability[] {
    return [...this.entries.values()];
  }
}

class MemoryMixed implements PaymentCheckoutMixedCoordinatorPort {
  public onlineCalls = 0;
  public cashCalls = 0;
  public removeCalls = 0;
  public lastOnlineInput: {
    actionId: string;
    orderGuid: string;
    provider: PaymentProvider;
    amount: ReturnType<typeof aud>;
  } | null = null;
  public onlineResult = mixed("recovery-required", {
    errorCode: "ONLINE_REQUIRED",
  });
  public cashResult = mixed("partial", { remaining: aud(500) });
  public removeResult = mixed("partial");

  public constructor(private readonly drafts: MemoryDrafts) {}

  public async addOnlineTender(input: {
    actionId: string;
    orderGuid: string;
    provider: PaymentProvider;
    amount: ReturnType<typeof aud>;
  }): Promise<ReturnType<typeof mixed>> {
    this.onlineCalls += 1;
    this.lastOnlineInput = input;
    return this.onlineResult;
  }

  public async recoverOnlineAttempt(_input: {
    orderGuid: string;
    attemptId: string;
  }): Promise<ReturnType<typeof mixed>> {
    return this.onlineResult;
  }

  public async addCashTender(_input: {
    actionId: string;
    orderGuid: string;
    amount: ReturnType<typeof aud>;
  }): Promise<ReturnType<typeof mixed>> {
    this.cashCalls += 1;
    if (this.drafts.afterCash) {
      this.drafts.current = this.drafts.afterCash;
      this.drafts.afterCash = null;
    }
    return this.cashResult;
  }

  public async removeTender(_input: {
    actionId: string;
    orderGuid: string;
    tenderGuid: string;
  }): Promise<ReturnType<typeof mixed>> {
    this.removeCalls += 1;
    if (this.drafts.afterRemove) {
      this.drafts.current = this.drafts.afterRemove;
      this.drafts.afterRemove = null;
    }
    return this.removeResult;
  }
}

function startInput() {
  return {
    checkoutIntentId: "checkout-intent-1",
    expectedCartRevision: 7,
    actionId: "card-action-1",
    provider: "square" as const,
    amount: aud(1_000),
  };
}

function lease(): PaymentCartLease {
  const pricingState = {
    revision: 7,
    mode: "sale" as const,
    asOfIso: "2026-07-28T00:00:00.000Z",
    promotions: [
      {
        id: "promo-1",
        name: "Promotion",
        effectiveStartIso: "2026-07-01T00:00:00.000Z",
        effectiveEndIso: "2026-07-31T23:59:59.000Z",
        isExclusive: false,
        priority: 1,
        applyQuantity: 2,
        fixedPrice: aud(900),
        maxApplicationsPerOrder: 1,
        products: [{ productCode: "P1", unitWeight: 1 }],
      },
    ],
    lines: [
      {
        lineId: "line-1",
        productCode: "P1",
        itemNumber: "I1",
        lookupCode: "930000000001",
        displayName: "Product 1",
        quantity: 1,
        unitPriceCents: 1_100,
        basePriceSource: "catalog" as const,
        kind: "sale" as const,
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: {
          kind: "manual-percent" as const,
          basisPoints: 1_000,
        },
      },
    ],
  };
  const cart = {
    revision: 7,
    mode: "sale" as const,
    lines: [
      {
        lineId: "line-1",
        productCode: "P1",
        itemNumber: "I1",
        lookupCode: "930000000001",
        displayName: "Product 1",
        quantity: "1",
        unitPrice: aud(1_100),
        discount: aud(100),
        actualAmount: aud(1_000),
        priceSource: "catalog" as const,
        kind: "sale" as const,
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
      },
    ],
    subtotal: aud(1_100),
    discount: aud(100),
    actualAmount: aud(1_000),
  };
  return {
    leaseId: "lease-1",
    checkoutIntentId: "checkout-intent-1",
    revision: 7,
    total: aud(1_000),
    cart,
    pricingState,
  };
}

function draft(
  overrides: Partial<PaymentCheckoutDraft> = {},
): PaymentCheckoutDraft {
  return {
    checkoutIntentId: "checkout-intent-1",
    orderGuid: "order-1",
    cartRevision: 7,
    state: "DraftPrepared",
    total: aud(1_000),
    remaining: aud(1_000),
    tenders: [],
    ...overrides,
  };
}

function attempt(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-1",
    idempotencyKey: "idempotency-1",
    orderGuid: "order-1",
    provider: "square",
    operation: "purchase",
    amount: aud(1_000),
    state: "Submitted",
    references: {
      checkoutId: "checkout-secret",
      paymentId: "payment-secret",
      sessionId: "session-secret",
      txnRef: "txn-secret",
      rfn: "rfn-secret",
      voucherReservationToken: "voucher-secret",
    },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:01:00.000Z",
    lastErrorCode: null,
    ...overrides,
  };
}

function cancellationRecovery(value: PaymentAttempt) {
  return {
    draft: draft(),
    attemptId: value.attemptId,
    preparedAction: {
      actionId: "card-action-1",
      provider: value.provider,
      operation: "purchase" as const,
      amount: value.amount,
    },
  };
}

function mixed(
  status:
    | "awaiting-terminal"
    | "pending"
    | "unknown"
    | "partial"
    | "completed"
    | "declined"
    | "cancelled"
    | "recovery-required",
  overrides: Partial<{
    remaining: ReturnType<typeof aud>;
    attemptId: string | null;
    tenderGuid: string | null;
    errorCode: string | null;
  }> = {},
) {
  return {
    status,
    orderGuid: "order-1",
    remaining: aud(1_000),
    attemptId: null,
    tenderGuid: null,
    capability: "available" as const,
    errorCode: null,
    ...overrides,
  };
}

function aud(cents: number) {
  return { currency: "AUD", cents } as const;
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}
