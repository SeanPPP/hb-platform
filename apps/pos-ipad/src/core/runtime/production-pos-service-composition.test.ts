import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import {
  calculateCatalogPageChecksum,
  type CatalogPageDigest,
  type CatalogLookupItem,
} from "../../features/catalog/hbpos-catalog-remote";
import { CATALOG_DOWNLOAD_PERMISSION } from "../../features/catalog/maintenance/catalog-maintenance-authorization";
import type { CashCheckoutResult } from "../../features/checkout/cash";
import type { CustomerDisplayAdvertisementCachePort } from "../../features/customer-display";
import {
  HOLD_ORDER_PERMISSION,
  RECALL_LIST_PERMISSION,
  RECALL_RESTORE_PERMISSION,
} from "../../features/held-orders/held-orders-domain";
import {
  REMOTE_HISTORY_REPRINT_PERMISSION,
  REMOTE_HISTORY_VIEW_PERMISSION,
} from "../../features/remote-history/remote-history-presenter";
import { PricingCart } from "../../features/sales/domain";
import { SALES_PERMISSIONS } from "../../features/sales/runtime/sales-operation-security";
import {
  SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
  SETTINGS_VIEW_PERMISSION,
} from "../../features/settings/settings-authorization";
import type {
  LocalSyncHistoryOrder,
  LocalSyncHistorySupportContext,
} from "../../features/sync-history";
import {
  SYNC_HISTORY_EXPORT_PERMISSION,
  SYNC_HISTORY_MANUAL_SYNC_PERMISSION,
  SYNC_HISTORY_VIEW_PERMISSION,
} from "../../features/sync-history/sync-history-authorization";
import type {
  HbposTransport,
  HbposTransportRequest,
} from "../api/hbpos-api";
import type {
  CustomerDisplaySnapshot,
  DailyCloseArchiveCommit,
  DailyCloseRepositoryPort,
  DailyCloseScope,
  DailyCloseSummary,
  DisplayStatus,
  ExternalCustomerDisplayPort,
  HeldOrderRecordRepositoryPort,
  SpecialProductItem,
  TerminalCartFence,
} from "../contracts";
import type {
  DurableCashOrderCommit,
  LocalOrder,
} from "../contracts/order";
import type {
  ActiveCatalogMetadata,
  ActiveCatalogPromotions,
} from "../db/catalog-repository";
import { PosDatabase } from "../db/pos-database";
import type { SensitivePayloadEncryptor } from "../db/sqlite-repositories";

import type { PaymentProviderRuntimeBootstrap } from "./payment-provider-runtime-bootstrap";
import {
  createPostCommitFulfilmentCashCheckout,
  createProductionPosRuntimeServices,
  type ProductionSettingsRuntimeConfiguration,
} from "./production-pos-service-composition";

const ids = [
  "00000000-0000-4000-8000-000000000001",
  "00000000-0000-4000-8000-000000000002",
  "00000000-0000-4000-8000-000000000003",
  "00000000-0000-4000-8000-000000000004",
  "00000000-0000-4000-8000-000000000005",
  "00000000-0000-4000-8000-000000000006",
  "00000000-0000-4000-8000-000000000007",
  "00000000-0000-4000-8000-000000000008",
] as const;

const nodeCatalogPageDigest: CatalogPageDigest = async (payload) =>
  createHash("sha256").update(payload, "utf8").digest("hex");

test("生产组合只暴露 route 所需业务面，并以 DurableCashCheckoutService 提交现金订单", async () => {
  let nextId = 0;
  const durableCommits: DurableCashOrderCommit[] = [];
  let hardwareCalls = 0;
  let frozenSettingsReads = 0;
  let cashierAuthorizationClears = 0;
  let cashierLockNotifications = 0;
  let preparedReprint: Readonly<{
    orderGuid: string;
    printerId: string;
    receiptBytes: Uint8Array;
  }> | null = null;
  const services = createProductionPosRuntimeServices({
    database: databaseFor(durableCommits, {
      lastOrder: lastCashOrder(),
      onFrozenSettingsRead: () => { frozenSettingsReads += 1; },
      onReprintPrepared: (input) => { preparedReprint = input; },
    }),
    transport: {} as HbposTransport,
    encryptor: encryptor(),
    syncSecurity: { async lockDevice() {} },
    auditMetadata: {
      storeCode: "S001",
      deviceCode: "IPAD-1",
      appVersion: "0.1.0-test",
      instanceId: ids[0],
    },
    supportAppId: "com.hbweb.posipad",
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: () => ids[nextId++] ?? "00000000-0000-4000-8000-000000000099",
    random: () => 0.5,
    sha256Hex: async (material) => `sha256:${material}`,
    catalogPageDigest: nodeCatalogPageDigest,
    createPrinter: () => ({
      async connect() {
        hardwareCalls += 1;
      },
      async print() {
        hardwareCalls += 1;
        return { status: "printed", errorCode: null } as const;
      },
      async open() {
        hardwareCalls += 1;
        return { status: "completed", errorCode: null } as const;
      },
    }),
    connectivity: { async isOnline() { return true; } },
    cashierAuthentication: {
      async login(request) {
        const canOpenDrawer = request.userBarcode === "cashier-drawer";
        return {
          source: "online",
          session: {
            authorizationToken: "cashier-session-secret",
            authorizationExpiresAtUtc: "2026-07-29T00:00:00.000Z",
            emergencyGrantId: "must-not-leak",
            cashierId: canOpenDrawer ? "cashier-2" : "cashier-1",
            userGuid: "user-1",
            cashierName: canOpenDrawer ? "Next Cashier" : "Cashier",
            storeCode: request.storeCode,
            deviceCode: request.deviceCode,
            permissionCodes: canOpenDrawer
              ? [
                  "Permissions.PosTerminal.CashDrawer.Open",
                  SALES_PERMISSIONS.view,
                  SALES_PERMISSIONS.addItem,
                ]
              : [
                  SALES_PERMISSIONS.view,
                  SALES_PERMISSIONS.addItem,
                  SALES_PERMISSIONS.changePrice,
                ],
          },
        };
      },
    },
    cashierSessionSecurity: {
      async getDeviceIdentity() {
        return { storeCode: "S001", deviceCode: "IPAD-1" };
      },
      async clearAuthorization() {
        cashierAuthorizationClears += 1;
      },
      subscribeSessionInvalidation() {
        return () => undefined;
      },
    },
    newTransactionGate: {
      getGate: () => ({
        state: "enabled",
        canStartNewTransaction: true,
        canContinueRecovery: true,
      }),
    },
    operationAuthorization: {
      cashierAuthentication: {
        async login() {
          throw new Error("supervisor scan is not used in this test");
        },
      },
    },
    cashierLock: {
      onLocked() {
        cashierLockNotifications += 1;
      },
    },
  });

  assert.equal(hardwareCalls, 0, "组合根启动不得连接或操作硬件");
  await services.initialize();
  assert.equal("database" in services, false);
  assert.equal("encryptor" in services, false);
  assert.equal("transport" in services, false);
  assert.equal(services.payments.status, "unavailable");
  if (services.payments.status === "unavailable") {
    assert.deepEqual(services.payments.blockers, [
      "SQUARE_TERMINAL_CONFIGURATION_MISSING",
      "LINKLY_ENVIRONMENT_MISSING",
      "VOUCHER_PROTECTED_TOKEN_STORE_MISSING",
      "APPROVED_PAYMENT_COMPLETION_PLANNER_MISSING",
    ]);
  }
  assert.equal(services.capabilities.cashCheckout.status, "available");
  assert.equal(services.capabilities.offlineReturns.status, "available");
  assert.equal(services.capabilities.returns.status, "available");
  assert.equal(services.capabilities.cashDrawer.status, "available");
  assert.equal(services.capabilities.receiptReprint.status, "available");
  assert.equal(
    services.capabilities.supervisorAuthorization.status,
    "available",
  );
  assert.equal(services.operationAuthorization.status, "available");
  assert.equal(
    services.operationAuthorization.status === "available" &&
      "service" in services.operationAuthorization,
    false,
    "route 不能取得原始授权服务或会话激活入口",
  );
  assert.equal("cashierAuthentication" in services, false);
  assert.equal(services.returns.status, "available");
  assert.equal(
    services.returns.status === "available" &&
      ("database" in services.returns ||
        "ledger" in services.returns ||
        "authorization" in services.returns),
    false,
    "route 不能取得退货数据库、账本或原始授权服务",
  );
  assert.throws(
    () => services.remoteHistory.createPresenter({ online: true }),
    /CURRENT_CASHIER_REQUIRED/,
    "远程历史不得接受 route 伪造的门店、终端或权限身份",
  );
  const cashierSummary = await services.cashierSession.signIn(
    "cashier-no-drawer",
  );
  assert.deepEqual(cashierSummary, {
    cashierId: "cashier-1",
    userGuid: "user-1",
    cashierName: "Cashier",
    storeCode: "S001",
    deviceCode: "IPAD-1",
    permissions: [
      SALES_PERMISSIONS.addItem,
      SALES_PERMISSIONS.changePrice,
      SALES_PERMISSIONS.view,
    ],
    source: "online",
  });
  assert.doesNotMatch(
    JSON.stringify(cashierSummary),
    /session|token|expires|emergencyGrant/i,
  );
  const remoteHistoryWithoutTrustedPermission =
    services.remoteHistory.createPresenter({ online: false });
  assert.equal(
    remoteHistoryWithoutTrustedPermission.getState().kind,
    "unauthorized",
  );
  assert.equal(
    remoteHistoryWithoutTrustedPermission.getState().filters.deviceCode,
    "IPAD-1",
  );
  remoteHistoryWithoutTrustedPermission.destroy();
  const syncHistoryPresenter = services.syncHistory.createPresenter(
    cashierSummary.permissions,
  );
  assert.equal(syncHistoryPresenter.getState().kind, "empty");
  assert.equal(syncHistoryPresenter.getState().access.canView, false);
  await assert.rejects(
    () => syncHistoryPresenter.createSupportExport(),
    /permission-required/,
  );
  syncHistoryPresenter.destroy();
  assert.equal(services.fulfilment.reprint.status, "available");
  const settingsReadsBeforeReprint = frozenSettingsReads;
  const reprintResult = await services.fulfilment.reprint.execute();
  const capturedReprint = preparedReprint as Readonly<{
    orderGuid: string;
    printerId: string;
    receiptBytes: Uint8Array;
  }> | null;
  assert.deepEqual(reprintResult, { state: "Printed", errorCode: null });
  assert.equal(
    frozenSettingsReads,
    settingsReadsBeforeReprint + 1,
    "重打单次准备只能读取一次持久设置",
  );
  assert.equal(capturedReprint?.orderGuid, "receipt-order-1");
  assert.equal(capturedReprint?.printerId, "printer-1");
  assert.match(
    new TextDecoder().decode(capturedReprint?.receiptBytes),
    /\*\*\* REPRINT \*\*\*/,
  );

  const presenter = services.sales.createPresenter();
  assert.deepEqual(presenter.getState().capabilities, {
    catalog: true,
    cartEditing: true,
    cashCheckout: true,
    hold: true,
    lock: true,
  });
  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), true);
  assert.equal(presenter.getState().cart.lines.length, 1);

  assert.equal(presenter.openCash(), true);
  presenter.setExactCash();
  assert.equal(await presenter.submitCash(), true);
  assert.equal(durableCommits.length, 1);
  assert.equal(
    presenter.getState().success?.drawerDisposition,
    "permission-denied",
  );
  assert.equal(presenter.startNewSale(), true);
  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), true);
  const clearsBeforeLock = cashierAuthorizationClears;
  assert.equal(await presenter.lockTerminal(), true);
  assert.equal(cashierAuthorizationClears, clearsBeforeLock + 1);
  assert.equal(cashierLockNotifications, 1);
  assert.deepEqual(
    services.operationAuthorization.status === "available"
      ? services.operationAuthorization.getState()
      : null,
    { kind: "idle" },
  );
  assert.throws(
    () => services.sales.createPresenter(),
    /CURRENT_CASHIER_REQUIRED/,
  );
  assert.equal(presenter.openCash(), true);
  presenter.setExactCash();
  assert.equal(await presenter.submitCash(), false);
  assert.equal(durableCommits.length, 1, "旧 presenter 不得在锁屏后继续收款");
  presenter.destroy();

  await services.cashierSession.signIn("cashier-drawer");
  const unlockedPresenter = services.sales.createPresenter();
  assert.equal(
    unlockedPresenter.getState().cart.lines.length,
    1,
    "锁屏后重新认证必须保留当前购物车",
  );
  assert.equal(unlockedPresenter.openCash(), true);
  unlockedPresenter.setExactCash();
  assert.equal(await unlockedPresenter.submitCash(), true);
  assert.equal(durableCommits.length, 2);
  assert.equal(unlockedPresenter.getState().success?.drawerDisposition, "queued");
  assert.equal(
    durableCommits[0]?.terminalContext.kind,
    "none",
    "普通销售必须由共享 terminal session 注入 none 上下文",
  );
  assert.equal("checkout" in services, false, "route 不得绕过共享购物车直接提交");
  unlockedPresenter.destroy();
});

