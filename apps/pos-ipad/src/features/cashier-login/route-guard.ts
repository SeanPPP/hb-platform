import type { ActiveCashierSummary } from "./cashier-login-store";

import type { PosRuntimeState } from "@/core/runtime/pos-runtime";

export type PosEntryRoute =
  | "/registration"
  | "/login"
  | "/sales"
  | null;

export type ProtectedSalesRouteGate =
  | "check-device-identity"
  | "redirect-index"
  | "redirect-login";

/** 返回 null 时保留启动/失败/锁定页，不允许通过路由猜测为已授权。 */
export function resolvePosEntryRoute(
  runtime: PosRuntimeState,
  activeCashier: ActiveCashierSummary | null,
): PosEntryRoute {
  if (
    runtime.phase === "registration-required" ||
    runtime.phase === "pending-approval" ||
    runtime.phase === "locked"
  ) {
    return "/registration";
  }
  if (
    (runtime.phase !== "ready" && runtime.phase !== "ready-offline") ||
    (runtime.device !== "authorized-local" &&
      runtime.device !== "authorized-online")
  ) {
    return null;
  }
  return activeCashier ? "/sales" : "/login";
}

export function isActiveCashierBoundToDevice(
  cashier: ActiveCashierSummary,
  identity: Readonly<{ storeCode: string; deviceCode: string }>,
): boolean {
  return (
    cashier.storeCode === identity.storeCode &&
    cashier.deviceCode === identity.deviceCode
  );
}

/**
 * `/sales` 直链也必须重新执行设备与收银员门禁；入口页的 Redirect 不能代替受保护页自身校验。
 */
export function resolveProtectedSalesRouteGate(
  runtime: PosRuntimeState,
  activeCashier: ActiveCashierSummary | null,
): ProtectedSalesRouteGate {
  if (
    (runtime.phase !== "ready" && runtime.phase !== "ready-offline") ||
    (runtime.device !== "authorized-local" &&
      runtime.device !== "authorized-online")
  ) {
    return "redirect-index";
  }
  return activeCashier ? "check-device-identity" : "redirect-login";
}
