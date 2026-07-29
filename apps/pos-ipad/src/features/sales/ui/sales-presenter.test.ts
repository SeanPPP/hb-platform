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
    getPendingCatalogWorkCount: () => 0,
    subscribePendingCatalogWork: () => () => undefined,
    async settlePendingCatalogWork() {
      return { timedOut: false };
    },
    disposePendingCatalogWork() {},
    releasePreparedCheckout() {},
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

test("空购物车禁止进入现金结账，未接入能力不会伪造成功", async () => {
  const { presenter } = createPresenter({
    cart: new MemoryCartPort(EMPTY_SALE_CART),
  });

  assert.equal(await presenter.openCash(), false);
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
  assert.equal(await presenter.openCash(), false);
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

  assert.equal(await presenter.openCash(), true);
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

  await presenter.openCash();
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

  await presenter.openCash();
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
  assert.equal(await committedPresenter.openCash(), true);
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

test("扫码提交立即清空输入，连续扫码不等待前一次在线查询", async () => {
  const lookups: string[] = [];
  const completions: (() => void)[] = [];
  const { presenter } = createPresenter({
    workflow: {
      ...createWorkflow(async () => ({
        completed: true,
        canClearCart: true,
        orderGuid: "order-scan",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "queued" },
      })),
      addByLookupCode(lookupCode) {
        lookups.push(lookupCode);
        return new Promise<void>((resolve) => {
          completions.push(resolve);
        });
      },
    },
  });

  presenter.setQuery("930000000001");
  const first = presenter.addLookupCode();
  assert.equal(presenter.getState().query, "");

  presenter.setQuery("930000000002");
  const second = presenter.addLookupCode();
  assert.equal(presenter.getState().query, "");
  assert.deepEqual(lookups, ["930000000001", "930000000002"]);

  completions.forEach((complete) => complete());
  assert.equal(await first, true);
  assert.equal(await second, true);
  presenter.destroy();
});

test("扫码后台未找到或网络失败不弹出噪声错误", async () => {
  const { presenter } = createPresenter({
    workflow: {
      ...createWorkflow(async () => ({
        completed: true,
        canClearCart: true,
        orderGuid: "order-scan-failure",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "queued" },
      })),
      async addByLookupCode() {
        throw new Error("remote lookup unavailable");
      },
    },
  });

  presenter.setQuery("930000000099");
  assert.equal(await presenter.addLookupCode(), false);
  assert.equal(presenter.getState().query, "");
  assert.equal(presenter.getState().errorCode, null);
  presenter.destroy();
});

test("Presenter 订阅目录核验数量并在销毁时解除订阅", () => {
  let pendingCount = 2;
  const listeners = new Set<() => void>();
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-pending",
      cashDueCents: 995,
      changeCents: 5,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    getPendingCatalogWorkCount: () => pendingCount,
    subscribePendingCatalogWork(listener: () => void) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    async settlePendingCatalogWork() {
      return { timedOut: false };
    },
  };
  const { presenter } = createPresenter({ workflow });
  const pendingFromState = () =>
    (presenter.getState() as unknown as { pendingLookupCount?: number })
      .pendingLookupCount;

  assert.equal(pendingFromState(), 2);
  pendingCount = 1;
  listeners.forEach((listener) => listener());
  assert.equal(pendingFromState(), 1);

  presenter.destroy();
  assert.equal(listeners.size, 0);
});

test("Presenter 销毁时先 fence 待处理目录任务，再解除 pending 订阅", () => {
  const events: string[] = [];
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-dispose",
      cashDueCents: 995,
      changeCents: 5,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    subscribePendingCatalogWork: () => () => {
      events.push("unsubscribe-pending");
    },
    disposePendingCatalogWork() {
      events.push("dispose");
    },
    releasePreparedCheckout() {},
  };
  const { presenter } = createPresenter({ workflow });

  presenter.destroy();
  assert.deepEqual(events, ["dispose", "unsubscribe-pending"]);
});

test("Presenter 销毁后丢弃迟到的在线结账准备结果", async () => {
  let resolveSettlement:
    | ((result: Readonly<{ timedOut: boolean }>) => void)
    | undefined;
  const settlement = new Promise<Readonly<{ timedOut: boolean }>>((resolve) => {
    resolveSettlement = resolve;
  });
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-destroyed-prepare",
      cashDueCents: 995,
      changeCents: 5,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    settlePendingCatalogWork: () => settlement,
  };
  const { presenter } = createPresenter({ workflow });

  const preparation = presenter.prepareOnlineCheckout();
  presenter.destroy();
  resolveSettlement?.({ timedOut: false });

  assert.equal(await preparation, null);
});

test("空车但存在待处理扫码时仍等待核验，远程命中自动加购后进入现金结账", async () => {
  let pendingCount = 1;
  let settleCalls = 0;
  const cart = new MemoryCartPort(EMPTY_SALE_CART);
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-pending-miss",
      cashDueCents: 725,
      changeCents: 0,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    getPendingCatalogWorkCount: () => pendingCount,
    subscribePendingCatalogWork: () => () => undefined,
    async settlePendingCatalogWork() {
      settleCalls += 1;
      cart.snapshot = {
        ...saleCart(725),
        revision: 1,
      };
      pendingCount = 0;
      return { timedOut: false };
    },
  };
  const { presenter } = createPresenter({ cart, workflow });

  const opening = presenter.openCash();
  assert.equal(presenter.getState().phase, "verifying-checkout");
  assert.equal(await opening, true);
  assert.equal(settleCalls, 1);
  assert.equal(presenter.getState().phase, "cash");
  assert.equal(presenter.getState().cart.actualAmount.cents, 725);
  presenter.destroy();
});

