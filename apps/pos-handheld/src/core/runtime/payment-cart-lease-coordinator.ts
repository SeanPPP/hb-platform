import type {
  PaymentCartLease,
  PaymentCartLeasePort,
} from "../../features/payments/runtime/payment-checkout-runtime";
import {
  ACTIVE_PRICING_CART_BUSY,
  ACTIVE_PRICING_CART_TERMINAL_RECOVERY_REQUIRED,
  type ActivePricingCartLease,
  ActivePricingCartSession,
} from "../../features/sales/runtime";
import type {
  CartSnapshot,
  PricingCartStateSnapshot,
} from "../contracts";

export type PaymentCartRecoveryMaterial = Readonly<{
  checkoutIntentId: string;
  cart: CartSnapshot;
  pricingState: PricingCartStateSnapshot;
}>;

export interface PaymentCartRecoveryMaterialPort {
  /**
   * 只返回可信 SQLCipher 恢复材料。没有阻塞支付时返回 null；多个候选必须在
   * persistence 层失败关闭，不能由组合根猜选。
   */
  findBlockingCart(): Promise<PaymentCartRecoveryMaterial | null>;
}

type HeldPaymentCartLease = {
  readonly publicLease: PaymentCartLease;
  readonly sessionLease: ActivePricingCartLease;
  release(): void;
  operation: Promise<void> | null;
};

/**
 * 把跨多个终端请求的支付生命周期映射到 ActivePricingCartSession 的单个
 * exclusive callback。只有订单耐久完成或明确安全取消，callback 才会结束。
 */
export class ActivePricingCartPaymentLeaseCoordinator
implements PaymentCartLeasePort {
  private held: HeldPaymentCartLease | null = null;
  private acquireInFlight: Promise<PaymentCartLease> | null = null;
  private initializeInFlight: Promise<PaymentCartLease | null> | null = null;
  private initialized = false;

  public constructor(
    private readonly activeCart: ActivePricingCartSession,
    private readonly recovery: PaymentCartRecoveryMaterialPort,
    private readonly createLeaseId: () => string,
  ) {}

  /**
   * 必须在公开 sales/payment facade 前调用。崩溃遗留草稿先恢复完整定价状态，
   * 随即取得长期 exclusive lease，普通销售页面没有短暂可写窗口。
   */
  public initializeRecovery(): Promise<PaymentCartLease | null> {
    if (this.initialized) {
      return Promise.resolve(this.held?.publicLease ?? null);
    }
    if (this.initializeInFlight) return this.initializeInFlight;

    const operation = this.initializeRecoveryOnce().finally(() => {
      if (this.initializeInFlight === operation) {
        this.initializeInFlight = null;
      }
    });
    this.initializeInFlight = operation;
    return operation;
  }

  public async acquireExact(input: {
    checkoutIntentId: string;
    expectedRevision: number;
  }): Promise<PaymentCartLease> {
    const expected = normalizeAcquisition(input);
    if (this.held) {
      return Promise.resolve(assertHeldMatches(this.held, expected));
    }
    if (this.acquireInFlight) {
      return this.acquireInFlight.then((lease) =>
        assertPublicLeaseMatches(lease, expected),
      );
    }

    const operation = this.acquireNew(expected).finally(() => {
      if (this.acquireInFlight === operation) {
        this.acquireInFlight = null;
      }
    });
    this.acquireInFlight = operation;
    return operation;
  }

  public async readExact(lease: PaymentCartLease): Promise<PaymentCartLease> {
    const held = this.requireHeld(lease);
    const current = held.sessionLease.read();
    if (
      current.cart !== held.publicLease.cart ||
      current.pricingState !== held.publicLease.pricingState ||
      current.cart.revision !== held.publicLease.revision
    ) {
      throw paymentLeaseError(
        "PAYMENT_CART_LEASE_CONFLICT",
        "Payment cart changed while its exclusive lease was active.",
      );
    }
    return held.publicLease;
  }

  public async clearAfterCompleted(
    lease: PaymentCartLease,
    orderGuid: string,
  ): Promise<void> {
    const held = this.requireHeld(lease);
    const normalizedOrderGuid = requiredText(orderGuid, "order guid");
    // clear 失败时保持 lease，不允许 UI 把未确认完成的订单当作安全退出。
    held.sessionLease.clearAfterCommittedOrder(normalizedOrderGuid);
    await this.releaseHeld(held);
  }

  public async releaseAfterSafeCancel(
    lease: PaymentCartLease,
    orderGuid: string,
  ): Promise<void> {
    const held = this.requireHeld(lease);
    requiredText(orderGuid, "order guid");
    // DB adapter 已确认该草稿可关闭并耐久完成 CAS；这里仅释放内存写锁。
    await this.releaseHeld(held);
  }

  private async initializeRecoveryOnce(): Promise<PaymentCartLease | null> {
    const material = await this.recovery.findBlockingCart();
    if (!material) {
      this.initialized = true;
      return null;
    }

    const normalized = normalizeRecoveryMaterial(material);
    const current = this.activeCart.read();
    if (current.terminalRecoveryRequired) {
      throw paymentLeaseError(
        ACTIVE_PRICING_CART_TERMINAL_RECOVERY_REQUIRED,
        "Held-order recovery and payment recovery cannot own the cart together.",
      );
    }
    if (current.cart.lines.length > 0) {
      throw paymentLeaseError(
        ACTIVE_PRICING_CART_BUSY,
        "Payment recovery cannot replace a non-empty active cart.",
      );
    }

    const restored = this.activeCart.replace(
      normalized.pricingState,
      null,
    );
    assertCartValueMatches(restored.cart, normalized.cart);
    const lease = await this.acquireExact({
      checkoutIntentId: normalized.checkoutIntentId,
      expectedRevision: normalized.cart.revision,
    });
    this.initialized = true;
    return lease;
  }

  private acquireNew(input: {
    checkoutIntentId: string;
    expectedRevision: number;
  }): Promise<PaymentCartLease> {
    let acquiredResolve!: (lease: PaymentCartLease) => void;
    let acquiredReject!: (error: unknown) => void;
    let settled = false;
    const acquired = new Promise<PaymentCartLease>((resolve, reject) => {
      acquiredResolve = resolve;
      acquiredReject = reject;
    });
    let releaseResolve!: () => void;
    const releaseGate = new Promise<void>((resolve) => {
      releaseResolve = resolve;
    });

    const operation = this.activeCart.runExclusive(async (sessionLease) => {
      const snapshot = sessionLease.read();
      if (
        snapshot.cart.revision !== input.expectedRevision ||
        snapshot.pricingState.revision !== input.expectedRevision ||
        snapshot.cart.lines.length === 0
      ) {
        throw paymentLeaseError(
          "PAYMENT_CART_LEASE_CONFLICT",
          "Payment checkout no longer matches the active cart revision.",
        );
      }
      const publicLease = Object.freeze({
        leaseId: requiredText(this.createLeaseId(), "payment lease id"),
        checkoutIntentId: input.checkoutIntentId,
        revision: input.expectedRevision,
        total: snapshot.cart.actualAmount,
        cart: snapshot.cart,
        pricingState: snapshot.pricingState,
      }) satisfies PaymentCartLease;
      const held: HeldPaymentCartLease = {
        publicLease,
        sessionLease,
        release: releaseResolve,
        operation: null,
      };
      this.held = held;
      settled = true;
      acquiredResolve(publicLease);
      await releaseGate;
    });

    const held = this.held;
    if (held) held.operation = operation;
    void operation.catch((error: unknown) => {
      if (!settled) acquiredReject(error);
      if (this.held === held) this.held = null;
    });
    return acquired;
  }

  private requireHeld(lease: PaymentCartLease): HeldPaymentCartLease {
    const held = this.held;
    if (
      !held ||
      held.publicLease !== lease ||
      held.publicLease.leaseId !== lease.leaseId
    ) {
      throw paymentLeaseError(
        "PAYMENT_CART_LEASE_CONFLICT",
        "Payment cart lease is stale or belongs to another checkout.",
      );
    }
    return held;
  }

  private async releaseHeld(held: HeldPaymentCartLease): Promise<void> {
    if (this.held !== held) {
      throw paymentLeaseError(
        "PAYMENT_CART_LEASE_CONFLICT",
        "Payment cart lease was already released.",
      );
    }
    held.release();
    await held.operation;
    if (this.held === held) this.held = null;
  }
}

