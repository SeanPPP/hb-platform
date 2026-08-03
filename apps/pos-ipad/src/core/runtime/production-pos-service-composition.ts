import type {
  AppUpdateRestartSafetySnapshot,
  UpdateTransitionLeaseCoordinator,
} from "../../features/app-updates";
import type { AttendanceAuditRuntimeFactory } from "../../features/attendance-audit/attendance-audit-runtime";
import { CashierLockService } from "../../features/cashier-lock";
import {
  RemoteCatalogLookupRevalidationService,
} from "../../features/catalog/catalog-lookup-revalidation";
import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressObserver,
  CatalogSummary,
} from "../../features/catalog/catalog-refresh-contract";
import {
  CatalogRefreshCoordinator,
  type CatalogRefreshState,
} from "../../features/catalog/catalog-refresh-coordinator";
import {
  CatalogSnapshotService,
  type CatalogActivationResult,
} from "../../features/catalog/catalog-snapshot-service";
import {
  HbposCatalogPageApi,
  type CatalogPageDigest,
} from "../../features/catalog/hbpos-catalog-remote";
import { CATALOG_DOWNLOAD_PERMISSION } from "../../features/catalog/maintenance/catalog-maintenance-authorization";
import { HbposCatalogLookupApi } from "../../features/catalog/remote-catalog-fallback";
import { DurableCashCheckoutService } from "../../features/checkout/cash";
import {
  CustomerDisplayAdvertisementPlayback,
  CustomerDisplayCoordinator,
  CustomerDisplayPublisher,
  HbposAdvertisementApi,
  type AdvertisementRefreshResult,
  type CustomerDisplayAdvertisementCachePort,
  type CustomerDisplayPublishResult,
} from "../../features/customer-display";
import { CustomerDisplaySensitiveContentGuard } from "../../features/customer-display/customer-display-sensitive-content-guard";
import { DailyClosePresenter } from "../../features/daily-close/daily-close-presenter";
import type { DailyCloseRuntimeFactory } from "../../features/daily-close/daily-close-runtime";
import {
  FulfilmentService,
  type FulfilmentActionResult,
  type FulfilmentAuthorizationContext,
} from "../../features/fulfilment";
import {
  type ActivePricingCartPort,
  type HeldOrderActionResult,
  type HeldOrderAuthorizationPort,
} from "../../features/held-orders/held-orders-domain";
import { HeldOrdersOrchestrator } from "../../features/held-orders/held-orders-orchestrator";
import { HeldOrdersPresenter } from "../../features/held-orders/held-orders-presenter";
import { HbposInstallmentsApi } from "../../features/installments/hbpos-installments-api";
import type { InstallmentsRuntimeFactory } from "../../features/installments/installment-runtime";
import type {
  LocalHistoryPort,
  LocalHistoryReceiptPreviewPort,
  LocalHistoryReprintPort,
} from "../../features/local-history/local-history-domain";
import {
  LOCAL_HISTORY_REPRINT_PERMISSION,
  LOCAL_HISTORY_VIEW_PERMISSION,
  LocalHistoryPresenter,
} from "../../features/local-history/local-history-presenter";
import type { LocalHistoryPresenterFactory } from "../../features/local-history/local-history-runtime";
import {
  OperationAuthorizationService,
  type OperationAuthorizationPublicState,
  type OperationAuthorizationServiceOptions,
  type SupervisorBarcodeScanResult,
} from "../../features/operation-authorization";
import type { PaymentConnectivityPort } from "../../features/payments/payment-attempt-service";
import { VoucherHbposApi } from "../../features/payments/voucher";
import {
  CashFulfilmentPlanner,
  LocalHistoryReceiptPreviewService,
  OrderRepositoryReceiptReprintSource,
  ReceiptReprintPreparationService,
  RemoteHistoryReceiptReprintPreparationService,
  isRemoteHistoryReceiptReprintEligible,
  type ReceiptCompletionSettlementSource,
  type ReceiptPreviewSettingsSource,
  type ReceiptReprintSettingsSource,
} from "../../features/receipts";
import {
  buildDailyCloseReceipt,
  dailyCloseReceiptToEscPosBytes,
} from "../../features/receipts/daily-close-receipt";
import { ProtectedRefundVoucherReceiptRenderer } from "../../features/receipts/refund-voucher-receipt-renderer";
import {
  OrderRepositoryReturnReceiptRenderer,
  type ReturnReceiptSettingsPort,
} from "../../features/receipts/return-receipt-renderer";
import {
  PostSyncVoucherLatestBalanceApi,
  VoucherBalancePostSyncService,
  VoucherBalanceReceiptRenderer,
} from "../../features/receipts/voucher-balance-receipt";
import { HbposRemoteHistoryApi } from "../../features/remote-history/remote-history-api";
import { REMOTE_HISTORY_REPRINT_PERMISSION } from "../../features/remote-history/remote-history-presenter";
import {
  createHbposRemoteHistoryPresenterFactory,
  type RemoteHistoryPresenterFactory,
} from "../../features/remote-history/remote-history-runtime";
import { HbposReturnHistoryApi } from "../../features/returns/adapters/return-lookup-adapter";
import { PricingCart } from "../../features/sales/domain";
import {
  ACTIVE_PRICING_CART_STALE_SNAPSHOT,
  ActivePromotionSnapshotLoader,
  ActivePricingCartSession,
  createConnectedSalesPresenter,
  SALES_NEW_TRANSACTIONS_DISABLED,
  type ConnectedSalesIdentity,
} from "../../features/sales/runtime";
import type {
  SalesAuthorizedOperationContext,
  SalesOperationAuthorizationPort,
} from "../../features/sales/runtime/sales-operation-security";
import type { SalesPresenter } from "../../features/sales/ui/sales-presenter";
import {
  SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
  SETTINGS_CATALOG_RESET_PERMISSION,
} from "../../features/settings/settings-authorization";
import type { SettingsRuntimeFactory } from "../../features/settings/settings-runtime";
import { HbposSpecialProductsApi } from "../../features/special-products/hbpos-special-products-api";
import { SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION } from "../../features/special-products/special-products-authorization";
import { SpecialProductsPresenter } from "../../features/special-products/special-products-presenter";
import type { SpecialProductsRuntimeFactory } from "../../features/special-products/special-products-runtime";
import type { LocalSyncHistoryPort } from "../../features/sync-history/sync-history-domain";
import { SyncHistoryPresenter } from "../../features/sync-history/sync-history-presenter";
import type { HbposTransport } from "../api/hbpos-api";
import { createAud, type CartSnapshot } from "../contracts";
import type { NewTransactionGate } from "../contracts/app-updates";
import type { CashDrawerPort } from "../contracts/drawer";
import type {
  CustomerDisplaySnapshot,
  DisplayStatus,
  ExternalCustomerDisplayPort,
} from "../contracts/external-display";
import type { PrinterPort } from "../contracts/printer";
import type {
  RecallActiveBinding,
  TerminalCartFence,
  TerminalCartScope,
} from "../contracts/terminal-cart";
import type { LocalCatalogMatch } from "../db/catalog-repository";
import { PosDatabase } from "../db/pos-database";
import type { ReceiptPrinterSettings } from "../db/pos-settings-repository";
import type { LinklyOrderSyncEnvironment } from "../db/sqlite-order-sync-material";
import type {
  PosRepositoryBundle,
  SensitivePayloadEncryptor,
} from "../db/sqlite-repositories";
import type {
  CashierAuthenticationService,
} from "../security/cashier-authentication";
import {
  HbposAuditBatchAdapter,
  HbposOrderSyncAdapter,
  type HbposAuditMetadata,
} from "../sync/hbpos-sync-adapters";
import {
  PosSyncCoordinator,
  SyncLifecycleController,
  type SyncSecurityPort,
} from "../sync/sync-coordinator";

import {
  CurrentCashierSession,
  type PosCashierSummary,
  type TrustedCashierLease,
  type TrustedCashierSession,
} from "./current-cashier-session";
import type { PaymentProviderRuntimeBootstrap } from "./payment-provider-runtime-bootstrap";
import {
  createProductionAttendanceAuditRuntime,
  type ProductionAttendanceAuditRuntimeDependencies,
} from "./production-attendance-audit-runtime";
import {
  ProductionInstallmentPaymentAdapter,
  type InstallmentCardProvider,
} from "./production-installment-payment-adapter";
import { HbposInstallmentRefundProvenance } from "./production-installment-refund-provenance";
import { createProductionInstallmentRuntime } from "./production-installment-runtime";
import {
  createProductionPaymentRuntime,
  type PosPaymentRuntimeService,
} from "./production-payment-runtime";
import {
  ProductionReturnCashRefundAdapter,
  ProductionReturnOnlineRefundRouter,
} from "./production-return-online-refund-router";
import {
  createProductionReturnRuntime,
  type PosReturnRuntimeService,
} from "./production-return-runtime";
import {
  createProductionSettingsComposition,
  type ProductionSettingsCompositionInput,
} from "./production-settings-composition";
import { ReturnFulfilmentRuntime } from "./return-fulfilment-runtime";

export type { PosCashierSummary } from "./current-cashier-session";

export type RuntimeCapability =
  | Readonly<{ status: "available" }>
  | Readonly<{ status: "unavailable"; reason: string }>;

export type PosRuntimeCapabilities = Readonly<{
  catalog: RuntimeCapability;
  cashCheckout: RuntimeCapability;
  fulfilment: RuntimeCapability;
  printerAdapter: RuntimeCapability;
  cashDrawer: RuntimeCapability;
  receiptReprint: RuntimeCapability;
  offlineReturns: RuntimeCapability;
  returns: RuntimeCapability;
  payments: RuntimeCapability;
  installments: RuntimeCapability;
  supervisorAuthorization: RuntimeCapability;
}>;

export type PosCatalogRuntimeService = Readonly<{
  findExact(lookupCode: string): Promise<LocalCatalogMatch | null>;
  searchByName(
    query: string,
    limit: number,
    offset?: number,
  ): Promise<readonly LocalCatalogMatch[]>;
  getCurrentCatalog(
    input: Readonly<{
      storeCode: string;
      signal?: AbortSignal | undefined;
    }>,
  ): Promise<CatalogSummary | null>;
  getRefreshState(): CatalogRefreshState;
  subscribeRefresh(listener: () => void): () => void;
  downloadAndActivate(
    input: Readonly<{
      storeCode: string;
      onProgress?: CatalogRefreshProgressObserver | undefined;
      signal?: AbortSignal | undefined;
    }>,
  ): Promise<CatalogRefreshOutcome>;
}>;

export type PosReceiptSettingsService = Readonly<{
  get(): Promise<ReceiptPrinterSettings>;
  save(input: ReceiptPrinterSettings): Promise<ReceiptPrinterSettings>;
}>;

export type PosAuthorizedFulfilmentActionResult =
  | FulfilmentActionResult
  | Readonly<{
      state: "denied";
      errorCode: string | null;
    }>;

export type PosAuthorizedFulfilmentAction = Readonly<{
  status: "available";
  execute(): Promise<PosAuthorizedFulfilmentActionResult>;
}>;

export type PosFulfilmentRuntimeService = Readonly<{
  drainAutomaticQueue(): Promise<Readonly<{ printed: number; drawersOpened: number }>>;
  retryFailedPrint(jobId: string): Promise<FulfilmentActionResult>;
  retryFailedDrawer(eventId: string): Promise<FulfilmentActionResult>;
  reprintCurrentReceipt(
    orderGuid: string,
  ): Promise<PosAuthorizedFulfilmentActionResult>;
  reprint: PosAuthorizedFulfilmentAction;
  openCashDrawer: PosAuthorizedFulfilmentAction;
}>;

export type PosSalesRuntimeService = Readonly<{
  /**
   * 路由只提供已完成登录的身份摘要和权限码；目录和现金服务始终取自这个生产
   * 组合根，因而不会再退回没有持久化依赖的 disconnected presenter。
   */
  createPresenter(): SalesPresenter;
}>;

export type PosHeldOrdersRuntimeService = Readonly<{
  createPresenter(): HeldOrdersPresenter;
}>;

export type PosSyncHistoryRuntimeService = Readonly<{
  createPresenter(permissionCodes: readonly string[]): SyncHistoryPresenter;
}>;

export type PosCustomerDisplayRuntimeService =
  | Readonly<{
      status: "available";
      getStatus(): Promise<DisplayStatus>;
      setEnabled(enabled: boolean): Promise<unknown>;
      subscribe(listener: (status: DisplayStatus) => void): () => void;
      showCart(): Promise<CustomerDisplayPublishResult>;
      showPayment(): Promise<CustomerDisplayPublishResult>;
      showChange(changeCents: number): Promise<CustomerDisplayPublishResult>;
      showSuccess(changeCents: number): Promise<CustomerDisplayPublishResult>;
      clearSensitiveContent(): Promise<CustomerDisplayPublishResult>;
      setAdvert(
        advert: CustomerDisplaySnapshot["advert"],
      ): Promise<CustomerDisplayPublishResult>;
      refreshAdvertisements(
        force?: boolean,
      ): Promise<AdvertisementRefreshResult | "unavailable">;
      advanceAdvertisement(): Promise<boolean>;
      stopAdvertisements(): void;
    }>
  | Readonly<{
      status: "unavailable";
      reason: "EXTERNAL_DISPLAY_ADAPTER_MISSING";
    }>;

