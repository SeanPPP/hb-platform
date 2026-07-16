export const EMPLOYEE_PROFILE_REVIEW_PERMISSION =
  "EmployeeProfiles.ReviewSensitiveManagedStore";
export const EMPLOYEE_PROFILE_REVIEW_ROUTE = "employee-profile-review";

const ADMIN_ROLE_ALIASES = new Set([
  "admin",
  "管理员",
  "superadmin",
  "超级管理员",
]);
const STORE_MANAGER_ROLE_ALIASES = new Set(["storemanager", "店长", "经理"]);

export type EmployeeProfileReviewAccessReason =
  | "allowed"
  | "iosReview"
  | "device"
  | "role"
  | "permission"
  | "menu";

export interface EmployeeProfileReviewAccessInput {
  roleNames?: Iterable<string>;
  permissions?: Iterable<string>;
  menuRouteNames?: Iterable<string>;
  sessionKind?: "account" | "device" | "iosReview";
}

export interface EmployeeProfileReviewAccessResult {
  allowed: boolean;
  reason: EmployeeProfileReviewAccessReason;
}

function normalizeSet(values: Iterable<string> | undefined) {
  return new Set(Array.from(values ?? [], (value) => value.trim().toLocaleLowerCase()));
}

function hasAny(set: ReadonlySet<string>, aliases: ReadonlySet<string>) {
  return Array.from(aliases).some((alias) => set.has(alias));
}

export function getEmployeeProfileReviewAccess({
  roleNames,
  permissions,
  menuRouteNames,
  sessionKind = "account",
}: EmployeeProfileReviewAccessInput): EmployeeProfileReviewAccessResult {
  if (sessionKind === "iosReview") {
    return { allowed: false, reason: "iosReview" };
  }
  if (sessionKind === "device") {
    return { allowed: false, reason: "device" };
  }

  const roles = normalizeSet(roleNames);
  if (hasAny(roles, ADMIN_ROLE_ALIASES)) {
    return { allowed: true, reason: "allowed" };
  }

  // 纯仓库经理不具备店长别名会自然拒绝；双角色按后端店长分店范围授权。
  if (!hasAny(roles, STORE_MANAGER_ROLE_ALIASES)) {
    return { allowed: false, reason: "role" };
  }

  if (!new Set(permissions ?? []).has(EMPLOYEE_PROFILE_REVIEW_PERMISSION)) {
    return { allowed: false, reason: "permission" };
  }
  if (!new Set(menuRouteNames ?? []).has(EMPLOYEE_PROFILE_REVIEW_ROUTE)) {
    return { allowed: false, reason: "menu" };
  }
  return { allowed: true, reason: "allowed" };
}

export function filterEmployeeProfileReviewRouteNames(
  routeNames: Iterable<string>,
  reviewAllowed: boolean
) {
  const orderedRouteNames = Array.from(routeNames);
  return reviewAllowed
    ? orderedRouteNames
    : orderedRouteNames.filter((routeName) => routeName !== EMPLOYEE_PROFILE_REVIEW_ROUTE);
}