test("生产销售本地目录未命中且在线时使用可信门店做远程精确回退", async () => {
  const requests: HbposTransportRequest[] = [];
  const lookupCode = "930000000999";
  const transport: HbposTransport = {
    async request<T>(request: HbposTransportRequest) {
      requests.push(request);
      return {
        status: 200,
        data: {
          success: true,
          data: {
            storeCode: "S001",
            lookupCode,
            lookupCodeNormalized: lookupCode,
            found: true,
            item: {
              storeCode: "S001",
              productCode: "P-REMOTE",
              referenceCode: "REF-REMOTE",
              displayName: "Remote tea",
              lookupCode,
              lookupCodeNormalized: lookupCode,
              itemNumber: "I-REMOTE",
              barcode: lookupCode,
              retailPrice: 2.5,
              priceSource: 1,
              priceSourceLabel: "Store retail",
              quantityFactor: 1,
              updatedAt: "2026-07-28T00:00:00.000Z",
              rowVersion: "remote-row-1",
              productImage: null,
              discountRate: null,
              isSpecialProduct: false,
            },
          },
        } as T,
      };
    },
  };
  const services = createTestComposition(databaseFor([]), {
    transport,
  });
  await services.initialize();
  await services.cashierSession.signIn("cashier");
  const presenter = services.sales.createPresenter();

  presenter.setQuery(lookupCode);
  assert.equal(await presenter.addLookupCode(), true);

  assert.deepEqual(
    requests.map((request) => ({
      url: request.url,
      params: request.params,
    })),
    [
      {
        url: "/api/v1/catalog/sellable-items/lookup",
        params: { storeCode: "S001", lookupCode },
      },
    ],
  );
  assert.equal(
    presenter.getState().cart.lines[0]?.productCode,
    "P-REMOTE",
  );
  assert.equal(
    presenter.getState().cart.lines[0]?.unitPrice.cents,
    250,
  );
  presenter.destroy();
});

test("收银员登录从当前门店 active 目录装载完整促销快照并重算共享购物车", async () => {
  const loadedStores: string[] = [];
  const services = createTestComposition(
    databaseFor([], {
      activeCatalogPromotions: {
        snapshotId: "catalog-active-1",
        storeCode: "S001",
        promotions: [
          {
            promotionId: "PROMO-2-FOR-1.50",
            definitionJson: JSON.stringify({
              promotionId: "PROMO-2-FOR-1.50",
              name: "2 for 1.50",
              effectiveStart: "2026-07-27T00:00:00.000Z",
              effectiveEnd: "2026-07-29T00:00:00.000Z",
              isExclusive: false,
              priority: 10,
              applyQuantity: 2,
              fixedPrice: 1.5,
              maxApplicationsPerOrder: null,
              products: [{ productCode: "P-1", unitWeight: 1 }],
            }),
          },
        ],
      },
      onActivePromotionsLoad(storeCode) {
        loadedStores.push(storeCode);
      },
    }),
  );

  await services.initialize();
  await services.cashierSession.signIn("cashier");
  const presenter = services.sales.createPresenter();
  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), true);
  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), true);

  assert.deepEqual(loadedStores, ["S001"]);
  assert.equal(presenter.getState().cart.actualAmount.cents, 150);
  assert.equal(presenter.getState().cart.discount.cents, 50);
  presenter.destroy();
});

test("终端启动门禁并发只执行一次，崩溃遗留 HoldClear 必须确认后才开放销售", async () => {
  let confirmations = 0;
  const scope = { storeCode: "S001", deviceCode: "IPAD-1" } as const;
  const fence: TerminalCartFence = {
    scope,
    kind: "HoldClear",
    holdId: "hold-clear-1",
    recallAttemptId: null,
    boundOrderGuid: null,
    createdAtIso: "2026-07-28T00:00:00.000Z",
  };
  const services = createTestComposition(
    databaseFor([], {
      heldOrderRecords: {
        async getTerminalFence() {
          return fence;
        },
        async confirmHoldCartCleared(input) {
          confirmations += 1;
          assert.deepEqual(input, { scope, holdId: fence.holdId });
          return true;
        },
      },
    }),
  );

  assert.throws(
    () => services.sales.createPresenter(),
    /not initialized/i,
  );
  await Promise.all([services.initialize(), services.initialize()]);
  assert.equal(confirmations, 1);
  await services.cashierSession.signIn("cashier");
  const presenter = services.sales.createPresenter();
  assert.equal(presenter.getState().cart.lines.length, 0);
  presenter.destroy();
});

test("分期生产服务使用独立支付账本和第二套 provider 上下文，blocking action 同时阻止安全重启", async () => {
  let boundVoucherContext = false;
  const database = databaseFor([]);
  Object.assign(database, {
    installmentSnapshots: () => ({
      async upsertForStore() {},
      async listForStore() {
        return [];
      },
    }),
    installmentActions: () => ({
      async loadBlocking() {
        return { actionId: "blocking-installment" };
      },
    }),
    installmentPaymentPersistence: () => ({
      providerAttempts: {},
      voucherIntents: {},
      voucherProtectedTokens: {},
      voucherContextForAttempt: async () => {
        throw new Error("not called by this composition test");
      },
      voucherMaterials: {},
      refundProvenance: {},
    }),
  });
  const configuredAvailability = {
    getAvailability(provider: "square" | "linkly-cloud" | "voucher") {
      return {
        provider,
        available: false,
        blocker: "PAYMENT_PROVIDER_UNKNOWN" as const,
      };
    },
    listAvailability() {
      return [];
    },
  };
  const bootstrap = {
    providers: {
      ...configuredAvailability,
      get() {
        throw new Error("provider is outside this composition test");
      },
      listAvailableProviders() {
        return [];
      },
      getVoucherApprovedPurchaseReleasePort() {
        return {
          status: "unavailable" as const,
          reason: "PAYMENT_PROVIDER_UNKNOWN" as const,
        };
      },
    },
    configurationAvailability: configuredAvailability,
    bindVoucherContextProvider() {
      boundVoucherContext = true;
    },
    createLinklyOperator() {
      return null;
    },
  } as PaymentProviderRuntimeBootstrap;
  const services = createTestComposition(database, {
    installmentBootstrap: bootstrap,
  });

  await services.initialize();
  await services.cashierSession.signIn("cashier");

  assert.equal(boundVoucherContext, true);
  assert.equal("createPresenter" in services.installments, true);
  assert.equal(
    (await services.appUpdateSafety.getSnapshot()).hasUnresolvedPayment,
    true,
  );
});

