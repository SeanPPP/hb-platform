import {
  Redirect,
  type Href,
  useLocalSearchParams,
  useRouter,
} from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  resolveProtectedSalesRouteGate,
  useCashierLoginStore,
} from "@/features/cashier-login";
import {
  PaymentScreen,
  type PaymentCheckoutEntryContext,
  type PaymentPresenter,
  resolvePaymentLocale,
} from "@/features/payments/ui";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type PaymentPresenterBinding = Readonly<{
  services: object;
  cashier: object;
  presenter: PaymentPresenter;
}>;

type PaymentEntryParseResult =
  | Readonly<{ kind: "recovery"; entry: null }>
  | Readonly<{ kind: "entry"; entry: PaymentCheckoutEntryContext }>
  | Readonly<{ kind: "invalid" }>;

/** 只接受 sales route 生成的最小 checkout 上下文；支付事实仍由组合根复核。 */
export default function PaymentRoute() {
  const { replace } = useRouter();
  const runtime = usePosRuntime();
  const { i18n } = useTranslation();
  const activeCashier = useCashierLoginStore((state) => state.activeCashier);
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const { checkoutIntentId, revision, totalCents } = useLocalSearchParams<{
    checkoutIntentId?: string | string[];
    revision?: string | string[];
    totalCents?: string | string[];
  }>();
  const entry = useMemo(
    () => parsePaymentEntry({ checkoutIntentId, revision, totalCents }),
    [checkoutIntentId, revision, totalCents],
  );
  const entryKey = entry.kind === "entry"
    ? `${entry.entry.checkoutIntentId}:${entry.entry.expectedCartRevision}:${entry.entry.total.cents}`
    : entry.kind;
  const validEntry = useMemo(
    () => (entry.kind === "invalid" ? null : entry.entry),
    [entry],
  );
  const gate = resolveProtectedSalesRouteGate(runtime.state, activeCashier);
  const [binding, setBinding] = useState<PaymentPresenterBinding | null>(null);
  const [creationFailed, setCreationFailed] = useState(false);
  const [unavailable, setUnavailable] = useState(false);
  const presenter =
    binding?.services === runtime.services && binding.cashier === activeCashier
      ? binding.presenter
      : null;

  useEffect(() => {
    if (
      gate !== "check-device-identity" ||
      !activeCashier ||
      !runtime.services ||
      entry.kind === "invalid"
    ) {
      setBinding(null);
      return undefined;
    }

    const services = runtime.services;
    const cashier = activeCashier;
    const paymentService = services.payments;
    if (paymentService.status !== "available") {
      setUnavailable(true);
      return undefined;
    }

    let cancelled = false;
    let createdPresenter: PaymentPresenter | null = null;
    setBinding(null);
    setCreationFailed(false);
    setUnavailable(false);

    // 若路由参数来自旧页面而 SQLCipher 已发现恢复项，恢复项拥有优先权且不采纳新交易参数。
    void paymentService.hasRecoveryRequired().then(
      (recoveryRequired) => {
        if (cancelled) return;
        try {
          const nextPresenter = paymentService.createPresenter(
            recoveryRequired ? null : validEntry,
          );
          createdPresenter = nextPresenter;
          if (cancelled) {
            nextPresenter.destroy();
            createdPresenter = null;
            return;
          }
          setBinding({ services, cashier, presenter: nextPresenter });
        } catch {
          if (!cancelled) {
            clearActiveCashier();
            setCreationFailed(true);
          }
        }
      },
      () => {
        if (!cancelled) {
          clearActiveCashier();
          setCreationFailed(true);
        }
      },
    );

    return () => {
      cancelled = true;
      createdPresenter?.destroy();
      createdPresenter = null;
    };
  }, [
    activeCashier,
    clearActiveCashier,
    entry.kind,
    entryKey,
    gate,
    runtime.services,
    validEntry,
  ]);

  if (gate === "redirect-index") {
    return <Redirect href={"/" as Href} />;
  }
  if (gate === "redirect-login" || creationFailed) {
    return <Redirect href={"/login" as Href} />;
  }
  if (entry.kind === "invalid" || unavailable) {
    return <Redirect href={"/sales" as Href} />;
  }
  if (!presenter) {
    return <BootstrapScreen />;
  }

  return (
    <PaymentScreen
      locale={resolvePaymentLocale(i18n.resolvedLanguage ?? i18n.language)}
      onBack={() => replace("/sales" as Href)}
      onComplete={() => replace("/sales" as Href)}
      presenter={presenter}
    />
  );
}

function parsePaymentEntry(input: Readonly<{
  checkoutIntentId?: string | string[] | undefined;
  revision?: string | string[] | undefined;
  totalCents?: string | string[] | undefined;
}>): PaymentEntryParseResult {
  const values = [input.checkoutIntentId, input.revision, input.totalCents];
  if (values.every((value) => value === undefined)) {
    return { kind: "recovery", entry: null };
  }
  if (values.some((value) => typeof value !== "string")) {
    return { kind: "invalid" };
  }

  const { checkoutIntentId, revision: rawRevision, totalCents: rawTotalCents } = input;
  if (
    typeof checkoutIntentId !== "string" ||
    typeof rawRevision !== "string" ||
    typeof rawTotalCents !== "string"
  ) {
    return { kind: "invalid" };
  }
  const normalizedCheckoutIntentId = checkoutIntentId.trim();
  const revision = parseNonNegativeInteger(rawRevision);
  const totalCents = parsePositiveInteger(rawTotalCents);
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(
      normalizedCheckoutIntentId,
    ) ||
    revision === null ||
    totalCents === null
  ) {
    return { kind: "invalid" };
  }
  return {
    kind: "entry",
    entry: {
      checkoutIntentId: normalizedCheckoutIntentId,
      expectedCartRevision: revision,
      total: { currency: "AUD", cents: totalCents },
    },
  };
}

function parseNonNegativeInteger(value: string): number | null {
  if (!/^\d+$/.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function parsePositiveInteger(value: string): number | null {
  const parsed = parseNonNegativeInteger(value);
  return parsed !== null && parsed > 0 ? parsed : null;
}
