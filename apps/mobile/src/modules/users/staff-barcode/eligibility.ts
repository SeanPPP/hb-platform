import { hasPrivilegedAccessRole, isAccessAdminRoleName, isStoreManagerRoleName, isStoreStaffRoleName } from "../access-management";

export function canManageStaffBarcode(input: {
  authenticated: boolean;
  deviceOnly: boolean;
  canEditUsers: boolean;
  canManagePosStore: boolean;
  actorGuid: string;
  actorRoles: string[];
  targetGuid: string;
  targetStatus: number;
  targetRoles: string[];
}) {
  if (!input.authenticated || input.deviceOnly || !input.canEditUsers || !input.canManagePosStore
    || !input.actorGuid.trim() || !input.targetGuid.trim() || input.targetStatus !== 1) return false;
  const isAdmin = input.actorRoles.some(isAccessAdminRoleName);
  if (!isAdmin && !input.actorRoles.some(isStoreManagerRoleName)) return false;
  // 与服务端保持同一员工/受保护角色边界，避免把必然拒绝的目标加入打印队列。
  if (!input.targetRoles.some(isStoreStaffRoleName)) return false;
  return isAdmin || (input.actorGuid.trim().toLowerCase() !== input.targetGuid.trim().toLowerCase()
    && !hasPrivilegedAccessRole(input.targetRoles));
}