test("RecallActive 启动只建立隐藏门禁，登录后仍需双权限 recover 才恢复并成交", async () => {
  const durableCommits: DurableCashOrderCommit[] = [];
  let recallLoads = 0;
  const pricingCart = new PricingCart();
  pricingCart.addItem({
    lineId: "recalled-line-1",
    productCode: "P-1",
    itemNumber: "I-1",
    lookupCode: "930000000001",
    displayName: "Milk",
    unitPrice: { currency: "AUD", cents: 100 },
    syncProvenance: { referenceCode: null, priceSource: 0 },
  });
  const scope = { storeCode: "S001", deviceCode: "IPAD-1" } as const;
  const binding = {
    kind: "recalled" as const,
    scope,
    holdId: "hold-recalled-1",
    recallAttemptId: "recall-attempt-1",
  };
  const fence: TerminalCartFence = {
    scope,
    kind: "RecallActive",
    holdId: binding.holdId,
    recallAttemptId: binding.recallAttemptId,
    boundOrderGuid: null,
    createdAtIso: "2026-07-28T00:00:00.000Z",
  };
  const heldAtIso = "2026-07-27T23:00:00.000Z";
  const services = createTestComposition(
    databaseFor(durableCommits, {
      heldOrderRecords: {
        async getTerminalFence() {
          return fence;
        },
        async loadRecallForFence(input) {
          recallLoads += 1;
          assert.deepEqual(input, binding);
          return {
            hold: {
              holdId: binding.holdId,
              localSequence: 7,
              scope,
              heldBy: { cashierId: "cashier-1", cashierName: "Cashier" },
              status: "Recalling",
              itemCount: 1,
              subtotalCents: 100,
              discountCents: 0,
              actualAmountCents: 100,
              heldAtIso,
              recallingAtIso: "2026-07-28T00:00:00.000Z",
            },
            recallAttemptId: binding.recallAttemptId,
            payload: {
              version: 1,
              pricingState: pricingCart.stateSnapshot(),
            },
          };
        },
      },
    }),
    {
      cashierPermissions: [
        RECALL_LIST_PERMISSION,
        RECALL_RESTORE_PERMISSION,
      ],
      supervisorPermissions: [],
    },
  );
  await services.initialize();
  assert.equal(recallLoads, 0, "启动阶段不得读取或暴露上一位收银员的挂单内容");
  await services.cashierSession.signIn("cashier");

  const presenter = services.sales.createPresenter();
  assert.equal(presenter.getState().cart.lines.length, 0);
  presenter.setQuery("930000000001");
  assert.equal(
    await presenter.addLookupCode(),
    false,
    "隐藏恢复门禁解除前普通编辑必须 fail-closed",
  );

  const heldPresenter = services.heldOrders.createPresenter();
  assert.deepEqual(await heldPresenter.recover(binding.holdId), {
    ok: true,
    code: "recovered",
    holdId: binding.holdId,
  });
  assert.equal(recallLoads, 1);
  assert.equal(presenter.getState().cart.lines[0]?.lineId, "recalled-line-1");
  assert.equal(presenter.openCash(), true);
  presenter.setExactCash();
  assert.equal(await presenter.submitCash(), true);

  assert.equal(durableCommits.length, 1);
  assert.deepEqual(durableCommits[0]?.terminalContext, binding);
  assert.deepEqual(
    durableCommits[0]?.recalledHoldCompletion?.binding,
    binding,
  );
  assert.equal(presenter.getState().cart.lines.length, 0);
  heldPresenter.destroy();
  presenter.destroy();
});

test("销售挂单使用全局主管授权且只执行一次，会话失效取消待授权动作", async () => {
  let fence: TerminalCartFence | null = null;
  let holdCalls = 0;
  const invalidationCapture: { listener?: () => void } = {};
  const scope = { storeCode: "S001", deviceCode: "IPAD-1" } as const;
  const services = createTestComposition(
    databaseFor([], {
      heldOrderRecords: {
        async getTerminalFence() {
          return fence;
        },
        async hold(command) {
          holdCalls += 1;
          fence = {
            scope,
            kind: "HoldClear",
            holdId: command.holdId,
            recallAttemptId: null,
            boundOrderGuid: null,
            createdAtIso: command.heldAtIso,
          };
          return {
            holdId: command.holdId,
            localSequence: holdCalls,
            scope,
            heldBy: command.heldBy,
            status: "Pending",
            itemCount: command.payload.pricingState.lines.length,
            subtotalCents: 100,
            discountCents: 0,
            actualAmountCents: 100,
            heldAtIso: command.heldAtIso,
            recallingAtIso: null,
          };
        },
        async confirmHoldCartCleared(input) {
          if (
            fence?.kind !== "HoldClear" ||
            fence.holdId !== input.holdId ||
            input.scope.storeCode !== scope.storeCode ||
            input.scope.deviceCode !== scope.deviceCode
          ) {
            return false;
          }
          fence = null;
          return true;
        },
      },
    }),
    {
      supervisorPermissions: [HOLD_ORDER_PERMISSION],
      captureInvalidation(listener) {
        invalidationCapture.listener = listener;
      },
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier");
  const presenter = services.sales.createPresenter();
  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), true);

  const pending = presenter.holdCart();
  await Promise.resolve();
  assert.equal(services.operationAuthorization.status, "available");
  if (services.operationAuthorization.status !== "available") {
    throw new Error("operation authorization must be available");
  }
  const authorizationState = services.operationAuthorization.getState();
  assert.equal(authorizationState.kind, "awaiting-supervisor");
  if (authorizationState.kind !== "awaiting-supervisor") {
    throw new Error("supervisor authorization must be pending");
  }
  assert.ok(authorizationState.actionId);
  assert.equal(authorizationState.permissionCode, HOLD_ORDER_PERMISSION);
  assert.equal(authorizationState.screen, "held-orders");
  assert.equal(authorizationState.action, "hold");
  assert.equal(authorizationState.verifying, false);
  const firstScan =
    services.operationAuthorization.submitSupervisorBarcode("supervisor");
  const duplicateScan =
    services.operationAuthorization.submitSupervisorBarcode("supervisor");
  assert.equal((await duplicateScan).outcome, "duplicate-ignored");
  assert.equal((await firstScan).outcome, "authorized");
  assert.equal(await pending, true);
  assert.equal(holdCalls, 1);
  assert.equal(presenter.getState().cart.lines.length, 0);

  presenter.setQuery("930000000001");
  assert.equal(await presenter.addLookupCode(), true);
  const cancelled = presenter.holdCart();
  await Promise.resolve();
  const invalidateSession = invalidationCapture.listener;
  if (typeof invalidateSession !== "function") {
    throw new Error("session invalidation listener was not captured");
  }
  invalidateSession();
  assert.equal(await cancelled, false);
  assert.equal(holdCalls, 1);
  assert.equal(presenter.getState().cart.lines.length, 1);
  presenter.destroy();
});

test("同步历史 presenter 只使用可信收银员 lease，会话失效后拒绝读取、导出和补传", async () => {
  let restoreCalls = 0;
  const invalidationCapture: { listener?: () => void } = {};
  const order = pendingSyncHistoryOrder();
  const services = createTestComposition(
    databaseFor([], {
      syncHistoryOrders: [order],
      onSyncHistoryRestore() {
        restoreCalls += 1;
      },
    }),
    {
      cashierPermissions: [
        SYNC_HISTORY_EXPORT_PERMISSION,
        SYNC_HISTORY_MANUAL_SYNC_PERMISSION,
        SYNC_HISTORY_VIEW_PERMISSION,
      ],
      captureInvalidation(listener) {
        invalidationCapture.listener = listener;
      },
    },
  );
  await services.initialize();
  assert.throws(
    () => services.syncHistory.createPresenter([]),
    /CURRENT_CASHIER_REQUIRED/,
  );

  const cashier = await services.cashierSession.signIn("cashier");
  const presenter = services.syncHistory.createPresenter(
    cashier.permissions,
  );
  await presenter.refresh();
  assert.equal(presenter.getState().rows.length, 1);
  presenter.setSelected(order.orderGuid, true);

  const invalidate = invalidationCapture.listener;
  if (!invalidate) throw new Error("session invalidation listener was not captured");
  invalidate();

  assert.deepEqual(await presenter.requestRetransmitSelected(), {
    kind: "failed",
    requestedCount: 0,
    skippedCount: 0,
    reauthenticationRequiredCount: 0,
    supervisorRequiredCount: 0,
    errorCode: "retransmit-failed",
  });
  assert.equal(restoreCalls, 0, "失效后的旧 presenter 不得恢复 outbox");
  await assert.rejects(
    () => presenter.createSupportExport(),
    /CURRENT_CASHIER_REQUIRED/,
  );
  await presenter.refresh();
  assert.equal(presenter.getState().kind, "failed");
  presenter.destroy();
});

