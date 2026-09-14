import type { PaymentAcknowledgementRuntimePort } from "@hb/pos-payments-core/features/payments/payment-acknowledgement-service";

import type { PaymentCheckoutPublicSnapshot, PaymentCheckoutRuntimePort } from "./payment-checkout-runtime";

import type { PaymentAttempt } from "@/core/contracts";

type Options = Readonly<{
  runtime: PaymentCheckoutRuntimePort;
  acknowledgements: PaymentAcknowledgementRuntimePort;
  findPendingAttempt(): Promise<PaymentAttempt | null>;
  getAttempt(attemptId: string): Promise<PaymentAttempt | null>;
  canAcknowledge(attempt: PaymentAttempt): Promise<boolean>;
  readFinalSnapshot(attempt: PaymentAttempt): Promise<PaymentCheckoutPublicSnapshot>;
  assertView(): void | Promise<void>;
  assertAcknowledge(): void | Promise<void>;
  assertFinancialAvailable?(): void | Promise<void>;
}>;

/**
 * 终态确认与交易恢复分开：队列来自已持久的原 attempt，ACK-only 路径永远不进入
 * mixed coordinator、provider.recover 或购物车 lease。确认失败保留原金融状态。
 */
export function withPersistedLinklyAcknowledgementRecovery(options: Options): PaymentCheckoutRuntimePort {
  const eligible = async (attempt: PaymentAttempt | null): Promise<boolean> => {
    return attempt !== null && attempt.provider === "linkly-cloud" && !attempt.providerAcknowledgedAtIso &&
      ["Approved", "Declined", "Cancelled"].includes(attempt.state) &&
      (attempt.state === "Approved" || Boolean(attempt.references.sessionId?.trim())) &&
      (attempt.state !== "Approved" || await options.canAcknowledge(attempt));
  };
  const pending = async (): Promise<PaymentAttempt | null> => {
    await options.assertView();
    const attempt = await options.findPendingAttempt();
    await options.assertView();
    return await eligible(attempt) ? attempt : null;
  };
  const project = (base: PaymentCheckoutPublicSnapshot, attempt: PaymentAttempt): PaymentCheckoutPublicSnapshot => ({
    ...base, attemptId: attempt.attemptId, attemptCreatedAtIso: attempt.createdAtIso, provider: "linkly-cloud",
    status: "recovery-required", errorCode: "LINKLY_ACKNOWLEDGEMENT_PENDING",
    allowedActions: { start: false, changeProvider: false, recover: true, cancel: false, addCash: false, removeTender: false },
  });
  const pendingSnapshot = async (attempt: PaymentAttempt) => project(await options.readFinalSnapshot(attempt), attempt);
  const acknowledge = async (attempt: PaymentAttempt, base: PaymentCheckoutPublicSnapshot): Promise<PaymentCheckoutPublicSnapshot> => {
    try {
      await options.assertAcknowledge();
      const result = await options.acknowledgements.acknowledge(attempt.attemptId);
      if (!result.acknowledged || result.pending) return project(base, attempt);
      return base;
    } catch {
      // 本地落账已完成，权限变化、网络错误与确认标记错误只保留 ACK 待办。
      return project(base, attempt);
    }
  };
  const after = async (base: PaymentCheckoutPublicSnapshot): Promise<PaymentCheckoutPublicSnapshot> => {
    // 安全取消会关闭草稿并清除公开 attempt 指针，仍需从耐久队列确认原取消交易。
    if (base.status === "cancelled" && base.attemptId === null) {
      try {
        const cancelled = await pending();
        return cancelled?.orderGuid === base.orderGuid ? acknowledge(cancelled, base) : base;
      } catch {
        return { ...base, status: "recovery-required", errorCode: "LINKLY_ACKNOWLEDGEMENT_PENDING",
          allowedActions: { start: false, changeProvider: false, recover: true, cancel: false, addCash: false, removeTender: false } };
      }
    }
    if (base.provider !== "linkly-cloud" || !base.attemptId) return base;
    if (!["completed", "partial", "declined", "cancelled"].includes(base.status)) return base;
    try {
      const attempt = await options.getAttempt(base.attemptId);
      if (!attempt || !(await eligible(attempt))) return base;
      return acknowledge(attempt, base);
    } catch {
      return {
        ...base, status: "recovery-required", errorCode: "LINKLY_ACKNOWLEDGEMENT_PENDING",
        allowedActions: { start: false, changeProvider: false, recover: true, cancel: false, addCash: false, removeTender: false },
      };
    }
  };
  const retry = async (attempt: PaymentAttempt): Promise<PaymentCheckoutPublicSnapshot> => {
    const base = await options.readFinalSnapshot(attempt);
    const resolved = await acknowledge(attempt, base);
    if (resolved.errorCode === "LINKLY_ACKNOWLEDGEMENT_PENDING") return resolved;
    const next = await pending();
    if (next) return pendingSnapshot(next);
    // 关闭的失败/取消订单不再带 attempt 指针，页面可安全退出；不会重新打开旧购物车。
    return attempt.state === "Declined" || attempt.state === "Cancelled"
      ? { ...resolved, attemptId: null, attemptCreatedAtIso: null }
      : resolved;
  };
  const mutate = async (operation: () => Promise<PaymentCheckoutPublicSnapshot>, financial = true) => {
    const blocked = await pending();
    if (blocked) return pendingSnapshot(blocked);
    if (financial) await options.assertFinancialAvailable?.();
    return after(await operation());
  };
  const runtime = options.runtime;
  return {
    listProviderAvailability: () => runtime.listProviderAvailability(),
    canTakeCash: () => runtime.canTakeCash?.() === true,
    async read(orderGuid) { const blocked = await pending(); return blocked ? pendingSnapshot(blocked) : runtime.read(orderGuid); },
    async findRecoveryRequired() { const blocked = await pending(); return blocked ? pendingSnapshot(blocked) : runtime.findRecoveryRequired(); },
    async resumeCurrent(input) {
      const blocked = await pending();
      if (blocked) return retry(blocked);
      await options.assertFinancialAvailable?.();
      const result = await runtime.resumeCurrent(input);
      return result ? after(result) : null;
    },
    start: (input) => mutate(() => runtime.start(input)),
    ...(runtime.startCash ? { startCash: (input: Parameters<NonNullable<PaymentCheckoutRuntimePort["startCash"]>>[0]) => mutate(() => runtime.startCash!(input)) } : {}),
    async recover(input) {
      // 人工 ACK 已写 marker 后先读原交易，避免刷新旧页面触发 legacy 后端发现。
      await options.assertView();
      const known = await options.getAttempt(input.attemptId);
      await options.assertView();
      const final = known && known.orderGuid === input.orderGuid && known.provider === "linkly-cloud" &&
        ["Approved", "Declined", "Cancelled"].includes(known.state) &&
        (known.state === "Approved" || Boolean(known.references.sessionId?.trim())) &&
        (known.state !== "Approved" || await options.canAcknowledge(known));
      if (known && final && known.providerAcknowledgedAtIso) {
        const base = await options.readFinalSnapshot(known);
        return known.state === "Declined" || known.state === "Cancelled"
          ? { ...base, attemptId: null, attemptCreatedAtIso: null } : base;
      }
      const blocked = await pending();
      if (blocked) return input.attemptId === blocked.attemptId && input.orderGuid === blocked.orderGuid ? retry(blocked) : pendingSnapshot(blocked);
      if (known && final) return retry(known);
      await options.assertFinancialAvailable?.();
      return after(await runtime.recover(input));
    },
    cancel: (input) => mutate(() => runtime.cancel(input), false),
    abandonPrepared: (input) => mutate(() => runtime.abandonPrepared(input), false),
    addCash: (input) => mutate(() => runtime.addCash(input)),
    removeTender: (input) => mutate(() => runtime.removeTender(input)),
    ...(runtime.retryTenderReversal ? { retryTenderReversal: (input: Parameters<NonNullable<PaymentCheckoutRuntimePort["retryTenderReversal"]>>[0]) => mutate(() => runtime.retryTenderReversal!(input)) } : {}),
  };
}
