import type { AccessControl, CurrentUser, UserStoreDto } from "@/modules/auth/types";
import { hasPrivilegedAccessRole, isStoreStaffRoleName } from "@/modules/users/access-management";

export interface IdentityUserForm {
  username: string; email: string; fullName: string; isActive: boolean; password: string; confirmPassword: string;
}
export const EMPTY_IDENTITY_USER_FORM: IdentityUserForm = { username: "", email: "", fullName: "", isActive: true, password: "", confirmPassword: "" };

export function validateIdentityUserForm(form: IdentityUserForm, creating: boolean) {
  if (form.username.trim().length < 3 || form.username.trim().length > 50) return "usernameInvalid";
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim())) return "emailInvalid";
  if (creating && form.password.length < 6) return "passwordInvalid";
  if (creating && form.password !== form.confirmPassword) return "passwordMismatch";
  return null;
}

export function isIdentitySessionAllowed(session: { isAuthenticated: boolean; sessionKind: string; iosReviewOfflineGuardActive: boolean }, permission: boolean) {
  return session.isAuthenticated && session.sessionKind === "account" && !session.iosReviewOfflineGuardActive && permission;
}

export function managedIdentityStores(user: CurrentUser | null) {
  return (user?.stores ?? []).filter((store): store is UserStoreDto & { storeGUID: string } => store.isPrimary === true && !!store.storeGUID);
}

export function canModifyIdentityUser(actor: CurrentUser | null, access: AccessControl, target: { userGUID: string; roleNames: string[] }, permission: string) {
  if (!access.hasPermission(permission)) return false;
  if (!access.isStoreLevelManager) return true;
  // 门店级管理者不能通过全局页面管理自己或更高权限身份。
  return actor?.userGUID.toLowerCase() !== target.userGUID.toLowerCase() && !hasPrivilegedAccessRole(target.roleNames);
}

export function selectableIdentityRoles<T extends { roleGUID: string; roleName: string }>(roles: T[], scoped: boolean) {
  return scoped ? roles.filter(role => isStoreStaffRoleName(role.roleName)) : roles;
}

export function isUncertainIdentityWrite(error: unknown) {
  const candidate = error as {
    response?: { status?: number };
    status?: number;
    apiBusinessError?: boolean;
  } | null;
  const status = candidate?.response?.status ?? candidate?.status;
  if (status !== undefined) return status >= 500 || status === 408;
  // success=false 的业务信封已经到达客户端，属于确定失败；只有没有服务端结论的异常才需冻结防重。
  return candidate?.apiBusinessError !== true;
}

export function sameIdentityUserForm(a: IdentityUserForm, b: IdentityUserForm) {
  return (Object.keys(a) as (keyof IdentityUserForm)[]).every(key => a[key] === b[key]);
}