test("远程历史重打绑定当前收银员 lease，并从本地订单账本重新渲染后进入耐久打印状态机", async () => {
  const orderGuid = "10000000-0000-4000-8000-000000000001";
  const order = { ...lastCashOrder(), orderGuid };
  const preparedOrderGuids: string[] = [];
  const printedJobIds: string[] = [];
  const transport: HbposTransport = {
    async request<T>(request: HbposTransportRequest) {
      if (request.url === "/api/v1/orders/history") {
        return {
          status: 200,
          data: {
            success: true,
            data: {
              orders: [
                {
                  orderGuid,
                  storeCode: "S001",
                  deviceCode: "IPAD-1",
                  cashierName: "Cashier",
                  soldAt: order.soldAtIso,
                  totalAmount: 1,
                  discountAmount: 0,
                  actualAmount: 1,
                  lineCount: 1,
                  paymentSummary: "Cash",
                  statusLabel: "Synced",
                },
              ],
            },
          } as T,
        };
      }
      return {
        status: 200,
        data: {
          success: true,
          data: {
            orderGuid,
            storeCode: "S001",
            deviceCode: "IPAD-1",
            cashierName: "Cashier",
            soldAt: order.soldAtIso,
            totalAmount: 1,
            discountAmount: 0,
            actualAmount: 1,
            lines: [
              {
                orderLineGuid:
                  "20000000-0000-4000-8000-000000000001",
                productCode: "P-1",
                displayName: "Milk",
                quantity: 1,
                unitPrice: 1,
                discountAmount: 0,
                actualAmount: 1,
                kind: 1,
              },
            ],
            payments: [
              {
                paymentGuid:
                  "30000000-0000-4000-8000-000000000001",
                method: 1,
                amount: 1,
                reference: null,
                cardTransactions: [],
              },
            ],
          },
        } as T,
      };
    },
  };
  const services = createTestComposition(
    databaseFor([], {
      lastOrder: order,
      onReprintPrepared(input) {
        preparedOrderGuids.push(input.orderGuid);
      },
    }),
    {
      cashierPermissions: [
        REMOTE_HISTORY_REPRINT_PERMISSION,
        REMOTE_HISTORY_VIEW_PERMISSION,
      ],
      onPrint(jobId) {
        printedJobIds.push(jobId);
      },
      transport,
    },
  );

  await services.initialize();
  await services.cashierSession.signIn("cashier");
  const presenter = services.remoteHistory.createPresenter({
    online: true,
  });
  await presenter.refresh();
  assert.equal(presenter.capabilities.reprint, true);

  await presenter.reprintSelected();

  assert.deepEqual(preparedOrderGuids, [orderGuid]);
  assert.deepEqual(printedJobIds, ["reprint-job-1"]);
  assert.deepEqual(presenter.getState().reprint, {
    kind: "succeeded",
    orderGuid,
  });
  presenter.destroy();
});

test("现金提交耐久化成功后立即触发履约，履约失败不改变成功结果", async () => {
  const expected: CashCheckoutResult = {
    completed: true,
    canClearCart: true,
    orderGuid: "cash-order-1",
    cashDueCents: 100,
    changeCents: 0,
    postCommit: {
      requestDrawer: false,
      drawerDisposition: "not-required",
      printPolicy: "never",
    },
  };
  const trace: string[] = [];
  const checkout = createPostCommitFulfilmentCashCheckout(
    {
      async complete() {
        trace.push("committed");
        return expected;
      },
    },
    async () => {
      trace.push("drain-started");
      throw new Error("printer is temporarily unavailable");
    },
  );

  const result = await checkout.complete({} as never);

  assert.equal(result, expected);
  assert.deepEqual(trace, ["committed", "drain-started"]);
});

test("生产退货服务按可信收银员作用域启用，缺少主管授权时明确不可用", async () => {
  const available = createTestComposition(databaseFor([]), {
    cashierPermissions: ["Permissions.PosTerminal.Returns.View"],
    supervisorPermissions: [],
  });
  await available.initialize();
  await available.cashierSession.signIn("cashier");
  assert.equal(available.returns.status, "available");
  if (available.returns.status !== "available") {
    throw new Error("returns runtime should be available");
  }
  assert.equal(await available.returns.hasRecoveryRequired(), false);
  const presenter = await available.returns.createPresenter();
  assert.equal(presenter.getState().mode, "receipt");

  const unavailable = createTestComposition(databaseFor([]));
  await unavailable.initialize();
  await unavailable.cashierSession.signIn("cashier");
  assert.deepEqual(unavailable.returns, {
    status: "unavailable",
    reason: "SUPERVISOR_AUTHENTICATION_MISSING",
  });
});

test("生产退货组合必须取得二次加密退款券材料端口，不能保留不可用占位渲染器", async () => {
  let protectedMaterialFacadeCalls = 0;
  const services = createTestComposition(
    databaseFor([], {
      onRefundVoucherPrintMaterialCreated() {
        protectedMaterialFacadeCalls += 1;
      },
    }),
    {
      cashierPermissions: ["Permissions.PosTerminal.Returns.View"],
      supervisorPermissions: [],
    },
  );

  await services.initialize();
  assert.equal(services.returns.status, "available");
  assert.equal(protectedMaterialFacadeCalls, 1);
});

test("特殊商品只从可信收银员作用域加载，并将服务端价格来源原值带入共享购物车", async () => {
  const item: SpecialProductItem = {
    storeCode: "S001",
    productCode: "SPECIAL-1",
    referenceCode: "REF-SPECIAL-1",
    itemNumber: "ITEM-SPECIAL-1",
    displayName: "Special item",
    barcode: "930000000099",
    lookupCode: "930000000099",
    retailPriceCents: 275,
    priceSource: 3,
    quantityFactor: 1,
    productImage: null,
    discountRate: null,
    sortOrder: 0,
  };
  const services = createTestComposition(
    databaseFor([], { specialProductItems: [item] }),
    {
      cashierPermissions: [
        "Permissions.PosTerminal.SpecialProducts.View",
        "Permissions.PosTerminal.SpecialProducts.AddToCart",
      ],
    },
  );
  await services.initialize();
  assert.throws(
    () => services.specialProducts.createPresenter(),
    /CURRENT_CASHIER_REQUIRED/,
  );
  await services.cashierSession.signIn("cashier");

  const specialProducts = services.specialProducts.createPresenter();
  await specialProducts.load();
  assert.equal(specialProducts.getState().items.length, 1);
  await specialProducts.addToCart("SPECIAL-1");

  const cart = services.sales.createPresenter().getState().cart;
  assert.deepEqual(cart.lines[0]?.syncProvenance, {
    referenceCode: "REF-SPECIAL-1",
    priceSource: 3,
  });
  assert.equal(cart.lines[0]?.unitPrice.cents, 275);
  specialProducts.destroy();
});

