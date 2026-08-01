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
  SALES_CHECKOUT_PREPARED,
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
import type {
  CatalogLookupRevalidationPort,
  CatalogLookupRevalidationResult,
} from "@/features/catalog/catalog-lookup-revalidation";
import { PricingCart } from "@/features/sales/domain";
import type { SalesFeedbackEvent } from "@/features/sales/ui/sales-presenter";

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

test("每次商品加购只发布一个权威 outcome，并由领域 disposition 区分新增和增量", async () => {
  const dependencies = connected({
    catalog: new Catalog([
      item(),
      item({
        productCode: "OPENITEM",
        itemNumber: null,
        lookupCode: "OPENITEM",
        lookupCodeNormalized: "OPENITEM",
      }),
    ]),
  });
  const outcomes: SalesFeedbackEvent[] = [];
  dependencies.workflow.subscribeLookupOutcome?.((outcome) => {
    outcomes.push(outcome);
  });

  await dependencies.workflow.addByLookupCode("930000000001", {
    source: "hid",
  });
  await dependencies.workflow.addByLookupCode("930000000001", {
    source: "hid",
  });
  await dependencies.workflow.addProduct({
    productCode: "P-TEA",
    itemNumber: "100",
    lookupCode: "930000000001",
    displayName: "Tea",
    unitPriceCents: 500,
  });
  await dependencies.workflow.addOpenItem(120);

  assert.deepEqual(
    outcomes.map(({ source, kind, lineId }) => ({ source, kind, lineId })),
    [
      { source: "hid", kind: "added", lineId: "line-1" },
      { source: "hid", kind: "incremented", lineId: "line-1" },
      { source: "manual", kind: "incremented", lineId: "line-1" },
      { source: "manual", kind: "added", lineId: "line-4" },
    ],
  );
  assert.equal(new Set(outcomes.map((outcome) => outcome.attemptId)).size, 4);
});

test("同步未找到与交易门禁分别发布 not-found 和 failed-blocked", async () => {
  const missing = connected({ catalog: new Catalog([]) });
  const missingOutcomes: SalesFeedbackEvent[] = [];
  missing.workflow.subscribeLookupOutcome?.((outcome) => {
    missingOutcomes.push(outcome);
  });

  await assert.rejects(() =>
    missing.workflow.addByLookupCode("MISSING", { source: "camera" }),
  );
  assert.deepEqual(
    missingOutcomes.map(({ source, kind }) => ({ source, kind })),
    [{ source: "camera", kind: "not-found" }],
  );

  const blocked = connected({
    catalog: new Catalog([item()]),
    newTransactionGate: { canStartNewTransaction: () => false },
  });
  const blockedOutcomes: SalesFeedbackEvent[] = [];
  blocked.workflow.subscribeLookupOutcome?.((outcome) => {
    blockedOutcomes.push(outcome);
  });
  await assert.rejects(() =>
    blocked.workflow.addByLookupCode("930000000001"),
  );
  assert.deepEqual(
    blockedOutcomes.map(({ source, kind }) => ({ source, kind })),
    [{ source: "manual", kind: "failed-blocked" }],
  );
});

test("本地 miss 等远程权威结果后再发布 added 或 not-found", async () => {
  const found = deferred<CatalogLookupRevalidationResult>();
  const foundDependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(found.promise),
    catalogWorkScheduler: immediateScheduler(),
  });
  const foundOutcomes: SalesFeedbackEvent[] = [];
  foundDependencies.workflow.subscribeLookupOutcome?.((outcome) => {
    foundOutcomes.push(outcome);
  });

  await foundDependencies.workflow.addByLookupCode("930000000001", {
    source: "hid",
  });
  assert.deepEqual(foundOutcomes, []);
  found.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item(),
  });
  await waitFor(
    () => foundDependencies.workflow.getPendingCatalogWorkCount() === 0,
  );
  assert.deepEqual(
    foundOutcomes.map(({ source, kind, lineId }) => ({ source, kind, lineId })),
    [{ source: "hid", kind: "added", lineId: "line-1" }],
  );

  const notFound = deferred<CatalogLookupRevalidationResult>();
  const missingDependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(notFound.promise),
    catalogWorkScheduler: immediateScheduler(),
  });
  const missingOutcomes: SalesFeedbackEvent[] = [];
  missingDependencies.workflow.subscribeLookupOutcome?.((outcome) => {
    missingOutcomes.push(outcome);
  });
  await missingDependencies.workflow.addByLookupCode("930000000001");
  notFound.resolve({ kind: "not-found", baseSnapshotId: "snapshot-1" });
  await waitFor(
    () => missingDependencies.workflow.getPendingCatalogWorkCount() === 0,
  );
  assert.deepEqual(missingOutcomes.map((outcome) => outcome.kind), [
    "not-found",
  ]);
});

