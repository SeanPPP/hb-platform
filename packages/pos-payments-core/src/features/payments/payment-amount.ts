import type { Money } from "@hb/pos-domain/core/contracts/money";
import type { PaymentOperation } from "@hb/pos-domain/core/contracts/payment";

/**
 * attempt/订单账本使用有符号金额，支付提供方请求统一使用正 magnitude。
 *
 * Number.MIN_SAFE_INTEGER 显式拒绝，避免把边界哨兵值转换成外部请求金额。
 */
export function paymentProviderAmountCents(
  operation: PaymentOperation,
  amount: Money,
): number | null {
  if (
    amount.currency !== "AUD" ||
    !Number.isSafeInteger(amount.cents)
  ) {
    return null;
  }
  if (operation === "purchase") {
    return amount.cents > 0 ? amount.cents : null;
  }
  if (
    amount.cents >= 0 ||
    amount.cents === Number.MIN_SAFE_INTEGER
  ) {
    return null;
  }
  return -amount.cents;
}
