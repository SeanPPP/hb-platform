import type {
  SalesCapabilities,
  SalesCartPort,
  SalesPresenterDependencies,
  SalesProductSearchItem,
  SalesWorkflowPort,
} from "../ui/sales-presenter";
import { SalesPresenter } from "../ui/sales-presenter";

import { createAud, type CartSnapshot } from "@/core/contracts";
import type { LocalCatalogMatch } from "@/core/db/catalog-repository";
import type {
  CashCheckoutInput,
  CashCheckoutResult,
} from "@/features/checkout/cash/cash-checkout-service";
import {
  ACTIVE_PRICING_CART_STALE_SNAPSHOT,
  ActivePricingCartSession,
} from "@/features/sales/runtime/active-pricing-cart-session";
import {
  AuthorizedSalesOperationExecutor,
  SALES_PERMISSIONS,
  cartMutationRejected,
  quickLineDiscountPermission,
  quickOrderDiscountPermission,
  type SalesOperationSecurity,
} from "@/features/sales/runtime/sales-operation-security";

export const SALES_NEW_TRANSACTIONS_DISABLED =
  "NEW_TRANSACTIONS_DISABLED";

export interface LocalCatalogPort {
  findExact(lookupCode: string): Promise<LocalCatalogMatch | null>;
  searchByName(
    query: string,
    limit: number,
    offset?: number,
  ): Promise<readonly LocalCatalogMatch[]>;
}

/** 仅要求 durable 服务兼容 complete 入口，不把 runtime 绑定到具体实现类。 */
export interface DurableCashCheckoutPort {
  complete(input: CashCheckoutInput): Promise<CashCheckoutResult>;
}

export interface SalesHoldPort {
  hold(cart: CartSnapshot): Promise<void>;
}

export interface SalesLockPort {
  lock(): Promise<void>;
}

/**
 * 由组合根绑定到创建 presenter 时的可信 cashier lease。401、403、锁屏或换人后，
 * assertActive 必须同步 fail-closed，且 UI 不得构造或替换这个 Port。
 */
export interface ConnectedSalesSessionGuard {
  assertActive(): void;
}

export type ConnectedSalesIdentity = Readonly<{
  storeCode: string;
  deviceCode: string;
  cashierId: string;
  cashierName: string;
}>;

export type ConnectedSalesRuntimeDependencies = Readonly<{
  activeCartSession: ActivePricingCartSession;
  catalog?: LocalCatalogPort | undefined;
  cashCheckout?: DurableCashCheckoutPort | undefined;
  identity: ConnectedSalesIdentity;
  hold?: SalesHoldPort;
  lock?: SalesLockPort;
  sessionGuard: ConnectedSalesSessionGuard;
  newTransactionGate: Readonly<{
    canStartNewTransaction(): boolean;
  }>;
  createCheckoutIntentId(): string;
  createLineId(): string;
  operationSecurity: SalesOperationSecurity;
}>;

/**
 * PricingCart 是同步领域对象；这个 adapter 将每次成功变更显式发布给 React presenter。
 * 订单成功前不存在 clear 入口，因此失败/异常路径不能意外清空购物车。
 */
export class PricingCartSalesAdapter implements SalesCartPort {
  private readonly listenerSubscriptions = new Set<() => void>();

  public constructor(
    private readonly activeCart: ActivePricingCartSession,
    private readonly sessionGuard: ConnectedSalesSessionGuard,
    private readonly operations: AuthorizedSalesOperationExecutor,
  ) {}

  public getSnapshot(): CartSnapshot {
    this.sessionGuard.assertActive();
    return this.activeCart.getSnapshot();
  }

