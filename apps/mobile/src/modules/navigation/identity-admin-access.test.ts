import assert from "node:assert/strict";
import { resolveIdentityAdminRouteNames } from "./identity-admin-access";
import { buildWorkbenchSections } from "./workbench";
import { resolveTabRouteCorrection } from "./default-route";

const admin = {
  isAuthenticated: true, sessionKind: "account", iosReviewOfflineGuardActive: false,
  isAdmin: true, canReadUsers: true, canReadRoles: true, menuReady: true,
};
const legacy = ["users", "employee-profile", "app-downloads", "settings"];
const routes = resolveIdentityAdminRouteNames(legacy, admin);
assert.deepEqual(routes, [...legacy, "user-admin", "roles"]);
assert.deepEqual(legacy, ["users", "employee-profile", "app-downloads", "settings"]);
assert.deepEqual(resolveIdentityAdminRouteNames(routes, admin), routes, "新服务端菜单不得重复补齐");

const section = buildWorkbenchSections(routes).find(item => item.key === "people-management");
assert.deepEqual(section?.items.map(item => item.routeName), [
  "users", "user-admin", "roles", "employee-profile", "app-downloads",
]);
for (const route of ["user-admin", "roles"]) {
  assert.equal(resolveTabRouteCorrection({
    currentRouteName: route, hasAppliedDefaultRoute: true, isDeviceMode: false, routeNames: routes,
  }), null, "点击新入口后不能被 Shell 纠偏回工作台");
}

assert.deepEqual(resolveIdentityAdminRouteNames(legacy, { ...admin, isAdmin: false }), legacy,
  "普通账号即使持有读权限，也不能补回服务端未下发或关闭的菜单");
assert.deepEqual(resolveIdentityAdminRouteNames(routes, { ...admin, isAdmin: false, canReadRoles: false }),
  [...legacy, "user-admin"], "服务端菜单仍须匹配页面自身权限");
assert.deepEqual(resolveIdentityAdminRouteNames(routes, { ...admin, isAdmin: false, canReadUsers: false, canReadRoles: false }), legacy);

for (const sessionKind of ["device", "deviceAccount", "iosReview", "none"]) {
  assert.deepEqual(resolveIdentityAdminRouteNames(routes, { ...admin, sessionKind }), legacy,
    `${sessionKind} 不能利用缓存管理员角色或后端菜单进入全局管理`);
}
assert.deepEqual(resolveIdentityAdminRouteNames(routes, { ...admin, isAuthenticated: false }), legacy);
assert.deepEqual(resolveIdentityAdminRouteNames(routes, { ...admin, iosReviewOfflineGuardActive: true }), legacy);
assert.deepEqual(resolveIdentityAdminRouteNames(legacy, { ...admin, menuReady: false }), legacy,
  "菜单加载中或失败不能补齐新入口");
assert.deepEqual(resolveIdentityAdminRouteNames([], admin), []);
assert.deepEqual(resolveIdentityAdminRouteNames(["settings"], admin), ["settings"]);
assert.deepEqual(resolveIdentityAdminRouteNames(["workbench", "settings"], admin), ["workbench", "settings"]);
console.log("identity-admin navigation access tests passed");
