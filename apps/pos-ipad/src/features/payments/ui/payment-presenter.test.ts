import assert from "node:assert/strict";
import test from "node:test";

import { paymentText } from "./payment-copy";
import {
  LINKLY_SAFE_OPERATOR_KEYS,
  PaymentPresenter,
  parseAudInput,
} from "./payment-presenter";

import type {
  Money,
  PaymentProvider,
} from "@/core/contracts";
import type {
  LinklyOperatorPublicResult,
  LinklyOperatorRuntimePort,
  LinklySafeOperatorKey,
} from "@/features/payments/runtime/linkly-operator-runtime";
import type {
  PaymentCheckoutPublicSnapshot,
  PaymentCheckoutRuntimePort,
} from "@/features/payments/runtime/payment-checkout-runtime";
import type {
  PaymentProviderAvailability,
} from "@/features/payments/runtime/payment-provider-registry";

test("冷启动 Unknown 仅保留恢复动作，重复恢复共享同一单飞并完成同一订单", async () => {
  const runtime = new FakePaymentRuntime();
  const pending = deferred<PaymentCheckoutPublicSnapshot>();
  runtime.recovery = snapshot({
    status: "unknown",
    provider: "square",
    attemptId: "attempt-local-1",
    errorCode: "PAYMENT_STATUS_UNKNOWN",
    allowedActions: actions({ recover: true }),
  });
  runtime.recoverImpl = async () => pending.promise;
  const presenter = createPresenter(runtime);

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

  const first = presenter.recover();
  const duplicate = presenter.recover();
  assert.strictEqual(first, duplicate);
  await tick();
  assert.equal(runtime.recoverCalls, 1);

  pending.resolve(
    snapshot({
      status: "completed",
      remaining: aud(0),
      provider: "square",
      attemptId: "attempt-local-1",
      tenders: [
        {
          tenderGuid: "tender-local-1",
          method: "card",
          amount: aud(1_000),
          reversible: true,
        },
      ],
    }),
  );
  assert.equal(await first, true);
  assert.equal(presenter.getState().phase, "success");
  assert.equal(presenter.getState().orderGuid, "order-local-1");
});

test("冷启动礼券撤销 Unknown 使用脱敏持久恢复入口，完成后清除旧恢复标记", async () => {
  const runtime = new FakePaymentRuntime();
  runtime.recovery = snapshot({
    status: "unknown",
    errorCode: "TENDER_REVERSAL_UNKNOWN",
    allowedActions: actions({ recover: true }),
    tenderReversalRecovery: {
      tenderGuid: "tender-voucher-restart-1",
      status: "unknown",
    },
  });
  runtime.retryTenderReversalImpl = async () =>
    snapshot({
      status: "partial",
      total: aud(1_000),
      remaining: aud(600),
      tenders: [
        {
          tenderGuid: "cash-after-reversal",
          method: "cash",
          amount: aud(400),
          reversible: true,
        },
      ],
      allowedActions: actions({
        start: true,
        changeProvider: true,
        addCash: true,
        removeTender: true,
      }),
    });
  const presenter = createPresenter(runtime);

  assert.equal(await presenter.initialize(), true);
  assert.deepEqual(presenter.getState().tenderReversalRecovery, {
    tenderGuid: "tender-voucher-restart-1",
    status: "unknown",
  });
  assert.equal(JSON.stringify(presenter.getState()).includes("actionId"), false);
  assert.equal(JSON.stringify(presenter.getState()).includes("token"), false);

  assert.equal(await presenter.recover(), true);
  assert.deepEqual(runtime.retryTenderReversalCalls, [
    {
      orderGuid: "order-local-1",
      tenderGuid: "tender-voucher-restart-1",
    },
  ]);
  assert.equal(runtime.recoverCalls, 0);
  assert.equal(presenter.getState().tenderReversalRecovery, null);
});

