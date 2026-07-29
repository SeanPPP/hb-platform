import { z } from "zod";

export const CurrencyCodeSchema = z.literal("AUD");

export const MoneySchema = z
  .object({
    currency: CurrencyCodeSchema,
    cents: z.number().int().refine(Number.isSafeInteger, "money cents must be a safe integer"),
  })
  .strict();

export type CurrencyCode = z.infer<typeof CurrencyCodeSchema>;
export type Money = Readonly<z.infer<typeof MoneySchema>>;

export function createAud(cents: number): Money {
  if (!Number.isSafeInteger(cents)) {
    throw new TypeError("AUD money must use safe integer cents");
  }

  return {
    currency: "AUD",
    cents,
  };
}

export function parseAud(value: string): Money {
  const normalized = value.trim();
  const match = /^(-?)(\d+)(?:\.(\d{1,2}))?$/.exec(normalized);

  if (!match) {
    throw new TypeError("AUD value must have at most two decimal places");
  }

  const [, sign, wholePart = "0", fractionalPart = ""] = match;
  const cents = Number(wholePart) * 100 + Number(fractionalPart.padEnd(2, "0"));
  return createAud(sign === "-" ? -cents : cents);
}

export function addMoney(...values: readonly Money[]): Money {
  return createAud(values.reduce((total, value) => total + value.cents, 0));
}

export function subtractMoney(left: Money, right: Money): Money {
  return createAud(left.cents - right.cents);
}

export function negateMoney(value: Money): Money {
  return createAud(-value.cents);
}

export function roundAudCash(value: Money): Money {
  const sign = Math.sign(value.cents);
  const absolute = Math.abs(value.cents);
  const rounded = Math.round(absolute / 5) * 5;
  return createAud(sign * rounded);
}