test("目录核验与在线准备期间拒绝行编辑、清车、挂单和重复准备", async () => {
  let resolveSettlement:
    | ((result: Readonly<{ timedOut: boolean }>) => void)
    | undefined;
  const settlement = new Promise<Readonly<{ timedOut: boolean }>>((resolve) => {
    resolveSettlement = resolve;
  });
  let holdCalls = 0;
  let releaseCalls = 0;
  const cart = new MemoryCartPort(saleCart());
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-frozen",
      cashDueCents: 995,
      changeCents: 5,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    getPendingCatalogWorkCount: () => 1,
    subscribePendingCatalogWork: () => () => undefined,
    settlePendingCatalogWork: () => settlement,
    disposePendingCatalogWork() {},
    releasePreparedCheckout() {
      releaseCalls += 1;
    },
    async holdCart() {
      holdCalls += 1;
    },
  };
  const { presenter } = createPresenter({ cart, workflow });

  const preparation = presenter.prepareOnlineCheckout();
  assert.equal(presenter.getState().phase, "verifying-checkout");
  assert.equal(await presenter.setLineQuantity("line-1", 2), false);
  assert.equal(await presenter.clearCart(), false);
  assert.equal(await presenter.holdCart(), false);
  assert.deepEqual(cart.mutations, []);
  assert.equal(holdCalls, 0);

  resolveSettlement?.({ timedOut: false });
  const prepared = await preparation;
  assert.equal(prepared?.revision, 1);
  assert.equal(presenter.getState().phase, "verifying-checkout");
  assert.equal(await presenter.prepareOnlineCheckout(), null);
  assert.equal(await presenter.setLineQuantity("line-1", 3), false);
  assert.deepEqual(cart.mutations, []);
  assert.equal(releaseCalls, 0);
  presenter.destroy();
});

test("现金取消释放 prepared checkout 后才恢复销售态", async () => {
  let releaseCalls = 0;
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-cash-cancel",
      cashDueCents: 995,
      changeCents: 5,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    disposePendingCatalogWork() {},
    releasePreparedCheckout() {
      releaseCalls += 1;
    },
  };
  const { presenter } = createPresenter({ workflow });

  assert.equal(await presenter.openCash(), true);
  assert.equal(presenter.getState().phase, "cash");
  assert.equal(presenter.closeCash(), true);
  assert.equal(releaseCalls, 1);
  assert.equal(presenter.getState().phase, "selling");
  presenter.destroy();
});

test("现金结账等待目录核验、阻止重复结账，并使用最新购物车金额", async () => {
  let resolveSettlement:
    | ((result: Readonly<{ timedOut: boolean }>) => void)
    | undefined;
  const settlement = new Promise<Readonly<{ timedOut: boolean }>>((resolve) => {
    resolveSettlement = resolve;
  });
  const settleCalls: number[] = [];
  const cart = new MemoryCartPort(saleCart(995));
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-prepare-cash",
      cashDueCents: 1_250,
      changeCents: 0,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    getPendingCatalogWorkCount: () => 1,
    subscribePendingCatalogWork: () => () => undefined,
    settlePendingCatalogWork(input: Readonly<{ timeoutMs: number }>) {
      settleCalls.push(input.timeoutMs);
      return settlement;
    },
  };
  const { presenter } = createPresenter({ cart, workflow });

  const opening = Promise.resolve(presenter.openCash());
  assert.equal(presenter.getState().phase, "verifying-checkout");
  await Promise.resolve();
  assert.deepEqual(settleCalls, [2_000]);
  assert.equal(await Promise.resolve(presenter.openCash()), false);

  cart.snapshot = {
    ...saleCart(1_250),
    revision: 2,
  };
  resolveSettlement?.({ timedOut: false });
  assert.equal(await opening, true);
  assert.equal(presenter.getState().phase, "cash");
  assert.equal(presenter.getState().cart.revision, 2);
  assert.equal(presenter.getState().cart.actualAmount.cents, 1_250);
  assert.equal(presenter.getState().cashTenderedText, "");
  presenter.destroy();
});

test("在线支付准备与现金共用核验门禁，超时或失败仍按最新本地金额继续", async () => {
  const cart = new MemoryCartPort(saleCart(995));
  const workflow = {
    ...createWorkflow(async () => ({
      completed: true as const,
      canClearCart: true as const,
      orderGuid: "order-prepare-online",
      cashDueCents: 1_500,
      changeCents: 0,
      postCommit: { drawerDisposition: "queued" as const },
    })),
    getPendingCatalogWorkCount: () => 1,
    subscribePendingCatalogWork: () => () => undefined,
    async settlePendingCatalogWork() {
      cart.snapshot = {
        ...saleCart(1_500),
        revision: 3,
      };
      throw new Error("timeout fence established");
    },
  };
  const { presenter } = createPresenter({ cart, workflow });
  const prepareOnlineCheckout = (
    presenter as unknown as {
      prepareOnlineCheckout(): Promise<CartSnapshot | null>;
    }
  ).prepareOnlineCheckout;

  const prepared = await prepareOnlineCheckout.call(presenter);
  assert.equal(prepared?.revision, 3);
  assert.equal(prepared?.actualAmount.cents, 1_500);
  assert.equal(presenter.getState().phase, "verifying-checkout");
  presenter.destroy();
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
