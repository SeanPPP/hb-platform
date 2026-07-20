import type { AccessControl } from "@/modules/auth/types";

const WAREHOUSE_PREORDER_BYPASS_PERMISSIONS = [
  "Warehouse.ManageOrders",
  "Warehouse.Manage",
] as const;

export function preorderActiveQueryKey(storeCode?: string | null) {
  return ["preorder", "active", storeCode?.trim() || null] as const;
}

export function shouldBlockNormalOrder(
  storeCode: string | null,
  serverValue: boolean | undefined,
  canBypass = false
) {
  if (canBypass) {
    return false;
  }
  // 已选择分店但门禁尚未返回时采用 fail-closed，避免短暂开放普通订货。
  return Boolean(storeCode && (serverValue ?? true));
}

export function canBypassPreorderGate(
  access: Pick<AccessControl, "isWarehouseStaffOnly" | "hasPermission">
) {
  const canManageWarehouseOrders = WAREHOUSE_PREORDER_BYPASS_PERMISSIONS.some(
    (permission) => access.hasPermission(permission)
  );
  if (canManageWarehouseOrders) return true;

  // 纯仓库员工只有具备普通建单权限时才有可绕过的提交入口，保持与服务端授权一致。
  return access.isWarehouseStaffOnly && access.hasPermission("Orders.Create");
}

export function resolveConfirmedGateValue(
  serverValue: boolean | undefined,
  isFetching: boolean,
  isError: boolean
) {
  return isFetching || isError ? undefined : serverValue;
}
