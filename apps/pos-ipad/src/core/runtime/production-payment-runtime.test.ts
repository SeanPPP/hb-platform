import assert from "node:assert/strict";
import test from "node:test";

import { CurrentCashierSession } from "./current-cashier-session";
import {
  createProductionPaymentRuntime,
  createProductionTenderReversalRouter,
} from "./production-payment-runtime";

import type {
  Money,
  OrderTender,
  PaymentAttempt,
  PaymentAttemptRepositoryPort,
  PaymentProvider,
} from "@/core/contracts";
import type { PosDatabase } from "@/core/db/pos-database";
import type {
  PaymentDraftRecovery,
} from "@/core/db/sqlite-payment-draft-recovery-store";
import type {
  PosRepositoryBundle,
  SensitivePayloadEncryptor,
} from "@/core/db/sqlite-repositories";
import type {
  PaymentProviderRuntimeBootstrap,
  PaymentProviderRuntimeBootstrapWithVoucherRelease,
} from "@/core/runtime/payment-provider-runtime-bootstrap";
import type {
  MixedPaymentOrderTruth,
  MixedTenderReversalPort,
  VoucherTenderReversalRecord,
  VoucherTenderReversalReason,
} from "@/features/payments/mixed";
import type {
  PaymentCheckoutDraft,
} from "@/features/payments/runtime/payment-checkout-runtime";
import type {
  VoucherApprovedPurchaseReleasePort,
} from "@/features/payments/runtime/payment-provider-registry";
import { PricingCart } from "@/features/sales/domain";
import { ActivePricingCartSession } from "@/features/sales/runtime";


const ALL_PAYMENT_PERMISSIONS = [
  "Permissions.PosTerminal.Payment.View",
  "Permissions.PosTerminal.Payment.TakeCash",
  "Permissions.PosTerminal.Payment.TakeCard",
  "Permissions.PosTerminal.Payment.TakeVoucher",
  "Permissions.PosTerminal.Payment.RemoveTender",
  "Permissions.PosTerminal.Payment.Confirm",
] as const;
const TEST_AUDIT_ACTOR = Object.freeze({
  cashierId: "cashier-1",
  cashierName: "Cashier",
  userGuid: null,
});

test("生产支付只公开 presenter/恢复布尔值，启动前和无可信收银员均 fail closed", async () => {
  let voucherBindCount = 0;
  const cashier = new CurrentCashierSession();
  const runtime = createProductionPaymentRuntime({
    database: database(),
    repositories: repositories(),
    encryptor,
    activeCart: new ActivePricingCartSession(
      new PricingCart(),
      () => new PricingCart(),
    ),
    currentCashier: cashier,
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: idFactory(),
    connectivity: { async isOnline() { return true; } },
    bootstrap: bootstrap(() => {
      voucherBindCount += 1;
    }),
    async drainFulfilment() {},
  });

  const service = runtime.service;
  assert.equal(service.status, "available");
  assert.notEqual(runtime.returnRefund, null);
  if (service.status !== "available") return;
  assert.deepEqual(Object.keys(service).sort(), [
    "createPresenter",
    "hasRecoveryRequired",
    "status",
  ]);
  assert.equal(
    JSON.stringify(service).includes("attempt"),
    false,
  );
  assert.equal(voucherBindCount, 1);

  assert.throws(
    () =>
      service.createPresenter({
        checkoutIntentId: "checkout-1",
        expectedCartRevision: 1,
        total: { currency: "AUD", cents: 500 },
      }),
    /PAYMENT_RUNTIME_NOT_INITIALIZED/,
  );

  await runtime.initializeRecovery();
  assert.throws(
    () => service.createPresenter(null),
    /CURRENT_CASHIER_REQUIRED/,
  );

  const authenticationEpoch = cashier.beginAuthentication();
  cashier.activate(
    authenticationEpoch,
    {
      source: "online",
      session: {
        cashierId: "cashier-1",
        cashierName: "Cashier",
        storeCode: "S1",
        deviceCode: "IPAD-1",
        permissionCodes: [...ALL_PAYMENT_PERMISSIONS],
      },
    },
    { storeCode: "S1", deviceCode: "IPAD-1" },
  );

  const presenter = service.createPresenter({
    checkoutIntentId: "checkout-1",
    expectedCartRevision: 1,
    total: { currency: "AUD", cents: 500 },
  });
  assert.equal(presenter.getState().total.cents, 500);
  assert.equal(
    JSON.stringify(presenter.getState()).includes("provider-reference"),
    false,
  );
  assert.equal(await service.hasRecoveryRequired(), false);
  presenter.destroy();

  assert.throws(
    () =>
      service.createPresenter({
        checkoutIntentId: "checkout-invalid",
        expectedCartRevision: -1,
        total: { currency: "AUD", cents: 500 },
      }),
    /PAYMENT_DRAFT_CONFLICT/,
  );
});

