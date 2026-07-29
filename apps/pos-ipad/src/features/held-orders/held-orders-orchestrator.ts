import {
  HOLD_ORDER_PERMISSION,
  RECALL_LIST_PERMISSION,
  RECALL_RESTORE_PERMISSION,
  assertIso,
  createHoldAudit,
  emptySalePricingState,
  heldOrderActor,
  heldOrderScope,
  isEmptySaleCart,
  isInHeldOrderScope,
  isSaleCart,
  requiredId,
  type ActivePricingCartLeasePort,
  type ActivePricingCartSnapshot,
  type HeldOrderActionResult,
  type HeldOrdersOrchestratorOptions,
} from "./held-orders-domain";

import type {
  HeldOrderSummary,
  RecallActiveBinding,
  RecallClaim,
  TerminalCartFence,
} from "@/core/contracts";

/**
 * 挂单/取单的耐久编排。主管扫码期间不持购物车锁；授权 callback 真正执行时
 * 才取得唯一 active-cart lease 并重新读取，所有 DB await 期间普通编辑 fail-closed。
 */
export class HeldOrdersOrchestrator {
  private mutationInFlight: Promise<HeldOrderActionResult> | null = null;
  private refreshInFlight: Promise<readonly HeldOrderSummary[]> | null = null;

  public constructor(private readonly options: HeldOrdersOrchestratorOptions) {}

  public hold(): Promise<HeldOrderActionResult> {
    return this.runMutation(() => this.holdOnce());
  }

  public recall(holdId: string): Promise<HeldOrderActionResult> {
    return this.runMutation(() => this.recallOnce(holdId));
  }

  public recover(holdId: string): Promise<HeldOrderActionResult> {
    return this.runMutation(() => this.recoverOnce(holdId));
  }

  public release(holdId: string): Promise<HeldOrderActionResult> {
    return this.runMutation(() => this.releaseOnce(holdId));
  }

  public list(): Promise<readonly HeldOrderSummary[]> {
    if (this.refreshInFlight) return this.refreshInFlight;
    const operation = this.listOnce().finally(() => {
      if (this.refreshInFlight === operation) this.refreshInFlight = null;
    });
    this.refreshInFlight = operation;
    return operation;
  }

  private runMutation(
    action: () => Promise<HeldOrderActionResult>,
  ): Promise<HeldOrderActionResult> {
    if (this.mutationInFlight) {
      return Promise.resolve({ ok: false, code: "operation-in-progress" });
    }
    const operation = action().finally(() => {
      if (this.mutationInFlight === operation) this.mutationInFlight = null;
    });
    this.mutationInFlight = operation;
    return operation;
  }

  private async holdOnce(): Promise<HeldOrderActionResult> {
    const authorized = await this.withAuthorization(
      HOLD_ORDER_PERMISSION,
      "hold",
      () => this.withCartLease((lease) => this.holdAuthorized(lease)),
    );
    return authorized ?? { ok: false, code: "authorization-denied" };
  }

  private async holdAuthorized(
    lease: ActivePricingCartLeasePort,
  ): Promise<HeldOrderActionResult> {
    const active = lease.read();
    if (active.terminalRecoveryRequired || active.recallBinding) {
      return { ok: false, code: "terminal-fence-blocked" };
    }
    if (!isSaleCart(active)) return { ok: false, code: "sale-mode-required" };
    if (isEmptySaleCart(active)) return { ok: false, code: "cart-empty" };

    let holdId: string;
    try {
      holdId = requiredId(this.options.createId, "Hold id");
      const heldAtIso = assertIso(this.options.nowIso());
      await this.options.repository.hold({
        holdId,
        scope: heldOrderScope(this.options.identity),
        heldBy: heldOrderActor(this.options.identity),
        payload: { version: 1, pricingState: active.pricingState },
        heldAtIso,
        audit: createHoldAudit({
          identity: this.options.identity,
          holdId,
          occurredAtIso: heldAtIso,
          beforeActualAmountCents: active.cart.actualAmount.cents,
          createId: this.options.createId,
        }),
      });
    } catch {
      return { ok: false, code: "hold-failed" };
    }

    try {
      // Pending + HoldClear 已耐久化后才能清车；失败时 fence 会锁住普通 checkout。
      await lease.replace(emptySalePricingState(active.pricingState), null);
    } catch {
      return {
        ok: false,
        code: "hold-committed-cart-not-cleared",
        holdId,
      };
    }

    try {
      const confirmed = await this.options.repository.confirmHoldCartCleared({
        scope: heldOrderScope(this.options.identity),
        holdId,
      });
      if (!confirmed) {
        return { ok: false, code: "hold-fence-not-cleared", holdId };
      }
    } catch {
      return { ok: false, code: "hold-fence-not-cleared", holdId };
    }
    return { ok: true, code: "held", holdId };
  }