test("生产更新门禁阻止空车开始普通或特殊商品交易，但不截断已有购物车", async () => {
  let canStartNewTransaction = false;
  const specialItem: SpecialProductItem = {
    storeCode: "S001",
    productCode: "SPECIAL-GATE",
    referenceCode: null,
    itemNumber: "ITEM-GATE",
    displayName: "Gated item",
    barcode: "930000000088",
    lookupCode: "930000000088",
    retailPriceCents: 300,
    priceSource: 0,
    quantityFactor: 1,
    productImage: null,
    discountRate: null,
    sortOrder: 0,
  };
  const services = createTestComposition(
    databaseFor([], { specialProductItems: [specialItem] }),
    {
      canStartNewTransaction: () => canStartNewTransaction,
      cashierPermissions: [
        "Permissions.PosTerminal.SpecialProducts.View",
        "Permissions.PosTerminal.SpecialProducts.AddToCart",
      ],
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier");

  const sales = services.sales.createPresenter();
  assert.deepEqual(await services.appUpdateSafety.getSnapshot(), {
    hasActiveCart: false,
    hasUnresolvedPayment: false,
    hasPendingDurableWrite: false,
  });
  sales.setQuery("930000000001");
  assert.equal(await sales.addLookupCode(), false);
  assert.equal(sales.getState().errorCode, "new-transactions-disabled");

  const specials = services.specialProducts.createPresenter();
  await specials.load();
  await specials.addToCart("SPECIAL-GATE");
  assert.equal(specials.getState().statusCode, "add-to-cart-failed");
  assert.equal(sales.getState().cart.lines.length, 0);

  canStartNewTransaction = true;
  await specials.addToCart("SPECIAL-GATE");
  assert.equal(specials.getState().statusCode, "added-to-cart");
  assert.equal(sales.getState().cart.lines.length, 1);
  assert.deepEqual(await services.appUpdateSafety.getSnapshot(), {
    hasActiveCart: true,
    hasUnresolvedPayment: false,
    hasPendingDurableWrite: false,
  });

  canStartNewTransaction = false;
  await specials.addToCart("SPECIAL-GATE");
  assert.equal(specials.getState().statusCode, "added-to-cart");
  assert.equal(sales.getState().cart.lines[0]?.quantity, "2");

  specials.destroy();
  sales.destroy();
});

test("生产组合把共享购物车发布到只读客显，支付清车后仍可显示原交易找零", async () => {
  const display = new RecordingExternalDisplay();
  const invalidationCapture: { listener?: () => void } = {};
  const services = createTestComposition(databaseFor([]), {
    externalDisplay: display,
    captureInvalidation(listener) {
      invalidationCapture.listener = listener;
    },
  });

  await services.initialize();
  assert.deepEqual(
    display.snapshots.map((snapshot) => snapshot.mode),
    ["idle"],
  );

  await services.cashierSession.signIn("cashier");
  const sales = services.sales.createPresenter();
  sales.setQuery("930000000001");
  assert.equal(await sales.addLookupCode(), true);
  await display.waitForCount(2);
  assert.equal(display.snapshots.at(-1)?.mode, "cart");
  assert.equal(display.snapshots.at(-1)?.items[0]?.name, "Milk");

  assert.equal(services.customerDisplay.status, "available");
  if (services.customerDisplay.status !== "available") {
    assert.fail("customer display should be available");
  }
  await services.customerDisplay.showPayment();
  sales.openCash();
  sales.setExactCash();
  assert.equal(await sales.submitCash(), true);
  await services.customerDisplay.showSuccess(25);

  const success = display.snapshots.at(-1);
  assert.equal(success?.mode, "success");
  assert.equal(success?.items[0]?.name, "Milk");
  assert.equal(success?.total.cents, 100);
  assert.equal(success?.change.cents, 25);
  assert.doesNotMatch(
    JSON.stringify(success),
    /cashier|token|customer|provider|authorization|reference/i,
  );

  const snapshotCountBeforeInvalidation = display.snapshots.length;
  invalidationCapture.listener?.();
  await display.waitForCount(snapshotCountBeforeInvalidation + 1);
  const cleared = display.snapshots.at(-1);
  assert.equal(cleared?.mode, "idle");
  assert.deepEqual(cleared?.items, []);
  assert.equal(cleared?.change.cents, 0);
  assert.equal(cleared?.advert, null);
  sales.destroy();
});

test("会话失效时客显清屏发布连续失败会有界重试并触发原生安全清屏，且不阻塞内存会话失效", async () => {
  const display = new RecordingExternalDisplay();
  const invalidationCapture: { listener?: () => void } = {};
  const services = createTestComposition(databaseFor([]), {
    externalDisplay: display,
    captureInvalidation(listener) {
      invalidationCapture.listener = listener;
    },
  });

  await services.initialize();
  await services.cashierSession.signIn("cashier");
  const sales = services.sales.createPresenter();
  sales.setQuery("930000000001");
  assert.equal(await sales.addLookupCode(), true);
  await display.waitForCount(2);
  assert.equal(display.snapshots.at(-1)?.mode, "cart");

  display.failNextPublishes = 3;
  invalidationCapture.listener?.();

  assert.throws(
    () => services.sales.createPresenter(),
    /CURRENT_CASHIER_REQUIRED/,
    "外屏 I/O 仍在处理时，可信收银员必须已同步失效",
  );
  await display.waitForForceBlank(1);

  assert.equal(display.failedPublishCalls, 3);
  assert.equal(display.forceBlankCalls, 1);
  assert.equal(display.disableCalls, 0);
  sales.destroy();
});

test("可信收银员登录后缓存当前门店广告，并只向客显发布本地素材 URI", async () => {
  const display = new RecordingExternalDisplay();
  const requestedUrls: string[] = [];
  const transport: HbposTransport = {
    async request<T>(request: HbposTransportRequest) {
      requestedUrls.push(request.url);
      return {
        status: 200,
        data: {
          success: true,
          data: {
            storeCode: "S001",
            generatedAt: "2026-07-28T00:00:00.000Z",
            items: [
              {
                id: "ad-1",
                title: "Weekend",
                description: null,
                mediaType: "image",
                mediaUrl: "https://cdn.example.com/ad.png",
                thumbnailUrl: null,
                objectKey: "ads/ad.png",
                originalFileName: "ad.png",
                contentType: "image/png",
                fileSize: 1_024,
                effectiveStart: "2026-07-27T00:00:00.000Z",
                effectiveEnd: "2026-07-29T00:00:00.000Z",
                sortOrder: 0,
              },
            ],
          },
        } as T,
      };
    },
  };
  const advertisementCache: CustomerDisplayAdvertisementCachePort = {
    async cache(items) {
      return items.map((item) => ({
        ...item,
        localUri: "file:///cache/customer-display/ad.png",
      }));
    },
  };
  const services = createTestComposition(databaseFor([]), {
    advertisementCache,
    customerDisplayAdvertisementCacheRootUri:
      "file:///cache/customer-display/",
    externalDisplay: display,
    transport,
  });

  await services.initialize();
  await services.cashierSession.signIn("cashier");
  await display.waitForCount(2);

  assert.deepEqual(requestedUrls, ["/api/v1/advertisements/active"]);
  assert.deepEqual(display.snapshots.at(-1)?.advert, {
    kind: "image",
    localUri: "file:///cache/customer-display/ad.png",
  });
  assert.doesNotMatch(
    JSON.stringify(display.snapshots.at(-1)),
    /cdn\.example\.com|cashier-session-secret/,
  );

  if (services.customerDisplay.status !== "available") {
    assert.fail("customer display should be available");
  }
  assert.equal(
    await services.customerDisplay.refreshAdvertisements(),
    "unchanged",
  );
  services.customerDisplay.stopAdvertisements();
});

test("本地目录摘要只向具备下载权限的可信同店收银员公开", async () => {
  const summary: ActiveCatalogMetadata = {
    snapshotId: "catalog-active-1",
    catalogVersion: "catalog-v3",
    itemCount: 42,
    activatedAt: "2026-07-28T00:00:00.000Z",
  };
  const services = createTestComposition(
    databaseFor([], { activeCatalogMetadata: summary }),
    { cashierPermissions: [CATALOG_DOWNLOAD_PERMISSION] },
  );
  await services.initialize();

  await assert.rejects(
    () => services.catalog.getCurrentCatalog({ storeCode: "S001" }),
    /CURRENT_CASHIER_REQUIRED/,
  );

  await services.cashierSession.signIn("cashier");
  assert.deepEqual(
    await services.catalog.getCurrentCatalog({ storeCode: "S001" }),
    summary,
  );
  await assert.rejects(
    () => services.catalog.getCurrentCatalog({ storeCode: "OTHER" }),
    /store/i,
  );

  const withoutPermission = createTestComposition(
    databaseFor([], { activeCatalogMetadata: summary }),
  );
  await withoutPermission.initialize();
  await withoutPermission.cashierSession.signIn("cashier");
  await assert.rejects(
    () => withoutPermission.catalog.getCurrentCatalog({ storeCode: "S001" }),
    /permission/i,
  );
});

test("下载期间会话失效会在激活前拒绝，既不覆盖 active 也不报告成功", async () => {
  let invalidate: (() => void) | undefined;
  let activations = 0;
  const services = createTestComposition(
    databaseFor([], {
      onCatalogActivate() { activations += 1; },
    }),
    {
      cashierPermissions: [CATALOG_DOWNLOAD_PERMISSION],
      captureInvalidation(listener) { invalidate = listener; },
      transport: await catalogDownloadTransport(),
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier");

  await assert.rejects(
    () => services.catalog.downloadAndActivate({
      storeCode: "S001",
      onProgress(event) {
        if (event.step === "products" && event.percent === 100) {
          invalidate?.();
        }
      },
    }),
    /CURRENT_CASHIER_REQUIRED/,
  );
  assert.equal(activations, 0);
});

test("激活后没有同快照促销状态时返回告警，并仍返回新 active 目录摘要", async () => {
  const services = createTestComposition(
    databaseFor([]),
    {
      cashierPermissions: [CATALOG_DOWNLOAD_PERMISSION],
      transport: await catalogDownloadTransport(),
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier");

  const outcome = await services.catalog.downloadAndActivate({
    storeCode: "S001",
  });

  assert.deepEqual(outcome, {
    kind: "activated-with-warning",
    summary: {
      snapshotId: "test-id-1",
      catalogVersion: "catalog-v3",
      itemCount: 1,
      activatedAt: "2026-07-28T00:00:00.000Z",
    },
    warningCode: "catalog-runtime-reload-failed",
  });
});

test("激活后促销快照与目录不一致时返回告警", async () => {
  const services = createTestComposition(
    databaseFor([], {
      activeCatalogPromotions: {
        snapshotId: "another-active-snapshot",
        storeCode: "S001",
        promotions: [],
      },
    }),
    {
      cashierPermissions: [CATALOG_DOWNLOAD_PERMISSION],
      transport: await catalogDownloadTransport(),
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier");

  const outcome = await services.catalog.downloadAndActivate({
    storeCode: "S001",
  });

  assert.equal(outcome.kind, "activated-with-warning");
  assert.equal(outcome.warningCode, "catalog-runtime-reload-failed");
});

test("settings 目录重载缺失、fallback 或快照不一致时保持已切换目录并报告失败", async () => {
  const cases: readonly Readonly<{
    name: string;
    database: NonNullable<Parameters<typeof databaseFor>[1]>;
  }>[] = [
    { name: "missing", database: {} },
    {
      name: "fallback",
      database: {
        onActivePromotionsLoad() {
          throw new Error("promotion reload failed");
        },
      },
    },
    {
      name: "mismatch",
      database: {
        activeCatalogPromotions: {
          snapshotId: "other-snapshot",
          storeCode: "S001",
          promotions: [],
        },
      },
    },
  ];

  for (const scenario of cases) {
    let activations = 0;
    const services = createTestComposition(
      databaseFor([], {
        ...scenario.database,
        onCatalogActivate() { activations += 1; },
      }),
      {
        cashierPermissions: [
          SETTINGS_VIEW_PERMISSION,
          SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
        ],
        settings: settingsRuntimeConfiguration(),
        transport: await catalogDownloadTransport(),
      },
    );
    await services.initialize();
    await services.cashierSession.signIn("cashier");
    assert.equal("createPresenter" in services.settings, true, scenario.name);
    if (!("createPresenter" in services.settings)) continue;
    const presenter = services.settings.createPresenter();
    await presenter.load();
    await presenter.downloadCatalog();

    assert.equal(activations, 1, scenario.name);
    assert.equal(
      presenter.getState().statusCode,
      "catalog-download-failed",
      scenario.name,
    );
  }
});

test("销毁 settings 下载会在激活前取消，并只清理 staging", async () => {
  let activateCount = 0;
  let discardCount = 0;
  let promotionsEntered!: () => void;
  let releasePromotions!: () => void;
  const entered = new Promise<void>((resolve) => { promotionsEntered = resolve; });
  const release = new Promise<void>((resolve) => { releasePromotions = resolve; });
  const services = createTestComposition(
    databaseFor([], {
      onCatalogActivate() { activateCount += 1; },
      onCatalogDiscard() { discardCount += 1; },
    }),
    {
      cashierPermissions: [
        SETTINGS_VIEW_PERMISSION,
        SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
      ],
      settings: settingsRuntimeConfiguration(),
      transport: await catalogDownloadTransport({
        beforePromotions: async () => {
          promotionsEntered();
          await release;
        },
      }),
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier");
  assert.equal("createPresenter" in services.settings, true);
  if (!("createPresenter" in services.settings)) return;
  const presenter = services.settings.createPresenter();
  await presenter.load();

  const download = presenter.downloadCatalog();
  await entered;
  presenter.destroy();
  releasePromotions();
  await download;

  assert.equal(activateCount, 0);
  assert.equal(discardCount, 1);
});

test("settings 目录下载绑定原 cashier lease，换班到同店身份也不能激活", async () => {
  let activateCount = 0;
  let discardCount = 0;
  let promotionsEntered!: () => void;
  let releasePromotions!: () => void;
  const entered = new Promise<void>((resolve) => { promotionsEntered = resolve; });
  const release = new Promise<void>((resolve) => { releasePromotions = resolve; });
  const services = createTestComposition(
    databaseFor([], {
      onCatalogActivate() { activateCount += 1; },
      onCatalogDiscard() { discardCount += 1; },
    }),
    {
      cashierPermissions: [
        SETTINGS_VIEW_PERMISSION,
        SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
      ],
      settings: settingsRuntimeConfiguration(),
      transport: await catalogDownloadTransport({
        beforePromotions: async () => {
          promotionsEntered();
          await release;
        },
      }),
    },
  );
  await services.initialize();
  await services.cashierSession.signIn("cashier-1");
  assert.equal("createPresenter" in services.settings, true);
  if (!("createPresenter" in services.settings)) return;
  const presenter = services.settings.createPresenter();
  await presenter.load();

  const download = presenter.downloadCatalog();
  await entered;
  await services.cashierSession.signIn("cashier-2");
  releasePromotions();
  await download;

  assert.equal(activateCount, 0);
  assert.equal(discardCount, 1);
});

test("日结只使用可信收银员作用域，先耐久归档再通过同一打印适配器输出 ESC/POS", async () => {
  const printed: Readonly<{
    jobId: string;
    bytes: Uint8Array;
  }>[] = [];
  const dailyClose = new MemoryDailyCloseRepository();
  const services = createTestComposition(
    databaseFor([], { dailyClose }),
    {
      cashierPermissions: [
        "Permissions.PosTerminal.DailyClose.View",
        "Permissions.PosTerminal.DailyClose.Save",
        "Permissions.PosTerminal.DailyClose.Reprint",
      ],
      onPrint(jobId, bytes) {
        printed.push({ jobId, bytes });
      },
    },
  );
  await services.initialize();

  assert.throws(
    () => services.dailyClose.createPresenter(),
    /CURRENT_CASHIER_REQUIRED/,
  );
  await services.cashierSession.signIn("cashier");
  const presenter = services.dailyClose.createPresenter();
  await presenter.load();
  assert.equal(presenter.getState().summary?.storeCode, "S001");
  assert.equal(presenter.setCount(100, 10), true);
  await presenter.saveAndPrint();

  assert.equal(dailyClose.saved.length, 1);
  assert.equal(dailyClose.saved[0]?.archive.storeCode, "S001");
  assert.equal(dailyClose.saved[0]?.archive.deviceCode, "IPAD-1");
  assert.equal(dailyClose.saved[0]?.archive.savedCashierId, "cashier-1");
  assert.equal(presenter.getState().statusCode, "saved-printed");
  assert.equal(printed.length, 1);
  assert.match(printed[0]?.jobId ?? "", /^daily-close:/);
  assert.deepEqual(
    [...(printed[0]?.bytes.slice(-3) ?? [])],
    [0x1d, 0x56, 0x00],
  );
  presenter.destroy();
});

function createTestComposition(
  database: PosDatabase,
  options: Readonly<{
    canStartNewTransaction?: () => boolean;
    cashierPermissions?: readonly string[];
    supervisorPermissions?: readonly string[];
    captureInvalidation?(listener: () => void): void;
    externalDisplay?: ExternalCustomerDisplayPort;
    advertisementCache?: CustomerDisplayAdvertisementCachePort;
    customerDisplayAdvertisementCacheRootUri?: string;
    transport?: HbposTransport;
    onPrint?(jobId: string, bytes: Uint8Array): void;
    installmentBootstrap?: PaymentProviderRuntimeBootstrap;
    settings?: ProductionSettingsRuntimeConfiguration;
  }> = {},
) {
  let nextId = 0;
  const supervisorPermissions = options.supervisorPermissions;
  return createProductionPosRuntimeServices({
    database,
    transport: options.transport ?? ({} as HbposTransport),
    encryptor: encryptor(),
    syncSecurity: { async lockDevice() {} },
    auditMetadata: {
      storeCode: "S001",
      deviceCode: "IPAD-1",
      appVersion: "0.1.0-test",
      instanceId: "test-instance",
    },
    supportAppId: "com.hbweb.posipad",
    clock: {
      now: () => new Date("2026-07-28T00:00:00.000Z"),
      nowIso: () => "2026-07-28T00:00:00.000Z",
    },
    createId: () => `test-id-${++nextId}`,
    random: () => 0.5,
    sha256Hex: async (material) => `sha256:${material}`,
    catalogPageDigest: nodeCatalogPageDigest,
    createPrinter: () => ({
      async connect() {},
      async print(jobId, bytes) {
        options.onPrint?.(jobId, bytes);
        return { status: "printed", errorCode: null } as const;
      },
      async open() {
        return { status: "completed", errorCode: null } as const;
      },
    }),
    connectivity: { async isOnline() { return true; } },
    ...(options.externalDisplay
      ? { externalDisplay: options.externalDisplay }
      : {}),
    ...(options.advertisementCache
      ? { advertisementCache: options.advertisementCache }
      : {}),
    ...(options.customerDisplayAdvertisementCacheRootUri
      ? {
          customerDisplayAdvertisementCacheRootUri:
            options.customerDisplayAdvertisementCacheRootUri,
        }
      : {}),
    cashierAuthentication: {
      async login(request) {
        return {
          source: "online",
          session: {
            authorizationToken: "cashier-session-secret",
            authorizationExpiresAtUtc: "2026-07-29T00:00:00.000Z",
            cashierId: "cashier-1",
            userGuid: "user-1",
            cashierName: "Cashier",
            storeCode: request.storeCode,
            deviceCode: request.deviceCode,
            permissionCodes: [
              SALES_PERMISSIONS.view,
              SALES_PERMISSIONS.addItem,
              ...(options.cashierPermissions ?? []),
            ],
          },
        };
      },
    },
    cashierSessionSecurity: {
      async getDeviceIdentity() {
        return { storeCode: "S001", deviceCode: "IPAD-1" };
      },
      async clearAuthorization() {},
      subscribeSessionInvalidation(listener) {
        options.captureInvalidation?.(listener);
        return () => undefined;
      },
    },
    newTransactionGate: {
      getGate: () => ({
        state:
          (options.canStartNewTransaction?.() ?? true)
            ? "enabled"
            : "disabled",
        canStartNewTransaction:
          options.canStartNewTransaction?.() ?? true,
        canContinueRecovery: true,
      }),
    },
    ...(supervisorPermissions
      ? {
          operationAuthorization: {
            cashierAuthentication: {
              async login(request: Readonly<{
                storeCode: string;
                deviceCode: string;
                userBarcode: string;
              }>) {
                return {
                  source: "online" as const,
                  session: {
                    authorizationToken: "supervisor-session-secret",
                    authorizationExpiresAtUtc:
                      "2026-07-29T00:00:00.000Z",
                    cashierId: "supervisor-1",
                    userGuid: "supervisor-user-1",
                    cashierName: "Supervisor",
                    storeCode: request.storeCode,
                    deviceCode: request.deviceCode,
                    permissionCodes: [...supervisorPermissions],
                  },
                };
              },
            },
          },
        }
      : {}),
    ...(options.installmentBootstrap
      ? {
          installments: {
            bootstrap: options.installmentBootstrap,
          },
        }
      : {}),
    ...(options.settings ? { settings: options.settings } : {}),
  });
}

function settingsRuntimeConfiguration(): ProductionSettingsRuntimeConfiguration {
  return {
    apiBaseUrl: "https://pos.example.test",
    appVersion: "0.1.0-test",
    updateChannel: "preview",
    readDevicePresentation: async () => ({
      deviceCode: "IPAD-1",
      storeCode: "S001",
      storeName: "Test Store",
      terminalName: "Test Terminal",
    }),
    paymentConfiguration: {
      current: null,
      availability: {
        square: { available: false, blockerCode: "not-configured" },
        linkly: { available: false, blockerCode: "not-configured" },
      },
      test: async () => undefined,
      save: async () => undefined,
    },
    apiConfiguration: {
      probe: async () => true,
      save: async () => undefined,
    },
    runtimeReload: { reload: async () => undefined },
    device: { reregister: async () => undefined },
    printer: {
      getStatus: async () => "ready",
      scan: async () => [],
      connect: async () => undefined,
      disconnect: async () => undefined,
      print: async () => ({ status: "printed", errorCode: null }),
      subscribe: () => () => undefined,
      open: async () => ({ status: "completed", errorCode: null }),
    },
    scanner: {
      status: "ready",
      test: async () => ({ source: "hid", value: "SKU-1" }),
    },
    appUpdate: {
      snapshot: () => ({
        channel: "preview",
        currentVersion: "0.1.0-test",
        availableVersion: null,
        updateRequired: false,
        restartAvailable: true,
      }),
      check: async () => ({
        channel: "preview",
        currentVersion: "0.1.0-test",
        availableVersion: null,
        updateRequired: false,
        restartAvailable: true,
      }),
      restart: async () => true,
    },
  };
}

async function catalogDownloadTransport(
  options: Readonly<{
    beforePromotions?(): void | Promise<void>;
  }> = {},
): Promise<HbposTransport> {
  const item: CatalogLookupItem = {
    storeCode: "S001",
    productCode: "P-001",
    referenceCode: null,
    displayName: "Milk",
    lookupCode: "930000000001",
    lookupCodeNormalized: "930000000001",
    itemNumber: "I-001",
    barcode: null,
    retailPrice: 12.34,
    priceSource: 0,
    priceSourceLabel: "product",
    quantityFactor: 1,
    updatedAt: "2026-07-28T00:00:00.000Z",
    rowVersion: "row-1",
    productImage: null,
    discountRate: null,
    isSpecialProduct: false,
  };
  const pageChecksum = await calculateCatalogPageChecksum(
    [item],
    nodeCatalogPageDigest,
  );
  return {
    async request<T>(request: HbposTransportRequest) {
      if (request.url === "/api/v1/catalog/sellable-items/page") {
        return {
          status: 200,
          data: {
            success: true,
            data: {
              storeCode: "S001",
              generatedAt: "2026-07-28T00:00:00.000Z",
              cursor: null,
              items: [item],
              deletedLookups: [],
              nextCursor: null,
              hasMore: false,
              totalCount: 1,
              catalogVersion: "catalog-v3",
              pageChecksum,
            },
          } as T,
        };
      }
      if (request.url === "/api/v1/catalog/promotions") {
        await options.beforePromotions?.();
        return {
          status: 200,
          data: {
            success: true,
            data: {
              storeCode: "S001",
              generatedAt: "2026-07-28T00:00:00.000Z",
              promotions: [],
            },
          } as T,
        };
      }
      throw new Error(`Unexpected catalog URL: ${request.url}`);
    },
  };
}

class RecordingExternalDisplay implements ExternalCustomerDisplayPort {
  public disableCalls = 0;
  public failedPublishCalls = 0;
  public failNextPublishes = 0;
  public forceBlankCalls = 0;
  public readonly snapshots: CustomerDisplaySnapshot[] = [];
  private readonly forceBlankWaiters: Readonly<{
    count: number;
    resolve(): void;
    timeout: ReturnType<typeof setTimeout>;
  }>[] = [];
  private readonly waiters: Readonly<{
    count: number;
    resolve(): void;
  }>[] = [];

  public async getStatus(): Promise<DisplayStatus> {
    return "ready";
  }

  public async setEnabled(enabled: boolean): Promise<void> {
    if (!enabled) this.disableCalls += 1;
  }

  public async publish(snapshot: CustomerDisplaySnapshot): Promise<void> {
    if (this.failNextPublishes > 0) {
      this.failNextPublishes -= 1;
      this.failedPublishCalls += 1;
      throw new Error("external display publish failed");
    }
    this.snapshots.push(snapshot);
    for (let index = this.waiters.length - 1; index >= 0; index -= 1) {
      const waiter = this.waiters[index];
      if (waiter && this.snapshots.length >= waiter.count) {
        this.waiters.splice(index, 1);
        waiter.resolve();
      }
    }
  }

  public async forceBlank(): Promise<void> {
    this.forceBlankCalls += 1;
    for (let index = this.forceBlankWaiters.length - 1; index >= 0; index -= 1) {
      const waiter = this.forceBlankWaiters[index];
      if (waiter && this.forceBlankCalls >= waiter.count) {
        this.forceBlankWaiters.splice(index, 1);
        clearTimeout(waiter.timeout);
        waiter.resolve();
      }
    }
  }

  public subscribe(): () => void {
    return () => undefined;
  }

  public waitForCount(count: number): Promise<void> {
    if (this.snapshots.length >= count) return Promise.resolve();
    return new Promise((resolve) => this.waiters.push({ count, resolve }));
  }

  public waitForForceBlank(count: number): Promise<void> {
    if (this.forceBlankCalls >= count) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        const index = this.forceBlankWaiters.findIndex(
          (waiter) => waiter.timeout === timeout,
        );
        if (index >= 0) this.forceBlankWaiters.splice(index, 1);
        reject(new Error("forceBlank was not called"));
      }, 100);
      this.forceBlankWaiters.push({ count, resolve, timeout });
    });
  }
}

function unavailableHeldOrderRecords(): HeldOrderRecordRepositoryPort {
  return {
    async hold() {
      throw new Error("held order hold is not configured");
    },
    async listPending() {
      return [];
    },
    async claimRecall() {
      return null;
    },
    async getTerminalFence() {
      return null;
    },
    async loadRecallForFence() {
      return null;
    },
    async confirmHoldCartCleared() {
      return false;
    },
    async releaseRecallAfterCartCleared() {
      return false;
    },
    async listRecoverable() {
      return [];
    },
  };
}

function databaseFor(
  durableCommits: DurableCashOrderCommit[],
  options: Readonly<{
    lastOrder?: LocalOrder;
    onFrozenSettingsRead?(): void;
    onReprintPrepared?(input: Readonly<{
      orderGuid: string;
      printerId: string;
      receiptBytes: Uint8Array;
    }>): void;
    heldOrderRecords?: Partial<HeldOrderRecordRepositoryPort>;
    syncHistoryOrders?: readonly LocalSyncHistoryOrder[];
    specialProductItems?: readonly SpecialProductItem[];
    dailyClose?: DailyCloseRepositoryPort;
    onSyncHistoryRestore?(): void;
    onRefundVoucherPrintMaterialCreated?(): void;
    activeCatalogPromotions?: ActiveCatalogPromotions | null;
    activeCatalogMetadata?: ActiveCatalogMetadata | null;
    onCatalogActivate?(): void;
    onCatalogDiscard?(): void;
    onActivePromotionsLoad?(storeCode: string): void;
  }> = {},
): PosDatabase {
  let activeCatalogMetadata = options.activeCatalogMetadata ?? null;
  let stagingCatalog: Readonly<{
    snapshotId: string;
    catalogVersion: string;
    itemCount: number;
  }> | null = null;
  const settings = {
    async getReceiptPrinterSettings() {
      options.onFrozenSettingsRead?.();
      return {
        printEnabled: true,
        drawerEnabled: true,
        peripheralId: "printer-1",
        paper: "80mm" as const,
        locale: "en" as const,
        brandName: "Hot Bargain",
        storeName: "Brisbane",
        address: "1 Queen St",
        phone: "0712345678",
        abn: "12 345 678 901",
      };
    },
    async saveReceiptPrinterSettings() {
      throw new Error("not used");
    },
  };
  const repositories = {
    orders: {
      async nextLocalSequence() { return 1; },
      async getByGuid(orderGuid: string) {
        return options.lastOrder?.orderGuid === orderGuid
          ? options.lastOrder
          : null;
      },
      async listLocal() {
        return options.lastOrder ? [options.lastOrder] : [] as LocalOrder[];
      },
    },
    payments: {},
    outbox: {},
    audit: {},
    heldOrderRecords: {
      ...unavailableHeldOrderRecords(),
      ...options.heldOrderRecords,
    },
  };

  return {
    repositories: () => repositories,
    orderSyncMaterial: () => ({
      async resolve(order: LocalOrder) {
        return order;
      },
    }),
    catalogSnapshots: () => ({
      async getActiveMetadata() {
        return activeCatalogMetadata;
      },
      async beginStaging(input: Readonly<{
        snapshotId: string;
        catalogVersion: string;
      }>) {
        stagingCatalog = {
          snapshotId: input.snapshotId,
          catalogVersion: input.catalogVersion,
          itemCount: 0,
        };
      },
      async appendPage(snapshotId: string, items: readonly unknown[]) {
        if (!stagingCatalog || stagingCatalog.snapshotId !== snapshotId) {
          throw new Error("unexpected catalog staging page");
        }
        stagingCatalog = {
          ...stagingCatalog,
          itemCount: stagingCatalog.itemCount + items.length,
        };
      },
      async replacePromotions() {},
      async activate(
        snapshotId: string,
        expectedItemCount: number,
        activatedAt: string,
      ) {
        if (
          !stagingCatalog ||
          stagingCatalog.snapshotId !== snapshotId ||
          stagingCatalog.itemCount !== expectedItemCount
        ) {
          throw new Error("unexpected catalog activation");
        }
        activeCatalogMetadata = {
          snapshotId,
          catalogVersion: stagingCatalog.catalogVersion,
          itemCount: expectedItemCount,
          activatedAt,
        };
        options.onCatalogActivate?.();
        stagingCatalog = null;
      },
      async discardStaging(snapshotId: string) {
        if (stagingCatalog?.snapshotId === snapshotId) stagingCatalog = null;
        options.onCatalogDiscard?.();
      },
      async loadActivePromotions(storeCode: string) {
        options.onActivePromotionsLoad?.(storeCode);
        return options.activeCatalogPromotions ?? null;
      },
      async findExact(lookupCode: string) {
        return lookupCode === "930000000001"
          ? {
              storeCode: "S001",
              productCode: "P-1",
              referenceCode: null,
              itemNumber: "I-1",
              displayName: "Milk",
              barcode: "930000000001",
              lookupCode: "930000000001",
              lookupCodeNormalized: "930000000001",
              retailPriceCents: 100,
              priceSource: 0,
              priceSourceLabel: "Retail",
              quantityFactor: 1,
              taxRateBasisPoints: 1_000,
              updatedAtIso: null,
              rowVersion: "1",
              productImage: null,
              discountRate: null,
              isSpecialProduct: false,
            }
          : null;
      },
      async searchByName() {
        return [];
      },
    }),
    specialProducts: () => ({
      async list(storeCode: string, limit: number, offset: number) {
        return (options.specialProductItems ?? [])
          .filter((item) => item.storeCode === storeCode)
          .slice(offset, offset + limit);
      },
      async searchCandidates() {
        return [];
      },
      async replaceDownloaded() {},
      async applyMark() {},
      async saveOrder() {},
    }),
    dailyCloses: () =>
      options.dailyClose ?? new MemoryDailyCloseRepository(),
    settings: () => settings,
    offlineReturnCapacity: () => ({
      async hasCapacity() {
        return false;
      },
    }),
    fulfilmentStore: () => reprintStore(options),
    receiptCompletionSettlements: () => ({
      async getByOrderGuid() {
        return options.lastOrder ? { cashChangeCents: 0 } : null;
      },
    }),
    cashOrderCommitter: () => ({
      async completeDurableCashOrder(input: DurableCashOrderCommit) {
        durableCommits.push(input);
        return {
          replayed: false,
          orderGuid: input.command.order.orderGuid,
          cashDueCents: input.intent.cashDueCents,
          changeCents: input.intent.changeCents,
        };
      },
    }),
    paymentOrderCommitter: () => ({}),
    returnCapacityVault: () => ({
      async protect() {
        throw new Error("return capacity protect is not used");
      },
    }),
    returnExecutionLedger: () => ({
      async listRecoverable() {
        return [];
      },
    }),
    returnFulfilmentPlans: () => ({
      async get() {
        return null;
      },
      async listPending() {
        return [];
      },
      async materialize() {
        throw new Error("return fulfilment materialize is not used");
      },
    }),
    refundVoucherPrintMaterial: () => {
      options.onRefundVoucherPrintMaterialCreated?.();
      return {
        async resolveApprovedRefundVoucher() {
          return null;
        },
      };
    },
    localSyncHistory: (
      supportContext: LocalSyncHistorySupportContext,
    ) => ({
      async listLocalSyncHistory() {
        const orders = options.syncHistoryOrders ?? [];
        return {
          orders,
          nextBeforeLocalSequence: null,
          pendingCount: orders.filter(
            (order) => order.outbox?.state === "pending",
          ).length,
        };
      },
      async restoreExistingOrderOutboxToPending(orderGuids: readonly string[]) {
        options.onSyncHistoryRestore?.();
        const available = new Set(
          (options.syncHistoryOrders ?? []).map((order) => order.orderGuid),
        );
        return {
          restoredOrderGuids: orderGuids.filter((orderGuid) =>
            available.has(orderGuid),
          ),
          skippedOrderGuids: orderGuids.filter(
            (orderGuid) => !available.has(orderGuid),
          ),
        };
      },
      async getSupportContext() {
        return supportContext;
      },
    }),
  } as unknown as PosDatabase;
}

class MemoryDailyCloseRepository implements DailyCloseRepositoryPort {
  public readonly saved: DailyCloseArchiveCommit[] = [];
  private readonly archives: DailyCloseArchiveCommit["archive"][] = [];

  public async summarize(scope: DailyCloseScope): Promise<DailyCloseSummary> {
    return {
      ...scope,
      orderCount: 1,
      returnQuantity: "0",
      tenders: [
        {
          method: "cash",
          salesCents: 1_000,
          refundCents: 0,
          netCents: 1_000,
        },
        {
          method: "card",
          salesCents: 0,
          refundCents: 0,
          netCents: 0,
        },
        {
          method: "voucher",
          salesCents: 0,
          refundCents: 0,
          netCents: 0,
        },
      ],
      expectedCashCents: 1_000,
    };
  }

  public async saveArchive(input: DailyCloseArchiveCommit) {
    this.saved.push(input);
    this.archives.unshift(input.archive);
    return { replayed: false, archive: input.archive };
  }

  public async getArchive(closeId: string) {
    return (
      this.archives.find((archive) => archive.closeId === closeId) ??
      null
    );
  }

  public async listArchives(
    scope: Readonly<{
      storeCode: string;
      deviceCode: string;
      businessDate?: string | null;
      limit: number;
    }>,
  ) {
    return this.archives
      .filter(
        (archive) =>
          archive.storeCode === scope.storeCode &&
          archive.deviceCode === scope.deviceCode &&
          (!scope.businessDate ||
            archive.businessDate === scope.businessDate),
      )
      .slice(0, scope.limit);
  }
}

function reprintStore(
  options: Parameters<typeof databaseFor>[1],
) {
  let reprint: Readonly<{
    jobId: string;
    orderGuid: string;
    printerId: string;
    isReprint: true;
    bytes: Uint8Array;
    state: "Queued";
    retryCount: number;
  }> | null = null;
  return {
    async createLastReceiptReprint(input: Readonly<{
      orderGuid: string;
      printerId: string;
      receiptBytes: Uint8Array;
    }>) {
      options?.onReprintPrepared?.(input);
      reprint = {
        jobId: "reprint-job-1",
        orderGuid: input.orderGuid,
        printerId: input.printerId,
        isReprint: true,
        bytes: input.receiptBytes,
        state: "Queued",
        retryCount: 0,
      };
      return reprint;
    },
    async claimQueuedPrintJob(jobId: string) {
      return reprint?.jobId === jobId ? { ...reprint, state: "Sending" as const } : null;
    },
    async finishPrintJob() {
      return true;
    },
  };
}

function lastCashOrder(): LocalOrder {
  return {
    orderGuid: "receipt-order-1",
    localSequence: 7,
    storeCode: "S001",
    deviceCode: "IPAD-1",
    cashierId: "cashier-1",
    cashierName: "Cashier",
    soldAtIso: "2026-07-28T00:00:00.000Z",
    state: "PendingSync",
    total: { currency: "AUD", cents: 100 },
    discount: { currency: "AUD", cents: 0 },
    actualAmount: { currency: "AUD", cents: 100 },
    lines: [{
      lineId: "receipt-line-1",
      productCode: "P-1",
      itemNumber: "I-1",
      lookupCode: "930000000001",
      displayName: "Milk",
      quantity: "1",
      unitPrice: { currency: "AUD", cents: 100 },
      discount: { currency: "AUD", cents: 0 },
      actualAmount: { currency: "AUD", cents: 100 },
      priceSource: "catalog",
      kind: "sale",
      returnSourceKey: null,
      originalOrderGuid: null,
      originalOrderDetailGuid: null,
    }],
    tenders: [{
      tenderGuid: "receipt-tender-1",
      method: "cash",
      amount: { currency: "AUD", cents: 100 },
      reference: null,
      reservationToken: null,
    }],
    originalOrderGuid: null,
  };
}

function pendingSyncHistoryOrder(): LocalSyncHistoryOrder {
  return {
    orderGuid: "pending-sync-order-1",
    localSequence: 8,
    storeCode: "S001",
    deviceCode: "IPAD-1",
    soldAtIso: "2026-07-28T00:00:00.000Z",
    state: "PendingSync",
    totalCents: 100,
    discountCents: 0,
    actualAmountCents: 100,
    tenders: [{ method: "cash", amountCents: 100 }],
    outbox: {
      state: "pending",
      attemptCount: 1,
      lastErrorCode: "NETWORK",
      nextAttemptAtIso: "2026-07-28T00:01:00.000Z",
    },
  };
}

function encryptor(): SensitivePayloadEncryptor {
  return {
    async encrypt(plaintext: string) {
      return new TextEncoder().encode(plaintext);
    },
    async decrypt(ciphertext: Uint8Array) {
      return new TextDecoder().decode(ciphertext);
    },
  } as SensitivePayloadEncryptor;
}