test("Blocked 礼券撤销即使快照错误开放恢复也零调用并保持脱敏", async () => {
  const runtime = new FakePaymentRuntime();
  runtime.recovery = snapshot({
    status: "recovery-required",
    errorCode: "TENDER_REVERSAL_BLOCKED",
    allowedActions: actions({ recover: true }),
    tenderReversalRecovery: {
      tenderGuid: "tender-voucher-blocked-1",
      status: "blocked",
    },
  });
  const presenter = createPresenter(runtime);

  assert.equal(await presenter.initialize(), true);
  assert.equal(presenter.getState().allowedActions.recover, false);
  assert.equal(await presenter.recover(), false);
  assert.equal(runtime.retryTenderReversalCalls.length, 0);
  assert.equal(runtime.recoverCalls, 0);
  assert.equal(JSON.stringify(presenter.getState()).includes("actionId"), false);
  assert.equal(JSON.stringify(presenter.getState()).includes("token"), false);
});

test("Blocked 礼券撤销提供明确的中英文稳定文案", () => {
  assert.equal(
    paymentText("en", "error.TENDER_REVERSAL_RECOVERY_REQUIRED"),
    "A saved voucher reversal must be recovered before taking another tender.",
  );
  assert.equal(
    paymentText("zh", "error.TENDER_REVERSAL_RECOVERY_REQUIRED"),
    "必须先恢复已保存的礼券撤销，才能继续收款。",
  );
  assert.equal(
    paymentText("en", "error.TENDER_REVERSAL_BLOCKED"),
    "Voucher reversal requires supervisor support and cannot be retried.",
  );
  assert.equal(
    paymentText("zh", "error.TENDER_REVERSAL_BLOCKED"),
    "礼券撤销已阻断，必须由主管处理且不能重试。",
  );
});

test("礼券码不进入公开状态，重复提交只调用一次并在异步边界后清除", async () => {
  const runtime = new FakePaymentRuntime();
  const pending = deferred<PaymentCheckoutPublicSnapshot>();
  runtime.startImpl = async () => pending.promise;
  const presenter = createPresenter(runtime);
  await presenter.initialize();
  assert.equal(presenter.selectMethod("voucher"), true);
  presenter.setAmountText("4.50");
  presenter.setVoucherCode(" VOUCHER-SECRET-123 ");

  assert.equal(presenter.getState().voucherCaptured, true);
  assert.equal(
    JSON.stringify(presenter.getState()).includes("VOUCHER-SECRET-123"),
    false,
  );

  const first = presenter.submitSelected();
  const duplicate = presenter.submitSelected();
  assert.strictEqual(first, duplicate);
  await tick();
  assert.equal(runtime.startCalls.length, 1);
  assert.deepEqual(runtime.startCalls[0], {
    checkoutIntentId: "checkout-local-1",
    expectedCartRevision: 7,
    actionId: "action-1",
    provider: "voucher",
    amount: aud(450),
    voucherCode: "VOUCHER-SECRET-123",
  });

  pending.resolve(
    snapshot({
      status: "partial",
      total: aud(1_000),
      remaining: aud(550),
      provider: "voucher",
      attemptId: "attempt-voucher-1",
      tenders: [
        {
          tenderGuid: "tender-voucher-1",
          method: "voucher",
          amount: aud(450),
          reversible: true,
        },
      ],
      allowedActions: actions({
        start: true,
        changeProvider: true,
        addCash: true,
        removeTender: true,
      }),
    }),
  );
  assert.equal(await first, true);
  assert.equal(presenter.getState().voucherCaptured, false);
  assert.equal(presenter.getState().sensitiveInputRevision, 1);
  assert.equal(
    JSON.stringify(presenter.getState()).includes("VOUCHER-SECRET-123"),
    false,
  );
});