  private async recallOnce(holdId: string): Promise<HeldOrderActionResult> {
    const authorized = await this.withRecallAuthorization("recall", () =>
      this.withCartLease((lease) => this.recallAuthorized(holdId, lease)),
    );
    return authorized ?? { ok: false, code: "authorization-denied" };
  }

  private async recallAuthorized(
    holdId: string,
    lease: ActivePricingCartLeasePort,
  ): Promise<HeldOrderActionResult> {
    const active = lease.read();
    if (active.terminalRecoveryRequired || active.recallBinding) {
      return { ok: false, code: "terminal-fence-blocked" };
    }
    if (!isSaleCart(active)) return { ok: false, code: "sale-mode-required" };
    if (!isEmptySaleCart(active)) return { ok: false, code: "cart-not-empty" };

    const normalizedHoldId = requiredId(() => holdId, "Hold id");
    const recallAttemptId = requiredId(
      this.options.createId,
      "Recall attempt id",
    );
    let claim: RecallClaim | null;
    try {
      claim = await this.options.repository.claimRecall({
        holdId: normalizedHoldId,
        scope: heldOrderScope(this.options.identity),
        recalledBy: heldOrderActor(this.options.identity),
        recallAttemptId,
        recallingAtIso: assertIso(this.options.nowIso()),
      });
    } catch {
      return { ok: false, code: "claim-failed", holdId: normalizedHoldId };
    }
    if (
      !claim ||
      !isInHeldOrderScope(
        claim.hold,
        heldOrderScope(this.options.identity),
      )
    ) {
      return { ok: false, code: "claim-failed", holdId: normalizedHoldId };
    }
    return this.restoreClaim(lease, active, claim, "recalled");
  }

  private async recoverOnce(holdId: string): Promise<HeldOrderActionResult> {
    const authorized = await this.withRecallAuthorization("recover", () =>
      this.withCartLease((lease) => this.recoverAuthorized(holdId, lease)),
    );
    return authorized ?? { ok: false, code: "authorization-denied" };
  }

  private async recoverAuthorized(
    holdId: string,
    lease: ActivePricingCartLeasePort,
  ): Promise<HeldOrderActionResult> {
    const active = lease.read();
    if (!isSaleCart(active)) return { ok: false, code: "sale-mode-required" };
    const normalizedHoldId = requiredId(() => holdId, "Hold id");
    if (
      active.recallBinding?.holdId === normalizedHoldId &&
      active.recallBinding.scope.storeCode === this.options.identity.storeCode &&
      active.recallBinding.scope.deviceCode === this.options.identity.deviceCode
    ) {
      return { ok: true, code: "recovered", holdId: normalizedHoldId };
    }
    if (active.recallBinding) {
      return { ok: false, code: "terminal-fence-blocked" };
    }
    if (!active.terminalRecoveryRequired && !isEmptySaleCart(active)) {
      return { ok: false, code: "cart-not-empty" };
    }

    const claim = await this.loadRecallClaim(normalizedHoldId);
    if (!claim) {
      return { ok: false, code: "claim-failed", holdId: normalizedHoldId };
    }
    return this.restoreClaim(lease, active, claim, "recovered");
  }

