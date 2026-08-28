import { useEffect } from "react";

import { useCashierLoginStore } from "./cashier-login-store";
import { createCashierInvalidationHandler } from "@hb/pos-domain/features/cashier-login/cashier-session-invalidation-recovery";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";

/**
 * 将 core 的无秘密认证失效事件投影到内存收银员状态。
 * 401/403 后销售路由会立即失去 active cashier，而本地订单与 outbox 保留。
 */
export function CashierSessionInvalidationBridge() {
  const runtime = usePosRuntime();
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );

  useEffect(() => {
    const invalidation = runtime.services?.cashierSessionInvalidation;
    if (!invalidation) {
      return undefined;
    }
    return invalidation.subscribe(
      createCashierInvalidationHandler({
        clearActiveCashier,
        lockRuntime: () => {
          runtime.updateOperationalState({
            backend: "rejected",
            device: "locked",
          });
        },
      }),
    );
  }, [clearActiveCashier, runtime]);

  useEffect(() => {
    if (runtime.state.phase === "locked") {
      clearActiveCashier();
    }
  }, [clearActiveCashier, runtime.state.phase]);

  return null;
}
