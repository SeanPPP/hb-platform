import assert from "node:assert/strict";
import test from "node:test";

import {
  ACTIVE_PRICING_CART_BUSY,
  ACTIVE_PRICING_CART_STALE_SNAPSHOT,
  ActivePricingCartSession,
} from "./active-pricing-cart-session";
import {
  createConnectedSalesDependencies,
  createConnectedSalesPresenter,
  SALES_NEW_TRANSACTIONS_DISABLED,
  PricingCartSalesAdapter,
  type DurableCashCheckoutPort,
  type LocalCatalogPort,
} from "./connected-sales-runtime";
import {
  AuthorizedSalesOperationExecutor,
  type SalesOperationSecurity,
} from "./sales-operation-security";

import type { LocalCatalogMatch } from "@/core/db/catalog-repository";
import { PricingCart } from "@/features/sales/domain";

const item = (overrides: Partial<LocalCatalogMatch> = {}): LocalCatalogMatch => ({
  storeCode: "S1", productCode: "P-TEA", referenceCode: null, itemNumber: "100", displayName: "Tea", barcode: "930000000001", lookupCode: "930000000001", lookupCodeNormalized: "930000000001", retailPriceCents: 500, priceSource: 0, priceSourceLabel: "Retail", quantityFactor: 1, taxRateBasisPoints: 1_000, updatedAtIso: null, rowVersion: "1", productImage: null, discountRate: null, isSpecialProduct: false,
  ...overrides,
});

class Catalog implements LocalCatalogPort {
  public constructor(private readonly values: readonly LocalCatalogMatch[]) {}
  public async findExact(code: string): Promise<LocalCatalogMatch | null> {
    const normalized = code.trim().toUpperCase();
    return this.values.find((value) => value.lookupCodeNormalized === normalized) ?? null;
  }
  public async searchByName(query: string): Promise<readonly LocalCatalogMatch[]> {
    const normalized = query.trim().toLowerCase();
    return this.values.filter((value) => value.displayName.toLowerCase().includes(normalized));
  }
}

class CashCheckout implements DurableCashCheckoutPort {
  public readonly calls: unknown[] = [];
  public fail = false;
  public async complete(input: Parameters<DurableCashCheckoutPort["complete"]>[0]) {
    this.calls.push(input);
    if (this.fail) throw new Error("disk full");
    return { completed: true as const, canClearCart: true as const, orderGuid: "order-1", cashDueCents: 500, changeCents: 0, postCommit: { requestDrawer: true, drawerDisposition: "queued" as const, printPolicy: "automatic" as const } };
  }
}

test("目录搜索和 HID 精确扫码只使用本地目录，并以稳定 product/lookup 身份加入定价购物车", async () => {
  const dependencies = connected({
    catalog: new Catalog([item({ quantityFactor: 6 })]),
  });

  const results = await dependencies.workflow.searchProducts("tea");
  await dependencies.workflow.addByLookupCode("930000000001");

  assert.deepEqual(results, [{ productCode: "P-TEA", itemNumber: "100", lookupCode: "930000000001", displayName: "Tea", unitPriceCents: 500 }]);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.productCode, "P-TEA");
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.lookupCode, "930000000001");
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.quantity, "6");
  await assert.rejects(
    () => dependencies.workflow.addProduct({ ...results[0]!, productCode: "forged" }),
    /identity/i,
  );
});

test("更新策略未启用时空车在目录查询前 fail-closed，已有购物车仍可继续当前交易", async () => {
  let exactCalls = 0;
  const sharedCart = activeCart();
  const catalog: LocalCatalogPort = {
    async findExact() {
      exactCalls += 1;
      return item();
    },
    async searchByName() {
      return [];
    },
  };
  const gated = connected({
    activeCartSession: sharedCart,
    catalog,
    newTransactionGate: {
      canStartNewTransaction: () => false,
    },
  });

  await assert.rejects(
    () => gated.workflow.addByLookupCode("930000000001"),
    hasCode(SALES_NEW_TRANSACTIONS_DISABLED),
  );
  assert.equal(exactCalls, 0);
  assert.equal(sharedCart.getSnapshot().lines.length, 0);

  const existingCart = activeCart(cartWithLine());
  const continuing = connected({
    activeCartSession: existingCart,
    catalog,
    createLineId: () => "line-2",
    newTransactionGate: {
      canStartNewTransaction: () => false,
    },
  });
  await continuing.workflow.addByLookupCode("930000000001");

  assert.equal(exactCalls, 1);
  assert.equal(existingCart.getSnapshot().lines.length, 2);
});