test("纯 DraftPrepared 使用 abandonPrepared，已有 attempt 才调用 provider cancel", async () => {
  const runtime = new FakePaymentRuntime();
  runtime.recovery = snapshot({
    status: "draft-prepared",
    allowedActions: actions({
      start: true,
      changeProvider: true,
      cancel: true,
      addCash: true,
    }),
  });
  runtime.abandonImpl = async () =>
    snapshot({
      status: "cancelled",
      allowedActions: actions(),
    });
  const presenter = createPresenter(runtime);
  await presenter.initialize();

  assert.equal(await presenter.cancel(), true);
  assert.equal(runtime.abandonCalls.length, 1);
  assert.equal(runtime.cancelCalls, 0);
  assert.equal(presenter.getState().phase, "cancelled");
});

test("确定离线且未创建 attempt 时保留安全改现金入口", async () => {
  const runtime = new FakePaymentRuntime();
  runtime.startImpl = async () =>
    snapshot({
      status: "recovery-required",
      errorCode: "ONLINE_REQUIRED",
      allowedActions: actions({
        start: true,
        changeProvider: true,
        addCash: true,
      }),
    });
  runtime.addCashImpl = async () =>
    snapshot({
      status: "completed",
      remaining: aud(0),
      tenders: [
        {
          tenderGuid: "cash-offline-safe",
          method: "cash",
          amount: aud(1_000),
          reversible: false,
        },
      ],
    });
  const presenter = createPresenter(runtime);
  await presenter.initialize();
  assert.equal(await presenter.submitSelected(), false);
  assert.equal(presenter.getState().phase, "offline-cash");
  assert.equal(presenter.selectMethod("cash"), true);
  assert.equal(await presenter.submitSelected(), true);
  assert.equal(runtime.addCashCalls, 1);
  assert.equal(presenter.getState().phase, "success");
});

test("首次进入支付页可直接现金结账，超付只提交应收并公开找零", async () => {
  const runtime = new FakePaymentRuntime();
  runtime.startCashImpl = async () =>
    snapshot({
      status: "completed",
      remaining: aud(0),
      tenders: [
        {
          tenderGuid: "cash-first-entry",
          method: "cash",
          amount: aud(1_000),
          reversible: false,
        },
      ],
    });
  const presenter = createPresenter(runtime);
  await presenter.initialize();
  assert.equal(presenter.selectMethod("cash"), true);
  presenter.setAmountText("20.00");

  assert.equal(await presenter.submitSelected(), true);
  assert.deepEqual(runtime.startCashCalls, [
    {
      checkoutIntentId: "checkout-local-1",
      expectedCartRevision: 7,
      actionId: "action-1",
      amount: aud(2_000),
    },
  ]);
  assert.deepEqual(presenter.getState().checkout.cash, {
    tenderedCents: 2_000,
    appliedCents: 1_000,
    changeCents: 1_000,
  });
});

test("Linkly UI 仅发送 attemptId 与枚举安全键，完成后走同一 attempt 恢复", async () => {
  const runtime = new FakePaymentRuntime();
  runtime.recovery = snapshot({
    status: "awaiting-terminal",
    provider: "linkly-cloud",
    attemptId: "attempt-linkly-1",
    errorCode: "PAYMENT_TERMINAL_AWAITED",
    allowedActions: actions({ recover: true, cancel: true }),
  });
  runtime.recoverImpl = async () =>
    snapshot({
      status: "partial",
      total: aud(1_000),
      remaining: aud(400),
      provider: "linkly-cloud",
      attemptId: "attempt-linkly-1",
      tenders: [
        {
          tenderGuid: "tender-card-1",
          method: "card",
          amount: aud(600),
          reversible: true,
        },
      ],
      allowedActions: actions({
        start: true,
        changeProvider: true,
        addCash: true,
        removeTender: true,
      }),
    });
  const linkly = new FakeLinklyOperator();
  const presenter = createPresenter(runtime, linkly);
  await presenter.initialize();

  assert.deepEqual(
    presenter.getState().linkly.allowedKeys,
    LINKLY_SAFE_OPERATOR_KEYS,
  );
  assert.equal(await presenter.sendLinklyKey("yes"), true);
  assert.deepEqual(linkly.sendCalls, [
    { attemptId: "attempt-linkly-1", key: "yes" },
  ]);
  assert.equal(runtime.recoverCalls, 1);
  assert.equal(presenter.getState().phase, "partial");
  assert.equal(
    JSON.stringify(presenter.getState()).includes("session"),
    false,
  );
});

