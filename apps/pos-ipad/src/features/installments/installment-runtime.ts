import type { InstallmentCheckoutPresenter } from "./installment-checkout-presenter";
import type { InstallmentPresenter } from "./installment-presenter";

import type {
  InstallmentCreatePaymentEntry,
  InstallmentRepaymentPaymentEntry,
} from "@/features/payments/ui/unified-payment-entry";

/**
 * 组合根持有可信 cashier lease、活动购物车、SQLCipher 缓存和支付恢复账本；
 * React 路由只取得零参数工厂，不能构造身份、门店或支付证据。
 */
export interface InstallmentsRuntimeFactory {
  createPresenter(): InstallmentPresenter;
  prepareCreateCheckout(): InstallmentCreatePaymentEntry;
  createCheckoutPresenter(
    entry:
      | InstallmentCreatePaymentEntry
      | InstallmentRepaymentPaymentEntry
      | null,
  ): InstallmentCheckoutPresenter;
  hasRecoveryRequired(): Promise<boolean>;
}

export function resolveInstallmentsRuntimeFactory(
  services: object,
): InstallmentsRuntimeFactory | null {
  if (!("installments" in services)) return null;
  const candidate = services.installments;
  if (
    typeof candidate !== "object" ||
    candidate === null ||
    !("createPresenter" in candidate) ||
    typeof candidate.createPresenter !== "function" ||
    !("prepareCreateCheckout" in candidate) ||
    typeof candidate.prepareCreateCheckout !== "function" ||
    !("createCheckoutPresenter" in candidate) ||
    typeof candidate.createCheckoutPresenter !== "function" ||
    !("hasRecoveryRequired" in candidate) ||
    typeof candidate.hasRecoveryRequired !== "function"
  ) {
    return null;
  }
  return candidate as InstallmentsRuntimeFactory;
}
