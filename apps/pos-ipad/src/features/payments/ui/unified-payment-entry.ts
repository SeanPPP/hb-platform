import type { Money } from "@/core/contracts";

export type RegularPaymentEntry = Readonly<{
  kind: "regular";
  checkoutIntentId: string;
  expectedCartRevision: number;
  total: Money;
  lines: readonly RegularPaymentEntryLine[];
}>;

export type RegularPaymentEntryLine = Readonly<{
  lineKey: string;
  displayName: string;
  quantity: string;
  actualAmountCents: number;
}>;

export type InstallmentCreatePaymentEntry = Readonly<{
  kind: "installment-create";
  checkoutIntentId: string;
  expectedCartRevision: number;
}>;

export type InstallmentRepaymentPaymentEntry = Readonly<{
  kind: "installment-repayment";
  installmentGuid: string;
}>;

export type PaymentRecoveryEntry = Readonly<{
  kind: "recovery";
  ledger: "regular" | "installment";
}>;

/**
 * 路由只传最小、不可受信的定位上下文。购物车、顾客、余额、权限和恢复事实
 * 必须由对应 production runtime 重新读取并核对。
 */
export type UnifiedPaymentEntry =
  | RegularPaymentEntry
  | InstallmentCreatePaymentEntry
  | InstallmentRepaymentPaymentEntry
  | PaymentRecoveryEntry;

export function regularPaymentEntry(input: Readonly<{
  checkoutIntentId: string;
  expectedCartRevision: number;
  total: Money;
  lines?: readonly RegularPaymentEntryLine[];
}>): RegularPaymentEntry {
  return Object.freeze({
    kind: "regular",
    checkoutIntentId: requiredUuid(input.checkoutIntentId),
    expectedCartRevision: nonNegativeInteger(input.expectedCartRevision),
    total: positiveAud(input.total),
    lines: Object.freeze(
      (input.lines ?? []).map((line) =>
        Object.freeze({
          lineKey: requiredText(line.lineKey, "Payment line key"),
          displayName: requiredText(
            line.displayName,
            "Payment line display name",
          ),
          quantity: requiredText(line.quantity, "Payment line quantity"),
          actualAmountCents: safeInteger(
            line.actualAmountCents,
            "Payment line amount",
          ),
        }),
      ),
    ),
  });
}

export function installmentCreatePaymentEntry(input: Readonly<{
  checkoutIntentId: string;
  expectedCartRevision: number;
}>): InstallmentCreatePaymentEntry {
  return Object.freeze({
    kind: "installment-create",
    checkoutIntentId: requiredUuid(input.checkoutIntentId),
    expectedCartRevision: nonNegativeInteger(input.expectedCartRevision),
  });
}

export function installmentRepaymentPaymentEntry(
  installmentGuid: string,
): InstallmentRepaymentPaymentEntry {
  return Object.freeze({
    kind: "installment-repayment",
    installmentGuid: requiredUuid(installmentGuid),
  });
}

export function paymentRecoveryEntry(
  ledger: PaymentRecoveryEntry["ledger"],
): PaymentRecoveryEntry {
  return Object.freeze({ kind: "recovery", ledger });
}

function requiredUuid(value: string): string {
  const normalized = value.trim().toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new TypeError("Payment entry ID is invalid.");
  }
  return normalized;
}

function nonNegativeInteger(value: number): number {
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new TypeError("Payment cart revision is invalid.");
  }
  return value;
}

function safeInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value)) throw new TypeError(`${label} is invalid.`);
  return value;
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new TypeError(`${label} is invalid.`);
  return normalized;
}

function positiveAud(value: Money): Money {
  if (
    value.currency !== "AUD" ||
    !Number.isSafeInteger(value.cents) ||
    value.cents <= 0
  ) {
    throw new TypeError("Payment total is invalid.");
  }
  return Object.freeze({ currency: "AUD", cents: value.cents });
}
