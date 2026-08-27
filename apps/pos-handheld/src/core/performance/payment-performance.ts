import {
  POS_CLIENT_METRICS,
  clientMetrics,
  type ClientMetricDraft,
} from "./client-metrics";

import type {
  PaymentScreenPresenter,
} from "@/features/payments/ui/payment-presenter";

const PAYMENT_INSTRUMENTED = Symbol("payment-performance-instrumented");

type InstrumentedPaymentPresenter = PaymentScreenPresenter & {
  [PAYMENT_INSTRUMENTED]?: true;
};

/**
 * submitSelected 返回前，provider 结果已完成耐久化并由 presenter 发布为 UI state。
 * 包装器只观察公开 facade，不读取订单号、attempt、金额或 provider payload。
 */
export function instrumentPaymentPresenter<T extends PaymentScreenPresenter>(
  presenter: T,
  dependencies: Readonly<{
    now(): number;
    record(draft: ClientMetricDraft): void;
  }> = {
    now: monotonicNow,
    record: (draft) => clientMetrics.record(draft),
  },
): T {
  const instrumented = presenter as T & InstrumentedPaymentPresenter;
  if (instrumented[PAYMENT_INSTRUMENTED]) return presenter;

  const submitSelected = presenter.submitSelected.bind(presenter);
  Object.defineProperty(instrumented, PAYMENT_INSTRUMENTED, {
    configurable: false,
    enumerable: false,
    value: true,
  });
  Object.defineProperty(presenter, "submitSelected", {
    configurable: true,
    enumerable: false,
    writable: true,
    value: (): Promise<boolean> => {
      const paymentType = presenter.getState().selectedMethod;
      // provider 可能同步进入 busy；单调时钟必须在调用原 presenter 前读取。
      const startedAt = safeNow(dependencies.now);
      const pending = submitSelected();
      // 前置校验返回 false 时不会进入 runExclusive，也没有 provider 调用。
      if (
        paymentType === null ||
        startedAt === null ||
        presenter.getState().busy !== true
      ) {
        return pending;
      }
      return pending.then(
        (result) => {
          recordPaymentOutcome(
            dependencies,
            startedAt,
            paymentType,
            completedPaymentOutcome(presenter.getState()),
          );
          return result;
        },
        (error: unknown) => {
          recordPaymentOutcome(
            dependencies,
            startedAt,
            paymentType,
            "failure",
          );
          throw error;
        },
      );
    },
  });
  return presenter;
}

function recordPaymentOutcome(
  dependencies: Readonly<{
    now(): number;
    record(draft: ClientMetricDraft): void;
  }>,
  startedAt: number,
  paymentType: NonNullable<
    ReturnType<PaymentScreenPresenter["getState"]>["selectedMethod"]
  >,
  outcome: PaymentPerformanceOutcome,
): void {
  const finishedAt = safeNow(dependencies.now);
  if (finishedAt === null) return;
  safeRecord(dependencies.record, {
    metric: POS_CLIENT_METRICS.paymentResponse,
    valueMs: Math.max(0, finishedAt - startedAt),
    dimensions: { paymentType, outcome },
  });
}

type PaymentPerformanceOutcome = "success" | "rejected" | "timeout" | "failure";

function completedPaymentOutcome(
  state: ReturnType<PaymentScreenPresenter["getState"]>,
): PaymentPerformanceOutcome {
  // 仅使用 presenter 完成后已公开的状态；不读取订单、attempt 或 provider 明细。
  if (state.phase === "success" || state.phase === "partial") {
    return "success";
  }
  if (state.phase === "declined") return "rejected";
  if (state.phase === "unknown") return "timeout";
  // recovery-required 还包含本地一致性问题，只有稳定的 Linkly 未知契约才算 timeout。
  if (
    state.phase === "recovery-required" &&
    (state.runtimeErrorCode === "PAYMENT_STATUS_UNKNOWN" ||
      state.runtimeErrorCode === "LINKLY_UNKNOWN_REQUIRES_RECOVERY")
  ) {
    return "timeout";
  }
  return "failure";
}

function safeNow(now: () => number): number | null {
  try {
    return now();
  } catch {
    return null;
  }
}

function safeRecord(
  record: (draft: ClientMetricDraft) => void,
  draft: ClientMetricDraft,
): void {
  try {
    record(draft);
  } catch {
    // 性能指标失败不得改变支付 promise 的成功/失败语义。
  }
}

function monotonicNow(): number {
  return typeof performance === "undefined"
    ? Date.now()
    : performance.now();
}