export type PosOperationAuthorizationRuntimeService =
  | Readonly<{
      status: "available";
      getState(): OperationAuthorizationPublicState;
      subscribe(
        listener: (state: OperationAuthorizationPublicState) => void,
      ): () => void;
      submitSupervisorBarcode(
        barcode: string,
      ): Promise<SupervisorBarcodeScanResult>;
      cancel(actionId?: string): boolean;
    }>
  | Readonly<{
      status: "unavailable";
      reason: string;
    }>;

export type PosReturnsRuntimeService =
  | (PosReturnRuntimeService & Readonly<{ status: "available" }>)
  | Readonly<{
      status: "unavailable";
      reason: string;
    }>;

export type PosSettingsRuntimeService =
  | SettingsRuntimeFactory
  | Readonly<{
      status: "unavailable";
      reason: "SETTINGS_ADAPTER_MISSING";
    }>;

export type PosAttendanceAuditRuntimeService =
  | AttendanceAuditRuntimeFactory
  | Readonly<{
      status: "unavailable";
      reason: "ATTENDANCE_SECURITY_ADAPTER_MISSING";
    }>;

export type PosInstallmentsRuntimeService =
  | InstallmentsRuntimeFactory
  | Readonly<{
      status: "unavailable";
      reason: "INSTALLMENT_PAYMENT_PERSISTENCE_MISSING";
    }>;

type AuthenticatedSalesSession = TrustedCashierSession &
  ConnectedSalesIdentity;

export type PosCashierSessionRuntimeService = Readonly<{
  /** 只接收条码并返回脱敏投影；设备范围、权限和 bearer token 均由组合根掌握。 */
  signIn(userBarcode: string): Promise<PosCashierSummary>;
}>;

/**
 * 这是可交给 route 的最小业务面：没有裸 SQLite、字段加密器、HTTP transport 或授权 token。
 */
export type ProductionPosRuntimeServices = Readonly<{
  appUpdateSafety: Readonly<{
    getSnapshot(): Promise<AppUpdateRestartSafetySnapshot>;
  }>;
  attendanceAudit: PosAttendanceAuditRuntimeService;
  catalog: PosCatalogRuntimeService;
  catalogRefresh: CatalogRefreshCoordinator;
  receiptSettings: PosReceiptSettingsService;
  fulfilment: PosFulfilmentRuntimeService;
  sync: Readonly<{
    requestDrain: PosSyncCoordinator["requestDrain"];
    onApplicationStarted: SyncLifecycleController["onApplicationStarted"];
    onForeground: SyncLifecycleController["onForeground"];
    onNetworkChanged: SyncLifecycleController["onNetworkChanged"];
    shutdown: SyncLifecycleController["shutdown"];
  }>;
  payments: PosPaymentRuntimeService;
  operationAuthorization: PosOperationAuthorizationRuntimeService;
  cashierSession: PosCashierSessionRuntimeService;
  localHistory: LocalHistoryPresenterFactory;
  remoteHistory: RemoteHistoryPresenterFactory;
  specialProducts: SpecialProductsRuntimeFactory;
  customerDisplay: PosCustomerDisplayRuntimeService;
  dailyClose: DailyCloseRuntimeFactory;
  sales: PosSalesRuntimeService;
  heldOrders: PosHeldOrdersRuntimeService;
  syncHistory: PosSyncHistoryRuntimeService;
  returns: PosReturnsRuntimeService;
  installments: PosInstallmentsRuntimeService;
  settings: PosSettingsRuntimeService;
  capabilities: PosRuntimeCapabilities;
}>;

/** 仅组合根持有；initialize 完成后才能把 services 交给 React route。 */
export type ProductionPosRuntimeComposition = ProductionPosRuntimeServices &
  Readonly<{
    initialize(): Promise<void>;
    shutdownBackgroundWork(): Promise<void>;
  }>;

export type RuntimeClock = Readonly<{
  now(): Date;
  nowIso(): string;
}>;

export type PaymentRuntimeConfiguration = Readonly<{
  bootstrap: PaymentProviderRuntimeBootstrap;
  /** 只用于 Linkly 订单 material；未配置时其他 tender 仍可同步。 */
  linklyEnvironment: LinklyOrderSyncEnvironment | null;
}>;

export type InstallmentRuntimeConfiguration = Readonly<{
  bootstrap: PaymentProviderRuntimeBootstrap;
}>;

export type CashierLockRuntimeConfiguration = Readonly<{
  revokeTemporaryAuthorizations?(): void;
  onLocked(): void;
}>;

export type CashierSessionSecurityConfiguration = Readonly<{
  getDeviceIdentity(): Promise<Readonly<{
    storeCode: string;
    deviceCode: string;
  }> | null>;
  clearAuthorization(): Promise<void>;
  subscribeSessionInvalidation(listener: () => void): (() => void) | void;
}>;

export type OperationAuthorizationRuntimeConfiguration = Pick<
  OperationAuthorizationServiceOptions,
  "cashierAuthentication"
>;

export type ProductionSettingsRuntimeConfiguration = Pick<
  ProductionSettingsCompositionInput,
  | "apiBaseUrl"
  | "appVersion"
  | "updateChannel"
  | "squareSetup"
  | "readDevicePresentation"
  | "paymentConfiguration"
  | "apiConfiguration"
  | "runtimeReload"
  | "device"
  | "scanner"
  | "appUpdate"
> &
  Readonly<{
    /**
     * Settings 与履约必须拿到同一个惰性原生 adapter 实例，避免分别连接同一 BLE
     * 外设；createPrinter 仍保持旧测试所需的窄返回类型。
     */
    printer: ProductionSettingsCompositionInput["printer"];
  }>;

export type ProductionAttendanceAuditRuntimeConfiguration = Omit<
  ProductionAttendanceAuditRuntimeDependencies,
  "currentCashier" | "terminal"
>;

type FulfilmentHardwarePort = Pick<PrinterPort, "connect" | "print"> &
  Pick<CashDrawerPort, "open">;
type SalesCashCheckoutPort = Pick<DurableCashCheckoutService, "complete">;
type TerminalCartInitializer = (() => Promise<void>) &
  Readonly<{ isReady(): boolean }>;

export type ProductionPosRuntimeCompositionDependencies = Readonly<{
  /** 只在组合根使用；绝不出现在 ProductionPosRuntimeServices。 */
  database: PosDatabase;
  transport: HbposTransport;
  encryptor: SensitivePayloadEncryptor;
  syncSecurity: SyncSecurityPort;
  auditMetadata: HbposAuditMetadata;
  supportAppId: string;
  clock: RuntimeClock;
  systemUptimeMilliseconds?: (() => number) | undefined;
  createId(): string;
  random(): number;
  sha256Hex(material: string): Promise<string>;
  /** 测试可注入 Node 摘要；生产省略时仍使用 Expo 原生 SHA256。 */
  catalogPageDigest?: CatalogPageDigest | undefined;
  createPrinter(): FulfilmentHardwarePort;
  externalDisplay?: ExternalCustomerDisplayPort | undefined;
  customerDisplayAdvertisementCacheRootUri?: string | undefined;
  advertisementCache?:
    | CustomerDisplayAdvertisementCachePort
    | undefined;
  businessTimeZone?: string | undefined;
  connectivity: PaymentConnectivityPort;
  cashierAuthentication: Pick<CashierAuthenticationService, "login">;
  cashierSessionSecurity: CashierSessionSecurityConfiguration;
  /** 未获得远端或可信缓存策略时保持 unchecked，必须 fail-closed。 */
  newTransactionGate: Readonly<{
    getGate(): NewTransactionGate;
  }>;
  appUpdateTransition?: UpdateTransitionLeaseCoordinator;
  cashierLock?: CashierLockRuntimeConfiguration | undefined;
  operationAuthorization?:
    | OperationAuthorizationRuntimeConfiguration
    | undefined;
  returnCapacity?: ((cart: CartSnapshot) => Promise<boolean>) | undefined;
  payments?: PaymentRuntimeConfiguration | undefined;
  installments?: InstallmentRuntimeConfiguration | undefined;
  settings?: ProductionSettingsRuntimeConfiguration | undefined;
  attendanceAudit?:
    | ProductionAttendanceAuditRuntimeConfiguration
    | undefined;
}>;

/**
 * 生产组合根：所有能改变账本、硬件或同步状态的服务共享同一 SQLCipher 数据库、
 * 加密器、时钟和 ID 工厂。route 仅获得上面的窄业务面。
 */
