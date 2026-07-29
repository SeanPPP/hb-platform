import { create } from "zustand";

import type { PosCashierSummary } from "@/core/runtime/production-pos-service-composition";

/** Zustand 仅缓存生产组合根返回的脱敏收银员投影。 */
export type ActiveCashierSummary = PosCashierSummary;

type CashierLoginState = Readonly<{
  activeCashier: ActiveCashierSummary | null;
  setActiveCashier(cashier: ActiveCashierSummary): void;
  clearActiveCashier(): void;
}>;

/** 只保留供界面显示和权限门禁使用的摘要；授权票据始终留在 Keychain。 */
export const useCashierLoginStore = create<CashierLoginState>((set) => ({
  activeCashier: null,
  setActiveCashier: (cashier) => set({ activeCashier: cashier }),
  clearActiveCashier: () => set({ activeCashier: null }),
}));