test("RecallActive 与同一支付草稿并存时启动保留 binding 并锁住恢复购物车", async () => {
  const cart = pricedCart();
  const binding = {
    kind: "recalled",
    scope: { storeCode: "S1", deviceCode: "IPAD-1" },
    holdId: "hold-payment-recovery",
    recallAttemptId: "attempt-payment-recovery",
  } as const;
  const activeCart = new ActivePricingCartSession(
    new PricingCart(),
    () => new PricingCart(),
  );
  activeCart.blockForRecallRecovery(binding);
  const baseDatabase = database();
  const baseDrafts = baseDatabase.paymentDraftRecovery({
    createOrderGuid: () => "unused-order",
    createOrderLineGuid: () => "unused-line",
    createAuditEventId: () => "unused-audit",
  });
  const runtime = createProductionPaymentRuntime({
    database: {
      ...baseDatabase,
      paymentDraftRecovery: () => ({
        ...baseDrafts,
        async findBlockingRecovery() {
          return { ...draftRecovery(cart), recallBinding: binding };
        },
      }),
    } as unknown as PosDatabase,
    repositories: repositories(),
    encryptor,
    activeCart,
    currentCashier: new CurrentCashierSession(),
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: idFactory(),
    connectivity: { async isOnline() { return true; } },
    bootstrap: bootstrap(() => undefined),
    async drainFulfilment() {},
  });

  await runtime.initializeRecovery();
  assert.deepEqual(activeCart.read().recallBinding, binding);
  assert.deepEqual(activeCart.read().cart, cart.snapshot());
  assert.equal(activeCart.read().terminalRecoveryRequired, false);
  assert.throws(
    () => activeCart.increaseLine("line-1"),
    { code: "ACTIVE_PRICING_CART_BUSY" },
  );
});

test("生产支付 facade 基于当前 cashier 的 TakeCash 权限透传现金可用性", async () => {
  const runtime = createProductionPaymentRuntime({
    database: database(),
    repositories: repositories(),
    encryptor,
    activeCart: new ActivePricingCartSession(
      new PricingCart(),
      () => new PricingCart(),
    ),
    currentCashier: activeCashier(
      ALL_PAYMENT_PERMISSIONS.filter(
        (code) => code !== "Permissions.PosTerminal.Payment.TakeCash",
      ),
    ),
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: idFactory(),
    connectivity: { async isOnline() { return true; } },
    bootstrap: bootstrap(() => undefined),
    async drainFulfilment() {},
  });
  await runtime.initializeRecovery();
  if (runtime.service.status !== "available") return;

  const presenter = runtime.service.createPresenter({
    checkoutIntentId: "checkout-no-cash",
    expectedCartRevision: 1,
    total: { currency: "AUD", cents: 500 },
  });
  assert.notEqual(presenter.getState().selectedMethod, "cash");
  assert.equal(await presenter.initialize(), true);
  assert.equal(presenter.getState().allowedActions.addCash, false);
});

test("普通支付展示行只投影可信活动购物车，忽略路由伪造明细", async () => {
  const cart = pricedCart();
  const activeCart = new ActivePricingCartSession(
    cart,
    () => new PricingCart(),
  );
  const snapshot = activeCart.getSnapshot();
  const runtime = createProductionPaymentRuntime({
    database: database(),
    repositories: repositories(),
    encryptor,
    activeCart,
    currentCashier: activeCashier(),
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: idFactory(),
    connectivity: { async isOnline() { return true; } },
    bootstrap: bootstrap(() => undefined),
    async drainFulfilment() {},
  });
  await runtime.initializeRecovery();
  const service = runtime.service;
  assert.equal(service.status, "available");
  if (service.status !== "available") return;

  const presenter = service.createPresenter({
    checkoutIntentId: "checkout-1",
    expectedCartRevision: snapshot.revision,
    total: snapshot.actualAmount,
    lines: [{
      lineKey: "forged-line",
      displayName: "Forged item",
      quantity: "99",
      actualAmountCents: 1,
    }],
  });
  assert.deepEqual(presenter.getState().checkout.lines, [{
    lineKey: "line-1",
    displayName: "Tea",
    quantity: "1",
    actualAmountCents: 1_000,
  }]);
  presenter.destroy();

  const stalePresenter = service.createPresenter({
    checkoutIntentId: "checkout-stale",
    expectedCartRevision: snapshot.revision + 1,
    total: snapshot.actualAmount,
    lines: [{
      lineKey: "forged-line",
      displayName: "Forged item",
      quantity: "99",
      actualAmountCents: 1,
    }],
  });
  assert.deepEqual(stalePresenter.getState().checkout.lines, []);
  stalePresenter.destroy();
});