export function createProductionPosRuntimeServices(
  input: ProductionPosRuntimeCompositionDependencies,
): ProductionPosRuntimeComposition {
  // 员工审计 scope 仅在组合根交给持久层；feature 继续只写业务事实，不能伪造上传身份。
  const repositories = input.database.repositories(
    input.encryptor,
    input.createId,
    input.auditMetadata,
  );
  const currentCashier = new CurrentCashierSession(
    input.systemUptimeMilliseconds,
    () => {
      // 内存 session/lease 已由 CurrentCashierSession 先失效；Keychain 清理异步收口。
      void input.cashierSessionSecurity
        .clearAuthorization()
        .catch(() => undefined);
    },
  );
  let customerDisplayAdvertisementPlayback:
    | CustomerDisplayAdvertisementPlayback
    | null = null;
  const operationAuthorization = input.operationAuthorization
    ? new OperationAuthorizationService({
        cashierAuthentication:
          input.operationAuthorization.cashierAuthentication,
        audit: repositories.audit,
        createId: input.createId,
        nowIso: input.clock.nowIso,
      })
    : null;
  const invalidateCurrentCashier = () => {
    customerDisplayAdvertisementPlayback?.stop();
    currentCashier.clear();
    operationAuthorization?.clearRequestingCashier();
    try {
      void customerDisplaySensitiveContentGuard
        ?.clearSensitiveContent()
        .catch(() => {
          // 原生 producer end 仍会在 JS 销毁时清空旧快照。
        });
    } catch {
      // 客显尚未初始化也不能阻止收银员会话 fail-closed。
    }
  };
  // 401、403、手动锁屏和 runtime 退出必须始终清除可信收银员；
  // 这条安全边界不能依赖可选的主管代授权 capability。
  input.cashierSessionSecurity.subscribeSessionInvalidation(
    invalidateCurrentCashier,
  );
  const baseCashierSession = createCashierSessionFacade(
    input.cashierAuthentication,
    input.cashierSessionSecurity,
    currentCashier,
    operationAuthorization,
    input.auditMetadata,
  );
  const resolveRemoteHistoryTrustedSession = () => {
      const session = currentCashier.require();
      assertTrustedCashierScope(session, input.auditMetadata);
      return {
        trustedStoreCode: session.storeCode,
        currentDeviceCode: session.deviceCode,
        permissionCodes: session.permissionCodes,
      };
    };
  const cashierLock = input.cashierLock
    ? new CashierLockService({
        authorization: {
          clear: input.cashierSessionSecurity.clearAuthorization,
        },
        audit: repositories.audit,
        temporaryAuthorizations: {
          revokeAll: () => {
            invalidateCurrentCashier();
            input.cashierLock?.revokeTemporaryAuthorizations?.();
          },
        },
        onLocked: input.cashierLock.onLocked,
        createId: input.createId,
        nowIso: input.clock.nowIso,
      })
    : null;
  // 与 WPF 一致，手动锁屏只更换活动收银员；共享 session 在本次 runtime 内
  // 唯一持有购物车，页面、挂单和结账都不能保存旧 PricingCart 实例。
  const activePricingCart = new ActivePricingCartSession(
    new PricingCart(),
    () => new PricingCart(),
    input.appUpdateTransition
      ? {
          canStartMutation: () =>
            !input.appUpdateTransition?.isTransitionActive(),
        }
      : {},
  );
  const catalogRepository = input.database.catalogSnapshots();
  const catalogLookupOverlay = input.database.catalogLookupOverlay();
  const localSalesCatalog = {
    findExact: (lookupCode: string) =>
      catalogLookupOverlay.findExact(
        input.auditMetadata.storeCode,
        lookupCode,
      ),
    searchByName: (
      query: string,
      limit: number,
      offset?: number,
    ) =>
      catalogLookupOverlay.searchByName(
        input.auditMetadata.storeCode,
        query,
        limit,
        offset,
      ),
  };
  const promotionSnapshotLoader = new ActivePromotionSnapshotLoader(
    activePricingCart,
    {
      loadActive: ({ storeCode }) =>
        catalogRepository.loadActivePromotions(storeCode),
    },
  );
  const customerDisplayPublisher = input.externalDisplay
    ? new CustomerDisplayPublisher(
        input.externalDisplay,
        input.customerDisplayAdvertisementCacheRootUri
          ? {
              advertisementCacheRootUri:
                input.customerDisplayAdvertisementCacheRootUri,
            }
          : {},
      )
    : null;
  const customerDisplayCoordinator = customerDisplayPublisher
    ? new CustomerDisplayCoordinator(
        activePricingCart,
        customerDisplayPublisher,
      )
    : null;
  const customerDisplaySensitiveContentGuard =
    customerDisplayCoordinator && input.externalDisplay
      ? new CustomerDisplaySensitiveContentGuard(
          customerDisplayCoordinator,
          input.externalDisplay,
        )
      : null;
  customerDisplayAdvertisementPlayback =
    customerDisplayCoordinator && input.advertisementCache
      ? new CustomerDisplayAdvertisementPlayback({
          remote: new HbposAdvertisementApi(input.transport),
          cache: input.advertisementCache,
          sink: customerDisplayCoordinator,
          now: input.clock.now,
        })
      : null;
  const cashierSession: PosCashierSessionRuntimeService = {
    signIn: async (userBarcode) => {
      const summary = await baseCashierSession.signIn(userBarcode);
      await promotionSnapshotLoader.load({
        storeCode: summary.storeCode,
        asOfIso: input.clock.nowIso(),
      });
      customerDisplayAdvertisementPlayback?.start(summary.storeCode);
      try {
        await repositories.audit.append([{
          eventId: createOperationAuditId(),
          eventType: "CASHIER_LOGIN",
          occurredAtIso: input.clock.nowIso(),
          orderGuid: null,
          correlationId: createOperationAuditId(),
          payload: {
            outcome: "Succeeded",
            source: "ipad-pos",
            action: "cashier-login",
            requestingCashierId: summary.cashierId,
            requestingCashierName: summary.cashierName,
            requestingUserGuid: summary.userGuid,
            isOfflineCached: summary.source === "offline-cache",
            isEmergencyOverride: summary.source === "emergency-override",
          },
        }]);
      } catch {
        // 已完成的可信登录不能因旁路审计故障被回滚或重新暴露旧会话。
      }
      return summary;
    },
  };
  const catalogRevalidation =
    new RemoteCatalogLookupRevalidationService({
    storeCode: input.auditMetadata.storeCode,
    local: localSalesCatalog,
    remote: new HbposCatalogLookupApi(input.transport),
    overlay: catalogLookupOverlay,
    isOnline: () => input.connectivity.isOnline(),
  });
  const specialProductsRepository = input.database.specialProducts();
  const dailyCloseRepository = input.database.dailyCloses();
  const specialProductsRemote = new HbposSpecialProductsApi(input.transport);
  const settingsRepository = input.database.settings();
  let dailyCloseReceiptSettings: ReceiptPrinterSettings | null = null;
  const offlineReturnCapacity = input.database.offlineReturnCapacity();
  const returnCapacity =
    input.returnCapacity ??
    ((cart: CartSnapshot) => offlineReturnCapacity.hasCapacity(cart));
  const fulfilmentStore = input.database.fulfilmentStore(
    input.encryptor,
    input.createId,
  );
  const voucherBalanceMaterials =
    input.database.voucherBalanceMaterials?.(input.encryptor) ?? null;
  let voucherBalancePostSync: VoucherBalancePostSyncService | null = null;
  const recoverVoucherBalancePrints = async (): Promise<void> => {
    try {
      await voucherBalancePostSync?.recoverPendingPrints();
    } catch {
      // 已同步订单不能因旁路打印恢复失败而阻止启动、前台恢复或设置保存。
    }
  };
  const baseReceiptSettings =
    receiptSettingsService(settingsRepository);
  const receiptSettings: PosReceiptSettingsService = {
    get: () => baseReceiptSettings.get(),
    save: async (settings) => {
      const saved =
        await settingsRepository.saveReceiptPrinterSettings(settings);
      dailyCloseReceiptSettings = saved;
      void recoverVoucherBalancePrints();
      return saved;
    },
  };
  const createCashCheckout = (cashierLease: TrustedCashierLease) =>
    createSessionCashCheckout(
      input,
      repositories,
      settingsRepository,
      returnCapacity,
      cashierLease,
      activePricingCart,
    );
  const heldCartInitialize = createTerminalCartInitializer(
    repositories.heldOrderRecords,
    activePricingCart,
    {
      storeCode: input.auditMetadata.storeCode,
      deviceCode: input.auditMetadata.deviceCode,
    },
  );
  const catalogue = new CatalogSnapshotService(
    catalogRepository,
    new HbposCatalogPageApi(input.transport, input.catalogPageDigest),
    {
      createSnapshotId: input.createId,
      nowIso: input.clock.nowIso,
    },
  );
  catalogue.resumeRetiredCleanup();
  const catalogRefreshCoordinator = new CatalogRefreshCoordinator();
  const runUpdateOperation = <T>(
    operation: () => T | Promise<T>,
  ): Promise<T> => {
    if (input.appUpdateTransition) {
      return input.appUpdateTransition.runOperation(operation);
    }
    try {
      return Promise.resolve(operation());
    } catch (error) {
      return Promise.reject(error);
    }
  };
  input.appUpdateTransition?.bindTransitionBarrier((operation) =>
    // 固定锁序：transition 已封门并等普通 operation 清零，再取原始目录门，
    // 最后等活动购物车并持锁完成最终安全快照与更新动作。
    catalogRefreshCoordinator.runExclusive(() =>
      runUpdateTransitionWithActiveCart(activePricingCart, operation),
    ),
  );
  const executeCatalogDownload = (
    cashierLease: TrustedCashierLease,
    requestedStoreCode: string,
    signal: AbortSignal,
    observer: CatalogRefreshProgressObserver,
  ) => {
    requireCatalogDownloadLease(
      cashierLease,
      input.auditMetadata,
      requestedStoreCode,
    );
    return downloadRuntimeCatalog(
      catalogue,
      catalogRepository,
      promotionSnapshotLoader,
      cashierLease,
      input.auditMetadata,
      input.clock.nowIso,
      signal,
      observer,
    ).then((outcome) => {
      void catalogLookupOverlay
        .cleanupOldGenerations()
        .catch(() => undefined);
      return outcome;
    });
  };
  const startCatalogRefresh = (
    requestedStoreCode: string,
    callerSignal?: AbortSignal | undefined,
    observer?: CatalogRefreshProgressObserver | undefined,
  ) =>
    runUpdateOperation(() => {
      throwIfCatalogRequestAborted(callerSignal);
      const cashierLease = currentCashier.createLease();
      const session = requireCatalogDownloadLease(
        cashierLease,
        input.auditMetadata,
        requestedStoreCode,
      );
      return catalogRefreshCoordinator.start({
        storeCode: session.storeCode,
        execute: ({ signal, onProgress }) =>
          executeCatalogDownload(
            cashierLease,
            session.storeCode,
            signal,
            (event) => {
              onProgress(event);
              observer?.(event);
            },
          ),
      });
    });
  const specialProducts: SpecialProductsRuntimeFactory = {
    createPresenter: () => {
      const cashierLease = currentCashier.createLease();
      const initialSession = cashierLease.get();
      assertTrustedCashierScope(initialSession, input.auditMetadata);
      const requireActiveSession = () => {
        const session = cashierLease.get();
        assertTrustedCashierScope(session, input.auditMetadata);
        return session;
      };
      const assertStore = (storeCode: string) => {
        const session = requireActiveSession();
        if (storeCode.trim() !== session.storeCode) {
          throw new Error(
            "Special products store does not match the current cashier.",
          );
        }
        return session;
      };
      return new SpecialProductsPresenter({
        storeCode: initialSession.storeCode,
        permissions: initialSession.permissionCodes,
        // route 会马上发布真实网络状态；默认离线可避免首帧误开放管理写操作。
        initialOnline: false,
        repository: {
          list: async (storeCode, limit, offset) => {
            assertStore(storeCode);
            const result = await specialProductsRepository.list(
              storeCode,
              limit,
              offset,
            );
            requireActiveSession();
            return result;
          },
          searchCandidates: async (storeCode, query, limit) => {
            assertStore(storeCode);
            const result =
              await specialProductsRepository.searchCandidates(
                storeCode,
                query,
                limit,
              );
            requireActiveSession();
            return result;
          },
          replaceDownloaded: async (storeCode, items) => {
            assertStore(storeCode);
            await specialProductsRepository.replaceDownloaded(
              storeCode,
              items,
            );
            requireActiveSession();
          },
          applyMark: async (
            storeCode,
            productCode,
            isSpecialProduct,
            items,
          ) => {
            assertStore(storeCode);
            await specialProductsRepository.applyMark(
              storeCode,
              productCode,
              isSpecialProduct,
              items,
            );
            requireActiveSession();
          },
          saveOrder: async (storeCode, orderedProductCodes) => {
            assertStore(storeCode);
            await specialProductsRepository.saveOrder(
              storeCode,
              orderedProductCodes,
            );
            requireActiveSession();
          },
        },
        remote: {
          getPage: async (request) => {
            assertStore(request.storeCode);
            const result = await specialProductsRemote.getPage(request);
            requireActiveSession();
            return result;
          },
          mark: async (request) => {
            assertStore(request.storeCode);
            const result = await specialProductsRemote.mark(request);
            requireActiveSession();
            return result;
          },
        },
        addToCart: {
          add: async (item) => {
            const session = assertStore(item.storeCode);
            if (
              !session.permissionCodes.includes(
                SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION,
              )
            ) {
              throw new Error(
                "Special products add-to-cart permission is required.",
              );
            }
            if (
              activePricingCart.getSnapshot().lines.length === 0 &&
              !input.newTransactionGate.getGate().canStartNewTransaction
            ) {
              throw Object.assign(
                new Error(
                  "New transactions are disabled by the iPad policy gate.",
                ),
                { code: SALES_NEW_TRANSACTIONS_DISABLED },
              );
            }
            return activePricingCart.addItemWithDisposition({
              lineId: input.createId(),
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
          },
        },
      });
    },
  };
  const receiptOrderSource = new OrderRepositoryReceiptReprintSource(
    repositories.orders,
  );
  const receiptSettlements = receiptCompletionSettlementSource(input.database);
  const frozenReceiptReprintSettings = receiptReprintSettings(settingsRepository);
  const receiptReprint = new ReceiptReprintPreparationService({
    orders: receiptOrderSource,
    settings: frozenReceiptReprintSettings,
    settlements: receiptSettlements,
    nowIso: input.clock.nowIso,
  });
  const remoteHistoryReceiptReprint =
    new RemoteHistoryReceiptReprintPreparationService({
      history: {
        async getDetails(orderGuid) {
          const session = currentCashier.require();
          assertTrustedCashierScope(session, input.auditMetadata);
          return new HbposRemoteHistoryApi(
            input.transport,
            session.storeCode,
          ).getDetails(orderGuid);
        },
      },
      settings: frozenReceiptReprintSettings,
      trustedStoreCode: input.auditMetadata.storeCode,
    });
  const localHistoryReceiptPreview = new LocalHistoryReceiptPreviewService({
    orders: receiptOrderSource,
    settings: receiptPreviewSettings(settingsRepository),
    settlements: receiptSettlements,
  });
  const printer = input.createPrinter();
  const fulfilment = new FulfilmentService({
    store: fulfilmentStore,
    printer,
    drawer: printer,
    nowIso: input.clock.nowIso,
    createAuditId: input.createId,
    createCorrelationId: input.createId,
    auditScope: {
      storeCode: input.auditMetadata.storeCode,
      deviceCode: input.auditMetadata.deviceCode,
    },
    prepareLastReceiptReprint: () => receiptReprint.prepareLast(),
    prepareReceiptReprint: (orderGuid, source) =>
      source === "remote-history"
        ? remoteHistoryReceiptReprint.prepare(orderGuid)
        : receiptReprint.prepareCurrent(orderGuid),
    prepareManualDrawerOpen: async () => {
      // 手动动作只能使用本次从持久设置读取并冻结的外设；配置损坏或禁用时不猜测。
      const current = await settingsRepository.getReceiptPrinterSettings();
      if (
        !current.drawerEnabled ||
        !isValidPeripheralId(current.peripheralId)
      ) {
        return null;
      }
      return Object.freeze({ printerId: current.peripheralId });
    },
    ...(input.appUpdateTransition
      ? { operationLease: input.appUpdateTransition }
      : {}),
  });
  if (voucherBalanceMaterials) {
    voucherBalancePostSync = new VoucherBalancePostSyncService({
      api: new PostSyncVoucherLatestBalanceApi(
        new VoucherHbposApi(input.transport),
      ),
      materials: voucherBalanceMaterials,
      renderer: new VoucherBalanceReceiptRenderer(
        returnReceiptSettings(settingsRepository),
      ),
      printQueue: fulfilmentStore,
      nowIso: input.clock.nowIso,
      requestPrintDrain: () => fulfilment.drainAutomaticQueue(),
    });
  }
  const remoteHistory = createHbposRemoteHistoryPresenterFactory(
    input.transport,
    resolveRemoteHistoryTrustedSession,
    () => {
      const cashierLease = currentCashier.createLease();
      const assertActive = () => {
        const active = trustedSalesSession(
          cashierLease,
          input.auditMetadata,
        );
        if (
          !active.permissionCodes.includes(
            REMOTE_HISTORY_REPRINT_PERMISSION,
          )
        ) {
          throw new Error("REMOTE_HISTORY_REPRINT_PERMISSION_REQUIRED");
        }
        return active;
      };
      return {
        canReprint: isRemoteHistoryReceiptReprintEligible,
        async reprintExistingOrder(orderGuid) {
          const session = assertActive();
          const result = await fulfilment.reprintReceipt(
            orderGuid,
            "remote-history",
            {
              actionId: input.createId(),
              permissionCode: REMOTE_HISTORY_REPRINT_PERMISSION,
              authorizationMode: "current-cashier",
              requestingCashierId: session.cashierId,
              requestingCashierName: session.cashierName,
              requestingUserGuid: session.userGuid,
              authorizingCashierId: null,
            },
            assertActive,
          );
          if (result.state !== "Printed") {
            throw Object.assign(
              new Error("Remote history receipt reprint failed."),
              {
                code:
                  result.errorCode ??
                  `REPRINT_${result.state.toUpperCase().replaceAll("-", "_")}`,
              },
            );
          }
        },
      };
    },
  );
  const localHistory = createLocalHistoryRuntime(
    input,
    currentCashier,
    fulfilment,
    localHistoryReceiptPreview,
  );
  const coordinator = new PosSyncCoordinator({
    outbox: repositories.outbox,
    auditRepository: repositories.audit,
    auditDelivery: repositories.auditDelivery,
    orderSync: new HbposOrderSyncAdapter(
      input.transport,
      repositories.orders,
      {
        resolver: input.database.orderSyncMaterial(
          input.encryptor,
          input.createId,
        ),
        linklyEnvironment: input.payments?.linklyEnvironment ?? null,
      },
      voucherBalancePostSync,
    ),
    auditUploader: new HbposAuditBatchAdapter(
      input.transport,
      repositories.orders,
      input.auditMetadata,
    ),
    security: input.syncSecurity,
    now: input.clock.now,
    random: input.random,
    ...(input.appUpdateTransition
      ? { operationLease: input.appUpdateTransition }
      : {}),
  });
  const postCommitWork = createPostCommitWorkDrain(
    () => fulfilment.drainAutomaticQueue(),
    () => coordinator.requestDrain(),
  );
  const lifecycle = new SyncLifecycleController(coordinator);
  const syncHistory = createSyncHistoryRuntime(
    input,
    coordinator,
    currentCashier,
  );
  const createSalesCashCheckout = (cashierLease: TrustedCashierLease) =>
    createPostCommitFulfilmentCashCheckout(
      createCashCheckout(cashierLease),
      postCommitWork,
    );
  const createTerminalActionAuthorization = () => {
    const cashierLease = currentCashier.createLease();
    trustedSalesSession(cashierLease, input.auditMetadata);
    return Object.freeze({
      authorization:
        operationAuthorization ??
        createCurrentCashierSalesAuthorization(
          cashierLease,
          input.auditMetadata,
        ),
      assertActive: () => {
        trustedSalesSession(cashierLease, input.auditMetadata);
      },
      actor: () => {
        const session = trustedSalesSession(
          cashierLease,
          input.auditMetadata,
        );
        return Object.freeze({
          cashierId: session.cashierId,
          cashierName: session.cashierName,
          userGuid: session.userGuid,
        });
      },
    });
  };
  const startCatalogReset = (callerSignal: AbortSignal) =>
    runUpdateOperation(() => {
      throwIfCatalogRequestAborted(callerSignal);
      const cashierLease = currentCashier.createLease();
      const session = requireSettingsCatalogSession(
        cashierLease,
        input.auditMetadata,
        input.auditMetadata.storeCode,
        SETTINGS_CATALOG_RESET_PERMISSION,
      );
      return catalogRefreshCoordinator.start({
        storeCode: session.storeCode,
        execute: async ({ signal, onProgress }) => {
          const outcome = await downloadSettingsCatalog(
            catalogue,
            catalogRepository,
            promotionSnapshotLoader,
            cashierLease,
            input.auditMetadata,
            input.clock.nowIso,
            true,
            signal,
            onProgress,
          );
          void catalogLookupOverlay
            .cleanupOldGenerations()
            .catch(() => undefined);
          return outcome;
        },
      });
    });
  const reprint = createAuthorizedFulfilmentAction({
    permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
    action: "reprint-last-receipt",
    createActionId: input.createId,
    createAuthorization: createTerminalActionAuthorization,
    execute: (authorization, assertActive) =>
      fulfilment.reprintLastReceipt(authorization, assertActive),
  });
  const currentReceiptReprint = createAuthorizedFulfilmentAction<
    [orderGuid: string]
  >({
    permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
    action: "reprint-current-receipt",
    createActionId: input.createId,
    createAuthorization: createTerminalActionAuthorization,
    operationKey: (orderGuid) => orderGuid,
    execute: (authorization, assertActive, orderGuid) =>
      fulfilment.reprintCurrentReceipt(
        orderGuid,
        authorization,
        assertActive,
      ),
  });
  const openCashDrawer = createAuthorizedFulfilmentAction({
    permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
    action: "open-cash-drawer",
    createActionId: input.createId,
    createAuthorization: createTerminalActionAuthorization,
    execute: (authorization, assertActive) =>
      fulfilment.openDrawerManually(authorization, assertActive),
  });

  const paymentRuntime = createProductionPaymentRuntime({
    database: input.database,
    repositories,
    encryptor: input.encryptor,
    activeCart: activePricingCart,
    currentCashier,
    terminal: input.auditMetadata,
    clock: input.clock,
    createId: input.createId,
    connectivity: input.connectivity,
    bootstrap: input.payments?.bootstrap,
    drainFulfilment: postCommitWork,
  });
  const payments = paymentRuntime.service;
  const installmentConfiguration = input.installments;
  const installmentActionStore = installmentConfiguration
    ? input.database.installmentActions(input.encryptor)
    : null;
  const installments: PosInstallmentsRuntimeService =
    installmentConfiguration
    ? (() => {
        const actionStore = installmentActionStore;
        if (!actionStore) {
          throw new Error(
            "Installment action persistence is unavailable.",
          );
        }
        const bootstrap = installmentConfiguration.bootstrap;
        const persistence =
          input.database.installmentPaymentPersistence(
            input.encryptor,
            input.createId,
          );
        bootstrap.bindVoucherContextProvider(
          persistence.voucherContextForAttempt,
        );
        const installmentPayments =
          new ProductionInstallmentPaymentAdapter({
            store: persistence.providerAttempts,
            providers: bootstrap.providers,
            cardProviderSelection: {
              async loadEnabledProviders() {
                return bootstrap.providers
                  .listAvailableProviders()
                  .filter(
                    (
                      provider,
                    ): provider is InstallmentCardProvider =>
                      provider === "square" ||
                      provider === "linkly-cloud",
                  );
              },
            },
            provenance: new HbposInstallmentRefundProvenance(
              input.transport,
              persistence.refundProvenance,
            ),
            voucherMaterials: persistence.voucherMaterials,
            createId: input.createId,
            nowIso: input.clock.nowIso,
          });
        return createProductionInstallmentRuntime({
          currentCashier,
          terminal: input.auditMetadata,
          activeCart: activePricingCart,
          connectivity: input.connectivity,
          api: new HbposInstallmentsApi(
            input.transport,
            input.auditMetadata.storeCode,
          ),
          snapshotCache:
            input.database.installmentSnapshots(input.encryptor),
          actionStore,
          payments: installmentPayments,
          voucherIntents: persistence.voucherIntents,
          sha256Hex: input.sha256Hex,
          createId: input.createId,
          nowIso: input.clock.nowIso,
        });
      })()
    : {
        status: "unavailable",
        reason: "INSTALLMENT_PAYMENT_PERSISTENCE_MISSING",
      };
  const returns: PosReturnsRuntimeService = operationAuthorization
    ? createAvailableReturnRuntime({
        input,
        repositories,
        settings: settingsRepository,
        currentCashier,
        authorization: operationAuthorization,
        providerRefund: paymentRuntime.returnRefund,
        requestOrderSyncDrain: () => coordinator.requestDrain(),
      })
    : {
        status: "unavailable",
        reason: "SUPERVISOR_AUTHENTICATION_MISSING",
      };
  const settings: PosSettingsRuntimeService = input.settings
    ? createProductionSettingsComposition({
        ...input.settings,
        currentCashier,
        terminal: input.auditMetadata,
        activeCart: activePricingCart,
        createId: input.createId,
        catalog: {
          getActiveMetadata: () =>
            catalogRepository.getActiveMetadata(),
          getRefreshState: catalogRefreshCoordinator.getState,
          subscribeRefresh: catalogRefreshCoordinator.subscribe,
          runExclusive: (operation) =>
            runUpdateOperation(() =>
              catalogRefreshCoordinator.runExclusive(operation),
            ),
          download: async (signal) =>
            (
              await startCatalogRefresh(
                input.auditMetadata.storeCode,
                signal,
              )
            ).summary,
          reset: async (signal) =>
            (await startCatalogReset(signal)).summary,
        },
        receiptSettings,
        paymentConfigurationTransition: {
          run: (operation) => {
            // 支付配置保存会整应用 reload，必须复用同步、履约与目录共享的全局封门。
            if (!input.appUpdateTransition) {
              return Promise.reject(
                Object.assign(
                  new Error("Payment configuration transition is unavailable."),
                  { code: "PAYMENT_CONFIGURATION_TRANSITION_UNAVAILABLE" },
                ),
              );
            }
            return input.appUpdateTransition.runTransition(operation);
          },
        },
        pendingData: {
          read: async () => {
            const durable =
              await input.database.settingsSafety().read();
            const [
              paymentRecoveryRequired,
              returnRecoveryRequired,
              installmentRecoveryRequired,
            ] = await Promise.all([
              payments.status === "available"
                ? payments.hasRecoveryRequired()
                : Promise.resolve(false),
              returns.status === "available"
                ? returns.hasRecoveryRequired()
                : Promise.resolve(false),
              installmentActionStore
                ? installmentActionStore
                    .loadBlocking(input.auditMetadata)
                    .then((action) => action !== null)
                : Promise.resolve(false),
            ]);
            return Object.freeze({
              ...durable,
              hasFulfilmentInFlight: fulfilment.isHardwareBusy(),
              hasSyncOrAuditInFlight: coordinator.isDraining(),
              // 支付配置允许普通已耐久业务继续排队，但任何支付、分期或退货恢复
              // 都可能依赖旧通道，必须统一进入支付配置专用阻断信号。
              unresolvedPaymentCount: Math.max(
                durable.unresolvedPaymentCount,
                paymentRecoveryRequired ||
                  installmentRecoveryRequired ||
                  returnRecoveryRequired
                  ? 1
                  : 0,
              ),
              pendingReturnCount: Math.max(
                durable.pendingReturnCount,
                returnRecoveryRequired ? 1 : 0,
              ),
            });
          },
        },
        cashDrawerTest: {
          execute: () => openCashDrawer.execute(),
        },
        printer: input.settings.printer,
        ...(input.externalDisplay
          ? { externalDisplay: input.externalDisplay }
          : {}),
      })
    : {
        status: "unavailable",
        reason: "SETTINGS_ADAPTER_MISSING",
      };
  const attendanceAudit: PosAttendanceAuditRuntimeService =
    input.attendanceAudit
      ? createProductionAttendanceAuditRuntime({
          ...input.attendanceAudit,
          currentCashier,
          terminal: input.auditMetadata,
        })
      : {
          status: "unavailable",
          reason: "ATTENDANCE_SECURITY_ADAPTER_MISSING",
        };
  const initialize = createCombinedTerminalInitializer(
    heldCartInitialize,
    paymentRuntime.initializeRecovery,
    async () => {
      dailyCloseReceiptSettings =
        await settingsRepository.getReceiptPrinterSettings();
      await customerDisplayCoordinator?.initialize();
      await recoverVoucherBalancePrints();
    },
  );
  const createHeldOrdersOrchestrator = (
    cashierLease: TrustedCashierLease = currentCashier.createLease(),
  ): HeldOrdersOrchestrator => {
    const session = trustedSalesSession(cashierLease, input.auditMetadata);
    if (!operationAuthorization) {
      throw new Error("Supervisor authorization is unavailable.");
    }
    return new HeldOrdersOrchestrator({
      repository: repositories.heldOrderRecords,
      activeCart: createHeldActiveCartPort(activePricingCart),
      authorization: createHeldOrderAuthorization(
        operationAuthorization,
        input.createId,
        cashierLease,
      ),
      identity: session,
      createId: input.createId,
      nowIso: input.clock.nowIso,
    });
  };
  const capabilities: PosRuntimeCapabilities = {
    catalog: { status: "available" },
    cashCheckout: { status: "available" },
    fulfilment: { status: "available" },
    // 无原生模块时 adapter 本身会返回明确失败结果，启动不触碰硬件也不会崩溃。
    printerAdapter: { status: "available" },
    // 钱箱适配器已经接通；每笔现金单的 drawerDisposition 再按冻结的登录权限决定。
    cashDrawer: { status: "available" },
    receiptReprint: { status: "available" },
    // SQLCipher 始终提供只读容量预检；最终额度仍由现金订单同一事务内的 CAS 扣减。
    offlineReturns: { status: "available" },
    returns: returns.status === "available"
      ? { status: "available" }
      : { status: "unavailable", reason: returns.reason },
    payments: payments.status === "available"
      ? { status: "available" }
      : { status: "unavailable", reason: payments.blockers.join(",") },
    installments:
      "createPresenter" in installments
        ? { status: "available" }
        : { status: "unavailable", reason: installments.reason },
    supervisorAuthorization: operationAuthorization
      ? { status: "available" }
      : {
          status: "unavailable",
          reason: "SUPERVISOR_AUTHENTICATION_MISSING",
        },
  };

  const catalog: PosCatalogRuntimeService = {
    findExact: (lookupCode) =>
      localSalesCatalog.findExact(lookupCode),
    searchByName: (query, limit, offset) =>
      localSalesCatalog.searchByName(query, limit, offset),
    getCurrentCatalog: async (request) => {
      throwIfCatalogRequestAborted(request.signal);
      requireCatalogDownloadSession(
        currentCashier,
        input.auditMetadata,
        request.storeCode,
      );
      const summary = await catalogRepository.getActiveMetadata();
      throwIfCatalogRequestAborted(request.signal);
      return summary;
    },
    getRefreshState: catalogRefreshCoordinator.getState,
    subscribeRefresh: catalogRefreshCoordinator.subscribe,
    downloadAndActivate: (request) =>
      runUpdateOperation(() => {
        throwIfCatalogRequestAborted(request.signal);
        const cashierLease = currentCashier.createLease();
        return executeCatalogDownload(
          cashierLease,
          request.storeCode,
          request.signal ?? new AbortController().signal,
          request.onProgress ?? (() => undefined),
        );
      }),
  };

  return {
    initialize,
    shutdownBackgroundWork: () => catalogRefreshCoordinator.shutdown(),
    attendanceAudit,
    appUpdateSafety: {
      getSnapshot: async () => {
        const cartBefore = activePricingCart.getSnapshot().lines.length > 0;
        const writeBefore =
          activePricingCart.hasPendingExclusiveOperation() &&
          input.appUpdateTransition?.isCriticalSectionActive() !==
            true;
        let hasUnresolvedPayment = true;
        let hasRecoveryRequired = true;
        try {
          const regularPaymentRecovery =
            payments.status === "available"
              ? await payments.hasRecoveryRequired()
              : false;
          const installmentPaymentRecovery =
            installmentActionStore
              ? (await installmentActionStore.loadBlocking(
                  input.auditMetadata,
                )) !== null
              : false;
          hasUnresolvedPayment =
            regularPaymentRecovery ||
            installmentPaymentRecovery;
        } catch {
          // 无法证明支付已稳定时必须禁止 reload，避免 Unknown 被误当成可安全重启。
        }
        try {
          hasRecoveryRequired =
            returns.status === "available"
              ? await returns.hasRecoveryRequired()
              : false;
        } catch {
          // 退货恢复读取失败同样不能当成安全；保持 true 等待下一次可信快照。
        }
        return Object.freeze({
          hasActiveCart:
            cartBefore ||
            activePricingCart.getSnapshot().lines.length > 0,
          hasUnresolvedPayment,
          hasPendingDurableWrite:
            writeBefore ||
            (activePricingCart.hasPendingExclusiveOperation() &&
              input.appUpdateTransition?.isCriticalSectionActive() !==
                true),
          hasRecoveryRequired,
          hasCatalogRefreshInFlight:
            catalogRefreshCoordinator.getState().kind === "running",
          hasSyncOrAuditInFlight: coordinator.isDraining(),
          hasFulfilmentInFlight: fulfilment.isHardwareBusy(),
        });
      },
    },
    catalog,
    catalogRefresh: catalogRefreshCoordinator,
    receiptSettings,
    fulfilment: {
      drainAutomaticQueue: () => fulfilment.drainAutomaticQueue(),
      retryFailedPrint: (jobId) => fulfilment.retryFailedPrint(jobId),
      retryFailedDrawer: (eventId) => fulfilment.retryFailedDrawer(eventId),
      reprintCurrentReceipt: (orderGuid) =>
        currentReceiptReprint.execute(orderGuid),
      reprint,
      openCashDrawer,
    },
    sync: {
      requestDrain: () => coordinator.requestDrain(),
      onApplicationStarted: () => lifecycle.onApplicationStarted(),
      onForeground: async () => {
        await recoverVoucherBalancePrints();
        return lifecycle.onForeground();
      },
      onNetworkChanged: (isOnline) => lifecycle.onNetworkChanged(isOnline),
      shutdown: () => lifecycle.shutdown(),
    },
    payments,
    operationAuthorization: operationAuthorization
      ? {
          status: "available",
          getState: () => operationAuthorization.getState(),
          subscribe: (listener) => operationAuthorization.subscribe(listener),
          submitSupervisorBarcode: (barcode) =>
            operationAuthorization.submitSupervisorBarcode(barcode),
          cancel: (actionId) => operationAuthorization.cancel(actionId),
        }
      : {
          status: "unavailable",
          reason: "SUPERVISOR_AUTHENTICATION_MISSING",
        },
    cashierSession,
    localHistory,
    remoteHistory,
    specialProducts,
    customerDisplay:
      customerDisplayCoordinator && customerDisplayPublisher
        ? {
            status: "available",
            getStatus: () => customerDisplayPublisher.getStatus(),
            setEnabled: (enabled) =>
              customerDisplayPublisher.setEnabled(enabled),
            subscribe: (listener) =>
              customerDisplayPublisher.subscribe(listener),
            showCart: () => customerDisplayCoordinator.showCart(),
            showPayment: () => customerDisplayCoordinator.showPayment(),
            showChange: (changeCents) =>
              customerDisplayCoordinator.showChange(changeCents),
            showSuccess: (changeCents) =>
              customerDisplayCoordinator.showSuccess(changeCents),
            clearSensitiveContent: () =>
              customerDisplaySensitiveContentGuard!.clearSensitiveContent(),
            setAdvert: (advert) =>
              customerDisplayCoordinator.setAdvert(advert),
            refreshAdvertisements: (force = false) => {
              const playback =
                customerDisplayAdvertisementPlayback;
              if (!playback) return Promise.resolve("unavailable");
              const session = currentCashier.require();
              assertTrustedCashierScope(
                session,
                input.auditMetadata,
              );
              return playback.refresh(session.storeCode, force);
            },
            advanceAdvertisement: () =>
              customerDisplayAdvertisementPlayback?.advance() ??
              Promise.resolve(false),
            stopAdvertisements: () => {
              customerDisplayAdvertisementPlayback?.stop();
            },
          }
        : {
            status: "unavailable",
            reason: "EXTERNAL_DISPLAY_ADAPTER_MISSING",
          },
    dailyClose: {
      createPresenter: () => {
        assertRuntimeInitialized(initialize);
        const cashierLease = currentCashier.createLease();
        const session = trustedSalesSession(
          cashierLease,
          input.auditMetadata,
        );
        const receipt = dailyCloseReceiptSettings;
        if (!receipt) {
          throw new Error("DAILY_CLOSE_SETTINGS_NOT_INITIALIZED");
        }
        const assertActiveScope = (
          storeCode: string,
          deviceCode: string,
        ) => {
          const active = trustedSalesSession(
            cashierLease,
            input.auditMetadata,
          );
          if (
            storeCode !== active.storeCode ||
            deviceCode !== active.deviceCode
          ) {
            throw new Error("DAILY_CLOSE_SCOPE_MISMATCH");
          }
          return active;
        };
        return new DailyClosePresenter({
          businessTimeZone: resolveRuntimeBusinessTimeZone(
            input.businessTimeZone,
          ),
          createId: input.createId,
          audit: repositories.audit,
          identity: {
            cashierId: session.cashierId,
            cashierName: session.cashierName,
            userGuid: session.userGuid,
            deviceCode: session.deviceCode,
            permissions: session.permissionCodes,
            storeCode: session.storeCode,
          },
          now: input.clock.now,
          receiptLocale: receipt.locale,
          receiptPaper: receipt.paper,
          storeName: receipt.storeName || session.storeCode,
          repository: {
            async summarize(scope) {
              assertActiveScope(scope.storeCode, scope.deviceCode);
              const result =
                await dailyCloseRepository.summarize(scope);
              assertActiveScope(
                result.storeCode,
                result.deviceCode,
              );
              return result;
            },
            async saveArchive(commit) {
              const active = assertActiveScope(
                commit.archive.storeCode,
                commit.archive.deviceCode,
              );
              if (
                commit.archive.savedCashierId !== active.cashierId ||
                commit.archive.savedCashierName !== active.cashierName
              ) {
                throw new Error("DAILY_CLOSE_CASHIER_MISMATCH");
              }
              const result =
                await dailyCloseRepository.saveArchive(commit);
              assertActiveScope(
                result.archive.storeCode,
                result.archive.deviceCode,
              );
              return result;
            },
            async getArchive(closeId) {
              const active = trustedSalesSession(
                cashierLease,
                input.auditMetadata,
              );
              const archive =
                await dailyCloseRepository.getArchive(closeId);
              trustedSalesSession(cashierLease, input.auditMetadata);
              if (
                archive &&
                (archive.storeCode !== active.storeCode ||
                  archive.deviceCode !== active.deviceCode)
              ) {
                throw new Error("DAILY_CLOSE_SCOPE_MISMATCH");
              }
              return archive;
            },
            async listArchives(scope) {
              assertActiveScope(scope.storeCode, scope.deviceCode);
              const archives =
                await dailyCloseRepository.listArchives(scope);
              for (const archive of archives) {
                assertActiveScope(
                  archive.storeCode,
                  archive.deviceCode,
                );
              }
              return archives;
            },
          },
          printer: {
            async print(job) {
              assertActiveScope(
                job.archive.storeCode,
                job.archive.deviceCode,
              );
              const currentSettings =
                dailyCloseReceiptSettings ?? receipt;
              if (
                !currentSettings.printEnabled ||
                !currentSettings.peripheralId
              ) {
                throw new Error("DAILY_CLOSE_PRINTER_UNAVAILABLE");
              }
              const document = buildDailyCloseReceipt({
                archive: job.archive,
                locale: currentSettings.locale,
                paper: currentSettings.paper,
                reprint: job.reprint,
                storeName:
                  currentSettings.storeName || session.storeCode,
              });
              await printer.connect(currentSettings.peripheralId);
              const result = await printer.print(
                `daily-close:${job.archive.closeId}:${input.createId()}`,
                dailyCloseReceiptToEscPosBytes(document),
              );
              if (result.status !== "printed") {
                throw Object.assign(
                  new Error("DAILY_CLOSE_PRINT_NOT_CONFIRMED"),
                  {
                    code:
                      result.errorCode ??
                      "DAILY_CLOSE_PRINT_NOT_CONFIRMED",
                  },
                );
              }
              trustedSalesSession(
                cashierLease,
                input.auditMetadata,
              );
            },
          },
        });
      },
    },
    sales: {
      createPresenter: () => {
        assertRuntimeInitialized(initialize);
        const cashierLease = currentCashier.createLease();
        const session = trustedSalesSession(cashierLease, input.auditMetadata);
        const heldOrders = operationAuthorization
          ? createHeldOrdersOrchestrator(cashierLease)
          : null;
        return createConnectedSalesPresenter({
          activeCartSession: activePricingCart,
          catalog,
          catalogRevalidation,
          cashCheckout: createSalesCashCheckout(cashierLease),
          identity: session,
          sessionGuard: {
            assertActive: () => {
              trustedSalesSession(cashierLease, input.auditMetadata);
            },
          },
          newTransactionGate: {
            canStartNewTransaction: () =>
              input.newTransactionGate.getGate()
                .canStartNewTransaction,
          },
          ...(heldOrders
            ? {
                hold: {
                  hold: (cart) =>
                    holdCurrentCart(
                      activePricingCart,
                      heldOrders,
                      cart,
                    ),
                },
              }
            : {}),
          ...(cashierLock
            ? {
                lock: {
                  lock: () =>
                    cashierLock.lock(
                      trustedSalesSession(cashierLease, input.auditMetadata),
                    ),
                },
              }
            : {}),
          createCheckoutIntentId: input.createId,
          createLineId: input.createId,
          operationSecurity: {
            authorization:
              operationAuthorization ??
              createCurrentCashierSalesAuthorization(
                cashierLease,
                input.auditMetadata,
              ),
            audit: repositories.audit,
            createActionId: input.createId,
            createAuditEventId: input.createId,
            nowIso: input.clock.nowIso,
          },
        });
      },
    },
    heldOrders: {
      createPresenter: () => {
        assertRuntimeInitialized(initialize);
        return new HeldOrdersPresenter(createHeldOrdersOrchestrator());
      },
    },
    syncHistory,
    returns,
    installments,
    settings,
    capabilities,
  };
}

function createCurrentCashierSalesAuthorization(
  cashierLease: TrustedCashierLease,
  terminal: HbposAuditMetadata,
): SalesOperationAuthorizationPort {
  return Object.freeze({
    async authorizeAndRun<T>(
      request: Readonly<{ permissionCode: string }>,
      operation: (
        context: SalesAuthorizedOperationContext,
      ) => T | Promise<T>,
    ) {
      const session = trustedSalesSession(cashierLease, terminal);
      if (!session.permissionCodes.includes(request.permissionCode)) {
        return Object.freeze({
          authorized: false as const,
          reason: "PERMISSION_DENIED",
        });
      }
      return Object.freeze({
        authorized: true as const,
        value: await operation({
          authorizationMode: "current-cashier",
          requestingCashierId: session.cashierId,
          authorizingCashierId: null,
          permissionCode: request.permissionCode,
        }),
      });
    },
  });
}

function createAuthorizedFulfilmentAction<
  TArguments extends readonly unknown[] = [],
>(input: Readonly<{
  permissionCode: string;
  action: string;
  createActionId(): string;
  operationKey?(...args: TArguments): string;
  createAuthorization(): Readonly<{
    authorization: SalesOperationAuthorizationPort;
    assertActive(): void;
    actor(): Readonly<{
      cashierId: string;
      cashierName: string | null;
      userGuid: string | null;
    }>;
  }>;
  execute(
    authorization: FulfilmentAuthorizationContext,
    assertActive: () => void,
    ...args: TArguments
  ): Promise<FulfilmentActionResult>;
}>): Readonly<{
  status: "available";
  execute(
    ...args: TArguments
  ): Promise<PosAuthorizedFulfilmentActionResult>;
}> {
  const inFlightByOperation = new Map<
    string,
    Promise<PosAuthorizedFulfilmentActionResult>
  >();

  return Object.freeze({
    status: "available" as const,
    execute(...args: TArguments): Promise<PosAuthorizedFulfilmentActionResult> {
      // 同一 UI 动作在完成前复用同一 Promise 和 actionId，避免双击绕过履约幂等。
      const operationKey = input.operationKey?.(...args) ?? "singleton";
      const inFlight = inFlightByOperation.get(operationKey);
      if (inFlight) return inFlight;

      let started: Promise<PosAuthorizedFulfilmentActionResult>;
      try {
        const authorization = input.createAuthorization();
        const actionId = input.createActionId();
        started = authorization.authorization
          .authorizeAndRun(
            {
              actionId,
              permissionCode: input.permissionCode,
              screen: "sales",
              action: input.action,
            },
            async (context) => {
              authorization.assertActive();
              const actor = authorization.actor();
              if (actor.cashierId !== context.requestingCashierId) {
                throw new Error("FULFILMENT_REQUESTING_CASHIER_MISMATCH");
              }
              const result = await input.execute(
                {
                  actionId,
                  permissionCode: context.permissionCode,
                  authorizationMode: context.authorizationMode,
                  requestingCashierId: context.requestingCashierId,
                  requestingCashierName: actor.cashierName,
                  requestingUserGuid: actor.userGuid,
                  authorizingCashierId: context.authorizingCashierId,
                },
                authorization.assertActive,
                ...args,
              );
              // 履约终态已与审计原子落库后必须返回真实结果；此处再查旧 lease
              // 只会把不可撤销的成功伪装成失败，并诱发新 actionId 重复硬件动作。
              return result;
            },
          )
          .then((result) =>
            result.authorized
              ? result.value
              : Object.freeze({
                  state: "denied" as const,
                  errorCode: result.reason,
                }),
          );
      } catch {
        started = Promise.resolve(
          Object.freeze({
            state: "denied" as const,
            errorCode: "NO_ACTIVE_CASHIER",
          }),
        );
      }

      const tracked = started.finally(() => {
        inFlightByOperation.delete(operationKey);
      });
      inFlightByOperation.set(operationKey, tracked);
      return tracked;
    },
  });
}

async function downloadSettingsCatalog(
  service: CatalogSnapshotService,
  repository: ReturnType<PosDatabase["catalogSnapshots"]>,
  promotionSnapshotLoader: ActivePromotionSnapshotLoader,
  cashierLease: TrustedCashierLease,
  terminal: HbposAuditMetadata,
  nowIso: () => string,
  reset: boolean,
  signal: AbortSignal,
  onProgress?: CatalogRefreshProgressObserver | undefined,
): Promise<CatalogRefreshOutcome> {
  throwIfCatalogRequestAborted(signal);
  const permissionCode = reset
    ? SETTINGS_CATALOG_RESET_PERMISSION
    : SETTINGS_CATALOG_DOWNLOAD_PERMISSION;
  const session = requireSettingsCatalogSession(
    cashierLease,
    terminal,
    terminal.storeCode,
    permissionCode,
  );
  let outcome: CatalogRefreshOutcome | null = null;
  const request = {
    storeCode: session.storeCode,
    signal,
    ...(onProgress ? { onProgress } : {}),
    beforeActivate: () => {
      requireSettingsCatalogSession(
        cashierLease,
        terminal,
        session.storeCode,
        permissionCode,
      );
    },
    afterActivate: async (activation: CatalogActivationResult) => {
      const fallback = catalogSummaryFromActivation(activation);
      let active: CatalogSummary;
      try {
        const verified = await repository.getActiveMetadata();
        if (!matchesActivation(verified, activation)) {
          outcome = {
            kind: "activated-with-warning",
            summary: fallback,
            warningCode: "catalog-activation-verification-failed",
          };
          return;
        }
        active = verified;
      } catch {
        outcome = {
          kind: "activated-with-warning",
          summary: fallback,
          warningCode: "catalog-activation-verification-failed",
        };
        return;
      }
      try {
        const after = requireSettingsCatalogSession(
          cashierLease,
          terminal,
          session.storeCode,
          permissionCode,
        );
        const promotionReload = await promotionSnapshotLoader.load({
          storeCode: after.storeCode,
          asOfIso: nowIso(),
        });
        if (
          promotionReload.status !== "loaded" ||
          promotionReload.snapshotId !== active.snapshotId
        ) {
          throw new Error("Catalog promotion runtime reload failed.");
        }
        outcome = { kind: "complete", summary: active };
      } catch {
        outcome = {
          kind: "activated-with-warning",
          summary: active,
          warningCode: "catalog-runtime-reload-failed",
        };
      }
    },
  };
  let activation: CatalogActivationResult;
  if (reset) {
    activation = await service.resetAndRedownload(request);
  } else {
    activation = await service.downloadAndActivate(request);
  }
  // 中文注释：目录一旦激活，后续验证/重载异常必须显示安全告警，不能误报为旧目录仍在使用。
  return (
    outcome ?? {
      kind: "activated-with-warning",
      summary: catalogSummaryFromActivation(activation),
      warningCode: "catalog-activation-verification-failed",
    }
  );
}

/**
 * 普通目录下载统一走 runtime 级 single-flight；发起时冻结 cashier lease，并在
 * 激活前及运行时促销重载时复核，页面离开不会改变该安全边界。
 */
async function downloadRuntimeCatalog(
  service: CatalogSnapshotService,
  repository: ReturnType<PosDatabase["catalogSnapshots"]>,
  promotionSnapshotLoader: ActivePromotionSnapshotLoader,
  cashierLease: TrustedCashierLease,
  terminal: HbposAuditMetadata,
  nowIso: () => string,
  signal: AbortSignal,
  onProgress: CatalogRefreshProgressObserver,
): Promise<CatalogRefreshOutcome> {
  const session = requireCatalogDownloadLease(
    cashierLease,
    terminal,
    terminal.storeCode,
  );
  let outcome: CatalogRefreshOutcome | null = null;
  const result = await service.downloadAndActivate({
    storeCode: session.storeCode,
    signal,
    onProgress,
    beforeActivate: () => {
      throwIfCatalogRequestAborted(signal);
      requireCatalogDownloadLease(
        cashierLease,
        terminal,
        session.storeCode,
      );
    },
    afterActivate: async (activation) => {
      const fallback = catalogSummaryFromActivation(activation);
      let active: CatalogSummary;
      try {
        const verified = await repository.getActiveMetadata();
        if (!matchesActivation(verified, activation)) {
          outcome = {
            kind: "activated-with-warning",
            summary: fallback,
            warningCode: "catalog-activation-verification-failed",
          };
          return;
        }
        active = verified;
      } catch {
        outcome = {
          kind: "activated-with-warning",
          summary: fallback,
          warningCode: "catalog-activation-verification-failed",
        };
        return;
      }
      try {
        const after = requireCatalogDownloadLease(
          cashierLease,
          terminal,
          session.storeCode,
        );
        const promotionReload = await promotionSnapshotLoader.load({
          storeCode: after.storeCode,
          asOfIso: nowIso(),
        });
        if (
          promotionReload.status !== "loaded" ||
          promotionReload.snapshotId !== active.snapshotId
        ) {
          throw new Error("Catalog promotion runtime reload failed.");
        }
        outcome = { kind: "complete", summary: active };
      } catch {
        outcome = {
          kind: "activated-with-warning",
          summary: active,
          warningCode: "catalog-runtime-reload-failed",
        };
      }
    },
  });
  return outcome ?? {
    kind: "activated-with-warning",
    summary: catalogSummaryFromActivation(result),
    warningCode: "catalog-activation-verification-failed",
  };
}

function createAvailableReturnRuntime(input: Readonly<{
  input: ProductionPosRuntimeCompositionDependencies;
  repositories: PosRepositoryBundle;
  settings: ReturnType<PosDatabase["settings"]>;
  currentCashier: CurrentCashierSession;
  authorization: OperationAuthorizationService;
  providerRefund: ReturnType<
    typeof createProductionPaymentRuntime
  >["returnRefund"];
  requestOrderSyncDrain: () => Promise<unknown>;
}>): PosReturnsRuntimeService {
  const receiptRenderer = new OrderRepositoryReturnReceiptRenderer(
    input.repositories.orders,
    returnReceiptSettings(input.settings),
  );
  const refundVoucherReceiptRenderer =
    new ProtectedRefundVoucherReceiptRenderer(
      input.repositories.orders,
      input.input.database.refundVoucherPrintMaterial(
        input.input.encryptor,
      ),
      returnReceiptSettings(input.settings),
      input.input.clock.now,
    );
  const fulfilment = new ReturnFulfilmentRuntime({
    plans: input.input.database.returnFulfilmentPlans(
      input.input.encryptor,
    ),
    resolveDrawerPrinterId: () =>
      resolveReturnDrawerPrinterId(input.settings),
    renderReceipt: (identity) => {
      if (identity.receiptKind === "refund-voucher") {
        // 券码只在专用渲染调用内从二次密文短暂恢复，绝不进入普通订单投影。
        return refundVoucherReceiptRenderer.render(
          identity.actionId,
          identity.returnOrderGuid,
        );
      }
      return receiptRenderer.render(identity.returnOrderGuid);
    },
  });
  const runtime = createProductionReturnRuntime({
    database: input.input.database,
    repositories: input.repositories,
    encryptor: input.input.encryptor,
    currentCashier: input.currentCashier,
    terminal: input.input.auditMetadata,
    authorization: input.authorization,
    historyApi: new HbposReturnHistoryApi(input.input.transport),
    connectivity: input.input.connectivity,
    cashRefund: new ProductionReturnCashRefundAdapter(),
    onlineRefund: new ProductionReturnOnlineRefundRouter({
      providerRefund: input.providerRefund,
    }),
    fulfilment: {
      materializeAction: (actionId) =>
        fulfilment.materializeAction(actionId),
      drainPending: createPostCommitWorkDrain(
        () => fulfilment.drainPending(),
        input.requestOrderSyncDrain,
      ),
    },
    sha256Hex: input.input.sha256Hex,
    createId: input.input.createId,
    nowIso: input.input.clock.nowIso,
    ...(input.input.appUpdateTransition
      ? { operationLease: input.input.appUpdateTransition }
      : {}),
  });
  return Object.freeze({
    status: "available" as const,
    ...runtime,
  });
}

function createTerminalCartInitializer(
  repository: PosRepositoryBundle["heldOrderRecords"],
  activeCart: ActivePricingCartSession,
  scope: TerminalCartScope,
): TerminalCartInitializer {
  let ready = false;
  let inFlight: Promise<void> | null = null;
  const normalizedScope = {
    storeCode: requiredCashierSessionText(scope.storeCode, "Terminal store code"),
    deviceCode: requiredCashierSessionText(
      scope.deviceCode,
      "Terminal device code",
    ),
  };

  const initialize = (() => {
    if (ready) return Promise.resolve();
    if (inFlight) return inFlight;
    const operation = initializeTerminalCartFence(
      repository,
      activeCart,
      normalizedScope,
    )
      .then(() => {
        ready = true;
      })
      .finally(() => {
        if (inFlight === operation) inFlight = null;
      });
    inFlight = operation;
    return operation;
  }) as TerminalCartInitializer;
  Object.defineProperty(initialize, "isReady", {
    configurable: false,
    enumerable: false,
    value: () => ready,
    writable: false,
  });
  return initialize;
}

function createCombinedTerminalInitializer(
  terminalCart: TerminalCartInitializer,
  initializePaymentRecovery: () => Promise<void>,
  initializeOptionalServices?: (() => Promise<void>) | undefined,
): TerminalCartInitializer {
  let ready = false;
  let inFlight: Promise<void> | null = null;
  const initialize = (() => {
    if (ready) return Promise.resolve();
    if (inFlight) return inFlight;
    // Held-order fence 必须先恢复；若 RecallActive 已占用购物车，支付恢复应失败关闭。
    const operation = terminalCart()
      .then(initializePaymentRecovery)
      .then(() => initializeOptionalServices?.())
      .then(() => {
        ready = true;
      })
      .finally(() => {
        if (inFlight === operation) inFlight = null;
      });
    inFlight = operation;
    return operation;
  }) as TerminalCartInitializer;
  Object.defineProperty(initialize, "isReady", {
    configurable: false,
    enumerable: false,
    value: () => ready,
    writable: false,
  });
  return initialize;
}

async function initializeTerminalCartFence(
  repository: PosRepositoryBundle["heldOrderRecords"],
  activeCart: ActivePricingCartSession,
  scope: TerminalCartScope,
): Promise<void> {
  const fence = await repository.getTerminalFence(scope);
  if (!fence) return;
  assertFenceScope(fence, scope);

  if (fence.kind === "HoldClear") {
    const confirmed = await repository.confirmHoldCartCleared({
      scope,
      holdId: fence.holdId,
    });
    if (!confirmed) {
      throw new Error("Terminal HoldClear fence could not be confirmed.");
    }
    return;
  }

  const binding = recallBindingFromFence(fence);
  // 启动时绝不读取或展示上一位收银员的冻结购物车；只保存私有 binding 并锁住
  // 普通编辑。之后必须由已登录收银员完成双权限 recover/release 才能继续。
  activeCart.blockForRecallRecovery(binding);
}

function createHeldActiveCartPort(
  activeCart: ActivePricingCartSession,
): ActivePricingCartPort {
  return {
    runExclusive: (operation) =>
      activeCart.runExclusive((lease) =>
        operation({
          read: () => lease.read(),
          blockForRecallRecovery: (recallBinding) => {
            lease.blockForRecallRecovery(recallBinding);
          },
          replace: (pricingState, recallBinding) => {
            lease.replace(pricingState, recallBinding);
          },
          setRecallBinding: (recallBinding) => {
            lease.setRecallBinding(recallBinding);
          },
        }),
      ),
  };
}

function createHeldOrderAuthorization(
  service: OperationAuthorizationService,
  createId: () => string,
  cashierLease: TrustedCashierLease,
): HeldOrderAuthorizationPort {
  return {
    async authorizeAndRun(input, operation) {
      const result = await service.authorizeAndRun(
        {
          actionId: requiredCashierSessionText(
            createId(),
            "Authorization action id",
          ),
          permissionCode: input.permissionCode,
          screen: "held-orders",
          action: input.action,
        },
        () => {
          cashierLease.get();
          return operation();
        },
      );
      return result.authorized
        ? { authorized: true, value: result.value }
        : { authorized: false };
    },
  };
}

async function holdCurrentCart(
  activeCart: ActivePricingCartSession,
  heldOrders: HeldOrdersOrchestrator,
  cart: CartSnapshot,
): Promise<void> {
  if (!activeCart.isCurrentCartSnapshot(cart)) {
    throw Object.assign(
      new Error("Held order cart snapshot is stale."),
      { code: ACTIVE_PRICING_CART_STALE_SNAPSHOT },
    );
  }
  const result = await heldOrders.hold();
  if (!result.ok) throw heldOrderFailure(result);
}

function heldOrderFailure(result: HeldOrderActionResult): Error {
  return Object.assign(
    new Error(`Held order action failed: ${result.code}.`),
    { code: result.code, holdId: result.holdId },
  );
}

function assertRuntimeInitialized(initializer: TerminalCartInitializer): void {
  if (!initializer.isReady()) {
    throw new Error("POS terminal cart runtime is not initialized.");
  }
}

function trustedSalesSession(
  cashierLease: TrustedCashierLease,
  metadata: HbposAuditMetadata,
): AuthenticatedSalesSession {
  const session = cashierLease.get();
  assertTrustedCashierScope(session, metadata);
  return session;
}

function assertTrustedCashierScope(
  session: TrustedCashierSession,
  metadata: HbposAuditMetadata,
): void {
  const storeCode = requiredCashierSessionText(
    session.storeCode,
    "Cashier store code",
  );
  const deviceCode = requiredCashierSessionText(
    session.deviceCode,
    "Cashier device code",
  );
  if (
    storeCode !== metadata.storeCode ||
    deviceCode !== metadata.deviceCode
  ) {
    throw new Error("Authenticated cashier session does not match this terminal.");
  }
}

function requireCatalogDownloadSession(
  currentCashier: CurrentCashierSession,
  metadata: HbposAuditMetadata,
  requestedStoreCode: string,
): TrustedCashierSession {
  const session = currentCashier.require();
  assertTrustedCashierScope(session, metadata);
  if (!session.permissionCodes.includes(CATALOG_DOWNLOAD_PERMISSION)) {
    throw Object.assign(
      new Error("Catalog download permission is required."),
      { code: "CATALOG_DOWNLOAD_PERMISSION_REQUIRED" },
    );
  }
  if (requestedStoreCode.trim() !== session.storeCode) {
    throw new Error("Catalog download store does not match the current cashier.");
  }
  return session;
}

function requireCatalogDownloadLease(
  cashierLease: TrustedCashierLease,
  metadata: HbposAuditMetadata,
  requestedStoreCode: string,
): TrustedCashierSession {
  const session = cashierLease.get();
  assertTrustedCashierScope(session, metadata);
  if (!session.permissionCodes.includes(CATALOG_DOWNLOAD_PERMISSION)) {
    throw Object.assign(
      new Error("Catalog download permission is required."),
      { code: "CATALOG_DOWNLOAD_PERMISSION_REQUIRED" },
    );
  }
  if (requestedStoreCode.trim() !== session.storeCode) {
    throw new Error("Catalog download store does not match the current cashier.");
  }
  return session;
}

/** Settings 必须绑定原发起 cashier lease，并在每个激活边界复核其专属权限。 */
function requireSettingsCatalogSession(
  cashierLease: TrustedCashierLease,
  metadata: HbposAuditMetadata,
  requestedStoreCode: string,
  permissionCode: string,
): TrustedCashierSession {
  const session = cashierLease.get();
  assertTrustedCashierScope(session, metadata);
  if (!session.permissionCodes.includes(permissionCode)) {
    throw Object.assign(
      new Error("Settings catalog permission is required."),
      { code: "SETTINGS_CATALOG_PERMISSION_REQUIRED" },
    );
  }
  if (requestedStoreCode.trim() !== session.storeCode) {
    throw new Error("Catalog download store does not match the current cashier.");
  }
  return session;
}

function throwIfCatalogRequestAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) {
    throw new Error("Catalog refresh was cancelled.");
  }
}

