import type { InstallmentPresenter } from "./installment-presenter";

/**
 * 组合根持有可信 cashier lease、活动购物车、SQLCipher 缓存和支付恢复账本；
 * React 路由只取得零参数工厂，不能构造身份、门店或支付证据。
 */
export interface InstallmentsRuntimeFactory {
  createPresenter(): InstallmentPresenter;
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
    typeof candidate.createPresenter !== "function"
  ) {
    return null;
  }
  return candidate as InstallmentsRuntimeFactory;
}
