export const VERSION_MANAGEMENT_ROUTES = [
  "app-downloads",
  "wpf-versions",
] as const;

export interface VersionManagementAccessInput {
  isAdmin: boolean;
  isAuthenticated: boolean;
  sessionKind: string;
}

export function canAccessVersionManagement({
  isAdmin,
  isAuthenticated,
  sessionKind,
}: VersionManagementAccessInput) {
  // 只接受真实管理员账号；审核演示和纯设备不能因缓存的角色获得访问权。
  return (
    isAdmin &&
    isAuthenticated &&
    (sessionKind === "account" || sessionKind === "deviceAccount")
  );
}

export function filterVersionManagementRoutes(
  routeNames: Iterable<string>,
  allowed: boolean,
) {
  return Array.from(routeNames).filter(
    (name) =>
      allowed || !VERSION_MANAGEMENT_ROUTES.some((route) => route === name),
  );
}