test("生产 reversal router 只把现金和礼券交给各自实现，银行卡始终失败关闭且零 provider 调用", async () => {
  const truth = mixedTruth();
  const cashCalls: string[] = [];
  const voucherProviderCalls: string[] = [];
  const cash: MixedTenderReversalPort = {
    async reverseTender(command) {
      cashCalls.push(command.tenderGuid);
      return reversedMutation(truth, command.actionId, command.tenderGuid);
    },
  };
  const voucher: MixedTenderReversalPort = {
    async reverseTender(command) {
      voucherProviderCalls.push(command.tenderGuid);
      return reversedMutation(truth, command.actionId, command.tenderGuid);
    },
  };
  const router = createProductionTenderReversalRouter({
    orderTruth: {
      async getPaymentTruth(orderGuid) {
        return orderGuid === truth.orderGuid ? truth : null;
      },
    },
    cash,
    voucher,
  });

  await router.reverseTender({
    actionId: "reverse-cash",
    orderGuid: "order-1",
    tenderGuid: "cash-1",
    actor: TEST_AUDIT_ACTOR,
  });
  await router.reverseTender({
    actionId: "reverse-voucher",
    orderGuid: "order-1",
    tenderGuid: "voucher-1",
    actor: TEST_AUDIT_ACTOR,
  });
  await assert.rejects(
    router.reverseTender({
      actionId: "reverse-card",
      orderGuid: "order-1",
      tenderGuid: "card-1",
      actor: TEST_AUDIT_ACTOR,
    }),
    /TENDER_REVERSAL_UNAVAILABLE/,
  );

  assert.deepEqual(cashCalls, ["cash-1"]);
  assert.deepEqual(voucherProviderCalls, ["voucher-1"]);
});

test("公开 draft 只有现金和具备 release capability 的礼券可撤，绝不暴露 action/token/provider ref", async () => {
  for (const available of [false, true]) {
    let releaseCalls = 0;
    const cashier = activeCashier();
    const runtime = createProductionPaymentRuntime({
      database: database(checkoutDraft()),
      repositories: repositories(),
      encryptor,
      activeCart: new ActivePricingCartSession(
        new PricingCart(),
        () => new PricingCart(),
      ),
      currentCashier: cashier,
      terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
      clock: {
        now: () => new Date("2026-07-28T00:00:00.000Z"),
        nowIso: () => "2026-07-28T00:00:00.000Z",
      },
      createId: idFactory(),
      connectivity: { async isOnline() { return true; } },
      bootstrap: voucherReleaseBootstrap(available, async () => {
        releaseCalls += 1;
        throw new Error("projection must not call provider");
      }),
      async drainFulfilment() {},
    });
    await runtime.initializeRecovery();
    const service = runtime.service;
    assert.equal(service.status, "available");
    if (service.status !== "available") continue;

    const presenter = service.createPresenter(null);
    assert.equal(await presenter.load("order-1"), true);
    assert.deepEqual(
      presenter.getState().tenders.map((tender) => [
        tender.method,
        tender.reversible,
      ]),
      [
        ["cash", true],
        ["card", false],
        ["voucher", available],
      ],
    );
    const serialized = JSON.stringify(presenter.getState());
    assert.equal(serialized.includes("actionId"), false);
    assert.equal(serialized.includes("reservation-secret"), false);
    assert.equal(serialized.includes("provider-reference"), false);
    assert.equal(releaseCalls, 0);
    presenter.destroy();
  }
});

test("生产 draft port 的 durable close 是唯一提交屏障，提交后不再读取草稿或复核会话", async () => {
  for (const branch of [
    { name: "DraftPrepared", cancellableAfterReversal: false },
    { name: "fully-reversed", cancellableAfterReversal: true },
  ]) {
    const cart = pricedCart();
    const cashier = activeCashier();
    const draft: PaymentCheckoutDraft = {
      checkoutIntentId: "checkout-1",
      orderGuid: "order-1",
      cartRevision: cart.snapshot().revision,
      state: branch.cancellableAfterReversal ? "Completing" : "DraftPrepared",
      total: aud(1_000),
      remaining: aud(1_000),
      cancellableAfterReversal: branch.cancellableAfterReversal,
      tenders: [],
    };
    const durable = durableCloseDatabase({
      draft,
      recovery: draftRecovery(cart),
      onCommitted: () => cashier.clear(),
    });
    const activeCart = new ActivePricingCartSession(
      new PricingCart(),
      () => new PricingCart(),
    );
    const runtime = createProductionPaymentRuntime({
      database: durable.database,
      repositories: repositories(),
      encryptor,
      activeCart,
      currentCashier: cashier,
      terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
      clock: {
        now: () => new Date("2026-07-28T00:00:00.000Z"),
        nowIso: () => "2026-07-28T00:00:00.000Z",
      },
      createId: idFactory(),
      connectivity: { async isOnline() { return true; } },
      bootstrap: bootstrap(() => undefined),
      async drainFulfilment() {},
    });
    await runtime.initializeRecovery();
    assert.equal(runtime.service.status, "available");
    if (runtime.service.status !== "available") continue;
    const presenter = runtime.service.createPresenter(null);
    assert.equal(await presenter.initialize(), true, branch.name);
    assert.equal(presenter.getState().allowedActions.cancel, true);

    assert.equal(await presenter.cancel(), true, branch.name);
    assert.equal(durable.readAfterCommit, 0, branch.name);
    assert.equal(
      branch.cancellableAfterReversal
        ? durable.closeFullyReversedCalls
        : durable.abandonPreparedCalls,
      1,
      branch.name,
    );
    activeCart.increaseLine("line-1");
    assert.equal(activeCart.read().cart.lines[0]?.quantity, "2");
    presenter.destroy();
  }
});