test("目录的 referenceCode 与全部后端 priceSource 0..4 原样冻结到购物车", () => {
  for (const priceSource of [0, 1, 2, 3, 4] as const) {
    const adapter = new PricingCartSalesAdapter(
      activeCart(),
      new SessionGuard(),
      operations(new SessionGuard()),
    );
    const referenceCode = `REF-${priceSource}`;

    adapter.addCatalogItem(
      item({
        lookupCode: `LOOKUP-${priceSource}`,
        lookupCodeNormalized: `LOOKUP-${priceSource}`,
        referenceCode,
        priceSource,
      }),
      `line-${priceSource}`,
    );

    assert.deepEqual(adapter.getSnapshot().lines[0]?.syncProvenance, {
      referenceCode,
      priceSource,
    });
    adapter.destroy();
  }
});

test("PricingCart 适配器发布增减和折扣后的快照，并且只有明确订单号才能重建空车", async () => {
  const adapter = new PricingCartSalesAdapter(
    activeCart(),
    new SessionGuard(),
    operations(new SessionGuard()),
  );
  let notifications = 0;
  adapter.subscribe(() => { notifications += 1; });
  const cart = new PricingCart();
  cart.addItem({
    lineId: "line-1",
    productCode: "P1",
    itemNumber: null,
    lookupCode: "1",
    displayName: "Tea",
    unitPrice: { currency: "AUD", cents: 500 },
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  const populated = new PricingCartSalesAdapter(
    activeCart(cart),
    new SessionGuard(),
    operations(new SessionGuard()),
  );
  populated.subscribe(() => { notifications += 1; });

  await populated.increaseLine("line-1");
  await populated.applyLineDiscountBasisPoints("line-1", 1_000);
  assert.equal(populated.getSnapshot().lines[0]?.actualAmount.cents, 900);
  await assert.rejects(() => populated.clearAfterCommittedOrder(" "), /order guid/i);
  await populated.clearAfterCommittedOrder("order-1");
  assert.equal(populated.getSnapshot().lines.length, 0);
  assert.equal(notifications, 3);
  adapter.destroy();
  populated.destroy();
});

test("现金确认透传同一 checkoutIntent，并强制使用安全注入的门店、设备和收银员身份", async () => {
  const checkout = new CashCheckout();
  const dependencies = connected({ cashCheckout: checkout });
  const confirmedCart = dependencies.cart.getSnapshot();
  await dependencies.workflow.completeCash({ checkoutIntentId: "intent-1", cart: confirmedCart, cashTenderedCents: 500 });

  assert.deepEqual(checkout.calls[0], {
    checkoutIntentId: "intent-1",
    cart: confirmedCart,
    cashTenderedCents: 500,
    storeCode: "S1",
    deviceCode: "IPAD1",
    cashierId: "C1",
    cashierName: "Alice",
  });
});

test("现金失败不清空购物车；Presenter 只在成功 result 后调用 adapter clear", async () => {
  const checkout = new CashCheckout();
  checkout.fail = true;
  const dependencies = connected({ cashCheckout: checkout });
  await dependencies.workflow.addByLookupCode("930000000001");

  await assert.rejects(() => dependencies.workflow.completeCash({ checkoutIntentId: "intent-fail", cart: dependencies.cart.getSnapshot(), cashTenderedCents: 500 }), /disk full/i);
  assert.equal(dependencies.cart.getSnapshot().lines.length, 1);
});

test("现金 durable commit 持有 exclusive lease；并发 Presenter 不能插入商品，成功后在锁内清车", async () => {
  const completion =
    deferred<Awaited<ReturnType<DurableCashCheckoutPort["complete"]>>>();
  const calls: unknown[] = [];
  const checkout: DurableCashCheckoutPort = {
    complete(input) {
      calls.push(input);
      return completion.promise;
    },
  };
  const dependencies = connected({ cashCheckout: checkout });
  await dependencies.workflow.addByLookupCode("930000000001");
  const confirmedCart = dependencies.cart.getSnapshot();
  const pending = dependencies.workflow.completeCash({
    checkoutIntentId: "intent-exclusive",
    cart: confirmedCart,
    cashTenderedCents: 500,
  });

  await assert.rejects(
    () => dependencies.cart.increaseLine("line-1"),
    hasCode(ACTIVE_PRICING_CART_BUSY),
  );
  completion.resolve(cashResult("order-exclusive"));
  await pending;

  assert.equal(calls.length, 1);
  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
});

test("现金确认快照已被另一个 Presenter 更新时 fail-closed，不能提交旧车", async () => {
  const checkout = new CashCheckout();
  const dependencies = connected({ cashCheckout: checkout });
  await dependencies.workflow.addByLookupCode("930000000001");
  const staleCart = dependencies.cart.getSnapshot();
  await dependencies.workflow.addByLookupCode("930000000001");

  await assert.rejects(
    () =>
      dependencies.workflow.completeCash({
        checkoutIntentId: "intent-stale",
        cart: staleCart,
        cashTenderedCents: 1_000,
      }),
    hasCode(ACTIVE_PRICING_CART_STALE_SNAPSHOT),
  );
  assert.equal(checkout.calls.length, 0);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.quantity, "2");
});

test("缺少目录、现金、hold 或 lock Port 时 capability 明确禁用，不能伪造成功", async () => {
  const dependencies = connected({ catalog: undefined, cashCheckout: undefined });
  const presenter = createConnectedSalesPresenter({
    activeCartSession: activeCart(),
    catalog: undefined,
    cashCheckout: undefined,
    identity: identity(),
    sessionGuard: new SessionGuard(),
    newTransactionGate: {
      canStartNewTransaction: () => true,
    },
    createCheckoutIntentId: () => "intent-disabled",
    createLineId: () => "line-disabled",
    operationSecurity: security(),
  });

  assert.deepEqual(dependencies.capabilities, { catalog: false, cartEditing: true, cashCheckout: false, hold: false, lock: false });
  await assert.rejects(() => dependencies.workflow.holdCart(dependencies.cart.getSnapshot()), /unavailable/i);
  await assert.rejects(() => dependencies.workflow.lockTerminal(), /unavailable/i);
  assert.equal(presenter.getState().capabilities.cashCheckout, false);
});

test("401、403、锁屏或换收银员后，旧 presenter 不能再增减、删除或修改折扣，新 presenter 正常", async () => {
  const sharedCart = activeCart(cartWithLine());
  const oldSession = new SessionGuard();
  const oldDependencies = connected({
    activeCartSession: sharedCart,
    sessionGuard: oldSession,
  });
  oldSession.invalidate();
  const before = sharedCart.getSnapshot();

  await assert.rejects(
    () => oldDependencies.cart.increaseLine("line-1"),
    /cashier session inactive/i,
  );
  await assert.rejects(
    () => oldDependencies.cart.decreaseLine("line-1"),
    /cashier session inactive/i,
  );
  await assert.rejects(
    () => oldDependencies.cart.removeLine("line-1"),
    /cashier session inactive/i,
  );
  await assert.rejects(
    () => oldDependencies.cart.applyLineDiscountBasisPoints("line-1", 1_000),
    /cashier session inactive/i,
  );
  assert.equal(sharedCart.getSnapshot(), before);

  const currentDependencies = connected({
    activeCartSession: sharedCart,
    sessionGuard: new SessionGuard(),
  });
  await currentDependencies.cart.increaseLine("line-1");
  await currentDependencies.cart.applyLineDiscountBasisPoints("line-1", 1_000);
  assert.equal(sharedCart.getSnapshot().lines[0]?.quantity, "2");
  assert.equal(sharedCart.getSnapshot().lines[0]?.discount.cents, 100);
});

test("findExact 延迟返回前切换收银员，旧 workflow 不能把商品注入共享购物车", async () => {
  const exact = deferred<LocalCatalogMatch | null>();
  const sharedCart = activeCart();
  const oldSession = new SessionGuard();
  const oldDependencies = connected({
    activeCartSession: sharedCart,
    catalog: {
      findExact: () => exact.promise,
      searchByName: async () => [],
    },
    sessionGuard: oldSession,
  });
  const pending = oldDependencies.workflow.addByLookupCode("930000000001");

  oldSession.invalidate();
  const currentDependencies = connected({
    activeCartSession: sharedCart,
    sessionGuard: new SessionGuard(),
  });
  exact.resolve(item());

  await assert.rejects(pending, /cashier session inactive/i);
  assert.equal(sharedCart.getSnapshot().lines.length, 0);
  await currentDependencies.workflow.addByLookupCode("930000000001");
  assert.equal(sharedCart.getSnapshot().lines.length, 1);
});

test("searchByName 延迟返回前会话失效，旧 workflow 不能返回新收银员上下文中的结果", async () => {
  const search = deferred<readonly LocalCatalogMatch[]>();
  const sessionGuard = new SessionGuard();
  const dependencies = connected({
    catalog: {
      findExact: async () => null,
      searchByName: () => search.promise,
    },
    sessionGuard,
  });
  const pending = dependencies.workflow.searchProducts("tea");

  sessionGuard.invalidate();
  search.resolve([item()]);

  await assert.rejects(pending, /cashier session inactive/i);
});

test("旧 adapter 失效后拒绝读取，且不再把新收银员的购物车更新推给旧 presenter", async () => {
  const sharedCart = activeCart(cartWithLine());
  const oldSession = new SessionGuard();
  const oldAdapter = new PricingCartSalesAdapter(
    sharedCart,
    oldSession,
    operations(oldSession),
  );
  const currentAdapter = new PricingCartSalesAdapter(
    sharedCart,
    new SessionGuard(),
    operations(new SessionGuard()),
  );
  let staleNotifications = 0;
  oldAdapter.subscribe(() => {
    staleNotifications += 1;
  });
  oldSession.invalidate();

  assert.throws(
    () => oldAdapter.getSnapshot(),
    /cashier session inactive/i,
  );
  await currentAdapter.increaseLine("line-1");
  assert.equal(staleNotifications, 0);
  assert.equal(currentAdapter.getSnapshot().lines[0]?.quantity, "2");

  oldAdapter.destroy();
  currentAdapter.destroy();
});

test("成功锁屏使当前 lease 失效时，workflow 仍向 presenter 返回真实成功", async () => {
  const sessionGuard = new SessionGuard();
  const presenter = createConnectedSalesPresenter({
    activeCartSession: activeCart(),
    catalog: new Catalog([item()]),
    identity: identity(),
    lock: {
      async lock() {
        sessionGuard.invalidate();
      },
    },
    sessionGuard,
    newTransactionGate: {
      canStartNewTransaction: () => true,
    },
    createCheckoutIntentId: () => "intent-lock",
    createLineId: () => "line-lock",
    operationSecurity: security(),
  });

  assert.equal(await presenter.lockTerminal(), true);
  assert.equal(presenter.getState().phase, "locked");
  assert.equal(presenter.getState().errorCode, null);
  presenter.destroy();
});

test("旧 presenter 提交现金时不会同步抛异常，而是以 rejected Promise fail-closed", async () => {
  const sessionGuard = new SessionGuard();
  const dependencies = connected({ sessionGuard });
  const cart = dependencies.cart.getSnapshot();
  sessionGuard.invalidate();
  let completion:
    | ReturnType<typeof dependencies.workflow.completeCash>
    | undefined;

  assert.doesNotThrow(() => {
    completion = dependencies.workflow.completeCash({
      checkoutIntentId: "intent-invalid-session",
      cart,
      cashTenderedCents: 500,
    });
  });
  assert.ok(completion);
  await assert.rejects(
    completion,
    /cashier session inactive/i,
  );
});

function connected(overrides: Partial<Parameters<typeof createConnectedSalesDependencies>[0]> = {}) {
  return createConnectedSalesDependencies({
    activeCartSession: activeCart(),
    catalog: new Catalog([item()]),
    cashCheckout: new CashCheckout(),
    identity: identity(),
    sessionGuard: new SessionGuard(),
    newTransactionGate: {
      canStartNewTransaction: () => true,
    },
    createCheckoutIntentId: () => "intent-default",
    createLineId: (() => { let value = 0; return () => `line-${++value}`; })(),
    operationSecurity: security(),
    ...overrides,
  });
}

function activeCart(initial = new PricingCart()) {
  return new ActivePricingCartSession(initial, () => new PricingCart());
}

function cashResult(orderGuid: string) {
  return {
    completed: true as const,
    canClearCart: true as const,
    orderGuid,
    cashDueCents: 500,
    changeCents: 0,
    postCommit: {
      requestDrawer: true,
      drawerDisposition: "queued" as const,
      printPolicy: "automatic" as const,
    },
  };
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function hasCode(code: string): (error: unknown) => boolean {
  return (error) =>
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    error.code === code;
}

function identity() {
  return { storeCode: "S1", deviceCode: "IPAD1", cashierId: "C1", cashierName: "Alice" };
}

function cartWithLine(): PricingCart {
  const cart = new PricingCart();
  cart.addItem({
    lineId: "line-1",
    productCode: "P1",
    itemNumber: null,
    lookupCode: "1",
    displayName: "Tea",
    unitPrice: { currency: "AUD", cents: 500 },
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  return cart;
}

class SessionGuard {
  private active = true;

  public assertActive(): void {
    if (!this.active) {
      throw new Error("Cashier session inactive.");
    }
  }

  public invalidate(): void {
    this.active = false;
  }
}

function operations(
  guard: SessionGuard,
): AuthorizedSalesOperationExecutor {
  return new AuthorizedSalesOperationExecutor(
    security(),
    { cashierId: "C1" },
    guard,
  );
}

function security(): SalesOperationSecurity {
  let nextId = 0;
  return {
    authorization: {
      async authorizeAndRun(input, operation) {
        return {
          authorized: true,
          value: await operation({
            authorizationMode: "current-cashier",
            requestingCashierId: "C1",
            authorizingCashierId: null,
            permissionCode: input.permissionCode,
          }),
        };
      },
    },
    audit: { append: async () => undefined },
    createActionId: () => uuid(++nextId),
    createAuditEventId: () => uuid(++nextId),
    nowIso: () => "2026-07-29T00:00:00.000Z",
  };
}

function uuid(value: number): string {
  return `00000000-0000-4000-8000-${String(value).padStart(12, "0")}`;
}
