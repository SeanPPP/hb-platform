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
import { resolveInstallmentsRuntimeFactory } from "@/features/installments";
import {
  installmentCreatePaymentEntry,
  installmentRepaymentPaymentEntry,
  PaymentScreen,
  regularPaymentEntry,
  resolvePaymentLocale,
  UnifiedPaymentFacade,
  type PaymentScreenPresenter,
  type UnifiedPaymentEntry,
  type UnifiedPaymentFacadeDependencies,
} from "@/features/payments/ui";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";

type PaymentPresenterBinding = Readonly<{
  services: object;
  cashier: object;
  presenter: PaymentScreenPresenter;
}>;

type PaymentEntryParseResult =
  | Readonly<{ kind: "recovery" }>
  | Readonly<{ kind: "entry"; entry: UnifiedPaymentEntry }>
  | Readonly<{ kind: "invalid" }>;

type PaymentRouteParams = Readonly<
  Record<string, string | string[] | undefined>
>;

type UnifiedPaymentBinding = Readonly<{
  facade: UnifiedPaymentFacade;
  installmentsAvailable: boolean;
  regularAvailable: boolean;
}>;

const UNAVAILABLE_REGULAR_LANE: UnifiedPaymentFacadeDependencies["regular"] =
  Object.freeze({
    createPresenter(): never {
      throw new Error("REGULAR_PAYMENT_RUNTIME_UNAVAILABLE");
    },
    hasRecoveryRequired: async () => false,
  });

const UNAVAILABLE_INSTALLMENT_LANE: UnifiedPaymentFacadeDependencies["installments"] =
  Object.freeze({
    prepareCreateCheckout(): never {
      throw new Error("INSTALLMENT_PAYMENT_RUNTIME_UNAVAILABLE");
    },
    createCheckoutPresenter(): never {
      throw new Error("INSTALLMENT_PAYMENT_RUNTIME_UNAVAILABLE");
    },
    hasRecoveryRequired: async () => false,
  });