  private async restoreClaim(
    lease: ActivePricingCartLeasePort,
    emptyCart: ActivePricingCartSnapshot,
    claim: RecallClaim,
    successCode: "recalled" | "recovered",
  ): Promise<HeldOrderActionResult> {
    const binding = recallBinding(claim);
    try {
      // 新取单先建立隐藏恢复围栏；崩溃恢复则以同一 binding 幂等确认现有围栏。
      await lease.blockForRecallRecovery(binding);
      // PricingCart 在隔离实例中校验成功后，购物车与 active binding 一次性交换。
      await lease.replace(claim.payload.pricingState, binding);
      return {
        ok: true,
        code: successCode,
        holdId: claim.hold.holdId,
      };
    } catch {
      return this.releaseAfterFailedRestore(
        lease,
        emptyCart,
        binding,
      );
    }
  }

  private async releaseAfterFailedRestore(
    lease: ActivePricingCartLeasePort,
    emptyCart: ActivePricingCartSnapshot,
    binding: RecallActiveBinding,
  ): Promise<HeldOrderActionResult> {
    try {
      // replace 采用先验证后 swap；仍显式恢复为空并保留 binding，直到 DB release 成功。
      await lease.replace(
        emptySalePricingState(emptyCart.pricingState),
        binding,
      );
    } catch {
      return {
        ok: false,
        code: "rollback-failed",
        holdId: binding.holdId,
      };
    }
    const released = await this.releaseRecallFence(binding);
    if (!released) {
      return {
        ok: false,
        code: "release-failed",
        holdId: binding.holdId,
      };
    }
    try {
      await lease.setRecallBinding(null);
    } catch {
      return {
        ok: false,
        code: "release-failed",
        holdId: binding.holdId,
      };
    }
    return {
      ok: false,
      code: "restore-failed",
      holdId: binding.holdId,
    };
  }

  private async releaseOnce(holdId: string): Promise<HeldOrderActionResult> {
    const authorized = await this.withRecallAuthorization("release", () =>
      this.withCartLease((lease) => this.releaseAuthorized(holdId, lease)),
    );
    return authorized ?? { ok: false, code: "authorization-denied" };
  }

  private async releaseAuthorized(
    holdId: string,
    lease: ActivePricingCartLeasePort,
  ): Promise<HeldOrderActionResult> {
    const active = lease.read();
    if (!isSaleCart(active)) return { ok: false, code: "sale-mode-required" };
    const normalizedHoldId = requiredId(() => holdId, "Hold id");
    const fence = await this.loadRecallFence(normalizedHoldId);
    if (!fence) {
      return { ok: false, code: "claim-failed", holdId: normalizedHoldId };
    }
    const binding = bindingFromFence(fence);
    if (
      active.recallBinding &&
      !sameRecallBinding(active.recallBinding, binding)
    ) {
      return {
        ok: false,
        code: "terminal-fence-blocked",
        holdId: normalizedHoldId,
      };
    }
    if (
      !active.recallBinding &&
      !active.terminalRecoveryRequired &&
      !isEmptySaleCart(active)
    ) {
      return { ok: false, code: "cart-not-empty", holdId: normalizedHoldId };
    }

    try {
      // hidden pending 只可由从耐久 fence 派生的精确 binding 解除。
      await lease.blockForRecallRecovery(binding);
      await lease.replace(
        emptySalePricingState(active.pricingState),
        binding,
      );
    } catch {
      return { ok: false, code: "rollback-failed", holdId: normalizedHoldId };
    }
    const released = await this.releaseRecallFence(binding);
    if (!released) {
      return { ok: false, code: "release-failed", holdId: normalizedHoldId };
    }
    try {
      await lease.setRecallBinding(null);
    } catch {
      return { ok: false, code: "release-failed", holdId: normalizedHoldId };
    }
    return { ok: true, code: "released", holdId: normalizedHoldId };
  }

  private async releaseRecallFence(
    binding: RecallActiveBinding,
  ): Promise<boolean> {
    try {
      return await this.options.repository.releaseRecallAfterCartCleared({
        binding,
        releasedAtIso: assertIso(this.options.nowIso()),
      });
    } catch {
      return false;
    }
  }