test("礼券 reversal Unknown 后即时关闭换 provider、加现金和再次移除，provider/cash 均零新增调用", async () => {
  let releaseCalls = 0;
  let providerCalls = 0;
  let cashCalls = 0;
  const attempt = approvedVoucherAttempt();
  const cart = pricedCart();
  const truth = voucherOnlyTruth();
  const draft = voucherCheckoutDraft(cart.snapshot().revision);
  const recovery = draftRecovery(cart);
  const record = voucherReversalRecord(truth);
  const cashier = activeCashier();
  const runtime = createProductionPaymentRuntime({
    database: reversalDatabase({
      draft,
      recovery,
      record,
      onCash: () => {
        cashCalls += 1;
      },
    }),
    repositories: repositories(attempt),
    encryptor,
    activeCart: new ActivePricingCartSession(
      new PricingCart(),
      () => new PricingCart(),
    ),
    currentCashier: cashier,
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: idFactory(),
    connectivity: { async isOnline() { return true; } },
    bootstrap: voucherReleaseBootstrap(
      true,
      async (source) => {
        releaseCalls += 1;
        return {
          state: "Unknown",
          references: source.references,
          receiptText: null,
          responseCode: "VOUCHER_RELEASE_RESULT_UNRESOLVED",
        };
      },
      () => {
        providerCalls += 1;
      },
    ),
    async drainFulfilment() {},
  });
  await runtime.initializeRecovery();
  const service = runtime.service;
  assert.equal(service.status, "available");
  if (service.status !== "available") return;
  const presenter = service.createPresenter({
    checkoutIntentId: "checkout-1",
    expectedCartRevision: draft.cartRevision,
    total: draft.total,
  });
  assert.equal(await presenter.load("order-1"), true);
  assert.equal(
    presenter.getState().tenders[0]?.reversible,
    true,
  );

  assert.equal(await presenter.removeTender("voucher-1"), false);
  assert.equal(presenter.getState().phase, "unknown");
  assert.deepEqual(presenter.getState().allowedActions, {
    start: false,
    changeProvider: false,
    recover: true,
    cancel: false,
    addCash: false,
    removeTender: false,
  });
  assert.equal(presenter.selectMethod("cash"), false);
  presenter.setAmountText("5.00");
  assert.equal(await presenter.submitSelected(), false);

  assert.equal(releaseCalls, 1);
  assert.equal(providerCalls, 0);
  assert.equal(cashCalls, 0);
  presenter.destroy();
});

test("重启后从持久 Unknown 撤券恢复原 action，恢复前所有新支付入口保持关闭", async () => {
  let releaseCalls = 0;
  let providerCalls = 0;
  let cashCalls = 0;
  const attempt = approvedVoucherAttempt();
  const cart = pricedCart();
  const draft = voucherCheckoutDraft(cart.snapshot().revision);
  const recovery = draftRecovery(cart);
  const persisted = {
    ...voucherReversalRecord(voucherOnlyTruth()),
    state: "Unknown" as const,
    attemptCount: 1,
    lastErrorCode: "VOUCHER_RELEASE_RESULT_UNRESOLVED",
  };
  const durableDatabase = reversalDatabase({
    draft,
    recovery,
    record: persisted,
    recoveryThrowsAfterFirst: true,
    onCash: () => {
      cashCalls += 1;
    },
  });
  const runtime = createProductionPaymentRuntime({
    database: durableDatabase,
    repositories: repositories(attempt),
    encryptor,
    activeCart: new ActivePricingCartSession(
      new PricingCart(),
      () => new PricingCart(),
    ),
    currentCashier: activeCashier(),
    terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: idFactory(),
    connectivity: { async isOnline() { return true; } },
    bootstrap: voucherReleaseBootstrap(
      true,
      async (source) => {
        releaseCalls += 1;
        return {
          state: "Cancelled",
          references: source.references,
          receiptText: null,
          responseCode: "VOUCHER_RELEASED",
        };
      },
      () => {
        providerCalls += 1;
      },
    ),
    async drainFulfilment() {},
  });
  await runtime.initializeRecovery();
  const service = runtime.service;
  assert.equal(service.status, "available");
  if (service.status !== "available") return;

  const presenter = service.createPresenter(null);
  assert.equal(await presenter.initialize(), true);
  assert.equal(presenter.getState().phase, "unknown");
  assert.deepEqual(presenter.getState().allowedActions, {
    start: false,
    changeProvider: false,
    recover: true,
    cancel: false,
    addCash: false,
    removeTender: false,
  });
  assert.equal(
    JSON.stringify(presenter.getState()).includes(persisted.actionId),
    false,
  );
  assert.equal(presenter.selectMethod("cash"), false);
  presenter.setAmountText("5.00");
  assert.equal(await presenter.submitSelected(), false);
  assert.equal(cashCalls, 0);
  assert.equal(providerCalls, 0);
  assert.equal(releaseCalls, 0);

  assert.equal(await presenter.recover(), true);
  assert.equal(releaseCalls, 1);
  assert.equal(providerCalls, 0);
  assert.equal(cashCalls, 0);
  assert.equal(presenter.getState().phase, "partial");
  assert.equal(presenter.getState().remaining.cents, 1_000);
  presenter.destroy();
});

