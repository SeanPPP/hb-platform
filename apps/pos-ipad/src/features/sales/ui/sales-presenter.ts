import {
  createAud,
  type CartSnapshot,
} from "@/core/contracts";
import type { CashDrawerDisposition } from "@/features/checkout/cash/cash-checkout-service";
import {
  calculateCashSettlement,
  roundCashAmount,
} from "@/features/sales/domain";

export const MIN_TOUCH_TARGET = 44;

export type SalesUiPhase =
  | "selling"
  | "verifying-checkout"
  | "cash"
  | "submitting-cash"
  | "success"
  | "locked";

export type SalesSearchStatus = "idle" | "searching" | "ready";

export type SalesErrorCode =
  | "search-required"
  | "search-failed"
  | "product-add-failed"
  | "authorization-denied"
  | "cart-update-failed"
  | "invalid-quantity"
  | "invalid-price"
  | "invalid-discount"
  | "empty-cart"
  | "cash-invalid"
  | "cash-insufficient"
  | "cash-failed"
  | "cart-clear-failed"
  | "hold-failed"
  | "lock-failed"
  | "new-transactions-disabled"
  | "runtime-unavailable";

export type SalesProductSearchItem = Readonly<{
  productCode: string;
  itemNumber: string | null;
  lookupCode: string;
  displayName: string;
  unitPriceCents: number;
}>;

export type SalesCapabilities = Readonly<{
  catalog: boolean;
  cartEditing: boolean;
  cashCheckout: boolean;
  hold: boolean;
  lock: boolean;
}>;

export type SalesCashCompletion = Readonly<{
  completed: true;
  canClearCart: true;
  orderGuid: string;
  cashDueCents: number;
  changeCents: number;
  postCommit: Readonly<{
    drawerDisposition: CashDrawerDisposition;
  }>;
}>;

export type SalesSuccessState = Readonly<{
  orderGuid: string;
  cashDueCents: number;
  changeCents: number;
  clearCartSignalled: boolean;
  drawerDisposition: CashDrawerDisposition;
}>;

export type SalesPresenterState = Readonly<{
  phase: SalesUiPhase;
  cart: CartSnapshot;
  query: string;
  searchStatus: SalesSearchStatus;
  searchResults: readonly SalesProductSearchItem[];
  pendingLookupCount: number;
  cashTenderedText: string;
  errorCode: SalesErrorCode | null;
  success: SalesSuccessState | null;
  capabilities: SalesCapabilities;
}>;

export interface SalesCartPort {
  getSnapshot(): CartSnapshot;
  subscribe(listener: () => void): () => void;
  increaseLine(lineId: string): Promise<void>;
  decreaseLine(lineId: string): Promise<void>;
  removeLine(lineId: string): Promise<void>;
  applyLineDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void>;
  setLineQuantity(lineId: string, quantity: number): Promise<void>;
  setLineUnitPriceCents(
    lineId: string,
    unitPriceCents: number,
  ): Promise<void>;
  applyLineDiscountAmountCents(
    lineId: string,
    discountCents: number,
  ): Promise<void>;
  applyLineManualDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void>;
  applyOrderDiscountAmountCents(discountCents: number): Promise<void>;
  applyOrderManualDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void>;
  applyOrderQuickDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void>;
  clearCart(): Promise<void>;
  /**
   * 该方法是“事务已经提交，UI 现在可以清空”的单向信号。
   * 实现不得在现金服务返回 committed 之前清空购物车。
   */
  clearAfterCommittedOrder(orderGuid: string): Promise<void>;
}

export interface SalesWorkflowPort {
  searchProducts(query: string): Promise<readonly SalesProductSearchItem[]>;
  addProduct(product: SalesProductSearchItem): Promise<void>;
  addByLookupCode(lookupCode: string): Promise<void>;
  addOpenItem(unitPriceCents: number): Promise<void>;
  getPendingCatalogWorkCount(): number;
  subscribePendingCatalogWork(listener: () => void): () => void;
  settlePendingCatalogWork(
    input: Readonly<{ timeoutMs: number }>,
  ): Promise<Readonly<{ timedOut: boolean }>>;
  disposePendingCatalogWork(): void;
  releasePreparedCheckout(): void;
  completeCash(input: Readonly<{
    checkoutIntentId: string;
    cart: CartSnapshot;
    cashTenderedCents: number | null;
  }>): Promise<SalesCashCompletion>;
  holdCart(cart: CartSnapshot): Promise<void>;
  lockTerminal(): Promise<void>;
}