test("销毁后异步结果不回流；未知异常只映射稳定失败码且不泄露 message", async () => {
  const recovery = deferred<PaymentCheckoutPublicSnapshot | null>();
  const runtime = new FakePaymentRuntime();
  runtime.findRecoveryImpl = async () => recovery.promise;
  const presenter = createPresenter(runtime);
  let emissions = 0;
  presenter.subscribe(() => {
    emissions += 1;
  });
  const loading = presenter.initialize();
  await tick();
  const emissionsBeforeDestroy = emissions;
  presenter.destroy();
  recovery.resolve(snapshot({ status: "unknown" }));
  assert.equal(await loading, false);
  assert.equal(emissions, emissionsBeforeDestroy);

  const failingRuntime = new FakePaymentRuntime();
  failingRuntime.startImpl = async () => {
    throw new Error("provider-token-secret");
  };
  const failing = createPresenter(failingRuntime);
  await failing.initialize();
  assert.equal(await failing.submitSelected(), false);
  assert.equal(
    failing.getState().runtimeErrorCode,
    "PAYMENT_CHECKOUT_FAILED",
  );
  assert.equal(
    JSON.stringify(failing.getState()).includes("provider-token-secret"),
    false,
  );
});

test("AUD 输入仅接受正数与最多两位小数", () => {
  assert.deepEqual(parseAudInput("4"), aud(400));
  assert.deepEqual(parseAudInput("4.5"), aud(450));
  assert.deepEqual(parseAudInput("4.50"), aud(450));
  assert.equal(parseAudInput("0"), null);
  assert.equal(parseAudInput("-1"), null);
  assert.equal(parseAudInput("1.234"), null);
  assert.equal(parseAudInput("1,000"), null);
});

class FakePaymentRuntime implements PaymentCheckoutRuntimePort {
  public recovery: PaymentCheckoutPublicSnapshot | null = null;
  public readonly startCalls: unknown[] = [];
  public readonly startCashCalls: unknown[] = [];
  public recoverCalls = 0;
  public readonly retryTenderReversalCalls: unknown[] = [];
  public addCashCalls = 0;
  public cancelCalls = 0;
  public readonly abandonCalls: unknown[] = [];
  public findRecoveryImpl: () => Promise<PaymentCheckoutPublicSnapshot | null> =
    async () => this.recovery;
  public startImpl: (
    input: Parameters<PaymentCheckoutRuntimePort["start"]>[0],
  ) => Promise<PaymentCheckoutPublicSnapshot> = async () =>
    snapshot({ status: "pending" });
  public startCashImpl: (
    input: Parameters<
      NonNullable<PaymentCheckoutRuntimePort["startCash"]>
    >[0],
  ) => Promise<PaymentCheckoutPublicSnapshot> = async () =>
    snapshot({ status: "pending" });
  public recoverImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "pending" });
  public retryTenderReversalImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "partial" });
  public abandonImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "cancelled" });
  public addCashImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "partial" });

  public listProviderAvailability(): readonly PaymentProviderAvailability[] {
    return [
      availability("square"),
      availability("linkly-cloud"),
      availability("voucher"),
    ];
  }

  public async read(): Promise<PaymentCheckoutPublicSnapshot> {
    return snapshot();
  }

  public findRecoveryRequired(): Promise<PaymentCheckoutPublicSnapshot | null> {
    return this.findRecoveryImpl();
  }

  public async resumeCurrent(): Promise<PaymentCheckoutPublicSnapshot | null> {
    return snapshot({ status: "pending" });
  }

  public start(
    input: Parameters<PaymentCheckoutRuntimePort["start"]>[0],
  ): Promise<PaymentCheckoutPublicSnapshot> {
    this.startCalls.push(input);
    return this.startImpl(input);
  }

  public startCash(
    input: Parameters<
      NonNullable<PaymentCheckoutRuntimePort["startCash"]>
    >[0],
  ): Promise<PaymentCheckoutPublicSnapshot> {
    this.startCashCalls.push(input);
    return this.startCashImpl(input);
  }

  public recover(): Promise<PaymentCheckoutPublicSnapshot> {
    this.recoverCalls += 1;
    return this.recoverImpl();
  }

  public retryTenderReversal(input: {
    orderGuid: string;
    tenderGuid: string;
  }): Promise<PaymentCheckoutPublicSnapshot> {
    this.retryTenderReversalCalls.push(input);
    return this.retryTenderReversalImpl();
  }

  public async cancel(): Promise<PaymentCheckoutPublicSnapshot> {
    this.cancelCalls += 1;
    return snapshot({ status: "cancelled" });
  }

  public abandonPrepared(
    input: Parameters<PaymentCheckoutRuntimePort["abandonPrepared"]>[0],
  ): Promise<PaymentCheckoutPublicSnapshot> {
    this.abandonCalls.push(input);
    return this.abandonImpl();
  }

  public addCash(): Promise<PaymentCheckoutPublicSnapshot> {
    this.addCashCalls += 1;
    return this.addCashImpl();
  }

  public async removeTender(): Promise<PaymentCheckoutPublicSnapshot> {
    return snapshot({ status: "partial" });
  }
}

