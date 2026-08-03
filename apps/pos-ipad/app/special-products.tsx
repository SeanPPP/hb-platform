import { Redirect, type Href, useRouter } from "expo-router";
import { useEffect, useState } from "react";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  isActiveCashierBoundToDevice,
  resolveProtectedSalesRouteGate,
  useCashierLoginStore,
} from "@/features/cashier-login";
import {
  resolveSpecialProductsAccess,
  resolveSpecialProductsRuntimeFactory,
  SpecialProductsScreen,
  SpecialProductsUnavailableScreen,
  type SpecialProductsPresenter,
} from "@/features/special-products";
import { usePosSound } from "@/ui/feedback/pos-sound-context";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type SpecialProductsBinding = Readonly<{
  cashier: object;
  presenter: SpecialProductsPresenter;
  services: object;
}>;

/**
 * 直链与销售入口执行同一设备/收银员门禁；factory 仅为 Wave4 组合根未接线期间
 * 的窄桥接点，真实权限、门店、cart lease 必须继续由 factory 内部复核。
 */
export default function SpecialProductsRoute() {
  const router = useRouter();
  const runtime = usePosRuntime();
  const activeCashier = useCashierLoginStore((state) => state.activeCashier);
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const gate = resolveProtectedSalesRouteGate(runtime.state, activeCashier);
  const access = resolveSpecialProductsAccess(
    activeCashier?.permissions ?? [],
  );
  const factory = runtime.services
    ? resolveSpecialProductsRuntimeFactory(runtime.services)
    : null;
  const [binding, setBinding] = useState<SpecialProductsBinding | null>(null);
  const [runtimeUnavailable, setRuntimeUnavailable] = useState(false);
  const [presenterCreationFailed, setPresenterCreationFailed] =
    useState(false);
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
      setPresenterCreationFailed(false);
      return undefined;
    }

    let cancelled = false;
    let createdPresenter: SpecialProductsPresenter | null = null;
    const services = runtime.services;
    const cashier = activeCashier;
    setBinding(null);
    setRuntimeUnavailable(false);
    setPresenterCreationFailed(false);
    void services.deviceSession
      .getDeviceIdentity()
      .then((identity) => {
        if (cancelled) return;
        if (!identity || !isActiveCashierBoundToDevice(cashier, identity)) {
          clearActiveCashier();
          return;
        }
        if (!factory) {
          setRuntimeUnavailable(true);
          return;
        }
        try {
          createdPresenter = factory.createPresenter();
          if (cancelled) {
            createdPresenter.destroy();
            createdPresenter = null;
            return;
          }
          setBinding({ cashier, presenter: createdPresenter, services });
        } catch {
          if (!cancelled) {
            clearActiveCashier();
            setPresenterCreationFailed(true);
          }
        }
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
  if (gate === "redirect-login" || presenterCreationFailed) {
    return <Redirect href={"/login" as Href} />;
  }
  if (!access.canView) {
    return <Redirect href={"/sales" as Href} />;
  }
  if (runtimeUnavailable) {
    return (
      <SpecialProductsUnavailableScreen
        onBack={() => router.dismissTo("/sales" as Href)}
      />
    );
  }
  if (!presenter) {
    return <BootstrapScreen />;
  }

  return (
    <>
      <SpecialProductsSoundBridge presenter={presenter} />
      <SpecialProductsScreen
        onBack={() => router.dismissTo("/sales" as Href)}
        presenter={presenter}
      />
    </>
  );
}

function SpecialProductsSoundBridge({
  presenter,
}: Readonly<{ presenter: SpecialProductsPresenter }>) {
  const { play } = usePosSound();
  useEffect(
    () =>
      presenter.subscribeFeedback((event) => {
        switch (event.kind) {
          case "query-found":
          case "query-empty":
          case "query-error":
            play(event.kind);
            return;
          case "added":
            play("cart-added");
            return;
          case "incremented":
            play("cart-incremented");
            return;
          case "failed-blocked":
            play("cart-failed-blocked");
        }
      }),
    [play, presenter],
  );
  return null;
}
