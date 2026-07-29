import assert from "node:assert/strict";
import test from "node:test";

import {
  createDisconnectedSalesPresenter,
  deriveCashDraft,
  EMPTY_SALE_CART,
  getAvailableTenderMethods,
  MIN_TOUCH_TARGET,
  parseCashInput,
  SalesPresenter,
  type SalesCapabilities,
  type SalesCartPort,
  type SalesCashCompletion,
  type SalesPresenterDependencies,
  type SalesWorkflowPort,
} from "./sales-presenter";

import {
  createAud,
  type CartLine,
  type CartSnapshot,
} from "@/core/contracts";

const ALL_CAPABILITIES: SalesCapabilities = {
  catalog: true,
  cartEditing: true,
  cashCheckout: true,
  hold: true,
  lock: true,
};

class MemoryCartPort implements SalesCartPort {
  public snapshot: CartSnapshot;
  public readonly clearSignals: string[] = [];
  public readonly mutations: {
    operation: string;
    lineId?: string;
    value?: number;
  }[] = [];
  private readonly listeners = new Set<() => void>();

  public constructor(snapshot: CartSnapshot) {
    this.snapshot = snapshot;
  }

  public getSnapshot(): CartSnapshot {
    return this.snapshot;
  }

  public subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public async increaseLine(): Promise<void> {}
  public async decreaseLine(): Promise<void> {}
  public async removeLine(): Promise<void> {}
  public async applyLineDiscountBasisPoints(): Promise<void> {}
  public async setLineQuantity(
    lineId: string,
    quantity: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "line-quantity",
      lineId,
      value: quantity,
    });
  }

  public async setLineUnitPriceCents(
    lineId: string,
    unitPriceCents: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "line-price",
      lineId,
      value: unitPriceCents,
    });
  }

  public async applyLineDiscountAmountCents(
    lineId: string,
    discountCents: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "line-discount-amount",
      lineId,
      value: discountCents,
    });
  }

  public async applyLineManualDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "line-discount-percent",
      lineId,
      value: basisPoints,
    });
  }

  public async applyOrderDiscountAmountCents(
    discountCents: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "order-discount-amount",
      value: discountCents,
    });
  }

  public async applyOrderManualDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "order-discount-percent",
      value: basisPoints,
    });
  }

  public async applyOrderQuickDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void> {
    this.mutations.push({
      operation: "order-discount-quick",
      value: basisPoints,
    });
  }

  public async clearCart(): Promise<void> {
    this.mutations.push({ operation: "clear-cart" });
  }

  public async clearAfterCommittedOrder(orderGuid: string): Promise<void> {
    this.clearSignals.push(orderGuid);
    this.snapshot = {
      ...EMPTY_SALE_CART,
      revision: this.snapshot.revision + 1,
    };
    for (const listener of this.listeners) {
      listener();
    }
  }
}

function saleCart(actualAmountCents = 995): CartSnapshot {
  const line: CartLine = {
    lineId: "line-1",
    productCode: "P-001",
    itemNumber: "I-001",
    lookupCode: "930000000001",
    displayName: "测试商品",
    quantity: "1",
    unitPrice: createAud(actualAmountCents),
    discount: createAud(0),
    actualAmount: createAud(actualAmountCents),
    priceSource: "catalog",
    kind: "sale",
    returnSourceKey: null,
    originalOrderGuid: null,
    originalOrderDetailGuid: null,
  };
  return {
    revision: 1,
    mode: "sale",
    lines: [line],
    subtotal: createAud(actualAmountCents),
    discount: createAud(0),
    actualAmount: createAud(actualAmountCents),
  };
}

function createWorkflow(
  completeCash: SalesWorkflowPort["completeCash"],
): SalesWorkflowPort {
  return {
    async searchProducts() {
      return [];
    },
    async addProduct() {},
    async addByLookupCode() {},
    async addOpenItem() {},
    completeCash,
    async holdCart() {},
    async lockTerminal() {},
  };
}

function createPresenter(
  input: Readonly<{
    cart?: MemoryCartPort;
    workflow?: SalesWorkflowPort;
    capabilities?: SalesCapabilities;
    createCheckoutIntentId?: () => string;
    canStartNewTransaction?: () => boolean;
  }> = {},
): Readonly<{ presenter: SalesPresenter; cart: MemoryCartPort }> {
  const cart = input.cart ?? new MemoryCartPort(saleCart());
  const dependencies: SalesPresenterDependencies = {
    cart,
    workflow:
      input.workflow ??
      createWorkflow(async () => ({
        completed: true,
        canClearCart: true,
        orderGuid: "order-1",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "queued" },
      })),
    capabilities: input.capabilities ?? ALL_CAPABILITIES,
    createCheckoutIntentId:
      input.createCheckoutIntentId ?? (() => "checkout-intent-1"),
    canStartNewTransaction:
      input.canStartNewTransaction ?? (() => true),
  };
  return {
    presenter: new SalesPresenter(dependencies),
    cart,
  };
}