export type SalesPresenterDependencies = Readonly<{
  cart: SalesCartPort;
  workflow: SalesWorkflowPort;
  capabilities: SalesCapabilities;
  createCheckoutIntentId(): string;
  /** 仅约束空购物车开始新交易；已有购物车、补传与支付恢复不得被拦截。 */
  canStartNewTransaction(): boolean;
}>;

export type CashDraft = Readonly<{
  cashDueCents: number;
  cashTenderedCents: number | null;
  normalizedTenderedCents: number | null;
  changeCents: number;
  valid: boolean;
  errorCode: "cash-invalid" | "cash-insufficient" | null;
}>;

export const EMPTY_SALE_CART: CartSnapshot = {
  revision: 0,
  mode: "sale",
  lines: [],
  subtotal: createAud(0),
  discount: createAud(0),
  actualAmount: createAud(0),
};

const DISCONNECTED_CAPABILITIES: SalesCapabilities = {
  catalog: false,
  cartEditing: false,
  cashCheckout: false,
  hold: false,
  lock: false,
};

/**
 * 销售 UI 只与这些 feature-private Port 通信，不直接构造目录、账本或外设服务。
 * 因此组合根未接入时可以诚实禁用交易，而不会产生“演示订单”或伪造成功。
 */
export class SalesPresenter {
  private state: SalesPresenterState;
  private readonly listeners = new Set<() => void>();
  private readonly unsubscribeCart: () => void;
  private readonly unsubscribePendingCatalogWork: () => void;
  private cashIntentId: string | null = null;
  private cashSubmission: Promise<boolean> | null = null;
  private checkoutPreparation: Promise<CartSnapshot | null> | null = null;
  private searchGeneration = 0;
  private destroyed = false;

  public constructor(private readonly dependencies: SalesPresenterDependencies) {
    this.state = {
      phase: "selling",
      cart: dependencies.cart.getSnapshot(),
      query: "",
      searchStatus: "idle",
      searchResults: [],
      pendingLookupCount: dependencies.workflow.getPendingCatalogWorkCount(),
      cashTenderedText: "",
      errorCode: null,
      success: null,
      capabilities: dependencies.capabilities,
    };
    this.unsubscribeCart = dependencies.cart.subscribe(() => {
      this.patchState({ cart: dependencies.cart.getSnapshot() });
    });
    this.unsubscribePendingCatalogWork =
      dependencies.workflow.subscribePendingCatalogWork(() => {
        this.patchState({
          pendingLookupCount:
            dependencies.workflow.getPendingCatalogWorkCount(),
        });
      });
  }