test("远程 found 的过期目录基线以唯一 failed-blocked 完成本次扫码", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const dependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(remote.promise, false),
    catalogWorkScheduler: immediateScheduler(),
  });
  const outcomes: SalesFeedbackEvent[] = [];
  dependencies.workflow.subscribeLookupOutcome?.((outcome) => {
    outcomes.push(outcome);
  });

  await dependencies.workflow.addByLookupCode("930000000001", {
    source: "hid",
  });
  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-expired",
    item: item(),
  });
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
  assert.deepEqual(
    outcomes.map(({ source, kind }) => ({ source, kind })),
    [{ source: "hid", kind: "failed-blocked" }],
  );
  assert.equal(outcomes.length, 1);
  assert.equal(new Set(outcomes.map((outcome) => outcome.attemptId)).size, 1);
});

test("远程 not-found 的过期目录基线以唯一 failed-blocked 完成本次扫码", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const dependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(remote.promise, false),
    catalogWorkScheduler: immediateScheduler(),
  });
  const outcomes: SalesFeedbackEvent[] = [];
  dependencies.workflow.subscribeLookupOutcome?.((outcome) => {
    outcomes.push(outcome);
  });

  await dependencies.workflow.addByLookupCode("930000000001", {
    source: "camera",
  });
  remote.resolve({
    kind: "not-found",
    baseSnapshotId: "snapshot-expired",
  });
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
  assert.deepEqual(
    outcomes.map(({ source, kind }) => ({ source, kind })),
    [{ source: "camera", kind: "failed-blocked" }],
  );
  assert.equal(outcomes.length, 1);
  assert.equal(new Set(outcomes.map((outcome) => outcome.attemptId)).size, 1);
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

test("本地命中先发布购物车并释放扫码，远程查询与回写分别等待 UI yield", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const firstYield = deferred<void>();
  const applyYield = deferred<void>();
  const events: string[] = [];
  let yieldCount = 0;
  const dependencies = connected({
    catalog: {
      async findExact() {
        events.push("local");
        return item();
      },
      async searchByName() {
        return [];
      },
    },
    catalogRevalidation: {
      revalidate() {
        events.push("remote");
        return remote.promise;
      },
      async isCurrentBaseSnapshot() {
        events.push("generation-check");
        return true;
      },
    },
    catalogWorkScheduler: {
      yieldToUi() {
        yieldCount += 1;
        events.push(`yield-${yieldCount}`);
        return yieldCount === 1 ? firstYield.promise : applyYield.promise;
      },
      waitForTimeout: () => new Promise(() => undefined),
    },
  });

  await dependencies.workflow.addByLookupCode("930000000001");

  assert.deepEqual(events, ["local", "yield-1"]);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.unitPrice.cents, 500);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 1);

  firstYield.resolve();
  await Promise.resolve();
  assert.deepEqual(events, ["local", "yield-1", "remote"]);
  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item({
      itemNumber: "NEW-100",
      displayName: "Fresh tea",
      retailPriceCents: 725,
      priceSource: 1,
    }),
  });
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(events, ["local", "yield-1", "remote", "yield-2"]);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.unitPrice.cents, 500);

  applyYield.resolve();
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  assert.deepEqual(events, [
    "local",
    "yield-1",
    "remote",
    "yield-2",
    "generation-check",
  ]);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.unitPrice.cents, 725);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.displayName, "Fresh tea");
});

