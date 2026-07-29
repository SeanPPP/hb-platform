import * as Crypto from "expo-crypto";
import { Redirect, type Href, useRouter } from "expo-router";
import {
  useCallback,
  useEffect,
  useRef,
  useState,
  useSyncExternalStore,
} from "react";
import { useTranslation } from "react-i18next";
import { Linking } from "react-native";

import type { NewTransactionGate } from "@/core/contracts/app-updates";
import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  resolveProtectedSalesRouteGate,
  useCashierLoginStore,
} from "@/features/cashier-login";
import { canDownloadCatalog } from "@/features/catalog/maintenance";
import { DAILY_CLOSE_VIEW_PERMISSION } from "@/features/daily-close";
import { INSTALLMENTS_VIEW_PERMISSION } from "@/features/installments";
import { REMOTE_HISTORY_VIEW_PERMISSION } from "@/features/remote-history";
import {
  resolveSalesLocale,
  type SalesPresenter,
  SalesScreen,
  type SalesToolbarActionId,
} from "@/features/sales/ui";
import { reconcileSalesToolbarOrder } from "@/features/sales/ui/sales-toolbar-order";
import { CameraScannerModal } from "@/features/scanner-camera";
import { SETTINGS_VIEW_PERMISSION } from "@/features/settings";
import { SPECIAL_PRODUCTS_VIEW_PERMISSION } from "@/features/special-products";
import { toggleAppLanguage } from "@/i18n";
import {
  readSalesToolbarOrder,
  saveSalesToolbarOrder,
} from "@/ui/preferences/terminal-ui-preferences";
import { RouteHidScannerCapture } from "@/ui/scanner/scanner-route-bridge";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type SalesPresenterBinding = Readonly<{
  services: object;
  cashier: object;
  presenter: SalesPresenter;
}>;

const UNCHECKED_UPDATE_GATE: NewTransactionGate = Object.freeze({
  state: "unchecked",
  canStartNewTransaction: false,
  canContinueRecovery: true,
});

