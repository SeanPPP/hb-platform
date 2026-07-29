import { Redirect, type Href, useRouter } from "expo-router";
import { useEffect, useState } from "react";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  isActiveCashierBoundToDevice,
  resolveProtectedSalesRouteGate,
  type ActiveCashierSummary,
  useCashierLoginStore,
} from "@/features/cashier-login";
import {
  hasRemoteHistoryViewPermission,
  RemoteHistoryScreen,
  RemoteHistoryUnavailableScreen,
  resolveRemoteHistoryPresenterFactory,
  type RemoteHistoryPresenter,
} from "@/features/remote-history";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type RemoteHistoryBinding = Readonly<{
  services: object;
  cashier: ActiveCashierSummary;
  presenter: RemoteHistoryPresenter;
}>;

/**
 * 远程历史在调用 API 前再次复核已认证设备；可信门店和终端只能来自设备记录。
 */
export default function RemoteHistoryRoute() {
  const router = useRouter();
  const runtime = usePosRuntime();
  const activeCashier = useCashierLoginStore((state) => state.activeCashier);
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const gate = resolveProtectedSalesRouteGate(runtime.state, activeCashier);
  const authorized =
    activeCashier !== null &&
    hasRemoteHistoryViewPermission(activeCashier.permissions);
  const online = runtime.state.backend === "reachable";
  const [binding, setBinding] = useState<RemoteHistoryBinding | null>(null);
  const [integrationUnavailable, setIntegrationUnavailable] = useState(false);
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
      !authorized
    ) {
      setBinding(null);
      setIntegrationUnavailable(false);
      return undefined;
    }

    let cancelled = false;
    let createdPresenter: RemoteHistoryPresenter | null = null;
    const services = runtime.services;
    const cashier = activeCashier;
    setBinding(null);
    setIntegrationUnavailable(false);
    void services.deviceSession
      .getDeviceIdentity()
      .then((identity) => {
        if (cancelled) return;
        if (!identity || !isActiveCashierBoundToDevice(cashier, identity)) {
          clearActiveCashier();
          return;
        }
        const factory = resolveRemoteHistoryPresenterFactory(services);
        if (!factory) {
          setIntegrationUnavailable(true);
          return;
        }
        try {
          createdPresenter = factory.createPresenter({
            online,
          });
        } catch {
          // Feature 组合失败不等同设备认证失败；保留会话并显示受控不可用。
          setIntegrationUnavailable(true);
          return;
        }
        if (cancelled) {
          createdPresenter.destroy();
          createdPresenter = null;
          return;
        }
        setBinding({ services, cashier, presenter: createdPresenter });
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
    activeCashier,
    authorized,
    clearActiveCashier,
    gate,
    online,
    runtime.services,
  ]);

  if (gate === "redirect-index") {
    return <Redirect href={"/" as Href} />;
  }
  if (gate === "redirect-login") {
    return <Redirect href={"/login" as Href} />;
  }
  if (!authorized) {
    return <Redirect href={"/sales" as Href} />;
  }
  if (integrationUnavailable) {
    return (
      <RemoteHistoryUnavailableScreen
        onBack={() => router.replace("/sales" as Href)}
      />
    );
  }
  if (!presenter) {
    return <BootstrapScreen />;
  }

  return (
    <RemoteHistoryScreen
      onBack={() => router.replace("/sales" as Href)}
      presenter={presenter}
    />
  );
}