function catalogSummaryFromActivation(
  result: CatalogActivationResult,
): CatalogSummary {
  return {
    snapshotId: result.snapshotId,
    catalogVersion: result.catalogVersion,
    itemCount: result.itemCount,
    activatedAt: result.activatedAt,
  };
}

function matchesActivation(
  active: CatalogSummary | null,
  result: Pick<CatalogActivationResult, "snapshotId" | "catalogVersion" | "itemCount">,
): active is CatalogSummary {
  return active !== null &&
    active.snapshotId === result.snapshotId &&
    active.catalogVersion === result.catalogVersion &&
    active.itemCount === result.itemCount;
}

function assertFenceScope(
  fence: TerminalCartFence,
  scope: TerminalCartScope,
): void {
  if (
    fence.scope.storeCode !== scope.storeCode ||
    fence.scope.deviceCode !== scope.deviceCode
  ) {
    throw new Error("Terminal cart fence scope mismatch.");
  }
}

function recallBindingFromFence(
  fence: TerminalCartFence,
): RecallActiveBinding {
  if (fence.kind !== "RecallActive" || !fence.recallAttemptId) {
    throw new Error("RecallActive terminal fence is incomplete.");
  }
  return {
    kind: "recalled",
    scope: fence.scope,
    holdId: fence.holdId,
    recallAttemptId: fence.recallAttemptId,
  };
}

