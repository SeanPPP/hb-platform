import assert from "node:assert/strict";
import test from "node:test";

import {
  POS_CLIENT_METRICS,
  createBusinessStartupTimer,
  type ClientMetricDraft,
} from "./client-metrics";
import { instrumentPaymentPresenter } from "./payment-performance";

import type {
  PaymentPresenterState,
  PaymentScreenPresenter,
} from "@/features/payments/ui/payment-presenter";

const STARTUP_MILESTONES = [
  "markRuntimeReady",
  "markSalesFirstFrameCommitted",
  "markSalesInteractive",
] as const;

test("业务冷启动缺任一里程碑都不得完成", () => {
  for (const omitted of STARTUP_MILESTONES) {
    const records: ClientMetricDraft[] = [];
    const timer = createBusinessStartupTimer({
      now: () => 470,
      record: (draft) => records.push(draft),
    });

    for (const milestone of STARTUP_MILESTONES) {
      if (milestone !== omitted) timer[milestone]();
    }

    assert.deepEqual(records, [], `缺少 ${omitted} 时不应完成`);
  }
});

test("业务冷启动三里程碑顺序无关且成功只记录一次", () => {
  const orders = [
    STARTUP_MILESTONES,
    [
      "markRuntimeReady",
      "markSalesInteractive",
      "markSalesFirstFrameCommitted",
    ] as const,
    [
      "markSalesFirstFrameCommitted",
      "markRuntimeReady",
      "markSalesInteractive",
    ] as const,
    [
      "markSalesFirstFrameCommitted",
      "markSalesInteractive",
      "markRuntimeReady",
    ] as const,
    [
      "markSalesInteractive",
      "markRuntimeReady",
      "markSalesFirstFrameCommitted",
    ] as const,
    [...STARTUP_MILESTONES].reverse(),
  ];

  for (const order of orders) {
    let now = 20;
    const records: ClientMetricDraft[] = [];
    const timer = createBusinessStartupTimer({
      now: () => now,
      record: (draft) => records.push(draft),
    });

    now = 470;
    for (const milestone of order) timer[milestone]();
    for (const milestone of STARTUP_MILESTONES) timer[milestone]();
    timer.fail();

    assert.deepEqual(records, [
      {
        metric: POS_CLIENT_METRICS.coldStart,
        valueMs: 450,
        dimensions: { outcome: "success" },
      },
    ]);
  }
});

test("业务冷启动可使用最早 JS 入口原点而不是计时器依赖加载完成时间", () => {
  const records: ClientMetricDraft[] = [];
  const timer = createBusinessStartupTimer({
    startedAt: 5,
    now: () => 500,
    record: (draft) => records.push(draft),
  });

  timer.markRuntimeReady();
  timer.markSalesFirstFrameCommitted();
  timer.markSalesInteractive();

  assert.equal(records[0]?.valueMs, 495);
});

test("业务冷启动任一失败立即按 failure 严格记录一次", () => {
  let now = 20;
  const records: ClientMetricDraft[] = [];
  const timer = createBusinessStartupTimer({
    now: () => now,
    record: (draft) => records.push(draft),
  });

  timer.markSalesFirstFrameCommitted();
  now = 170;
  timer.fail();
  timer.markRuntimeReady();
  timer.markSalesInteractive();
  timer.fail();

  assert.deepEqual(records, [
    {
      metric: POS_CLIENT_METRICS.coldStart,
      valueMs: 150,
      dimensions: { outcome: "failure" },
    },
  ]);
});

test("支付计时覆盖 provider 调用到持久化结果已发布 UI state", async () => {
  let now = 100;
  let clockReads = 0;
  const callOrder: string[] = [];
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  let state = paymentState("ready", false, "square");
  const records: {
    draft: ClientMetricDraft;
    phaseAtRecord: string;
  }[] = [];
  const presenter = {
    getState: () => state,
    submitSelected: async () => {
      callOrder.push("provider-start");
      state = paymentState("submitting", true, "square");
      await gate;
      state = paymentState("success", false, "square");
      return true;
    },
  } as unknown as PaymentScreenPresenter;
  instrumentPaymentPresenter(presenter, {
    now: () => {
      callOrder.push(clockReads++ === 0 ? "clock-start" : "clock-end");
      return now;
    },
    record: (draft) => {
      records.push({ draft, phaseAtRecord: state.phase });
    },
  });

  const pending = presenter.submitSelected();
  assert.deepEqual(callOrder.slice(0, 2), ["clock-start", "provider-start"]);
  now = 380;
  release();
  assert.equal(await pending, true);
  assert.deepEqual(records, [
    {
      draft: {
        metric: POS_CLIENT_METRICS.paymentResponse,
        valueMs: 280,
        dimensions: {
          paymentType: "square",
          outcome: "success",
        },
      },
      phaseAtRecord: "success",
    },
  ]);
});