  private async loadRecallClaim(holdId: string): Promise<RecallClaim | null> {
    const fence = await this.loadRecallFence(holdId);
    if (!fence) return null;
    try {
      const claim = await this.options.repository.loadRecallForFence(
        bindingFromFence(fence),
      );
      return claim &&
        isInHeldOrderScope(claim.hold, heldOrderScope(this.options.identity))
        ? claim
        : null;
    } catch {
      return null;
    }
  }

  private async loadRecallFence(
    holdId: string,
  ): Promise<TerminalCartFence | null> {
    try {
      const fence = await this.options.repository.getTerminalFence(
        heldOrderScope(this.options.identity),
      );
      return fence?.kind === "RecallActive" &&
        fence.holdId === holdId &&
        fence.recallAttemptId
        ? fence
        : null;
    } catch {
      return null;
    }
  }

  private async listOnce(): Promise<readonly HeldOrderSummary[]> {
    const rows = await this.withAuthorization(
      RECALL_LIST_PERMISSION,
      "list",
      () => this.listAuthorized(),
    );
    if (!rows) throw new Error("HELD_ORDER_LIST_UNAUTHORIZED");
    return rows;
  }

  private async listAuthorized(): Promise<readonly HeldOrderSummary[]> {
    const scope = heldOrderScope(this.options.identity);
    const [pending, recoverable] = await Promise.all([
      this.options.repository.listPending(scope, 200),
      this.options.repository.listRecoverable(scope),
    ]);
    const unique = new Map<string, HeldOrderSummary>();
    for (const entry of pending) {
      if (isInHeldOrderScope(entry, scope) && entry.status === "Pending") {
        unique.set(entry.holdId, entry);
      }
    }
    for (const claim of recoverable) {
      if (
        isInHeldOrderScope(claim.hold, scope) &&
        claim.hold.status === "Recalling"
      ) {
        unique.set(claim.hold.holdId, claim.hold);
      }
    }
    return [...unique.values()].sort(
      (left, right) => right.localSequence - left.localSequence,
    );
  }

  private async withRecallAuthorization<T>(
    action: "recall" | "recover" | "release",
    operation: () => Promise<T>,
  ): Promise<T | null> {
    return this.withAuthorization(RECALL_LIST_PERMISSION, action, () =>
      this.withAuthorization(RECALL_RESTORE_PERMISSION, action, operation),
    );
  }

  private async withAuthorization<T>(
    permissionCode: string,
    action: "hold" | "list" | "recall" | "recover" | "release",
    operation: () => Promise<T>,
  ): Promise<T | null> {
    const result = await this.options.authorization.authorizeAndRun(
      { permissionCode, action },
      operation,
    );
    return result.authorized ? result.value : null;
  }

  private async withCartLease(
    operation: (
      lease: ActivePricingCartLeasePort,
    ) => Promise<HeldOrderActionResult>,
  ): Promise<HeldOrderActionResult> {
    try {
      return await this.options.activeCart.runExclusive(operation);
    } catch (error: unknown) {
      if (
        error instanceof Error &&
        "code" in error &&
        error.code === "ACTIVE_PRICING_CART_BUSY"
      ) {
        return { ok: false, code: "operation-in-progress" };
      }
      throw error;
    }
  }
}

function recallBinding(claim: RecallClaim): RecallActiveBinding {
  return {
    kind: "recalled",
    scope: claim.hold.scope,
    holdId: claim.hold.holdId,
    recallAttemptId: claim.recallAttemptId,
  };
}

function bindingFromFence(fence: TerminalCartFence): RecallActiveBinding {
  if (fence.kind !== "RecallActive" || !fence.recallAttemptId) {
    throw new Error("RecallActive terminal fence is required.");
  }
  return {
    kind: "recalled",
    scope: fence.scope,
    holdId: fence.holdId,
    recallAttemptId: fence.recallAttemptId,
  };
}

function sameRecallBinding(
  left: RecallActiveBinding,
  right: RecallActiveBinding,
): boolean {
  return (
    left.holdId === right.holdId &&
    left.recallAttemptId === right.recallAttemptId &&
    left.scope.storeCode === right.scope.storeCode &&
    left.scope.deviceCode === right.scope.deviceCode
  );
}