class FakeLinklyOperator implements LinklyOperatorRuntimePort {
  public readonly sendCalls: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }[] = [];

  public async sendKey(input: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }): Promise<LinklyOperatorPublicResult> {
    this.sendCalls.push(input);
    return {
      attemptId: input.attemptId,
      status: "completed",
      errorCode: null,
      allowedKeys: [],
    };
  }

  public async markReceiptPrinted(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    return {
      attemptId,
      status: "completed",
      errorCode: null,
      allowedKeys: [],
    };
  }

  public async acknowledge(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    return {
      attemptId,
      status: "completed",
      errorCode: null,
      allowedKeys: [],
    };
  }
}

function createPresenter(
  runtime: FakePaymentRuntime,
  linklyOperator?: LinklyOperatorRuntimePort,
): PaymentPresenter {
  let action = 0;
  return new PaymentPresenter({
    runtime,
    ...(linklyOperator ? { linklyOperator } : {}),
    entry: {
      checkoutIntentId: "checkout-local-1",
      expectedCartRevision: 7,
      total: aud(1_000),
    },
    createActionId: () => `action-${++action}`,
  });
}

function snapshot(
  override: Partial<PaymentCheckoutPublicSnapshot> & {
    tenderReversalRecovery?: Readonly<{
      tenderGuid: string;
      status: "pending" | "unknown" | "blocked";
    }>;
  } = {},
): PaymentCheckoutPublicSnapshot {
  return {
    orderGuid: "order-local-1",
    total: aud(1_000),
    remaining: aud(1_000),
    tenders: [],
    attemptId: null,
    provider: null,
    status: "draft-prepared",
    errorCode: null,
    allowedActions: actions({
      start: true,
      changeProvider: true,
      cancel: true,
      addCash: true,
    }),
    ...override,
  };
}

function actions(
  override: Partial<PaymentCheckoutPublicSnapshot["allowedActions"]> = {},
): PaymentCheckoutPublicSnapshot["allowedActions"] {
  return {
    start: false,
    changeProvider: false,
    recover: false,
    cancel: false,
    addCash: false,
    removeTender: false,
    ...override,
  };
}

function availability(
  provider: PaymentProvider,
): PaymentProviderAvailability {
  return {
    provider,
    available: true,
    blocker: null,
  };
}

function aud(cents: number): Money {
  return { currency: "AUD", cents };
}

function deferred<T>(): {
  promise: Promise<T>;
  resolve(value: T): void;
} {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

async function tick(): Promise<void> {
  await new Promise<void>((resolve) => setImmediate(resolve));
}
