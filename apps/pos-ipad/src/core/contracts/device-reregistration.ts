export type DeviceReregistrationPreflight = Readonly<{
  activeCartLineCount: number;
  unresolvedPaymentCount: number;
  pendingOrderCount: number;
  pendingAuditCount: number;
  supportExportReady: boolean;
}>;

export type DeviceReregistrationDecision = Readonly<{
  allowed: boolean;
  code:
    | "READY"
    | "ACTIVE_CART"
    | "UNRESOLVED_PAYMENT"
    | "PENDING_OLD_SCOPE_DATA";
  /** 重注册无论成功或失败都不得清库。 */
  preserveLocalDatabase: true;
}>;

/**
 * 当前后端没有旧设备 scope 的恢复凭据，因此先把旧 scope 补传归零。
 * 这不会删除数据库；已同步历史仍完整保留。
 */
export function evaluateDeviceReregistrationPreflight(
  input: DeviceReregistrationPreflight,
): DeviceReregistrationDecision {
  for (const value of [
    input.activeCartLineCount,
    input.unresolvedPaymentCount,
    input.pendingOrderCount,
    input.pendingAuditCount,
  ]) {
    if (!Number.isSafeInteger(value) || value < 0) {
      throw new TypeError(
        "Device reregistration preflight counts must be non-negative integers.",
      );
    }
  }
  if (typeof input.supportExportReady !== "boolean") {
    throw new TypeError(
      "Device reregistration support export state is invalid.",
    );
  }
  const code =
    input.activeCartLineCount > 0
      ? "ACTIVE_CART"
      : input.unresolvedPaymentCount > 0
        ? "UNRESOLVED_PAYMENT"
        : input.pendingOrderCount > 0 || input.pendingAuditCount > 0
          ? "PENDING_OLD_SCOPE_DATA"
          : "READY";
  return Object.freeze({
    allowed: code === "READY",
    code,
    preserveLocalDatabase: true as const,
  });
}