  public subscribe(listener: () => void): () => void {
    this.sessionGuard.assertActive();
    const unsubscribeSession = this.activeCart.subscribe(() => {
      try {
        this.sessionGuard.assertActive();
      } catch {
        // 旧 presenter 失效后静默丢弃共享购物车的新快照，等待 route 卸载订阅。
        return;
      }
      listener();
    });
    let subscribed = true;
    const unsubscribe = () => {
      if (!subscribed) return;
      subscribed = false;
      this.listenerSubscriptions.delete(unsubscribe);
      unsubscribeSession();
    };
    this.listenerSubscriptions.add(unsubscribe);
    return unsubscribe;
  }

  public destroy(): void {
    for (const unsubscribe of [...this.listenerSubscriptions]) {
      unsubscribe();
    }
  }

  public async increaseLine(lineId: string): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.changeQuantity,
      "increase-line",
      "CART_ITEM_QUANTITY_CHANGE",
      () =>
        this.mutate(
          this.activeCart.increaseLine(lineId),
          "Unable to increase cart line.",
        ),
    );
  }

  public async decreaseLine(lineId: string): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.changeQuantity,
      "decrease-line",
      "CART_ITEM_QUANTITY_CHANGE",
      () =>
        this.mutate(
          this.activeCart.decreaseLine(lineId),
          "Unable to decrease cart line.",
        ),
    );
  }

  public async removeLine(lineId: string): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.removeLine,
      "remove-line",
      "CART_ITEM_REMOVE",
      () =>
        this.mutate(
          this.activeCart.removeLine(lineId),
          "Unable to remove cart line.",
        ),
    );
  }

  public async applyLineDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void> {
    const permissionCode =
      basisPoints === 0
        ? SALES_PERMISSIONS.lineManualDiscount
        : quickLineDiscountPermission(basisPoints);
    if (!permissionCode) {
      throw cartMutationRejected(
        "Quick line discount must be 10%, 20%, 30%, 40%, or 50%.",
      );
    }
    await this.runMutation(
      permissionCode,
      basisPoints === 0
        ? "manual-discount-percent"
        : "quick-discount-percent",
      "CART_LINE_DISCOUNT_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setLineDiscountPercentBps(
            lineId,
            basisPoints,
          ),
          "Unable to apply cart discount.",
        ),
    );
  }

  public async setLineQuantity(
    lineId: string,
    quantity: number,
  ): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.changeQuantity,
      "modify-quantity",
      "CART_ITEM_QUANTITY_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setLineQuantity(lineId, quantity),
          "Unable to set cart line quantity.",
        ),
    );
  }

  public async setLineUnitPriceCents(
    lineId: string,
    unitPriceCents: number,
  ): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.changePrice,
      "modify-price",
      "CART_ITEM_PRICE_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setLineUnitPrice(
            lineId,
            createAud(unitPriceCents),
          ),
          "Unable to set cart line price.",
        ),
    );
  }

  public async applyLineDiscountAmountCents(
    lineId: string,
    discountCents: number,
  ): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.lineManualDiscount,
      "manual-discount-amount",
      "CART_LINE_DISCOUNT_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setLineDiscountAmount(
            lineId,
            createAud(discountCents),
          ),
          "Unable to set cart line discount.",
        ),
    );
  }

  public async applyLineManualDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.lineManualDiscount,
      "manual-discount-percent",
      "CART_LINE_DISCOUNT_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setLineDiscountPercentBps(
            lineId,
            basisPoints,
          ),
          "Unable to set cart line discount.",
        ),
    );
  }

  public async applyOrderDiscountAmountCents(
    discountCents: number,
  ): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.orderManualDiscount,
      "manual-discount-amount",
      "CART_ORDER_DISCOUNT_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setOrderDiscountAmount(
            createAud(discountCents),
          ),
          "Unable to set order discount.",
        ),
    );
  }

  public async applyOrderManualDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.orderManualDiscount,
      "manual-discount-percent",
      "CART_ORDER_DISCOUNT_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setOrderDiscountPercentBps(basisPoints),
          "Unable to set order discount.",
        ),
    );
  }

  public async applyOrderQuickDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void> {
    const permissionCode = quickOrderDiscountPermission(basisPoints);
    if (!permissionCode) {
      throw cartMutationRejected(
        "Quick order discount must be 10%, 20%, 30%, 40%, or 50%.",
      );
    }
    await this.runMutation(
      permissionCode,
      "quick-discount-percent",
      "CART_ORDER_DISCOUNT_CHANGE",
      () =>
        this.mutate(
          this.activeCart.setOrderDiscountPercentBps(basisPoints),
          "Unable to set order discount.",
        ),
    );
  }

  public async clearCart(): Promise<void> {
    await this.runMutation(
      SALES_PERMISSIONS.clearCart,
      "clear-cart",
      "CART_CLEAR",
      () =>
        this.mutate(
          this.activeCart.clearManually(),
          "Unable to clear cart.",
        ),
    );
  }

  public async clearAfterCommittedOrder(orderGuid: string): Promise<void> {
    this.sessionGuard.assertActive();
    this.activeCart.clearAfterCommittedOrder(orderGuid);
  }

  public addCatalogItem(item: LocalCatalogMatch, lineId: string): void {
    this.sessionGuard.assertActive();
    this.activeCart.addItem({
      lineId: requiredText(lineId, "Cart line id"),
      productCode: item.productCode,
      itemNumber: item.itemNumber,
      lookupCode: item.lookupCode,
      displayName: item.displayName,
      quantity: item.quantityFactor,
      unitPrice: createAud(item.retailPriceCents),
      syncProvenance: {
        referenceCode: item.referenceCode,
        priceSource: item.priceSource,
      },
      priceSource: "catalog",
    });
  }

  public addOpenCatalogItem(
    item: LocalCatalogMatch,
    lineId: string,
    unitPriceCents: number,
  ): void {
    this.sessionGuard.assertActive();
    this.activeCart.addOpenItem({
      lineId: requiredText(lineId, "Cart line id"),
      productCode: item.productCode,
      itemNumber: item.itemNumber,
      lookupCode: item.lookupCode,
      displayName: item.displayName,
      unitPrice: createAud(unitPriceCents),
      syncProvenance: {
        referenceCode: item.referenceCode,
        priceSource: item.priceSource,
      },
    });
  }

  private runMutation(
    permissionCode: string,
    action: string,
    eventType:
      | "CART_ITEM_REMOVE"
      | "CART_ITEM_QUANTITY_CHANGE"
      | "CART_ITEM_PRICE_CHANGE"
      | "CART_LINE_DISCOUNT_CHANGE"
      | "CART_ORDER_DISCOUNT_CHANGE"
      | "CART_CLEAR",
    operation: () => void,
  ): Promise<void> {
    return this.operations.runCartMutation({
      permissionCode,
      action,
      eventType,
      getCart: () => this.getSnapshot(),
      operation,
    });
  }

  private mutate(success: boolean, failureMessage: string): void {
    if (!success) {
      throw cartMutationRejected(failureMessage);
    }
  }
}