function createCashierSessionFacade(
  authentication: Pick<CashierAuthenticationService, "login">,
  security: CashierSessionSecurityConfiguration,
  currentCashier: CurrentCashierSession,
  operationAuthorization: OperationAuthorizationService | null,
  terminalScope: Pick<HbposAuditMetadata, "storeCode" | "deviceCode">,
): PosCashierSessionRuntimeService {
  let inFlight: Promise<PosCashierSummary> | null = null;

  return {
    signIn(userBarcode) {
      if (inFlight) {
        return Promise.reject(
          Object.assign(
            new Error("Another cashier sign-in is already in progress."),
            { code: "CASHIER_SIGN_IN_IN_PROGRESS" },
          ),
        );
      }

      const operation = signInCashier({
        authentication,
        security,
        currentCashier,
        operationAuthorization,
        terminalScope,
        userBarcode,
      });
      inFlight = operation;
      return operation.then(
        (summary) => {
          if (inFlight === operation) inFlight = null;
          return summary;
        },
        (error: unknown) => {
          if (inFlight === operation) inFlight = null;
          throw error;
        },
      );
    },
  };
}

async function signInCashier(input: Readonly<{
  authentication: Pick<CashierAuthenticationService, "login">;
  security: CashierSessionSecurityConfiguration;
  currentCashier: CurrentCashierSession;
  operationAuthorization: OperationAuthorizationService | null;
  terminalScope: Pick<HbposAuditMetadata, "storeCode" | "deviceCode">;
  userBarcode: string;
}>): Promise<PosCashierSummary> {
  const userBarcode = requiredCashierSessionText(
    input.userBarcode,
    "Cashier barcode",
  );
  const authenticationEpoch = input.currentCashier.beginAuthentication();
  input.operationAuthorization?.clearRequestingCashier();

  try {
    // 先撤销上一位收银员的 bearer；底层在线或离线登录成功后才会写入新票据。
    await input.security.clearAuthorization();
    const deviceIdentity = await input.security.getDeviceIdentity();
    if (!deviceIdentity) {
      throw Object.assign(
        new Error("Registered device identity is required for cashier sign-in."),
        { code: "DEVICE_IDENTITY_REQUIRED" },
      );
    }
    const expectedDevice = {
      storeCode: requiredCashierSessionText(
        deviceIdentity.storeCode,
        "Device store code",
      ),
      deviceCode: requiredCashierSessionText(
        deviceIdentity.deviceCode,
        "Device code",
      ),
    };
    if (
      expectedDevice.storeCode !== input.terminalScope.storeCode ||
      expectedDevice.deviceCode !== input.terminalScope.deviceCode
    ) {
      throw new Error("Registered device identity does not match this POS runtime.");
    }

    const result = await input.authentication.login({
      ...expectedDevice,
      userBarcode,
    });
    const summary = input.currentCashier.activate(
      authenticationEpoch,
      result,
      expectedDevice,
    );
    const session = input.currentCashier.require();
    input.operationAuthorization?.activateRequestingCashier({
      cashierId: session.cashierId,
      cashierName: session.cashierName,
      userGuid: session.userGuid,
      storeCode: session.storeCode,
      deviceCode: session.deviceCode,
      permissions: session.permissionCodes,
    });
    return summary;
  } catch (error: unknown) {
    input.currentCashier.clear();
    input.operationAuthorization?.clearRequestingCashier();
    try {
      await input.security.clearAuthorization();
    } catch {
      // 登录已经失败；二次 Keychain 清理失败不能掩盖原始拒绝，但可信 session 仍为空。
    }
    throw error;
  }
}