test("本地命中先完成 ADD 审计，再把远程校准排入后台", async () => {
  const auditRelease = deferred<void>();
  const remote = deferred<CatalogLookupRevalidationResult>();
  const events: string[] = [];
  let nextId = 0;
  const dependencies = connected({
    catalog: {
      async findExact() {
        events.push("local");
        return item();
      },
      async searchByName() {
        return [];
      },
    },
    catalogRevalidation: new SharedRevalidation(remote.promise),
    catalogWorkScheduler: {
      async yieldToUi() {
        events.push("remote-yield");
      },
      waitForTimeout: () => new Promise<void>(() => undefined),
    },
    operationSecurity: {
      authorization: {
        async authorizeAndRun(input, operation) {
          return {
            authorized: true as const,
            value: await operation({
              authorizationMode: "current-cashier",
              requestingCashierId: "C1",
              authorizingCashierId: null,
              permissionCode: input.permissionCode,
            }),
          };
        },
      },
      audit: {
        async append() {
          events.push("add-audit");
          await auditRelease.promise;
        },
      },
      createActionId: () => uuid(++nextId),
      createAuditEventId: () => uuid(++nextId),
      nowIso: () => "2026-07-29T00:00:00.000Z",
    },
  });

  const adding = dependencies.workflow.addByLookupCode("930000000001");
  await waitFor(() => events.length >= 2);

  assert.deepEqual(events, ["local", "add-audit"]);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 0);

  auditRelease.resolve();
  await adding;

  assert.deepEqual(events, ["local", "add-audit", "remote-yield"]);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 1);
  remote.resolve({ kind: "unavailable" });
  await waitFor(
    () => dependencies.workflow.getPendingCatalogWorkCount() === 0,
  );
});

test("本地命中已加购但 ADD 审计暂停时，结账返回后不得再登记远程改价", async () => {
  const auditEntered = deferred<void>();
  const auditRelease = deferred<void>();
  let remoteCalls = 0;
  let nextId = 0;
  const dependencies = connected({
    catalogRevalidation: {
      async revalidate() {
        remoteCalls += 1;
        return {
          kind: "found" as const,
          baseSnapshotId: "snapshot-1",
          item: item({ retailPriceCents: 725 }),
        };
      },
      async isCurrentBaseSnapshot() {
        return true;
      },
    },
    catalogWorkScheduler: immediateScheduler(),
    operationSecurity: {
      authorization: {
        async authorizeAndRun(input, operation) {
          return {
            authorized: true as const,
            value: await operation({
              authorizationMode: "current-cashier",
              requestingCashierId: "C1",
              authorizingCashierId: null,
              permissionCode: input.permissionCode,
            }),
          };
        },
      },
      audit: {
        async append() {
          auditEntered.resolve();
          await auditRelease.promise;
        },
      },
      createActionId: () => uuid(++nextId),
      createAuditEventId: () => uuid(++nextId),
      nowIso: () => "2026-07-29T00:00:00.000Z",
    },
  });

  const adding = dependencies.workflow.addByLookupCode("930000000001");
  await auditEntered.promise;
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.unitPrice.cents, 500);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 0);

  assert.deepEqual(
    await dependencies.workflow.settlePendingCatalogWork({
      timeoutMs: 2_000,
    }),
    { timedOut: false },
  );
  auditRelease.resolve();
  await adding;
  await new Promise<void>((resolve) => setImmediate(resolve));

  assert.equal(remoteCalls, 0);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 0);
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.unitPrice.cents, 500);
});

test("本地 miss 授权暂停跨越结账边界时，不得登记远程自动加购", async () => {
  const authorizationEntered = deferred<void>();
  const authorizationRelease = deferred<void>();
  let remoteCalls = 0;
  let nextId = 0;
  const dependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: {
      async revalidate() {
        remoteCalls += 1;
        return {
          kind: "found" as const,
          baseSnapshotId: "snapshot-1",
          item: item(),
        };
      },
      async isCurrentBaseSnapshot() {
        return true;
      },
    },
    catalogWorkScheduler: immediateScheduler(),
    operationSecurity: {
      authorization: {
        async authorizeAndRun(input, operation) {
          authorizationEntered.resolve();
          await authorizationRelease.promise;
          return {
            authorized: true as const,
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
    },
  });

  const adding = dependencies.workflow.addByLookupCode("930000000001");
  await authorizationEntered.promise;
  assert.deepEqual(
    await dependencies.workflow.settlePendingCatalogWork({
      timeoutMs: 2_000,
    }),
    { timedOut: false },
  );
  authorizationRelease.resolve();

  await assert.rejects(adding, hasCode(SALES_CHECKOUT_PREPARED));
  await new Promise<void>((resolve) => setImmediate(resolve));
  assert.equal(remoteCalls, 0);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 0);
  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
});