/**
 * 生产组合的最窄边界：外层只能给出已认证的 identity、真实本地目录和 durable cash port。
 * SalesWorkflowPort 不接收身份字段，从而无法由 UI 伪造门店、设备或收银员。
 */
export function createConnectedSalesDependencies(
  input: ConnectedSalesRuntimeDependencies,
): SalesPresenterDependencies {
  assertIdentity(input.identity);
  input.sessionGuard.assertActive();
  const operations = new AuthorizedSalesOperationExecutor(
    input.operationSecurity,
    input.identity,
    input.sessionGuard,
  );
  const cart = new PricingCartSalesAdapter(
    input.activeCartSession,
    input.sessionGuard,
    operations,
  );
  const workflow = new ConnectedSalesWorkflow(
    cart,
    input.activeCartSession,
    input.catalog,
    input.cashCheckout,
    input.identity,
    input.hold,
    input.lock,
    input.sessionGuard,
    input.newTransactionGate,
    input.createLineId,
    operations,
  );

  return {
    cart,
    workflow,
    capabilities: deriveCapabilities(input),
    createCheckoutIntentId: input.createCheckoutIntentId,
    canStartNewTransaction:
      input.newTransactionGate.canStartNewTransaction,
  };
}

export function createConnectedSalesPresenter(
  input: ConnectedSalesRuntimeDependencies,
): SalesPresenter {
  return new SalesPresenter(createConnectedSalesDependencies(input));
}

