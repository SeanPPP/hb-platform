import { Redirect, type Href, useRouter } from "expo-router";
import { useEffect, useState } from "react";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  resolveProtectedSalesRouteGate,
  useCashierLoginStore,
} from "@/features/cashier-login";
import {
  HeldOrdersScreen,
  type HeldOrdersPresenter,
} from "@/features/held-orders";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type HeldOrdersPresenterBinding = Readonly<{
  services: object;
  cashier: object;
  presenter: HeldOrdersPresenter;
}>;

/** 组合根持有收银员身份，挂单路由只请求受限 presenter。 */
export default function HeldOrdersRoute() {
  const router = useRouter();
  const runtime = usePosRuntime();
  const activeCashier = useCashierLoginStore((state) => state.activeCashier);
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const gate = resolveProtectedSalesRouteGate(runtime.state, activeCashier);
  const [binding, setBinding] =
    useState<HeldOrdersPresenterBinding | null>(null);
  const [presenterCreationFailed, setPresenterCreationFailed] = useState(false);
  const presenter =
    binding?.services === runtime.services &&
    binding.cashier === activeCashier
      ? binding.presenter
      : null;

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
    let createdPresenter: HeldOrdersPresenter | null = null;
    const services = runtime.services;
    const cashier = activeCashier;
    setBinding(null);
    setPresenterCreationFailed(false);
    try {
      createdPresenter = services.heldOrders.createPresenter();
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

    return () => {
      cancelled = true;
      createdPresenter?.destroy();
      createdPresenter = null;
    };
  }, [activeCashier, clearActiveCashier, gate, runtime.services]);

  if (gate === "redirect-index") {
    return <Redirect href={"/" as Href} />;
  }
  if (gate === "redirect-login") {
    return <Redirect href={"/login" as Href} />;
  }
  if (presenterCreationFailed) {
    return <Redirect href={"/login" as Href} />;
  }
  if (!presenter) {
    return <BootstrapScreen />;
  }

  return (
    <HeldOrdersScreen
      onBack={() => router.replace("/sales" as Href)}
      presenter={presenter}
    />
  );
}