/**
 * 登录审计是旁路事实，不得消耗订单/支付/目录测试共用的业务 ID 序列。
 * 它只作为审计幂等键使用，不承载安全凭据或金额身份。
 */
function createOperationAuditId(): string {
  return "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx".replace(
    /[x]/gu,
    () => Math.floor(Math.random() * 16).toString(16),
  );
}

function requiredCashierSessionText(value: unknown, label: string): string {
  const normalized =
    typeof value === "string" && value.trim() ? value.trim() : null;
  if (!normalized) {
    throw new Error(`${label} is required in the authenticated cashier session.`);
  }
  return normalized;
}

/**
 * 销售层只在耐久事务已确认后触发履约。队列排空属于提交后的可恢复副作用，
 * 因此其失败不能改写现金成功结果、回滚订单或阻止购物车清空。
 */
export function createPostCommitFulfilmentCashCheckout(
  checkout: SalesCashCheckoutPort,
  drainFulfilment: () => Promise<unknown>,
): SalesCashCheckoutPort {
  return {
    async complete(input) {
      const result = await checkout.complete(input);
      void drainFulfilment().catch(() => undefined);
      return result;
    },
  };
}

/**
 * 三类订单都在耐久事务成功后共用该入口：上传与履约彼此独立，
 * 任一侧暂时失败都不能改写已完成的交易事实。
 */