test("本地 miss 立即释放连续扫码；共享远程结果仍按每次扫码自动加购", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const revalidation = new SharedRevalidation(remote.promise);
  const dependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: revalidation,
    catalogWorkScheduler: immediateScheduler(),
  });

  await Promise.all([
    dependencies.workflow.addByLookupCode("930000000001"),
    dependencies.workflow.addByLookupCode(" 930000000001 "),
  ]);

  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 2);
  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item(),
  });
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  assert.equal(dependencies.cart.getSnapshot().lines[0]?.quantity, "2");
});

test("结账准备允许既有校准收敛到最终购物车，返回后仍冻结新写入", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const dependencies = connected({
    catalogRevalidation: new SharedRevalidation(remote.promise),
    catalogWorkScheduler: immediateScheduler(),
  });

  await dependencies.workflow.addByLookupCode("930000000001");
  const settlement = dependencies.workflow.settlePendingCatalogWork({
    timeoutMs: 2_000,
  });
  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item({ retailPriceCents: 725 }),
  });

  assert.deepEqual(await settlement, { timedOut: false });
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.unitPrice.cents, 725);
  await assert.rejects(
    () => dependencies.cart.increaseLine("line-1"),
    hasCode(SALES_CHECKOUT_PREPARED),
  );

  dependencies.workflow.releasePreparedCheckout();
  await dependencies.cart.increaseLine("line-1");
  assert.equal(dependencies.cart.getSnapshot().lines[0]?.quantity, "2");
});

test("结账等待超时会 fence 迟到购物车写入，但远程目录任务仍可自行完成", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const timeout = deferred<void>();
  const dependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(remote.promise),
    catalogWorkScheduler: {
      yieldToUi: async () => undefined,
      waitForTimeout: () => timeout.promise,
    },
  });
  let pendingNotifications = 0;
  dependencies.workflow.subscribePendingCatalogWork(() => {
    pendingNotifications += 1;
  });

  await dependencies.workflow.addByLookupCode("930000000001");
  const settlement = dependencies.workflow.settlePendingCatalogWork({
    timeoutMs: 2_000,
  });
  timeout.resolve();

  assert.deepEqual(await settlement, { timedOut: true });
  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item(),
  });
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
  assert.ok(pendingNotifications >= 2);
  await assert.rejects(
    () => dependencies.workflow.addByLookupCode("930000000001"),
    hasCode(SALES_CHECKOUT_PREPARED),
  );
});

test("页面销毁同步清空 pending 观察者并 fence 迟到目录结果", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const dependencies = connected({
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(remote.promise),
    catalogWorkScheduler: immediateScheduler(),
  });
  let notifications = 0;
  dependencies.workflow.subscribePendingCatalogWork(() => {
    notifications += 1;
  });

  await dependencies.workflow.addByLookupCode("930000000001");
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 1);
  dependencies.workflow.disposePendingCatalogWork();
  const notificationsAfterDispose = notifications;
  assert.equal(dependencies.workflow.getPendingCatalogWorkCount(), 0);

  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item(),
  });
  await new Promise<void>((resolve) => setImmediate(resolve));
  await new Promise<void>((resolve) => setImmediate(resolve));

  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);
  assert.equal(notifications, notificationsAfterDispose);
});

