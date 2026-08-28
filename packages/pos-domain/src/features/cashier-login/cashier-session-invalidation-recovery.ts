import type { CashierSessionInvalidationReason } from "../../core/security/cashier-session-invalidation";

export type CashierInvalidationRecoveryDependencies = Readonly<{
  revokeTemporaryAuthorizations?(): void;
  clearActiveCashier(): void;
  /**
   * 只把运行态投影为 locked；不得关闭 SQLCipher。当前 403 请求的调用方仍需
   * 把订单/outbox 原子落为 Blocked403 后才算处理完成。
   */
  lockRuntime(): void;
}>;

/**
 * 403 表示设备或权限已被明确拒绝：清理收银员并把现有 runtime 标为 locked，
 * 但保持数据库连接可用。401 仅要求重新登录，不改变设备运行态。
 */
export function createCashierInvalidationHandler(
  dependencies: CashierInvalidationRecoveryDependencies,
): (reason: CashierSessionInvalidationReason) => void {
  return (reason) => {
    try {
      dependencies.revokeTemporaryAuthorizations?.();
    } catch {
      // 临时 scope 清理异常不能阻止更关键的活动收银员失效。
    }
    dependencies.clearActiveCashier();
    if (reason === "forbidden") {
      dependencies.lockRuntime();
    }
  };
}