/** 只接受 sales route 生成的最小 checkout 上下文；支付事实仍由组合根复核。 */
export default function PaymentRoute() {
  const { replace } = useRouter();
  const runtime = usePosRuntime();
  const { i18n } = useTranslation();
  const activeCashier = useCashierLoginStore((state) => state.activeCashier);
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const params = useLocalSearchParams() as PaymentRouteParams;
  const paramsFingerprint = paymentRouteParamsFingerprint(params);
  const entry = useMemo(
    () => parsePaymentEntryFingerprint(paramsFingerprint),
    [paramsFingerprint],
  );
  const requestedEntry = useMemo(
    () => (entry.kind === "entry" ? entry.entry : null),
    [entry],
  );
  const unifiedPayment = useMemo<UnifiedPaymentBinding | null>(
    () => {
      const services = runtime.services;
      if (!services) return null;
      const regularAvailable = services.payments.status === "available";
      const installments = resolveInstallmentsRuntimeFactory(services);
      return {
        facade: new UnifiedPaymentFacade({
          regular: regularAvailable
            ? services.payments
            : UNAVAILABLE_REGULAR_LANE,
          installments:
            installments ?? UNAVAILABLE_INSTALLMENT_LANE,
        }),
        installmentsAvailable: installments !== null,
        regularAvailable,
      };
    },
    [runtime.services],
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
      !unifiedPayment ||
      entry.kind === "invalid"
    ) {
      setBinding(null);
      return undefined;
    }

    const services = runtime.services;
    const cashier = activeCashier;
    let cancelled = false;
    let createdPresenter: PaymentScreenPresenter | null = null;
    setBinding(null);
    setCreationFailed(false);
    setUnavailable(false);

    // 恢复事实始终覆盖 URL；两账本同时阻塞时由 facade 固定选择普通支付。
    void unifiedPayment.facade.resolveRecovery().then(
      (recovery) => {
        if (cancelled) return;
        const nextEntry =
          recovery.kind === "ready"
            ? recovery.entry
            : requestedEntry;
        if (
          !nextEntry ||
          !isPaymentLaneAvailable(nextEntry, unifiedPayment)
        ) {
          setUnavailable(true);
          return;
        }
        try {
          const nextPresenter =
            unifiedPayment.facade.createPresenter(nextEntry);
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
    gate,
    requestedEntry,
    runtime.services,
    unifiedPayment,
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

function parsePaymentEntry(
  input: PaymentRouteParams,
): PaymentEntryParseResult {
  const presentKeys = Object.keys(input).filter(
    (key) => input[key] !== undefined,
  );
  if (presentKeys.length === 0) return { kind: "recovery" };
  if (
    presentKeys.some((key) => !PAYMENT_PARAM_KEYS.has(key)) ||
    presentKeys.some((key) => typeof input[key] !== "string")
  ) {
    return { kind: "invalid" };
  }

  const flow = input.flow;
  if (flow === undefined || flow === "regular") {
    const expectedKeys =
      flow === "regular"
        ? REGULAR_PAYMENT_KEYS_WITH_FLOW
        : REGULAR_PAYMENT_KEYS;
    if (!hasExactKeys(presentKeys, expectedKeys)) {
      return { kind: "invalid" };
    }
    const revision = parseNonNegativeInteger(String(input.revision));
    const totalCents = parsePositiveInteger(String(input.totalCents));
    if (revision === null || totalCents === null) {
      return { kind: "invalid" };
    }
    try {
      return {
        kind: "entry",
        entry: regularPaymentEntry({
          checkoutIntentId: String(input.checkoutIntentId),
          expectedCartRevision: revision,
          total: { currency: "AUD", cents: totalCents },
        }),
      };
    } catch {
      return { kind: "invalid" };
    }
  }

  if (flow === "installment-create") {
    if (!hasExactKeys(presentKeys, INSTALLMENT_CREATE_KEYS)) {
      return { kind: "invalid" };
    }
    const revision = parseNonNegativeInteger(String(input.revision));
    if (revision === null) return { kind: "invalid" };
    try {
      return {
        kind: "entry",
        entry: installmentCreatePaymentEntry({
          checkoutIntentId: String(input.checkoutIntentId),
          expectedCartRevision: revision,
        }),
      };
    } catch {
      return { kind: "invalid" };
    }
  }

  if (flow === "installment-repayment") {
    if (!hasExactKeys(presentKeys, INSTALLMENT_REPAYMENT_KEYS)) {
      return { kind: "invalid" };
    }
    try {
      return {
        kind: "entry",
        entry: installmentRepaymentPaymentEntry(
          String(input.installmentGuid),
        ),
      };
    } catch {
      return { kind: "invalid" };
    }
  }

  return { kind: "invalid" };
}

function paymentRouteParamsFingerprint(
  input: PaymentRouteParams,
): string {
  return JSON.stringify(
    Object.keys(input)
      .filter((key) => input[key] !== undefined)
      .sort()
      .map((key) => [key, input[key]]),
  );
}

function parsePaymentEntryFingerprint(
  fingerprint: string,
): PaymentEntryParseResult {
  const entries = JSON.parse(fingerprint) as readonly (
    readonly [string, string | string[]]
  )[];
  return parsePaymentEntry(Object.fromEntries(entries));
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

function hasExactKeys(
  actual: readonly string[],
  expected: ReadonlySet<string>,
): boolean {
  return (
    actual.length === expected.size &&
    actual.every((key) => expected.has(key))
  );
}

function isPaymentLaneAvailable(
  entry: UnifiedPaymentEntry,
  binding: UnifiedPaymentBinding,
): boolean {
  if (
    entry.kind === "regular" ||
    (entry.kind === "recovery" && entry.ledger === "regular")
  ) {
    return binding.regularAvailable;
  }
  return binding.installmentsAvailable;
}

const PAYMENT_PARAM_KEYS = new Set([
  "flow",
  "checkoutIntentId",
  "revision",
  "totalCents",
  "installmentGuid",
]);
const REGULAR_PAYMENT_KEYS = new Set([
  "checkoutIntentId",
  "revision",
  "totalCents",
]);
const REGULAR_PAYMENT_KEYS_WITH_FLOW = new Set([
  "flow",
  ...REGULAR_PAYMENT_KEYS,
]);
const INSTALLMENT_CREATE_KEYS = new Set([
  "flow",
  "checkoutIntentId",
  "revision",
]);
const INSTALLMENT_REPAYMENT_KEYS = new Set([
  "flow",
  "installmentGuid",
]);
