/**
 * 冷启动断网时的会话恢复决策（纯函数）。
 *
 * 只有设备绑定账号会话（deviceAccount）且本地存在设备会话与缓存用户时，
 * 才允许用缓存用户恢复登录态；401 等服务器明确拒绝、普通账号会话一律沿用清理逻辑。
 */
import { isNetworkUnavailableError } from "@/shared/network/network-error";

export type OfflineSessionRestoreDecision = "restore-from-cache" | "clear";

export interface OfflineSessionRestoreInput {
  error: unknown;
  sessionKind: string | null | undefined;
  hasStoredDeviceSession: boolean;
  cachedUser: unknown;
}

export function resolveOfflineSessionRestore(input: OfflineSessionRestoreInput): OfflineSessionRestoreDecision {
  if (input.sessionKind !== "deviceAccount") {
    return "clear";
  }
  if (!input.hasStoredDeviceSession) {
    return "clear";
  }
  if (!input.cachedUser || typeof input.cachedUser !== "object") {
    return "clear";
  }
  return isNetworkUnavailableError(input.error) ? "restore-from-cache" : "clear";
}