test("空购物车禁止进入现金结账，未接入能力不会伪造成功", () => {
  const { presenter } = createPresenter({
    cart: new MemoryCartPort(EMPTY_SALE_CART),
  });

  assert.equal(presenter.openCash(), false);
  assert.equal(presenter.getState().phase, "selling");
  assert.equal(presenter.getState().errorCode, "empty-cart");

  presenter.destroy();
});

test("未接入的生产路由 presenter 明确禁用全部能力且绝不进入成功态", async () => {
  const presenter = createDisconnectedSalesPresenter();

  assert.deepEqual(presenter.getState().capabilities, {
    catalog: false,
    cartEditing: false,
    cashCheckout: false,
    hold: false,
    lock: false,
  });
  assert.equal(presenter.openCash(), false);
  assert.equal(await presenter.submitCash(), false);
  assert.equal(presenter.getState().phase, "selling");
  assert.equal(presenter.getState().success, null);
  assert.equal(presenter.getState().errorCode, "runtime-unavailable");

  presenter.destroy();
});

test("离线或网络检测中只暴露现金支付，在线才暴露卡和券", () => {
  assert.deepEqual(getAvailableTenderMethods("offline"), ["cash"]);
  assert.deepEqual(getAvailableTenderMethods("checking"), ["cash"]);
  assert.deepEqual(getAvailableTenderMethods("online"), [
    "cash",
    "card",
    "voucher",
  ]);
});

test("现金输入严格按整数分币解析，并以 AUD 0.05 规则判断找零", () => {
  assert.equal(parseCashInput("$1,234.50"), 123_450);
  assert.equal(parseCashInput("10.005"), null);
  assert.equal(parseCashInput("-1.00"), null);

  assert.deepEqual(deriveCashDraft(saleCart(998), "10.00"), {
    cashDueCents: 1_000,
    cashTenderedCents: 1_000,
    normalizedTenderedCents: 1_000,
    changeCents: 0,
    valid: true,
    errorCode: null,
  });
  assert.equal(deriveCashDraft(saleCart(1_003), "10.00").errorCode, "cash-insufficient");
});

test("重复点击现金确认共享同一个 Promise，底层只提交一次", async () => {
  let completeCalls = 0;
  let resolveCompletion:
    | ((result: SalesCashCompletion) => void)
    | undefined;
  const completion = new Promise<SalesCashCompletion>((resolve) => {
    resolveCompletion = resolve;
  });
  const { presenter } = createPresenter({
    workflow: createWorkflow(() => {
      completeCalls += 1;
      return completion;
    }),
  });

  assert.equal(presenter.openCash(), true);
  presenter.setCashTenderedText("10.00");
  const first = presenter.submitCash();
  const duplicate = presenter.submitCash();

  assert.equal(first, duplicate);
  assert.equal(completeCalls, 1);
  assert.equal(presenter.getState().phase, "submitting-cash");

  resolveCompletion?.({
    completed: true,
    canClearCart: true,
    orderGuid: "order-duplicate-safe",
    cashDueCents: 995,
    changeCents: 5,
    postCommit: { drawerDisposition: "queued" },
  });
  assert.equal(await first, true);

  presenter.destroy();
});

test("只有交易提交成功后才发出清空购物车信号并进入成功页", async () => {
  const events: string[] = [];
  const cart = new MemoryCartPort(saleCart());
  const originalClear = cart.clearAfterCommittedOrder.bind(cart);
  cart.clearAfterCommittedOrder = async (orderGuid) => {
    events.push(`clear:${orderGuid}`);
    await originalClear(orderGuid);
  };
  const { presenter } = createPresenter({
    cart,
    workflow: createWorkflow(async () => {
      events.push("committed");
      return {
        completed: true,
        canClearCart: true,
        orderGuid: "order-committed",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "permission-denied" },
      };
    }),
  });

  presenter.openCash();
  presenter.setCashTenderedText("10");
  assert.equal(await presenter.submitCash(), true);

  assert.deepEqual(events, ["committed", "clear:order-committed"]);
  assert.deepEqual(cart.clearSignals, ["order-committed"]);
  assert.equal(presenter.getState().phase, "success");
  assert.equal(presenter.getState().cart.lines.length, 0);
  assert.equal(presenter.getState().success?.clearCartSignalled, true);
  assert.equal(
    presenter.getState().success?.drawerDisposition,
    "permission-denied",
  );

  presenter.destroy();
});

test("订单已提交但清空信号失败时仍显示成功，并阻止开始下一单", async () => {
  const cart = new MemoryCartPort(saleCart());
  cart.clearAfterCommittedOrder = async () => {
    throw new Error("cart adapter failed");
  };
  const { presenter } = createPresenter({ cart });

  presenter.openCash();
  presenter.setExactCash();
  assert.equal(await presenter.submitCash(), true);

  assert.equal(presenter.getState().phase, "success");
  assert.equal(presenter.getState().errorCode, "cart-clear-failed");
  assert.equal(presenter.getState().success?.clearCartSignalled, false);
  assert.equal(presenter.startNewSale(), false);
  assert.equal(presenter.getState().phase, "success");

  presenter.destroy();
});