test("结账准备同步冻结延迟授权中的写入，显式释放后才允许继续销售", async () => {
  const authorizationRelease = deferred<void>();
  const authorizationEntered = deferred<void>();
  let nextId = 0;
  const dependencies = connected({
    operationSecurity: {
      authorization: {
        async authorizeAndRun(input, operation) {
          authorizationEntered.resolve();
          await authorizationRelease.promise;
          return {
            authorized: true as const,
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
    },
  });

  const delayedAdd =
    dependencies.workflow.addByLookupCode("930000000001");
  await authorizationEntered.promise;
  const settlement = dependencies.workflow.settlePendingCatalogWork({
    timeoutMs: 2_000,
  });
  authorizationRelease.resolve();

  assert.deepEqual(await settlement, { timedOut: false });
  await assert.rejects(delayedAdd, hasCode(SALES_CHECKOUT_PREPARED));
  assert.equal(dependencies.cart.getSnapshot().lines.length, 0);

  dependencies.workflow.releasePreparedCheckout();
  await dependencies.workflow.addByLookupCode("930000000001");
  assert.equal(dependencies.cart.getSnapshot().lines.length, 1);
});

test("远程身份变化只更新目录，不改写本次交易的现有行", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const sharedCart = activeCart();
  const dependencies = connected({
    activeCartSession: sharedCart,
    catalogRevalidation: new SharedRevalidation(remote.promise),
    catalogWorkScheduler: immediateScheduler(),
  });

  await dependencies.workflow.addByLookupCode("930000000001");
  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item({
      productCode: "P-REPLACED",
      referenceCode: "REF-REPLACED",
      retailPriceCents: 999,
    }),
  });
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  const line = sharedCart.getSnapshot().lines[0];
  assert.equal(line?.productCode, "P-TEA");
  assert.equal(line?.unitPrice.cents, 500);
});

test("远程明确不存在保留已加购行，会话失效后迟到结果也不能注入购物车", async () => {
  const notFound = deferred<CatalogLookupRevalidationResult>();
  const first = connected({
    catalogRevalidation: new SharedRevalidation(notFound.promise),
    catalogWorkScheduler: immediateScheduler(),
  });
  await first.workflow.addByLookupCode("930000000001");
  notFound.resolve({
    kind: "not-found",
    baseSnapshotId: "snapshot-1",
  });
  await waitFor(() => first.workflow.getPendingCatalogWorkCount() === 0);
  assert.equal(first.cart.getSnapshot().lines[0]?.unitPrice.cents, 500);

  const sessionGuard = new SessionGuard();
  const late = deferred<CatalogLookupRevalidationResult>();
  const sharedCart = activeCart();
  const second = connected({
    activeCartSession: sharedCart,
    sessionGuard,
    catalog: new Catalog([]),
    catalogRevalidation: new SharedRevalidation(late.promise),
    catalogWorkScheduler: immediateScheduler(),
  });
  await second.workflow.addByLookupCode("930000000001");
  sessionGuard.invalidate();
  late.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item(),
  });
  await waitFor(() => second.workflow.getPendingCatalogWorkCount() === 0);
  assert.equal(sharedCart.getSnapshot().lines.length, 0);
});

test("支付 exclusive lease 期间迟到校准只保留本地行，不竞争共享购物车", async () => {
  const remote = deferred<CatalogLookupRevalidationResult>();
  const releaseLease = deferred<void>();
  const sharedCart = activeCart();
  const dependencies = connected({
    activeCartSession: sharedCart,
    catalogRevalidation: new SharedRevalidation(remote.promise),
    catalogWorkScheduler: immediateScheduler(),
  });
  await dependencies.workflow.addByLookupCode("930000000001");
  const exclusive = sharedCart.runExclusive(async () => {
    await releaseLease.promise;
  });

  remote.resolve({
    kind: "found",
    baseSnapshotId: "snapshot-1",
    item: item({ displayName: "Fresh tea", retailPriceCents: 725 }),
  });
  await waitFor(() => dependencies.workflow.getPendingCatalogWorkCount() === 0);

  assert.equal(sharedCart.getSnapshot().lines[0]?.displayName, "Tea");
  assert.equal(sharedCart.getSnapshot().lines[0]?.unitPrice.cents, 500);
  releaseLease.resolve();
  await exclusive;
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

class SharedRevalidation implements CatalogLookupRevalidationPort {
  public constructor(
    private readonly result: Promise<CatalogLookupRevalidationResult>,
    private readonly currentBaseSnapshot = true,
  ) {}

  public revalidate(): Promise<CatalogLookupRevalidationResult> {
    return this.result;
  }

  public async isCurrentBaseSnapshot(): Promise<boolean> {
    return this.currentBaseSnapshot;
  }
}

function immediateScheduler() {
  return {
    yieldToUi: async () => undefined,
    waitForTimeout: () => new Promise<void>(() => undefined),
  };
}

async function waitFor(predicate: () => boolean): Promise<void> {
  for (let attempts = 0; attempts < 50; attempts += 1) {
    if (predicate()) return;
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
  throw new Error("condition was not reached");
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
