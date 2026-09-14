import { isIdentitySessionAllowed } from "../identity-admin/user-logic";

interface IdentityAdminNavigationAccess {
  isAuthenticated: boolean;
  sessionKind: string;
  iosReviewOfflineGuardActive: boolean;
  isAdmin: boolean;
  canReadUsers: boolean;
  canReadRoles: boolean;
  menuReady: boolean;
}

export function resolveIdentityAdminRouteNames(
  routeNames: Iterable<string>,
  access: IdentityAdminNavigationAccess,
): string[] {
  const routes = Array.from(routeNames);
  const identityRoutes = [
    { name: "user-admin", allowed: isIdentitySessionAllowed(access, access.canReadUsers) },
    { name: "roles", allowed: isIdentitySessionAllowed(access, access.canReadRoles) },
  ];
  const result = routes.filter((name) =>
    identityRoutes.every((route) => route.name !== name || route.allowed),
  );

  // 服务端对管理员开放完整菜单。旧服务尚未注册新路由时，只补齐管理员入口；
  // 普通账号仍服从服务端菜单，加载失败或只有安全壳时也不能推断业务授权。
  const canCompleteAdminMenu = access.isAdmin && access.menuReady
    && routes.some((name) => name !== "settings" && name !== "workbench");
  if (canCompleteAdminMenu) {
    for (const route of identityRoutes) {
      if (route.allowed && !result.includes(route.name)) result.push(route.name);
    }
  }
  return result;
}