test("Prepared/Submitted/Unknown/Blocked 重启投影与 provider capability 一致且始终脱敏锁单", async (t) => {
  for (const fixture of [
    {
      state: "Prepared" as const,
      attemptCount: 0,
      lastErrorCode: null,
      releaseAvailable: true,
      phase: "recovery-required",
      recover: true,
      errorCode: "TENDER_REVERSAL_RECOVERY_REQUIRED",
    },
    {
      state: "Submitted" as const,
      attemptCount: 1,
      lastErrorCode: null,
      releaseAvailable: true,
      phase: "recovery-required",
      recover: true,
      errorCode: "TENDER_REVERSAL_RECOVERY_REQUIRED",
    },
    {
      state: "Unknown" as const,
      attemptCount: 1,
      lastErrorCode: "VOUCHER_RELEASE_RESULT_UNRESOLVED",
      releaseAvailable: false,
      phase: "unknown",
      recover: false,
      errorCode: "TENDER_REVERSAL_UNAVAILABLE",
    },
    {
      state: "Blocked" as const,
      attemptCount: 1,
      lastErrorCode: "VOUCHER_RELEASE_REJECTED",
      releaseAvailable: true,
      phase: "recovery-required",
      recover: false,
      errorCode: "TENDER_REVERSAL_BLOCKED",
    },
  ]) {
    await t.test(fixture.state, async () => {
      let releaseCalls = 0;
      let providerCalls = 0;
      let cashCalls = 0;
      const cart = pricedCart();
      const draft = voucherCheckoutDraft(cart.snapshot().revision);
      const persisted = {
        ...voucherReversalRecord(voucherOnlyTruth()),
        state: fixture.state,
        attemptCount: fixture.attemptCount,
        lastErrorCode: fixture.lastErrorCode,
      };
      const runtime = createProductionPaymentRuntime({
        database: reversalDatabase({
          draft,
          recovery: draftRecovery(cart),
          record: persisted,
          persisted: true,
          onCash: () => {
            cashCalls += 1;
          },
        }),
        repositories: repositories(approvedVoucherAttempt()),
        encryptor,
        activeCart: new ActivePricingCartSession(
          new PricingCart(),
          () => new PricingCart(),
        ),
        currentCashier: activeCashier(),
        terminal: { storeCode: "S1", deviceCode: "IPAD-1" },
        clock: {
          now: () => new Date("2026-07-28T00:00:00.000Z"),
          nowIso: () => "2026-07-28T00:00:00.000Z",
        },
        createId: idFactory(),
        connectivity: { async isOnline() { return true; } },
        bootstrap: voucherReleaseBootstrap(
          fixture.releaseAvailable,
          async (source) => {
            releaseCalls += 1;
            return {
              state: "Unknown",
              references: source.references,
              receiptText: null,
              responseCode: "TEST_MUST_NOT_RELEASE",
            };
          },
          () => {
            providerCalls += 1;
          },
        ),
        async drainFulfilment() {},
      });
      await runtime.initializeRecovery();
      const service = runtime.service;
      assert.equal(service.status, "available");
      if (service.status !== "available") return;
      const presenter = service.createPresenter(null);

      assert.equal(await presenter.initialize(), true);
      assert.equal(presenter.getState().phase, fixture.phase);
      assert.equal(
        presenter.getState().runtimeErrorCode,
        fixture.errorCode,
      );
      assert.deepEqual(presenter.getState().allowedActions, {
        start: false,
        changeProvider: false,
        recover: fixture.recover,
        cancel: false,
        addCash: false,
        removeTender: false,
      });
      const encoded = JSON.stringify(presenter.getState());
      assert.equal(encoded.includes(persisted.actionId), false);
      assert.equal(encoded.includes("reservation-secret"), false);
      assert.equal(encoded.includes("voucherCode"), false);
      assert.equal(presenter.selectMethod("cash"), false);
      presenter.setAmountText("5.00");
      assert.equal(await presenter.submitSelected(), false);
      if (!fixture.recover) {
        assert.equal(await presenter.recover(), false);
      }
      assert.equal(releaseCalls, 0);
      assert.equal(providerCalls, 0);
      assert.equal(cashCalls, 0);
      presenter.destroy();
    });
  }
});

function bootstrap(
  onBind: () => void,
): PaymentProviderRuntimeBootstrap {
  return {
    providers: {
      get() {
        throw new Error("provider execution is outside this composition test");
      },
      getAvailability(provider: PaymentProvider) {
        return {
          provider,
          available: false,
          blocker:
            provider === "square"
              ? "SQUARE_CONFIGURATION_MISSING"
              : provider === "linkly-cloud"
                ? "LINKLY_CONFIGURATION_MISSING"
                : "VOUCHER_CONFIGURATION_DISABLED",
        };
      },
      listAvailability() {
        return [
          {
            provider: "square",
            available: false,
            blocker: "SQUARE_CONFIGURATION_MISSING",
          },
          {
            provider: "linkly-cloud",
            available: false,
            blocker: "LINKLY_CONFIGURATION_MISSING",
          },
          {
            provider: "voucher",
            available: false,
            blocker: "VOUCHER_CONFIGURATION_DISABLED",
          },
        ];
      },
      listAvailableProviders() {
        return [];
      },
    } as unknown as PaymentProviderRuntimeBootstrap["providers"],
    configurationAvailability:
      {} as PaymentProviderRuntimeBootstrap["configurationAvailability"],
    bindVoucherContextProvider() {
      onBind();
    },
    createLinklyOperator() {
      return null;
    },
  };
}