test("更新门禁关闭后禁止空车加入商品和开始下一单，但不改变已提交订单结果", async () => {
  let canStartNewTransaction = false;
  let addCalls = 0;
  const cart = new MemoryCartPort(EMPTY_SALE_CART);
  const { presenter } = createPresenter({
    cart,
    canStartNewTransaction: () => canStartNewTransaction,
    workflow: {
      ...createWorkflow(async () => ({
        completed: true,
        canClearCart: true,
        orderGuid: "order-gated",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "queued" },
      })),
      async addByLookupCode() {
        addCalls += 1;
      },
    },
  });

  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), false);
  assert.equal(addCalls, 0);
  assert.equal(
    presenter.getState().errorCode,
    "new-transactions-disabled",
  );
  presenter.destroy();

  canStartNewTransaction = true;
  const { presenter: committedPresenter } = createPresenter({
    cart: new MemoryCartPort(saleCart()),
    canStartNewTransaction: () => canStartNewTransaction,
  });
  assert.equal(committedPresenter.openCash(), true);
  committedPresenter.setExactCash();
  assert.equal(await committedPresenter.submitCash(), true);
  canStartNewTransaction = false;

  assert.equal(committedPresenter.startNewSale(), false);
  assert.equal(committedPresenter.getState().phase, "success");
  assert.equal(
    committedPresenter.getState().errorCode,
    "new-transactions-disabled",
  );

  committedPresenter.destroy();
});

test("OPENITEM 只接受正整数分币，并透传给独立商品工作流", async () => {
  const openItemCalls: number[] = [];
  const { presenter } = createPresenter({
    workflow: {
      ...createWorkflow(async () => ({
        completed: true,
        canClearCart: true,
        orderGuid: "order-open-item",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "queued" },
      })),
      async addOpenItem(unitPriceCents) {
        openItemCalls.push(unitPriceCents);
      },
    },
  });

  assert.equal(await presenter.addOpenItem(0), false);
  assert.equal(presenter.getState().errorCode, "invalid-price");
  assert.equal(await presenter.addOpenItem(1_234), true);
  assert.deepEqual(openItemCalls, [1_234]);

  presenter.destroy();
});

test("数量、价格和行/整单折扣严格验证后透传对应 Port", async () => {
  const cart = new MemoryCartPort(saleCart(2_000));
  const { presenter } = createPresenter({ cart });

  assert.equal(await presenter.setLineQuantity("line-1", 0), false);
  assert.equal(presenter.getState().errorCode, "invalid-quantity");
  assert.equal(await presenter.setLineQuantity("line-1", 3), true);
  assert.equal(await presenter.setLineUnitPriceCents("line-1", 0), true);
  assert.equal(
    await presenter.applyLineDiscountAmountCents("line-1", 250),
    true,
  );
  assert.equal(
    await presenter.applyLineManualDiscountBasisPoints("line-1", 1_250),
    true,
  );
  assert.equal(await presenter.applyOrderDiscountAmountCents(400), true);
  assert.equal(
    await presenter.applyOrderManualDiscountBasisPoints(2_500),
    true,
  );
  assert.equal(await presenter.applyOrderQuickDiscount(3_000), true);

  assert.deepEqual(cart.mutations, [
    { operation: "line-quantity", lineId: "line-1", value: 3 },
    { operation: "line-price", lineId: "line-1", value: 0 },
    {
      operation: "line-discount-amount",
      lineId: "line-1",
      value: 250,
    },
    {
      operation: "line-discount-percent",
      lineId: "line-1",
      value: 1_250,
    },
    { operation: "order-discount-amount", value: 400 },
    { operation: "order-discount-percent", value: 2_500 },
    { operation: "order-discount-quick", value: 3_000 },
  ]);

  presenter.destroy();
});

test("手动清空购物车走授权 Port；授权拒绝统一显示主管授权错误", async () => {
  const cart = new MemoryCartPort(saleCart());
  const { presenter } = createPresenter({ cart });

  assert.equal(await presenter.clearCart(), true);
  assert.deepEqual(cart.mutations, [{ operation: "clear-cart" }]);

  cart.clearCart = async () => {
    throw Object.assign(new Error("denied"), {
      code: "SALES_OPERATION_NOT_AUTHORIZED",
    });
  };
  assert.equal(await presenter.clearCart(), false);
  assert.equal(presenter.getState().errorCode, "authorization-denied");

  presenter.destroy();
});

test("所有交互组件共享至少 44pt 的触控基线", () => {
  assert.equal(MIN_TOUCH_TARGET, 44);
});