function normalizeAcquisition(input: {
  checkoutIntentId: string;
  expectedRevision: number;
}): Readonly<{ checkoutIntentId: string; expectedRevision: number }> {
  if (!Number.isSafeInteger(input.expectedRevision) || input.expectedRevision < 0) {
    throw paymentLeaseError(
      "PAYMENT_CART_LEASE_CONFLICT",
      "Payment cart revision is invalid.",
    );
  }
  return {
    checkoutIntentId: requiredText(
      input.checkoutIntentId,
      "checkout intent id",
    ),
    expectedRevision: input.expectedRevision,
  };
}

function normalizeRecoveryMaterial(
  material: PaymentCartRecoveryMaterial,
): PaymentCartRecoveryMaterial {
  const checkoutIntentId = requiredText(
    material.checkoutIntentId,
    "checkout intent id",
  );
  if (
    material.cart.revision !== material.pricingState.revision ||
    material.cart.mode !== material.pricingState.mode ||
    material.cart.lines.length === 0
  ) {
    throw paymentLeaseError(
      "PAYMENT_CART_LEASE_CONFLICT",
      "Payment recovery material has inconsistent cart state.",
    );
  }
  return {
    checkoutIntentId,
    cart: material.cart,
    pricingState: material.pricingState,
  };
}

function assertHeldMatches(
  held: HeldPaymentCartLease,
  expected: Readonly<{ checkoutIntentId: string; expectedRevision: number }>,
): PaymentCartLease {
  return assertPublicLeaseMatches(held.publicLease, expected);
}

function assertPublicLeaseMatches(
  lease: PaymentCartLease,
  expected: Readonly<{ checkoutIntentId: string; expectedRevision: number }>,
): PaymentCartLease {
  if (
    lease.checkoutIntentId !== expected.checkoutIntentId ||
    lease.revision !== expected.expectedRevision
  ) {
    throw paymentLeaseError(
      "PAYMENT_CART_LEASE_CONFLICT",
      "Another checkout already owns the active cart.",
    );
  }
  return lease;
}

function assertCartValueMatches(
  actual: CartSnapshot,
  expected: CartSnapshot,
): void {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw paymentLeaseError(
      "PAYMENT_CART_LEASE_CONFLICT",
      "Recovered pricing state does not reproduce the persisted cart.",
    );
  }
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw paymentLeaseError(
      "PAYMENT_CART_LEASE_CONFLICT",
      `Payment ${label} is required.`,
    );
  }
  return normalized;
}

function paymentLeaseError(code: string, message: string): Error & {
  code: string;
} {
  return Object.assign(new Error(message), { code });
}
