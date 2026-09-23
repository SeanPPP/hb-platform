import type { AccessControl } from "@/modules/auth/types";
import type { CashRegisterUserScope } from "@/modules/cash-register-users/types";
import { PERMISSIONS } from "@/shared/utils/access";

export interface CashRegisterUserAccess {
  canView: boolean;
  canManage: boolean;
  canPrint: boolean;
}

/**
 * 移动端只认独立权限码：MobileManage 负责查看/创建/更新/启停，MobilePrint 负责查看/打印。
 * Web 的 Store.ManageOperations 不自动带出移动端入口；设备会话按操作人账号授权，不开放。
 */
export function resolveCashRegisterUserAccess(
  access: Pick<AccessControl, "isAdmin" | "hasPermission">,
  isDeviceMode: boolean
): CashRegisterUserAccess {
  if (isDeviceMode) {
    return { canView: false, canManage: false, canPrint: false };
  }
  const canManage = access.isAdmin || access.hasPermission(PERMISSIONS.CashRegisterUsers.MobileManage);
  const canPrint = access.isAdmin || access.hasPermission(PERMISSIONS.CashRegisterUsers.MobilePrint);
  return { canView: canManage || canPrint, canManage, canPrint };
}

/**
 * 以服务端实时判定为准：后台改了角色权限后，本页下拉刷新即可生效，无需重新登录。
 * 范围接口尚未返回（或失败）时回退到登录时缓存的权限；设备会话一律不开放。
 */
export function resolveEffectiveCashRegisterUserAccess(
  cached: CashRegisterUserAccess,
  scope: Pick<CashRegisterUserScope, "canManage" | "canPrint"> | undefined,
  isDeviceMode: boolean
): CashRegisterUserAccess {
  if (isDeviceMode) {
    return { canView: false, canManage: false, canPrint: false };
  }
  if (!scope) {
    return cached;
  }
  return { canView: scope.canManage || scope.canPrint, canManage: scope.canManage, canPrint: scope.canPrint };
}