export function createPostCommitWorkDrain<T>(
  drainFulfilment: () => Promise<T>,
  requestOrderSyncDrain: () => Promise<unknown>,
): () => Promise<T> {
  return () => {
    try {
      void requestOrderSyncDrain().catch(() => undefined);
    } catch {
      // 同步唤醒异常只保留耐久 outbox，后续定时或生命周期触发会继续处理。
    }
    return drainFulfilment();
  };
}

async function runUpdateTransitionWithActiveCart<T>(
  activeCart: ActivePricingCartSession,
  operation: () => Promise<T>,
): Promise<T> {
  // transition 已先同步关闭普通购物车入口；这里只等待此前已取得的 lease，
  // 不持有普通 operation lease，固定锁序因此不会形成 activeCart 反向等待。
  await activeCart.waitForExclusiveLeaseRelease();
  return activeCart.runUpdateTransitionExclusive(() => operation());
}

function receiptSettingsService(
  settings: ReturnType<PosDatabase["settings"]>,
): PosReceiptSettingsService {
  return {
    get: () => settings.getReceiptPrinterSettings(),
    save: (input) => settings.saveReceiptPrinterSettings(input),
  };
}

/**
 * 重打在一次准备动作中只读一次持久化设置，并冻结该次实际使用的外设与抬头。
 * 配置缺失时由 receipt domain 返回 not-found，不能从当前 UI 或旧打印作业猜测。
 */