test("支付计时按公开完成状态区分拒绝、provider 未知超时与普通失败", async () => {
  const cases: readonly {
    name: string;
    state: PaymentPresenterState;
    paymentType: "square" | "linkly-cloud";
    outcome: "rejected" | "timeout" | "failure";
  }[] = [
    {
      name: "明确 declined",
      state: paymentState("declined", false, "square"),
      paymentType: "square",
      outcome: "rejected",
    },
    {
      name: "provider 未知超时",
      state: {
        ...paymentState("unknown", false, "square"),
        runtimeErrorCode: "PAYMENT_STATUS_UNKNOWN",
      },
      paymentType: "square",
      outcome: "timeout",
    },
    {
      name: "Linkly 未知超时",
      state: {
        ...paymentState("recovery-required", false, "linkly-cloud"),
        runtimeErrorCode: "LINKLY_UNKNOWN_REQUIRES_RECOVERY",
      },
      paymentType: "linkly-cloud",
      outcome: "timeout",
    },
    {
      name: "普通本地失败",
      state: {
        ...paymentState("ready", false, "square"),
        runtimeErrorCode: "PAYMENT_START_FAILED",
      },
      paymentType: "square",
      outcome: "failure",
    },
  ];

  for (const expected of cases) {
    const records: ClientMetricDraft[] = [];
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    let state = paymentState("ready", false, expected.paymentType);
    const presenter = {
      getState: () => state,
      submitSelected: async () => {
        state = paymentState("submitting", true, expected.paymentType);
        await gate;
        state = expected.state;
        return false;
      },
    } as unknown as PaymentScreenPresenter;
    instrumentPaymentPresenter(presenter, {
      now: () => 100,
      record: (draft) => records.push(draft),
    });

    const pending = presenter.submitSelected();
    release();
    assert.equal(await pending, false, expected.name);
    assert.deepEqual(records, [
      {
        metric: POS_CLIENT_METRICS.paymentResponse,
        valueMs: 0,
        dimensions: {
          paymentType: expected.paymentType,
          outcome: expected.outcome,
        },
      },
    ], expected.name);
  }
});

test("支付 promise 异常仍记录普通 failure 并保持异常语义", async () => {
  const records: ClientMetricDraft[] = [];
  let state = paymentState("ready", false, "square");
  const presenter = {
    getState: () => state,
    submitSelected: async () => {
      state = paymentState("submitting", true, "square");
      throw new Error("local runtime failure");
    },
  } as unknown as PaymentScreenPresenter;
  instrumentPaymentPresenter(presenter, {
    now: () => 100,
    record: (draft) => records.push(draft),
  });

  await assert.rejects(presenter.submitSelected(), /local runtime failure/);
  assert.deepEqual(records, [
    {
      metric: POS_CLIENT_METRICS.paymentResponse,
      valueMs: 0,
      dimensions: { paymentType: "square", outcome: "failure" },
    },
  ]);
});

test("支付前置校验未进入 provider 时不产生伪耗时", async () => {
  const records: ClientMetricDraft[] = [];
  const presenter = {
    getState: () => paymentState("ready", false, "square"),
    submitSelected: async () => false,
  } as unknown as PaymentScreenPresenter;
  instrumentPaymentPresenter(presenter, {
    now: () => 100,
    record: (draft) => records.push(draft),
  });

  assert.equal(await presenter.submitSelected(), false);
  assert.deepEqual(records, []);
});

function paymentState(
  phase: PaymentPresenterState["phase"],
  busy: boolean,
  selectedMethod: PaymentPresenterState["selectedMethod"],
): PaymentPresenterState {
  return {
    phase,
    busy,
    initialized: true,
    providers: [],
    cashAvailable: false,
    selectedMethod,
    amountText: "10.00",
    voucherCaptured: false,
    sensitiveInputRevision: 0,
    fieldIssue: null,
    runtimeErrorCode: null,
    orderGuid: null,
    total: { currency: "AUD", cents: 1_000 },
    remaining: { currency: "AUD", cents: 1_000 },
    tenders: [],
    attemptId: null,
    attemptCreatedAtIso: null,
    provider: null,
    runtimeStatus: null,
    allowedActions: {
      start: true,
      changeProvider: true,
      recover: false,
      cancel: false,
      addCash: false,
      removeTender: false,
    },
    tenderReversalRecovery: null,
    checkout: {
      flow: "regular",
      lines: [],
      installmentCustomer: null,
      cash: { tenderedCents: 0, appliedCents: 0, changeCents: 0 },
      canConfirm: false,
      fullInstallmentConfirmationRequired: false,
    },
    linkly: { status: null, errorCode: null, allowedKeys: [] },
  };
}
