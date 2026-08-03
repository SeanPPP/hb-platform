import { Redirect, type Href, useRouter } from "expo-router";
import { useEffect, useState } from "react";

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

  useEffect(() => {
    presenter?.setOnline(runtime.state.backend === "reachable");
  }, [presenter, runtime.state.backend]);

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
        } catch {
          // 购物车、权限和 cashier lease 由 factory 复核；失败时留在管理页。
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
        } catch {
          // 拒绝任何非 UUID 的详情参数，避免把不可信输入带进支付路由。
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