function receiptReprintSettings(
  settings: ReturnType<PosDatabase["settings"]>,
): ReceiptReprintSettingsSource {
  return {
    async getFrozenReceiptSettings() {
      const current = await settings.getReceiptPrinterSettings();
      if (!isValidPeripheralId(current.peripheralId)) {
        return null;
      }
      return {
        printerId: current.peripheralId,
        paper: current.paper,
        locale: current.locale,
        store: {
          brandName: current.brandName,
          storeName: current.storeName,
          address: current.address,
          phone: current.phone,
          abn: current.abn,
        },
      };
    },
  };
}

/** 预览只冻结纸宽、语言和公开抬头；未配置打印机不应阻止查看。 */
function receiptPreviewSettings(
  settings: ReturnType<PosDatabase["settings"]>,
): ReceiptPreviewSettingsSource {
  return {
    async getFrozenReceiptPreviewSettings() {
      const current = await settings.getReceiptPrinterSettings();
      return {
        paper: current.paper,
        locale: current.locale,
        store: {
          brandName: current.brandName,
          storeName: current.storeName,
          address: current.address,
          phone: current.phone,
          abn: current.abn,
        },
      };
    },
  };
}

/**
 * 每次退货履约物化只读取一次当前持久化设置。未明确启用打印或外设身份
 * 损坏时保持 plan pending，由设置页修复后人工重试，不能猜测旧打印机。
 */
function returnReceiptSettings(
  settings: ReturnType<PosDatabase["settings"]>,
): ReturnReceiptSettingsPort {
  return {
    async getFrozenReturnReceiptSettings() {
      const current = await settings.getReceiptPrinterSettings();
      if (!current.printEnabled || !isValidPeripheralId(current.peripheralId)) {
        return null;
      }
      return {
        printerId: current.peripheralId,
        paper: current.paper,
        locale: current.locale,
        store: {
          brandName: current.brandName,
          storeName: current.storeName,
          address: current.address,
          phone: current.phone,
          abn: current.abn,
        },
      };
    },
  };
}

async function resolveReturnDrawerPrinterId(
  settings: ReturnType<PosDatabase["settings"]>,
): Promise<string> {
  const current = await settings.getReceiptPrinterSettings();
  if (!current.drawerEnabled || !isValidPeripheralId(current.peripheralId)) {
    throw new Error("RETURN_DRAWER_SETTINGS_MISSING");
  }
  return current.peripheralId;
}

function receiptCompletionSettlementSource(
  database: PosDatabase,
): ReceiptCompletionSettlementSource {
  const settlements = database.receiptCompletionSettlements();
  return {
    getCompletionSettlement: (orderGuid) => settlements.getByOrderGuid(orderGuid),
  };
}

function receiptFulfilmentSettings(
  settings: ReturnType<PosDatabase["settings"]>,
  cashDrawerPermissionAllowed: boolean,
) {
  return {
    async getSettings() {
      const current = await settings.getReceiptPrinterSettings();
      return {
        printEnabled: current.printEnabled,
        drawerEnabled: current.drawerEnabled,
        cashDrawerPermissionAllowed,
        printerId: current.peripheralId,
        paper: current.paper,
        locale: current.locale,
        store: {
          brandName: current.brandName,
          storeName: current.storeName,
          address: current.address,
          phone: current.phone,
          abn: current.abn,
        },
      };
    },
  };
}

function createSessionCashCheckout(
  input: ProductionPosRuntimeCompositionDependencies,
  repositories: PosRepositoryBundle,
  settings: ReturnType<PosDatabase["settings"]>,
  returnCapacity: (cart: CartSnapshot) => Promise<boolean>,
  cashierLease: TrustedCashierLease,
  activeCart: ActivePricingCartSession,
): DurableCashCheckoutService {
  const session = trustedSalesSession(cashierLease, input.auditMetadata);
  // 创建服务时复制可信 session 的冻结权限，React/Zustand 投影无法改变钱箱授权事实。
  const permissionCodes = new Set(session.permissionCodes);
  const cashDrawerPermissionAllowed = permissionCodes.has(
    "Permissions.PosTerminal.CashDrawer.Open",
  );
  const cashPlanner = new CashFulfilmentPlanner(
    receiptFulfilmentSettings(settings, cashDrawerPermissionAllowed),
    input.createId,
  );
  return new DurableCashCheckoutService(
    input.database.cashOrderCommitter(input.encryptor),
    cashPlanner,
    {
      createId: input.createId,
      nowIso: input.clock.nowIso,
      nextLocalSequence: () => repositories.orders.nextLocalSequence(),
      returnCapacity,
    },
    {
      resolve(cart) {
        // resolver 在单飞签名、规划、序号分配和 SQLCipher 写入之前执行。
        // 401、403、锁屏或换收银员都会使旧 presenter 的 lease 立即失效。
        trustedSalesSession(cashierLease, input.auditMetadata);
        const current = activeCart.read();
        if (current.cart !== cart) {
          throw Object.assign(
            new Error("Cash checkout cart snapshot is stale."),
            { code: ACTIVE_PRICING_CART_STALE_SNAPSHOT },
          );
        }
        return current.recallBinding ?? { kind: "none" };
      },
    },
  );
}

function createSyncHistoryRuntime(
  input: ProductionPosRuntimeCompositionDependencies,
  coordinator: PosSyncCoordinator,
  currentCashier: CurrentCashierSession,
): PosSyncHistoryRuntimeService {
  const store = input.database.localSyncHistory({
    appId: input.supportAppId,
    appVersion: input.auditMetadata.appVersion,
    deviceCode: input.auditMetadata.deviceCode,
    storeCode: input.auditMetadata.storeCode,
  });
  return {
    createPresenter: (permissionCodes) => {
      const cashierLease = currentCashier.createLease();
      const session = trustedSalesSession(cashierLease, input.auditMetadata);
      assertCashierPermissionSummary(permissionCodes, session.permissionCodes);
      const port: LocalSyncHistoryPort = {
        async listLocalSyncHistory(query) {
          trustedSalesSession(cashierLease, input.auditMetadata);
          const page = await store.listLocalSyncHistory(query);
          // 401/403/锁屏期间晚到的数据不能回流到旧 presenter。
          trustedSalesSession(cashierLease, input.auditMetadata);
          return page;
        },
        async getLocalSyncHistorySupportSnapshot(query) {
          trustedSalesSession(cashierLease, input.auditMetadata);
          const snapshot =
            await store.getLocalSyncHistorySupportSnapshot(query);
          // 支持包读取完成后再次验证租约，避免旧会话取得诊断数据。
          trustedSalesSession(cashierLease, input.auditMetadata);
          return snapshot;
        },
        async getSupportContext() {
          trustedSalesSession(cashierLease, input.auditMetadata);
          const context = await store.getSupportContext();
          trustedSalesSession(cashierLease, input.auditMetadata);
          return context;
        },
        async restoreExistingOrderOutboxToPending(orderGuids) {
          trustedSalesSession(cashierLease, input.auditMetadata);
          const result =
            await store.restoreExistingOrderOutboxToPending(orderGuids);
          // 若动作执行期间会话失效，保留已完成的耐久状态但不由旧页面触发补传。
          trustedSalesSession(cashierLease, input.auditMetadata);
          if (result.restoredOrderGuids.length > 0) {
            // 历史页只恢复既有 outbox；实际补传仍由同一个同步协调器取得租约并执行。
            void coordinator.requestDrain().catch(() => undefined);
          }
          return result;
        },
      };
      return new SyncHistoryPresenter({
        permissionCodes: session.permissionCodes,
        port,
      });
    },
  };
}

function createLocalHistoryRuntime(
  input: ProductionPosRuntimeCompositionDependencies,
  currentCashier: CurrentCashierSession,
  fulfilment: FulfilmentService,
  receiptPreview: LocalHistoryReceiptPreviewPort,
): LocalHistoryPresenterFactory {
  return {
    createPresenter: () => {
      const cashierLease = currentCashier.createLease();
      const session = trustedSalesSession(
        cashierLease,
        input.auditMetadata,
      );
      const store = input.database.localHistory({
        storeCode: session.storeCode,
        deviceCode: session.deviceCode,
      });
      const requirePermission = (permissionCode: string) => {
        const active = trustedSalesSession(
          cashierLease,
          input.auditMetadata,
        );
        if (!active.permissionCodes.includes(permissionCode)) {
          throw new Error("LOCAL_HISTORY_PERMISSION_REQUIRED");
        }
        return active;
      };
      const port: LocalHistoryPort = {
        async list(query) {
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          const page = await store.list(query);
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          return page;
        },
        async getDetails(orderGuid) {
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          const details = await store.getDetails(orderGuid);
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          return details;
        },
      };
      const receiptPreviewPort: LocalHistoryReceiptPreviewPort = {
        async getPreview(orderGuid) {
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          // 中文注释：先以可信本机门店/设备 scope 验证订单，再读取原始账本渲染。
          const details = await store.getDetails(orderGuid);
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          if (!details || details.orderGuid !== orderGuid) return null;
          const document = await receiptPreview.getPreview(details.orderGuid);
          requirePermission(LOCAL_HISTORY_VIEW_PERMISSION);
          return document;
        },
      };
      const reprintPort: LocalHistoryReprintPort = {
        async reprintExistingOrder(orderGuid) {
          const session = requirePermission(LOCAL_HISTORY_REPRINT_PERMISSION);
          // 中文注释：重打前重新按可信 scope 读取，不能只相信页面曾加载过的订单号。
          const details = await store.getDetails(orderGuid);
          requirePermission(LOCAL_HISTORY_REPRINT_PERMISSION);
          if (!details) {
            throw new Error("LOCAL_HISTORY_ORDER_NOT_PRINTABLE");
          }
          const result = await fulfilment.reprintReceipt(
            details.orderGuid,
            "local-history",
            {
              actionId: input.createId(),
              permissionCode: LOCAL_HISTORY_REPRINT_PERMISSION,
              authorizationMode: "current-cashier",
              requestingCashierId: session.cashierId,
              requestingCashierName: session.cashierName,
              requestingUserGuid: session.userGuid,
              authorizingCashierId: null,
            },
            () => {
              requirePermission(LOCAL_HISTORY_REPRINT_PERMISSION);
            },
          );
          if (result.state !== "Printed") {
            throw Object.assign(
              new Error("Local history receipt reprint failed."),
              {
                code:
                  result.errorCode ??
                  `REPRINT_${result.state
                    .toUpperCase()
                    .replaceAll("-", "_")}`,
              },
            );
          }
        },
      };
      return new LocalHistoryPresenter({
        businessTimeZone: resolveRuntimeBusinessTimeZone(
          input.businessTimeZone,
        ),
        now: input.clock.now,
        permissionCodes: session.permissionCodes,
        port,
        receiptPreviewPort,
        reprintPort,
      });
    },
  };
}

function assertCashierPermissionSummary(
  publicSummary: readonly string[],
  trustedPermissions: readonly string[],
): void {
  const normalized = [...new Set(publicSummary.map((permission) => permission.trim()))]
    .filter(Boolean)
    .sort();
  if (
    normalized.length !== trustedPermissions.length ||
    normalized.some(
      (permission, index) => permission !== trustedPermissions[index],
    )
  ) {
    throw new Error("CASHIER_PERMISSION_SUMMARY_MISMATCH");
  }
}

function isValidPeripheralId(value: string | null): value is string {
  return typeof value === "string" && /^[A-Za-z0-9._:-]{1,128}$/.test(value);
}

function resolveRuntimeBusinessTimeZone(value?: string): string {
  const candidate =
    typeof value === "string" && value.trim()
      ? value.trim()
      : "Australia/Brisbane";
  if (
    candidate.length > 128 ||
    /[\u0000-\u001f\u007f]/u.test(candidate)
  ) {
    throw new TypeError("POS business time zone is invalid.");
  }
  try {
    new Intl.DateTimeFormat("en-AU", {
      timeZone: candidate,
    }).format(0);
  } catch {
    throw new TypeError("POS business time zone is invalid.");
  }
  return candidate;
}