function voucherReleaseBootstrap(
  available: boolean,
  release: Extract<
    VoucherApprovedPurchaseReleasePort,
    { status: "available" }
  >["release"],
  onProviderGet: () => void = () => undefined,
): PaymentProviderRuntimeBootstrapWithVoucherRelease {
  const base = bootstrap(() => undefined);
  return {
    ...base,
    providers: {
      ...base.providers,
      get(provider: PaymentProvider) {
        onProviderGet();
        return {
          provider,
          async submit(source: PaymentAttempt) {
            return {
              state: "Unknown" as const,
              references: source.references,
              receiptText: null,
              responseCode: "TEST_PROVIDER_CALLED",
            };
          },
          async recover(source: PaymentAttempt) {
            return this.submit(source);
          },
          async cancel(source: PaymentAttempt) {
            return this.submit(source);
          },
          async refund(source: PaymentAttempt) {
            return this.submit(source);
          },
        };
      },
      getAvailability(provider: PaymentProvider) {
        return provider === "linkly-cloud"
          ? {
              provider,
              available: false,
              blocker: "LINKLY_CONFIGURATION_MISSING",
            }
          : { provider, available: true, blocker: null };
      },
      listAvailability() {
        return [
          { provider: "square", available: true, blocker: null },
          {
            provider: "linkly-cloud",
            available: false,
            blocker: "LINKLY_CONFIGURATION_MISSING",
          },
          { provider: "voucher", available: true, blocker: null },
        ];
      },
      listAvailableProviders() {
        return ["square", "voucher"];
      },
    } as unknown as PaymentProviderRuntimeBootstrap["providers"],
    voucherApprovedPurchaseRelease: available
      ? {
          status: "available",
          release,
        }
      : {
          status: "unavailable",
          reason: "VOUCHER_CONFIGURATION_DISABLED",
        },
  };
}

function database(draft: PaymentCheckoutDraft | null = null): PosDatabase {
  const draftStore = {
    async assertPersisted() {},
    async findBlockingRecovery() {
      return null;
    },
    async readDraft() {
      return draft;
    },
  };
  return {
    paymentDraftRecovery: () => draftStore,
    paymentActionBindings: () => ({}),
    voucherPreparationStore: () => ({
      async prepare() {
        return "protected-context";
      },
      async bindToAttempt() {
        return null;
      },
    }),
    settings: () => ({
      async getReceiptPrinterSettings() {
        return {
          printEnabled: false,
          drawerEnabled: false,
          peripheralId: null,
          paper: "80mm",
          locale: "en",
          brandName: "Hot Bargain",
          storeName: "Store",
          address: "",
          phone: "",
          abn: "",
        };
      },
    }),
    paymentOrderCommitter: () => ({}),
    mixedPaymentOrderTruth: () => ({
      async getPaymentTruth() {
        return null;
      },
    }),
    mixedPaymentTenders: () => ({
      async appendCashTenderAtomically() {
        throw new Error("not used");
      },
      async reverseTender() {
        throw new Error("not used");
      },
    }),
    voucherTenderReversals: () => ({
      async findBlocking() {
        return null;
      },
      async prepareOrLoad() {
        throw new Error("not used");
      },
      async markSubmitted() {
        throw new Error("not used");
      },
      async markUnknown() {
        throw new Error("not used");
      },
      async markBlocked() {
        throw new Error("not used");
      },
      async commitReleased() {
        throw new Error("not used");
      },
    }),
    returnCapacityVault: () => ({
      async get() {
        return null;
      },
      async resolveProtectedContext() {
        return null;
      },
    }),
  } as unknown as PosDatabase;
}

function durableCloseDatabase(input: Readonly<{
  draft: PaymentCheckoutDraft;
  recovery: PaymentDraftRecovery;
  onCommitted(): void;
}>): Readonly<{
  database: PosDatabase;
  readonly abandonPreparedCalls: number;
  readonly closeFullyReversedCalls: number;
  readonly readAfterCommit: number;
}> {
  let committed = false;
  let abandonPreparedCalls = 0;
  let closeFullyReversedCalls = 0;
  let readAfterCommit = 0;
  const close = () => {
    committed = true;
    input.onCommitted();
    return { replayed: false };
  };
  const draftStore = {
    async assertPersisted() {},
    async findBlockingRecovery() {
      return input.recovery;
    },
    async readDraft() {
      if (committed) {
        readAfterCommit += 1;
        throw new Error("draft must not be read after durable close");
      }
      return input.draft;
    },
    async abandonPreparedDraft() {
      abandonPreparedCalls += 1;
      return close();
    },
    async closeFullyReversedDraft() {
      closeFullyReversedCalls += 1;
      return close();
    },
  };
  return {
    database: {
      ...database(input.draft),
      paymentDraftRecovery: () => draftStore,
    } as unknown as PosDatabase,
    get abandonPreparedCalls() {
      return abandonPreparedCalls;
    },
    get closeFullyReversedCalls() {
      return closeFullyReversedCalls;
    },
    get readAfterCommit() {
      return readAfterCommit;
    },
  };
}