class ConnectedSalesWorkflow implements SalesWorkflowPort {
  public constructor(
    private readonly cart: PricingCartSalesAdapter,
    private readonly activeCart: ActivePricingCartSession,
    private readonly catalog: LocalCatalogPort | undefined,
    private readonly cashCheckout: DurableCashCheckoutPort | undefined,
    private readonly identity: ConnectedSalesIdentity,
    private readonly hold: SalesHoldPort | undefined,
    private readonly lock: SalesLockPort | undefined,
    private readonly sessionGuard: ConnectedSalesSessionGuard,
    private readonly newTransactionGate: Readonly<{
      canStartNewTransaction(): boolean;
    }>,
    private readonly createLineId: () => string,
    private readonly operations: AuthorizedSalesOperationExecutor,
  ) {}

  public async searchProducts(
    query: string,
  ): Promise<readonly SalesProductSearchItem[]> {
    return this.operations.runRead(
      SALES_PERMISSIONS.view,
      "search-products",
      async () => {
        const matches = await this.requireCatalog().searchByName(
          query,
          50,
          0,
        );
        this.sessionGuard.assertActive();
        return matches
          .filter(
            (match) => match.storeCode === this.identity.storeCode,
          )
          .map(toSearchItem);
      },
    );
  }

  public async addProduct(product: SalesProductSearchItem): Promise<void> {
    this.sessionGuard.assertActive();
    this.assertCanStartOrContinueTransaction();
    await this.operations.runCartMutation({
      permissionCode: SALES_PERMISSIONS.addItem,
      action: "add-selected",
      eventType: "CART_ITEM_ADD",
      getCart: () => this.cart.getSnapshot(),
      operation: async () => {
        const match = await this.requireExactCatalogItem(
          product.lookupCode,
        );
        this.assertCanStartOrContinueTransaction();
        if (
          match.productCode !== product.productCode ||
          match.lookupCode !== product.lookupCode
        ) {
          throw new Error(
            "Catalog product identity does not match the active local snapshot.",
          );
        }
        this.cart.addCatalogItem(match, this.createLineId());
      },
    });
  }

  public async addByLookupCode(lookupCode: string): Promise<void> {
    this.sessionGuard.assertActive();
    this.assertCanStartOrContinueTransaction();
    await this.operations.runCartMutation({
      permissionCode: SALES_PERMISSIONS.addItem,
      action: "scan-add-item",
      eventType: "CART_ITEM_ADD",
      getCart: () => this.cart.getSnapshot(),
      operation: async () => {
        const match = await this.requireExactCatalogItem(lookupCode);
        this.assertCanStartOrContinueTransaction();
        this.cart.addCatalogItem(match, this.createLineId());
      },
    });
  }

  public async addOpenItem(unitPriceCents: number): Promise<void> {
    if (!Number.isSafeInteger(unitPriceCents) || unitPriceCents <= 0) {
      throw cartMutationRejected(
        "Open item price must be positive AUD cents.",
      );
    }
    this.sessionGuard.assertActive();
    this.assertCanStartOrContinueTransaction();
    await this.operations.runCartMutation({
      permissionCode: SALES_PERMISSIONS.addOpenItem,
      action: "add-open-item",
      eventType: "CART_ITEM_ADD",
      getCart: () => this.cart.getSnapshot(),
      operation: async () => {
        const match = await this.requireExactCatalogItem("OPENITEM");
        this.assertCanStartOrContinueTransaction();
        this.cart.addOpenCatalogItem(
          match,
          this.createLineId(),
          unitPriceCents,
        );
      },
    });
  }

