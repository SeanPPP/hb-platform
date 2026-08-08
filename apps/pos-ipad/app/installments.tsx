import { Redirect, type Href, useRouter } from "expo-router";
import { useEffect, useRef, useState } from "react";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  isActiveCashierBoundToDevice,
  resolveProtectedSalesRouteGate,
  useCashierLoginStore,
} from "@/features/cashier-login";
import {
  InstallmentScreen,
  InstallmentsUnavailableScreen,
  resolveInstallmentsAccess,
  resolveInstallmentsRuntimeFactory,
  type InstallmentPresenter,
} from "@/features/installments";
import { installmentRepaymentPaymentEntry } from "@/features/payments/ui";
import { usePosShellStore } from "@/ui/shell/pos-shell-store";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type InstallmentsBinding = Readonly<{
  cashier: object;
  presenter: InstallmentPresenter;
  services: object;
}>;

/**
 * 直链与销售入口复用同一设备/收银员门禁。可信身份、支付 attempt、缓存和
 * 活动购物车均封装在零参数 runtime factory 内，路由不接触可伪造的业务输入。
 */
export default function InstallmentsRoute() {
  const router = useRouter();
  const runtime = usePosRuntime();
  const activeCashier = useCashierLoginStore(
    (state) => state.activeCashier,
  );
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const gate = resolveProtectedSalesRouteGate(
    runtime.state,
    activeCashier,
  );
  const access = resolveInstallmentsAccess(
    activeCashier?.permissions ?? [],
  );
  const factory = runtime.services
    ? resolveInstallmentsRuntimeFactory(runtime.services)
    : null;
  const [binding, setBinding] =
    useState<InstallmentsBinding | null>(null);
  const [runtimeUnavailable, setRuntimeUnavailable] = useState(false);
  // 网络信号来源：connectivity 由 NetworkStatusBridge 后端探测驱动，
  // 后端恢复后会自动翻转为 online（30s 周期 / App 前台恢复）。
  // runtime.state.backend 在启动后不会更新，无法驱动离线→在线恢复。
  const connectivity = usePosShellStore(
    (state) => state.connectivity,
  );
  const prevOnline = useRef<boolean | null>(null);
  const presenter =
    binding?.services === runtime.services &&
    binding.cashier === activeCashier
      ? binding.presenter
      : null;

  useEffect(() => {
    if (
      gate !== "check-device-identity" ||
      !activeCashier ||
      !runtime.services ||
      !access.canView
    ) {
      setBinding(null);
      setRuntimeUnavailable(false);
      return undefined;
    }

    let cancelled = false;
    let createdPresenter: InstallmentPresenter | null = null;
    const cashier = activeCashier;
    const services = runtime.services;
    setBinding(null);
    setRuntimeUnavailable(false);
    void services.deviceSession
      .getDeviceIdentity()
      .then((identity) => {
        if (cancelled) return;
        if (
          !identity ||
          !isActiveCashierBoundToDevice(cashier, identity)
        ) {
          clearActiveCashier();
          return;
        }
        if (!factory) {
          setRuntimeUnavailable(true);
          return;
        }
        try {
          createdPresenter = factory.createPresenter();
        } catch {
          setRuntimeUnavailable(true);
          return;
        }
        if (cancelled) {
          createdPresenter.destroy();
          createdPresenter = null;
          return;
        }
        setBinding({ cashier, presenter: createdPresenter, services });
      })
      .catch(() => {
        if (!cancelled) clearActiveCashier();
      });

    return () => {
      cancelled = true;
      createdPresenter?.destroy();
      createdPresenter = null;
    };
  }, [
    access.canView,
    activeCashier,
    clearActiveCashier,
    factory,
    gate,
    runtime.services,
  ]);

  // 网络从离线恢复为在线时，自动刷新分期数据，无需用户手动点击重试。
  useEffect(() => {
    if (!presenter) return;
    // checking 视为在线（未知时乐观），仅明确 offline 才阻断分期。
    const nextOnline =
      connectivity === "online" || connectivity === "checking";
    const wasOffline = prevOnline.current === false;
    presenter.setOnline(nextOnline);
    if (wasOffline && nextOnline) {
      // 离线期间数据已被清空，恢复后重新加载列表与能力。
      void presenter.load().catch(() => undefined);
    }
    prevOnline.current = nextOnline;
  }, [connectivity, presenter]);

  if (gate === "redirect-index") {
    return <Redirect href={"/" as Href} />;
  }
  if (gate === "redirect-login") {
    return <Redirect href={"/login" as Href} />;
  }
  if (!access.canView) {
    return <Redirect href={"/sales" as Href} />;
  }
  if (runtimeUnavailable) {
    return (
      <InstallmentsUnavailableScreen
        onBack={() => router.dismissTo("/sales" as Href)}
      />
    );
  }
  if (!presenter) {
    return <BootstrapScreen />;
  }

  const startCreatePayment = access.canCreate && factory
    ? () => {
        try {
          const entry = factory.prepareCreateCheckout();
          router.push({
            pathname: "/payment",
            params: {
              flow: entry.kind,
              checkoutIntentId: entry.checkoutIntentId,
              revision: String(entry.expectedCartRevision),
            },
          } as Href);
          return true;
        } catch {
          // 购物车、权限和 cashier lease 由 factory 复核；由页面明确提示未跳转。
          return false;
        }
      }
    : undefined;
  const startRepayment = access.canAddRepayment
    ? (installmentGuid: string) => {
        try {
          const entry =
            installmentRepaymentPaymentEntry(installmentGuid);
          router.push({
            pathname: "/payment",
            params: {
              flow: entry.kind,
              installmentGuid: entry.installmentGuid,
            },
          } as Href);
          return true;
        } catch {
          // 拒绝任何非 UUID 的详情参数，避免把不可信输入带进支付路由。
          return false;
        }
      }
    : undefined;

  return (
    <InstallmentScreen
      onBack={() => router.dismissTo("/sales" as Href)}
      presenter={presenter}
      {...(startCreatePayment
        ? { onStartCreate: startCreatePayment }
        : {})}
      {...(startRepayment
        ? { onStartRepayment: startRepayment }
        : {})}
    />
  );
}