/** 组合根确认当前收银员后，路由只创建其受限销售 presenter。 */
export default function SalesRoute() {
  const { push, replace } = useRouter();
  const runtime = usePosRuntime();
  const { i18n } = useTranslation();
  const activeCashier = useCashierLoginStore((state) => state.activeCashier);
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const gate = resolveProtectedSalesRouteGate(runtime.state, activeCashier);
  const subscribeUpdateGate = useCallback(
    (listener: () => void) =>
      runtime.services?.appUpdates.subscribe(listener) ?? (() => undefined),
    [runtime.services],
  );
  const getUpdateGate = useCallback(
    () => runtime.services?.appUpdates.getGate() ?? UNCHECKED_UPDATE_GATE,
    [runtime.services],
  );
  const updateGate = useSyncExternalStore(
    subscribeUpdateGate,
    getUpdateGate,
    getUpdateGate,
  );
  const [binding, setBinding] = useState<SalesPresenterBinding | null>(null);
  const [cameraScannerVisible, setCameraScannerVisible] = useState(false);
  const [manualInputActive, setManualInputActive] = useState(false);
  const [toolbarOrder, setToolbarOrder] = useState<
    readonly SalesToolbarActionId[]
  >(() => reconcileSalesToolbarOrder(readSalesToolbarOrder()));
  const hidRestoreTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [presenterCreationFailed, setPresenterCreationFailed] = useState(false);
  const clearHidRestoreTimer = useCallback(() => {
    if (hidRestoreTimer.current !== null) {
      clearTimeout(hidRestoreTimer.current);
      hidRestoreTimer.current = null;
    }
  }, []);
  const handleManualInputFocusChange = useCallback(
    (focused: boolean) => {
      clearHidRestoreTimer();
      if (focused) {
        setManualInputActive(true);
        return;
      }
      // 输入框交接会先 blur 再 focus；下一轮事件循环才恢复 HID，避免隐藏输入抢焦点。
      hidRestoreTimer.current = setTimeout(() => {
        hidRestoreTimer.current = null;
        setManualInputActive(false);
      }, 0);
    },
    [clearHidRestoreTimer],
  );
  const presenter =
    binding?.services === runtime.services && binding.cashier === activeCashier
      ? binding.presenter
      : null;
  const addScannedProduct = useCallback(
    async (barcode: string) => {
      if (!presenter) return;
      // presenter 在同步 setQuery 后立即读取查询值，连续扫码不会把前一条码拼入下一条。
      presenter.setQuery(barcode);
      await presenter.addLookupCode();
    },
    [presenter],
  );
  const scanner = runtime.services?.scanner.router ?? null;
  const handleToolbarOrderChange = useCallback(
    (nextOrder: readonly SalesToolbarActionId[]) => {
      const reconciledOrder = reconcileSalesToolbarOrder(nextOrder);
      setToolbarOrder(reconciledOrder);
      void saveSalesToolbarOrder(reconciledOrder).catch(() => undefined);
    },
    [],
  );
  const handleSwitchLanguage = useCallback(() => {
    void toggleAppLanguage();
  }, []);

  useEffect(() => {
    if (!scanner || !presenter) {
      setCameraScannerVisible(false);
    }
  }, [presenter, scanner]);

  useEffect(() => clearHidRestoreTimer, [clearHidRestoreTimer]);

  useEffect(() => {
    if (!cameraScannerVisible || !scanner || !presenter) {
      return undefined;
    }
    // 相机和 HID 复用同一商品上下文；租约防止异步弹窗切换造成串码。
    return scanner.acquireContext("product");
  }, [cameraScannerVisible, presenter, scanner]);

  useEffect(() => {
    if (
      gate !== "check-device-identity" ||
      !activeCashier ||
      !runtime.services
    ) {
      setBinding(null);
      return undefined;
    }

    let cancelled = false;
    let createdPresenter: SalesPresenter | null = null;
    const services = runtime.services;
    const cashier = activeCashier;
    setBinding(null);
    setPresenterCreationFailed(false);
    const createSalesPresenter = () => {
      try {
        createdPresenter = services.sales.createPresenter();
        if (cancelled) {
          createdPresenter.destroy();
          createdPresenter = null;
        } else {
          setBinding({ services, cashier, presenter: createdPresenter });
        }
      } catch {
        if (!cancelled) {
          clearActiveCashier();
          setPresenterCreationFailed(true);
        }
      }
    };

    // 支付恢复优先于新销售 presenter：遗留支付会持有同一购物车租约，不能短暂开放编辑。
    if (services.payments.status === "available") {
      void services.payments.hasRecoveryRequired().then(
        (required) => {
          if (cancelled) return;
          if (required) {
            replace("/payment" as Href);
            return;
          }
          createSalesPresenter();
        },
        () => {
          if (!cancelled) {
            clearActiveCashier();
            setPresenterCreationFailed(true);
          }
        },
      );
    } else {
      createSalesPresenter();
    }
    return () => {
      cancelled = true;
      createdPresenter?.destroy();
      createdPresenter = null;
    };
  }, [activeCashier, clearActiveCashier, gate, replace, runtime.services]);

  if (gate === "redirect-index") {
    return <Redirect href={"/" as Href} />;
  }
  if (gate === "redirect-login") {
    return <Redirect href={"/login" as Href} />;
  }
  if (presenterCreationFailed) {
    return <Redirect href={"/login" as Href} />;
  }
  if (!presenter || !runtime.services) {
    return <BootstrapScreen />;
  }
  const services = runtime.services;
  const appStoreUrl = services.appUpdates.getPolicy()?.appStoreUrl ?? null;

  return (
    <>
      <RouteHidScannerCapture
        context="product"
        enabled={!cameraScannerVisible && !manualInputActive}
        onScan={addScannedProduct}
        path="/sales"
      />
      <SalesScreen
        locale={resolveSalesLocale(i18n.resolvedLanguage ?? i18n.language)}
        newTransactionGate={updateGate}
        onManualInputFocusChange={handleManualInputFocusChange}
        onSwitchLanguage={handleSwitchLanguage}
        onToolbarOrderChange={handleToolbarOrderChange}
        {...(appStoreUrl
          ? {
              onOpenRequiredUpdate: () => {
                void Linking.openURL(appStoreUrl).catch(() => undefined);
              },
            }
          : {})}
        {...(activeCashier?.permissions.includes(DAILY_CLOSE_VIEW_PERMISSION)
          ? {
              onOpenDailyClose: () => push("/daily-close" as Href),
            }
          : {})}
        onOpenHeldOrders={() => push("/held-orders" as Href)}
        onOpenReturns={() => push("/returns" as Href)}
        {...(activeCashier?.permissions.includes(REMOTE_HISTORY_VIEW_PERMISSION)
          ? {
              onOpenRemoteHistory: () => push("/remote-history" as Href),
            }
          : {})}
        {...(activeCashier?.permissions.includes(
          SPECIAL_PRODUCTS_VIEW_PERMISSION,
        )
          ? {
              onOpenSpecialProducts: () => push("/special-products" as Href),
            }
          : {})}
        {...(activeCashier?.permissions.includes(INSTALLMENTS_VIEW_PERMISSION)
          ? {
              onOpenInstallments: () => push("/installments" as Href),
            }
          : {})}
        {...(activeCashier?.permissions.includes(SETTINGS_VIEW_PERMISSION)
          ? {
              onOpenSettings: () => push("/settings" as Href),
            }
          : {})}
        onOpenAttendanceAudit={() => push("/attendance-audit" as Href)}
        {...(scanner
          ? {
              onOpenCameraScanner: () => setCameraScannerVisible(true),
            }
          : {})}
        onOpenPayment={() => {
          const cart = presenter.getState().cart;
          if (
            cart.lines.length === 0 ||
            !Number.isSafeInteger(cart.revision) ||
            cart.revision < 0 ||
            !Number.isSafeInteger(cart.actualAmount.cents) ||
            cart.actualAmount.cents <= 0
          ) {
            return;
          }
          push({
            pathname: "/payment",
            params: {
              checkoutIntentId: Crypto.randomUUID(),
              revision: String(cart.revision),
              totalCents: String(cart.actualAmount.cents),
            },
          } as unknown as Href);
        }}
        onOpenSyncHistory={() => push("/sync-history" as Href)}
        presenter={presenter}
        toolbarOrder={toolbarOrder}
        {...(activeCashier && canDownloadCatalog(activeCashier.permissions)
          ? {
              onOpenCatalogMaintenance: () =>
                push("/catalog-maintenance" as Href),
            }
          : {})}
      />
      {scanner ? (
        <CameraScannerModal
          context="product"
          onClose={() => setCameraScannerVisible(false)}
          onScan={addScannedProduct}
          scanner={scanner}
          visible={cameraScannerVisible}
        />
      ) : null}
    </>
  );
}