  public async completeCash(input: Readonly<{
    checkoutIntentId: string;
    cart: CartSnapshot;
    cashTenderedCents: number | null;
  }>): Promise<CashCheckoutResult> {
    this.sessionGuard.assertActive();
    const checkout = this.cashCheckout;
    if (!checkout) {
      return Promise.reject(new Error("Durable cash checkout is unavailable."));
    }
    return this.activeCart.runExclusive(async (lease) => {
      this.sessionGuard.assertActive();
      if (!lease.isCurrentCartSnapshot(input.cart)) {
        throw Object.assign(
          new Error("Cash checkout cart snapshot is stale."),
          { code: ACTIVE_PRICING_CART_STALE_SNAPSHOT },
        );
      }

      // 中文注释：身份只来自已认证组合根，UI 仅能传交易内容与 intent。
      const result = await checkout.complete({ ...input, ...this.identity });
      try {
        // 先在同一 lease 内清车，阻止另一个 presenter 在 durable commit 后插入商品。
        lease.clearAfterCommittedOrder(result.orderGuid);
      } catch {
        // 已提交订单不能降级成“现金失败”；Presenter 会再次发出 clear 信号并显示明确告警。
      }
      return result;
    });
  }

  public async holdCart(cart: CartSnapshot): Promise<void> {
    this.sessionGuard.assertActive();
    if (!this.hold) {
      throw new Error("Sales hold is unavailable.");
    }
    await this.hold.hold(cart);
  }

  public async lockTerminal(): Promise<void> {
    this.sessionGuard.assertActive();
    if (!this.lock) {
      throw new Error("Terminal lock is unavailable.");
    }
    await this.lock.lock();
  }

  private requireCatalog(): LocalCatalogPort {
    if (!this.catalog) {
      throw new Error("Local catalog is unavailable.");
    }
    return this.catalog;
  }

  private assertCanStartOrContinueTransaction(): void {
    if (
      this.activeCart.getSnapshot().lines.length === 0 &&
      !this.newTransactionGate.canStartNewTransaction()
    ) {
      throw Object.assign(
        new Error("New transactions are disabled by the iPad policy gate."),
        { code: SALES_NEW_TRANSACTIONS_DISABLED },
      );
    }
  }

  private async requireExactCatalogItem(
    lookupCode: string,
  ): Promise<LocalCatalogMatch> {
    this.sessionGuard.assertActive();
    const match = await this.requireCatalog().findExact(lookupCode);
    this.sessionGuard.assertActive();
    if (!match || match.storeCode !== this.identity.storeCode) {
      throw new Error("Catalog item is unavailable for this store.");
    }
    return match;
  }
}

function deriveCapabilities(
  input: ConnectedSalesRuntimeDependencies,
): SalesCapabilities {
  return {
    catalog: input.catalog !== undefined,
    cartEditing: true,
    cashCheckout: input.cashCheckout !== undefined,
    hold: input.hold !== undefined,
    lock: input.lock !== undefined,
  };
}

function toSearchItem(item: LocalCatalogMatch): SalesProductSearchItem {
  return {
    productCode: item.productCode,
    itemNumber: item.itemNumber,
    lookupCode: item.lookupCode,
    displayName: item.displayName,
    unitPriceCents: item.retailPriceCents,
  };
}

function assertIdentity(identity: ConnectedSalesIdentity): void {
  requiredText(identity.storeCode, "Store code");
  requiredText(identity.deviceCode, "Device code");
  requiredText(identity.cashierId, "Cashier id");
  requiredText(identity.cashierName, "Cashier name");
}

function requiredText(value: string, label: string): string {
  if (!value.trim()) {
    throw new Error(`${label} is required.`);
  }
  return value;
}
