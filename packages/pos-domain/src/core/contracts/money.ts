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

/**
 * 与 C# decimal 一致的分位乘法：decimal.Round(quantity * cents,
 * MidpointRounding.AwayFromZero)。拒绝非有限 quantity、非安全整数 cents，
 * 结果必须落在 safe integer 范围。禁止 float 乘法 + epsilon hack：数量先经最短往返十进制字符串展开为整数分位（0.29 * 50 精确为 14.5 -> 15），
 * 整数数量直接走 BigInt 精确路径。
 */
export function multiplyCentsAwayFromZero(
  quantity: number,
  cents: number,
  label = "money product",
): number {
  if (!Number.isFinite(quantity)) {
    throw new RangeError(`${label} quantity must be finite.`);
  }
  if (!Number.isSafeInteger(cents)) {
    throw new RangeError(`${label} cents must be a safe integer.`);
  }
  if (Number.isSafeInteger(quantity)) {
    return bigIntToSafeInteger(BigInt(quantity) * BigInt(cents), label);
  }
  const decimal = decimalParts(quantity);
  const divisor = 10n ** BigInt(decimal.fractionDigits);
  const scaled = decimal.integer * BigInt(cents);
  const sign = scaled < 0n ? -1n : 1n;
  const magnitude = scaled < 0n ? -scaled : scaled;
  let quotient = magnitude / divisor;
  if ((magnitude % divisor) * 2n >= divisor) {
    quotient += 1n;
  }
  return bigIntToSafeInteger(sign * quotient, label);
}

/** 把有限 number 展开为「整数 × 10^-fractionDigits」，全程 BigInt，无浮点参与。 */
function decimalParts(value: number): Readonly<{
  integer: bigint;
  fractionDigits: number;
}> {
  const raw = String(value);
  const negative = raw.startsWith("-");
  const unsigned = negative ? raw.slice(1) : raw;
  const exponentMatch = /^(\d+(?:\.\d+)?)[eE]([+-]?\d+)$/.exec(unsigned);
  if (exponentMatch) {
    const mantissa = exponentMatch[1]!;
    const exponent = Number(exponentMatch[2]);
    if (!Number.isSafeInteger(exponent)) {
      throw new RangeError("quantity exponent is out of range.");
    }
    const [whole = "0", fraction = ""] = mantissa.split(".");
    const digits = whole + fraction;
    const fractionDigits = fraction.length - exponent;
    const magnitude = BigInt(digits === "" ? "0" : digits);
    const integer = negative ? -magnitude : magnitude;
    if (fractionDigits >= 0) {
      return { integer, fractionDigits };
    }
    return {
      integer: integer * 10n ** BigInt(-fractionDigits),
      fractionDigits: 0,
    };
  }
  const [whole = "0", fraction = ""] = unsigned.split(".");
  const digits = whole + fraction;
  const magnitude = BigInt(digits === "" ? "0" : digits);
  return {
    integer: negative ? -magnitude : magnitude,
    fractionDigits: fraction.length,
  };
}

function bigIntToSafeInteger(value: bigint, label: string): number {
  if (
    value > BigInt(Number.MAX_SAFE_INTEGER) ||
    value < BigInt(Number.MIN_SAFE_INTEGER)
  ) {
    throw new RangeError(`${label} exceeds the safe integer range.`);
  }
  return Number(value);
}