function repositories(
  initialAttempt: PaymentAttempt | null = null,
): PosRepositoryBundle {
  const attempts = new Map<string, PaymentAttempt>();
  if (initialAttempt) {
    attempts.set(initialAttempt.attemptId, initialAttempt);
  }
  return {
    orders: {
      async nextLocalSequence() {
        return 1;
      },
      async getByGuid() {
        return null;
      },
      async listLocal() {
        return [];
      },
    },
    payments: {
      async insertIfUnblocked(attempt: PaymentAttempt) {
        attempts.set(attempt.attemptId, attempt);
        return null;
      },
      async compareAndUpdate(
        _expected: PaymentAttempt,
        next: PaymentAttempt,
      ) {
        attempts.set(next.attemptId, next);
        return true;
      },
      async get(attemptId: string) {
        return attempts.get(attemptId) ?? null;
      },
      async findBlocking(orderGuid: string) {
        return (
          [...attempts.values()].find(
            (attempt) =>
              attempt.orderGuid === orderGuid &&
              attempt.state !== "Approved" &&
              attempt.state !== "Declined" &&
              attempt.state !== "Cancelled",
          ) ?? null
        );
      },
    } satisfies PaymentAttemptRepositoryPort,
  } as unknown as PosRepositoryBundle;
}

const encryptor: SensitivePayloadEncryptor = {
  async encrypt(value) {
    return new TextEncoder().encode(value);
  },
  async decrypt(value) {
    return new TextDecoder().decode(value);
  },
};

function idFactory(): () => string {
  let value = 0;
  return () => `id-${++value}`;
}

function activeCashier(
  permissionCodes: readonly string[] = ALL_PAYMENT_PERMISSIONS,
): CurrentCashierSession {
  const cashier = new CurrentCashierSession();
  const epoch = cashier.beginAuthentication();
  cashier.activate(
    epoch,
    {
      source: "online",
      session: {
        cashierId: "cashier-1",
        cashierName: "Cashier",
        storeCode: "S1",
        deviceCode: "IPAD-1",
        permissionCodes: [...permissionCodes],
      },
    },
    { storeCode: "S1", deviceCode: "IPAD-1" },
  );
  return cashier;
}

function checkoutDraft(): PaymentCheckoutDraft {
  return {
    checkoutIntentId: "checkout-1",
    orderGuid: "order-1",
    cartRevision: 0,
    state: "Completing",
    total: aud(1_000),
    remaining: aud(400),
    cancellableAfterReversal: false,
    tenders: [
      checkoutTender("cash-1", "cash", 100),
      checkoutTender("card-1", "card", 200),
      checkoutTender("voucher-1", "voucher", 300),
    ],
  };
}

function checkoutTender(
  tenderGuid: string,
  method: OrderTender["method"],
  cents: number,
) {
  return {
    tenderGuid,
    method,
    amount: aud(cents),
    reversible: true,
  };
}

function mixedTruth(): MixedPaymentOrderTruth {
  return {
    orderGuid: "order-1",
    state: "Completing",
    actualAmount: aud(1_000),
    tenders: [
      orderTender("cash-1", "cash", 100),
      orderTender("card-1", "card", 200),
      orderTender("voucher-1", "voucher", 300),
    ],
    reversalLinks: [],
  };
}

function orderTender(
  tenderGuid: string,
  method: OrderTender["method"],
  cents: number,
): OrderTender {
  return {
    tenderGuid,
    method,
    amount: aud(cents),
    reference:
      method === "card" ? "provider-reference-secret" : null,
    reservationToken:
      method === "voucher" ? "reservation-secret" : null,
  };
}

function reversedMutation(
  truth: MixedPaymentOrderTruth,
  actionId: string,
  sourceTenderGuid: string,
) {
  const source = truth.tenders.find(
    (tender) => tender.tenderGuid === sourceTenderGuid,
  );
  assert.ok(source);
  const reversalTenderGuid = `reversal-${actionId}`;
  return {
    state: "reversed" as const,
    replayed: false,
    reversalTenderGuid,
    truth: {
      ...truth,
      tenders: [
        ...truth.tenders,
        {
          ...source,
          tenderGuid: reversalTenderGuid,
          amount: aud(-source.amount.cents),
          reference: null,
          reservationToken: null,
        },
      ],
      reversalLinks: [
        ...truth.reversalLinks,
        {
          actionId,
          sourceTenderGuid,
          reversalTenderGuid,
        },
      ],
    },
  };
}

function aud(cents: number): Money {
  return { currency: "AUD", cents };
}

function approvedVoucherAttempt(): PaymentAttempt {
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
  };
}