  public readonly getState = (): SalesPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.searchGeneration += 1;
    try {
      // 先 fence 迟到的目录任务，之后才拆除视图订阅，避免销毁窗口注入购物车。
      this.dependencies.workflow.disposePendingCatalogWork();
    } finally {
      this.unsubscribeCart();
      this.unsubscribePendingCatalogWork();
      this.listeners.clear();
    }
  }

  public setQuery(query: string): void {
    this.patchState({
      query,
      errorCode:
        this.state.errorCode === "search-required" ? null : this.state.errorCode,
    });
  }

  public dismissError(): void {
    this.patchState({ errorCode: null });
  }

  public searchProducts(): Promise<boolean> {
    const query = this.state.query.trim();
    if (!this.dependencies.capabilities.catalog) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    if (!query) {
      this.patchState({ errorCode: "search-required" });
      return Promise.resolve(false);
    }

    const generation = ++this.searchGeneration;
    this.patchState({
      searchStatus: "searching",
      errorCode: null,
    });
    return this.dependencies.workflow
      .searchProducts(query)
      .then((results) => {
        if (generation !== this.searchGeneration) {
          return false;
        }
        this.patchState({
          searchStatus: "ready",
          searchResults: [...results],
        });
        return true;
      })
      .catch((error: unknown) => {
        if (generation === this.searchGeneration) {
          this.patchState({
            searchStatus: "idle",
            errorCode: hasErrorCode(
              error,
              "SALES_OPERATION_NOT_AUTHORIZED",
            )
              ? "authorization-denied"
              : "search-failed",
          });
        }
        return false;
      });
  }

  public addLookupCode(): Promise<boolean> {
    const lookupCode = this.state.query.trim();
    if (!this.dependencies.capabilities.catalog) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    if (!lookupCode) {
      this.patchState({ errorCode: "search-required" });
      return Promise.resolve(false);
    }
    if (!this.canMutateNewTransaction()) {
      return Promise.resolve(false);
    }

    // 扫码输入属于高频主路径：先释放输入框，远程校准和本地 miss 回查在后台完成。
    this.patchState({
      query: "",
      searchStatus: "idle",
      searchResults: [],
      errorCode: null,
    });
    return this.dependencies.workflow
      .addByLookupCode(lookupCode)
      .then(() => {
        this.patchState({ cart: this.dependencies.cart.getSnapshot() });
        return true;
      })
      .catch((error: unknown) => {
        if (hasErrorCode(error, "NEW_TRANSACTIONS_DISABLED")) {
          this.patchState({ errorCode: "new-transactions-disabled" });
        } else if (hasErrorCode(error, "SALES_OPERATION_NOT_AUTHORIZED")) {
          this.patchState({ errorCode: "authorization-denied" });
        }
        // 未找到、离线和远程超时均保留当前交易，不用噪声错误打断收银。
        return false;
      });
  }

  public addProduct(product: SalesProductSearchItem): Promise<boolean> {
    if (!this.dependencies.capabilities.catalog) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    if (!this.canMutateNewTransaction()) {
      return Promise.resolve(false);
    }
    return this.runProductMutation(() =>
      this.dependencies.workflow.addProduct(product),
    );
  }

  public addOpenItem(unitPriceCents: number): Promise<boolean> {
    if (!Number.isSafeInteger(unitPriceCents) || unitPriceCents <= 0) {
      this.patchState({ errorCode: "invalid-price" });
      return Promise.resolve(false);
    }
    if (!this.dependencies.capabilities.catalog) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    if (!this.canMutateNewTransaction()) {
      return Promise.resolve(false);
    }
    return this.runProductMutation(() =>
      this.dependencies.workflow.addOpenItem(unitPriceCents),
    );
  }

  public increaseLine(lineId: string): Promise<boolean> {
    return this.runCartMutation(() =>
      this.dependencies.cart.increaseLine(lineId),
    );
  }

  public decreaseLine(lineId: string): Promise<boolean> {
    return this.runCartMutation(() =>
      this.dependencies.cart.decreaseLine(lineId),
    );
  }

  public removeLine(lineId: string): Promise<boolean> {
    return this.runCartMutation(() =>
      this.dependencies.cart.removeLine(lineId),
    );
  }

  public applyLineDiscount(
    lineId: string,
    basisPoints: number,
  ): Promise<boolean> {
    if (
      !Number.isSafeInteger(basisPoints) ||
      basisPoints < 0 ||
      basisPoints > 10_000
    ) {
      this.patchState({ errorCode: "cart-update-failed" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.applyLineDiscountBasisPoints(
        lineId,
        basisPoints,
      ),
    );
  }

  public setLineQuantity(
    lineId: string,
    quantity: number,
  ): Promise<boolean> {
    if (!Number.isSafeInteger(quantity) || quantity <= 0) {
      this.patchState({ errorCode: "invalid-quantity" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.setLineQuantity(lineId, quantity),
    );
  }

  public setLineUnitPriceCents(
    lineId: string,
    unitPriceCents: number,
  ): Promise<boolean> {
    if (!Number.isSafeInteger(unitPriceCents) || unitPriceCents < 0) {
      this.patchState({ errorCode: "invalid-price" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.setLineUnitPriceCents(
        lineId,
        unitPriceCents,
      ),
    );
  }

  public applyLineDiscountAmountCents(
    lineId: string,
    discountCents: number,
  ): Promise<boolean> {
    if (!Number.isSafeInteger(discountCents) || discountCents < 0) {
      this.patchState({ errorCode: "invalid-discount" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.applyLineDiscountAmountCents(
        lineId,
        discountCents,
      ),
    );
  }

  public applyLineManualDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<boolean> {
    if (!validBasisPoints(basisPoints)) {
      this.patchState({ errorCode: "invalid-discount" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.applyLineManualDiscountBasisPoints(
        lineId,
        basisPoints,
      ),
    );
  }

  public applyOrderDiscountAmountCents(
    discountCents: number,
  ): Promise<boolean> {
    if (!Number.isSafeInteger(discountCents) || discountCents < 0) {
      this.patchState({ errorCode: "invalid-discount" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.applyOrderDiscountAmountCents(
        discountCents,
      ),
    );
  }

  public applyOrderManualDiscountBasisPoints(
    basisPoints: number,
  ): Promise<boolean> {
    if (!validBasisPoints(basisPoints)) {
      this.patchState({ errorCode: "invalid-discount" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.applyOrderManualDiscountBasisPoints(
        basisPoints,
      ),
    );
  }

  public applyOrderQuickDiscount(
    basisPoints: number,
  ): Promise<boolean> {
    if (
      basisPoints !== 1_000 &&
      basisPoints !== 2_000 &&
      basisPoints !== 3_000 &&
      basisPoints !== 4_000 &&
      basisPoints !== 5_000
    ) {
      this.patchState({ errorCode: "invalid-discount" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.applyOrderQuickDiscountBasisPoints(
        basisPoints,
      ),
    );
  }

  public clearCart(): Promise<boolean> {
    if (this.state.phase !== "selling") {
      return Promise.resolve(false);
    }
    if (this.state.cart.lines.length === 0) {
      this.patchState({ errorCode: "empty-cart" });
      return Promise.resolve(false);
    }
    return this.runCartMutation(() =>
      this.dependencies.cart.clearCart(),
    );
  }

  public async openCash(): Promise<boolean> {
    if (!this.dependencies.capabilities.cashCheckout) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return false;
    }
    const cart = await this.prepareCheckout();
    if (!cart) {
      return false;
    }

    this.cashIntentId = this.dependencies.createCheckoutIntentId();
    this.patchState({
      cart,
      phase: "cash",
      cashTenderedText: "",
      errorCode: null,
      success: null,
    });
    return true;
  }

  public prepareOnlineCheckout(): Promise<CartSnapshot | null> {
    return this.prepareCheckout();
  }

  public releasePreparedCheckout(): void {
    if (this.destroyed) return;
    this.dependencies.workflow.releasePreparedCheckout();
    if (this.state.phase === "verifying-checkout") {
      this.patchState({
        phase: "selling",
        pendingLookupCount:
          this.dependencies.workflow.getPendingCatalogWorkCount(),
      });
    }
  }

  public closeCash(): boolean {
    if (this.state.phase === "submitting-cash") {
      return false;
    }
    this.releasePreparedCheckout();
    this.cashIntentId = null;
    this.patchState({
      phase: "selling",
      cashTenderedText: "",
      errorCode: null,
    });
    return true;
  }

  public setCashTenderedText(value: string): void {
    if (this.state.phase !== "cash") {
      return;
    }
    this.patchState({
      cashTenderedText: value,
      errorCode:
        this.state.errorCode === "cash-invalid" ||
        this.state.errorCode === "cash-insufficient"
          ? null
          : this.state.errorCode,
    });
  }

  public setExactCash(): void {
    if (this.state.phase !== "cash") {
      return;
    }
    const cashDueCents = getCashDueCents(this.state.cart);
    this.setCashTenderedText(formatCashInput(cashDueCents));
  }

  /**
   * 非 async 包装保证重复点击拿到同一个 Promise；底层 completeCash 只调用一次。
   */
  public submitCash(): Promise<boolean> {
    if (this.cashSubmission) {
      return this.cashSubmission;
    }
    if (this.state.phase !== "cash") {
      return Promise.resolve(false);
    }

    const draft = deriveCashDraft(
      this.state.cart,
      this.state.cashTenderedText,
    );
    if (!draft.valid) {
      this.patchState({ errorCode: draft.errorCode });
      return Promise.resolve(false);
    }

    const checkoutIntentId =
      this.cashIntentId ?? this.dependencies.createCheckoutIntentId();
    this.cashIntentId = checkoutIntentId;
    const cartAtConfirmation = this.state.cart;
    this.patchState({
      phase: "submitting-cash",
      errorCode: null,
    });

    const submission = this.dependencies.workflow
      .completeCash({
        checkoutIntentId,
        cart: cartAtConfirmation,
        cashTenderedCents: draft.cashTenderedCents,
      })
      .then(
        async (result) => {
          if (!result.completed || !result.canClearCart) {
            this.patchState({
              phase: "cash",
              errorCode: "cash-failed",
            });
            return false;
          }

          let clearCartSignalled = true;
          let cartAfterCommit = cartAtConfirmation;
          try {
            await this.dependencies.cart.clearAfterCommittedOrder(
              result.orderGuid,
            );
            cartAfterCommit = this.dependencies.cart.getSnapshot();
            if (cartAfterCommit.lines.length > 0) {
              clearCartSignalled = false;
            }
          } catch {
            clearCartSignalled = false;
          }

          this.cashIntentId = null;
          this.patchState({
            phase: "success",
            cart: cartAfterCommit,
            cashTenderedText: "",
            errorCode: clearCartSignalled ? null : "cart-clear-failed",
            success: {
              orderGuid: result.orderGuid,
              cashDueCents: result.cashDueCents,
              changeCents: result.changeCents,
              clearCartSignalled,
              drawerDisposition: result.postCommit.drawerDisposition,
            },
          });
          return true;
        },
        () => {
          this.patchState({
            phase: "cash",
            errorCode: "cash-failed",
          });
          return false;
        },
      )
      .finally(() => {
        if (this.cashSubmission === submission) {
          this.cashSubmission = null;
        }
      });

    this.cashSubmission = submission;
    return submission;
  }

  public startNewSale(): boolean {
    if (this.state.phase !== "success") {
      return false;
    }
    if (this.state.success?.clearCartSignalled !== true) {
      this.patchState({ errorCode: "cart-clear-failed" });
      return false;
    }
    const cart = this.dependencies.cart.getSnapshot();
    if (cart.lines.length > 0) {
      this.patchState({ errorCode: "cart-clear-failed" });
      return false;
    }
    if (!this.dependencies.canStartNewTransaction()) {
      this.patchState({ errorCode: "new-transactions-disabled" });
      return false;
    }
    this.releasePreparedCheckout();
    this.patchState({
      phase: "selling",
      query: "",
      searchStatus: "idle",
      searchResults: [],
      cashTenderedText: "",
      errorCode: null,
      success: null,
      cart,
    });
    return true;
  }

  public holdCart(): Promise<boolean> {
    if (this.state.phase !== "selling") {
      return Promise.resolve(false);
    }
    if (!this.dependencies.capabilities.hold) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    if (this.state.cart.lines.length === 0) {
      this.patchState({ errorCode: "empty-cart" });
      return Promise.resolve(false);
    }

    return this.dependencies.workflow
      .holdCart(this.state.cart)
      .then(() => {
        this.patchState({
          cart: this.dependencies.cart.getSnapshot(),
          errorCode: null,
        });
        return true;
      })
      .catch(() => {
        this.patchState({ errorCode: "hold-failed" });
        return false;
      });
  }

  public lockTerminal(): Promise<boolean> {
    if (!this.dependencies.capabilities.lock) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    return this.dependencies.workflow
      .lockTerminal()
      .then(() => {
        this.patchState({ phase: "locked", errorCode: null });
        return true;
      })
      .catch(() => {
        this.patchState({ errorCode: "lock-failed" });
        return false;
      });
  }

  private runProductMutation(operation: () => Promise<void>): Promise<boolean> {
    return operation()
      .then(() => {
        this.patchState({
          cart: this.dependencies.cart.getSnapshot(),
          query: "",
          searchStatus: "idle",
          searchResults: [],
          errorCode: null,
        });
        return true;
      })
      .catch((error: unknown) => {
        this.patchState({
          errorCode: hasErrorCode(
            error,
            "NEW_TRANSACTIONS_DISABLED",
          )
            ? "new-transactions-disabled"
            : hasErrorCode(error, "SALES_OPERATION_NOT_AUTHORIZED")
              ? "authorization-denied"
              : "product-add-failed",
        });
        return false;
      });
  }

  private prepareCheckout(): Promise<CartSnapshot | null> {
    if (
      this.destroyed ||
      this.state.phase !== "selling" ||
      this.checkoutPreparation
    ) {
      return Promise.resolve(null);
    }
    const pendingLookupCount =
      this.dependencies.workflow.getPendingCatalogWorkCount();
    if (
      this.state.cart.lines.length === 0 &&
      pendingLookupCount <= 0
    ) {
      this.patchState({ errorCode: "empty-cart" });
      return Promise.resolve(null);
    }

    this.patchState({
      phase: "verifying-checkout",
      pendingLookupCount,
      cashTenderedText: "",
      errorCode: null,
    });
    const preparation = Promise.resolve()
      .then(() =>
        this.dependencies.workflow.settlePendingCatalogWork({
          timeoutMs: 2_000,
        }),
      )
      .catch(() => ({ timedOut: true }))
      .then(() => {
        const cart = this.dependencies.cart.getSnapshot();
        if (this.destroyed || this.state.phase !== "verifying-checkout") {
          return null;
        }
        if (cart.lines.length === 0) {
          this.releasePreparedCheckout();
          this.patchState({
            phase: "selling",
            cart,
            pendingLookupCount:
              this.dependencies.workflow.getPendingCatalogWorkCount(),
            errorCode: "empty-cart",
          });
          return null;
        }
        this.patchState({
          cart,
          pendingLookupCount:
            this.dependencies.workflow.getPendingCatalogWorkCount(),
        });
        return cart;
      })
      .finally(() => {
        if (this.checkoutPreparation === preparation) {
          this.checkoutPreparation = null;
        }
      });
    this.checkoutPreparation = preparation;
    return preparation;
  }

  private canMutateNewTransaction(): boolean {
    if (this.state.phase !== "selling") {
      return false;
    }
    if (
      this.state.cart.lines.length === 0 &&
      !this.dependencies.canStartNewTransaction()
    ) {
      this.patchState({ errorCode: "new-transactions-disabled" });
      return false;
    }
    return true;
  }

  private runCartMutation(operation: () => Promise<void>): Promise<boolean> {
    if (this.state.phase !== "selling") {
      return Promise.resolve(false);
    }
    if (!this.dependencies.capabilities.cartEditing) {
      this.patchState({ errorCode: "runtime-unavailable" });
      return Promise.resolve(false);
    }
    return operation()
      .then(() => {
        this.patchState({
          cart: this.dependencies.cart.getSnapshot(),
          errorCode: null,
        });
        return true;
      })
      .catch((error: unknown) => {
        this.patchState({
          errorCode: hasErrorCode(
            error,
            "SALES_OPERATION_NOT_AUTHORIZED",
          )
            ? "authorization-denied"
            : "cart-update-failed",
        });
        return false;
      });
  }

  private patchState(patch: Partial<SalesPresenterState>): void {
    if (this.destroyed) return;
    this.state = {
      ...this.state,
      ...patch,
    };
    for (const listener of this.listeners) {
      try {
        listener();
      } catch {
        // 视图订阅者故障不能反向改变“现金订单已提交”的业务结果。
      }
    }
  }
}

export function getCashDueCents(cart: CartSnapshot): number {
  return roundCashAmount(cart.actualAmount).cents;
}

export function deriveCashDraft(
  cart: CartSnapshot,
  cashTenderedText: string,
): CashDraft {
  const cashDueCents = getCashDueCents(cart);
  if (cashDueCents === 0) {
    return {
      cashDueCents,
      cashTenderedCents: null,
      normalizedTenderedCents: null,
      changeCents: 0,
      valid: true,
      errorCode: null,
    };
  }

  const cashTenderedCents = parseCashInput(cashTenderedText);
  if (cashTenderedCents === null) {
    return {
      cashDueCents,
      cashTenderedCents: null,
      normalizedTenderedCents: null,
      changeCents: 0,
      valid: false,
      errorCode: "cash-invalid",
    };
  }

  const settlement = calculateCashSettlement({
    actualAmount: cart.actualAmount,
    cashTendered: createAud(cashTenderedCents),
  });
  const enough =
    cart.actualAmount.cents >= 0
      ? settlement.normalizedCashTendered.cents >= settlement.cashDue.cents
      : settlement.normalizedCashTendered.cents <= settlement.cashDue.cents;

  return {
    cashDueCents: settlement.cashDue.cents,
    cashTenderedCents,
    normalizedTenderedCents: settlement.normalizedCashTendered.cents,
    changeCents: settlement.change.cents,
    valid: enough,
    errorCode: enough ? null : "cash-insufficient",
  };
}

export function parseCashInput(value: string): number | null {
  const normalized = value.trim().replace(/[$,\s]/g, "");
  const match = /^(\d+)(?:\.(\d{0,2}))?$/.exec(normalized);
  if (!match) {
    return null;
  }
  const whole = Number(match[1]);
  const cents = Number((match[2] ?? "").padEnd(2, "0"));
  const result = whole * 100 + cents;
  return Number.isSafeInteger(result) ? result : null;
}

export function formatCashInput(cents: number): string {
  const sign = cents < 0 ? "-" : "";
  const absolute = Math.abs(cents);
  return `${sign}${Math.floor(absolute / 100)}.${String(absolute % 100).padStart(2, "0")}`;
}

export function formatAud(cents: number, locale: "en" | "zh"): string {
  return new Intl.NumberFormat(locale === "zh" ? "zh-AU" : "en-AU", {
    style: "currency",
    currency: "AUD",
    currencyDisplay: "narrowSymbol",
  }).format(cents / 100);
}

export function getAvailableTenderMethods(
  connectivity: "checking" | "online" | "offline",
): readonly ("cash" | "card" | "voucher")[] {
  return connectivity === "online"
    ? ["cash", "card", "voucher"]
    : ["cash"];
}

export function createDisconnectedSalesPresenter(): SalesPresenter {
  const unavailable = async (): Promise<never> => {
    throw new Error("Sales runtime is not connected.");
  };
  const cart: SalesCartPort = {
    getSnapshot: () => EMPTY_SALE_CART,
    subscribe: () => () => undefined,
    increaseLine: unavailable,
    decreaseLine: unavailable,
    removeLine: unavailable,
    applyLineDiscountBasisPoints: unavailable,
    setLineQuantity: unavailable,
    setLineUnitPriceCents: unavailable,
    applyLineDiscountAmountCents: unavailable,
    applyLineManualDiscountBasisPoints: unavailable,
    applyOrderDiscountAmountCents: unavailable,
    applyOrderManualDiscountBasisPoints: unavailable,
    applyOrderQuickDiscountBasisPoints: unavailable,
    clearCart: unavailable,
    clearAfterCommittedOrder: unavailable,
  };
  const workflow: SalesWorkflowPort = {
    searchProducts: unavailable,
    addProduct: unavailable,
    addByLookupCode: unavailable,
    addOpenItem: unavailable,
    getPendingCatalogWorkCount: () => 0,
    subscribePendingCatalogWork: () => () => undefined,
    settlePendingCatalogWork: async () => ({ timedOut: false }),
    disposePendingCatalogWork: () => undefined,
    releasePreparedCheckout: () => undefined,
    completeCash: unavailable,
    holdCart: unavailable,
    lockTerminal: unavailable,
  };
  return new SalesPresenter({
    cart,
    workflow,
    capabilities: DISCONNECTED_CAPABILITIES,
    createCheckoutIntentId: () => "unavailable",
    canStartNewTransaction: () => false,
  });
}

function hasErrorCode(error: unknown, code: string): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    error.code === code
  );
}

function validBasisPoints(value: number): boolean {
  return (
    Number.isSafeInteger(value) && value >= 0 && value <= 10_000
  );
}
