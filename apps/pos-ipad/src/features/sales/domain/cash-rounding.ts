import { createAud, type Money } from "../../../core/contracts";

import type { CashSettlement } from "./types";

function assertMoney(value: Money, label: string): number {
  if (value.currency !== "AUD" || !Number.isSafeInteger(value.cents)) {
    throw new TypeError(`${label} must use safe integer AUD cents`);
  }
  return value.cents;
}

export function roundCashAmount(amount: Money): Money {
  const cents = assertMoney(amount, "cash amount");
  const sign = Math.sign(cents);
  const absolute = Math.abs(cents);
  const quotient = Math.floor(absolute / 5);
  const remainder = absolute % 5;
  return createAud(sign * (quotient + (remainder >= 3 ? 1 : 0)) * 5);
}

export function calculateCashSettlement(input: {
  actualAmount: Money;
  nonCashAmount?: Money;
  cashTendered: Money;
}): CashSettlement {
  const actual = assertMoney(input.actualAmount, "actual amount");
  const nonCash = assertMoney(
    input.nonCashAmount ?? createAud(0),
    "non-cash amount",
  );
  const cashTendered = assertMoney(
    input.cashTendered,
    "cash tendered",
  );

  if (actual >= 0) {
    const normalizedNonCash = Math.min(
      actual,
      Math.max(0, nonCash),
    );
    const remaining = actual - normalizedNonCash;
    const cashDue = roundCashAmount(createAud(remaining)).cents;
    const normalizedTendered = roundCashAmount(
      createAud(cashTendered),
    ).cents;
    return {
      cashDue: createAud(cashDue),
      normalizedCashTendered: createAud(normalizedTendered),
      change: createAud(Math.max(0, normalizedTendered - cashDue)),
      roundingAdjustment: createAud(cashDue - remaining),
    };
  }

  const absoluteActual = Math.abs(actual);
  const nonCashRefund = Math.min(
    absoluteActual,
    Math.max(0, -nonCash),
  );
  const remainingRefund = absoluteActual - nonCashRefund;
  const cashDue = -roundCashAmount(createAud(remainingRefund)).cents;
  const normalizedTendered = roundCashAmount(
    createAud(cashTendered),
  ).cents;
  return {
    cashDue: createAud(cashDue),
    normalizedCashTendered: createAud(normalizedTendered),
    change: createAud(0),
    roundingAdjustment: createAud(cashDue + remainingRefund),
  };
}
