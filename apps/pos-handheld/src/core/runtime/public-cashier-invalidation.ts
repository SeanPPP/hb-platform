import type {
  CashierSessionInvalidationBus,
  CashierSessionInvalidationListener,
} from "@hb/pos-domain/core/security/cashier-session-invalidation";

export type PosCashierInvalidationRuntimeService = Readonly<{
  subscribe(listener: CashierSessionInvalidationListener): () => void;
}>;

/** React 只能订阅无秘密失效事件，不能伪造 401/403 或主动清除其他会话。 */
export function createPublicCashierInvalidation(
  bus: CashierSessionInvalidationBus,
): PosCashierInvalidationRuntimeService {
  return Object.freeze({
    subscribe: (listener) => bus.subscribe(listener),
  });
}