function pricedCart(): PricingCart {
  const cart = new PricingCart();
  cart.addItem({
    lineId: "line-1",
    productCode: "P1",
    itemNumber: "1001",
    lookupCode: "930000000001",
    displayName: "Tea",
    unitPrice: aud(1_000),
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  return cart;
}

function voucherCheckoutDraft(
  cartRevision: number,
): PaymentCheckoutDraft {
  return {
    checkoutIntentId: "checkout-1",
    orderGuid: "order-1",
    cartRevision,
    state: "Completing",
    total: aud(1_000),
    remaining: aud(500),
    cancellableAfterReversal: false,
    tenders: [checkoutTender("voucher-1", "voucher", 500)],
  };
}

function voucherOnlyTruth(): MixedPaymentOrderTruth {
  return {
    orderGuid: "order-1",
    state: "Completing",
    actualAmount: aud(1_000),
    tenders: [orderTender("voucher-1", "voucher", 500)],
    reversalLinks: [],
  };
}

function draftRecovery(cart: PricingCart): PaymentDraftRecovery {
  return {
    kind: "DraftPrepared",
    attemptId: null,
    draftId: "checkout-1",
    orderGuid: "order-1",
    originalOrderGuid: null,
    localSequence: 1,
    soldAtIso: "2026-07-28T00:00:00.000Z",
    identity: {
      storeCode: "S1",
      deviceCode: "IPAD-1",
      cashierId: "cashier-1",
      cashierName: "Cashier",
    },
    cart: cart.snapshot(),
    pricingState: cart.stateSnapshot(),
    recallBinding: null,
    boundAction: null,
  };
}

function voucherReversalRecord(
  truth: MixedPaymentOrderTruth,
): VoucherTenderReversalRecord {
  return {
    actionId: "id-2",
    orderGuid: "order-1",
    sourceTenderGuid: "voucher-1",
    sourceAttemptId: "voucher-attempt-1",
    amount: aud(500),
    reason: "SALE",
    state: "Prepared",
    attemptCount: 0,
    lastErrorCode: null,
    reversalTenderGuid: null,
    actor: TEST_AUDIT_ACTOR,
    truth,
  };
}

function reversalDatabase(input: Readonly<{
  draft: PaymentCheckoutDraft;
  recovery: PaymentDraftRecovery;
  record: VoucherTenderReversalRecord;
  persisted?: boolean;
  recoveryThrowsAfterFirst?: boolean;
  onCash(): void;
}>): PosDatabase {
  let current = input.record;
  let created =
    input.persisted ?? input.record.state !== "Prepared";
  let recoveryReads = 0;
  return {
    ...database(input.draft),
    paymentDraftRecovery: () => ({
      async assertPersisted() {},
      async findBlockingRecovery() {
        recoveryReads += 1;
        if (
          input.recoveryThrowsAfterFirst &&
          recoveryReads > 1
        ) {
          throw new Error(
            "ordinary payment recovery is intentionally ambiguous",
          );
        }
        return input.recovery;
      },
      async readDraft() {
        return current.state === "Reversed"
          ? {
              ...input.draft,
              remaining: input.draft.total,
              tenders: [],
            }
          : input.draft;
      },
    }),
    paymentActionBindings: () => ({
      async bindOrGet(binding: unknown) {
        return binding;
      },
    }),
    mixedPaymentOrderTruth: () => ({
      async getPaymentTruth(orderGuid: string) {
        return orderGuid === current.orderGuid
          ? current.truth
          : null;
      },
    }),
    mixedPaymentTenders: () => ({
      async appendCashTenderAtomically() {
        input.onCash();
        throw new Error("cash must remain blocked");
      },
      async reverseTender() {
        throw new Error("cash reversal is not used");
      },
    }),
    voucherTenderReversals: () => ({
      async findBlocking(scope: Readonly<{
        storeCode: string;
        deviceCode: string;
      }>) {
        if (
          scope.storeCode !== "S1" ||
          scope.deviceCode !== "IPAD-1" ||
          !created ||
          current.state === "Reversed"
        ) {
          return null;
        }
        return current;
      },
      async prepareOrLoad(command: Readonly<{
        actionId: string;
        orderGuid: string;
        sourceTenderGuid: string;
        reason: VoucherTenderReversalReason;
      }>) {
        created = true;
        if (
          command.actionId !== current.actionId ||
          command.orderGuid !== current.orderGuid ||
          command.sourceTenderGuid !== current.sourceTenderGuid ||
          command.reason !== current.reason
        ) {
          throw new Error("persisted reversal action changed");
        }
        return current;
      },
      async markSubmitted(record: VoucherTenderReversalRecord) {
        current = {
          ...record,
          state: "Submitted",
          attemptCount: record.attemptCount + 1,
        };
        return current;
      },
      async markUnknown(
        record: VoucherTenderReversalRecord,
        errorCode: string,
      ) {
        current = {
          ...record,
          state: "Unknown",
          lastErrorCode: errorCode,
        };
        return current;
      },
      async markBlocked(
        record: VoucherTenderReversalRecord,
        errorCode: string,
      ) {
        current = {
          ...record,
          state: "Blocked",
          lastErrorCode: errorCode,
        };
        return current;
      },
      async commitReleased(record: VoucherTenderReversalRecord) {
        const reversed = reversedMutation(
          record.truth,
          record.actionId,
          record.sourceTenderGuid,
        );
        current = {
          ...record,
          state: "Reversed",
          lastErrorCode: null,
          reversalTenderGuid: reversed.reversalTenderGuid,
          truth: reversed.truth,
        };
        return current;
      },
    }),
  } as unknown as PosDatabase;
}
