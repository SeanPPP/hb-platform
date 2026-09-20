/**
 * 离线商品查询的资格门禁。
 *
 * 只有设备注册绑定的会话（纯设备模式 / 设备绑定账号模式）才允许下载分店快照、
 * 进入离线模式、冷启动离线恢复。普通账号密码登录与 iOS 审核态完全不启用，
 * 其断网行为与既有实现保持一致。
 */
export type OfflineEligibleSessionKind = "device" | "deviceAccount";

export interface OfflineEligibilityInput {
  /** 当前认证会话类型（`useAuthStore.sessionKind`）。 */
  sessionKind: string | null | undefined;
  /** 本地是否存在完整设备会话（hardwareId + authCode）。 */
  hasStoredDeviceSession: boolean;
}

export function isOfflineEligibleSessionKind(
  sessionKind: string | null | undefined,
): sessionKind is OfflineEligibleSessionKind {
  return sessionKind === "device" || sessionKind === "deviceAccount";
}

export function isOfflineProductQueryEligible(input: OfflineEligibilityInput): boolean {
  return isOfflineEligibleSessionKind(input.sessionKind) && input.hasStoredDeviceSession;
}

export function hasStoredDeviceSession(
  session: { hardwareId?: string | null; authCode?: string | null } | null | undefined,
): boolean {
  return Boolean(session?.hardwareId && session.authCode);
}
