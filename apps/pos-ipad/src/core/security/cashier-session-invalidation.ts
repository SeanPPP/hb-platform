export type CashierSessionInvalidationReason =
  | "unauthorized"
  | "forbidden"
  | "manual-lock";

export type CashierSessionInvalidationListener = (
  reason: CashierSessionInvalidationReason,
) => void;

/**
 * Keychain 票据失效与 React/Zustand 状态之间的无秘密事件桥。
 * 事件只携带原因枚举，绝不携带 authorization token、设备授权码或顾客资料。
 */
export class CashierSessionInvalidationBus {
  private readonly listeners =
    new Set<CashierSessionInvalidationListener>();

  public subscribe(listener: CashierSessionInvalidationListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public notify(reason: CashierSessionInvalidationReason): void {
    for (const listener of this.listeners) {
      try {
        listener(reason);
      } catch {
        // UI 监听器失败不能阻止 Keychain 清理或掩盖原始 401/403。
      }
    }
  }
}
