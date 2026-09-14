import assert from "node:assert/strict";
import {
  areMobileDraftsEqual,
  createAuthorizationDrafts,
  hasAuthorizationDraftChanges,
  splitMobileMenuItems,
  togglePermissionCodes,
  userMenuPermissionState,
} from "./user-authorization-logic";

const mobile = {
  bottom: ["dashboard"],
  store: ["stores"],
  operations: [],
  reports: ["reports"],
} as const;

const draft = createAuthorizationDrafts({
  stores: [{ storeGUID: "store-1", isPrimary: false }],
  roles: ["role-1"],
  permissions: {
    userGuid: "user-1",
    isSuperAdmin: false,
    implicitAllPermissions: false,
    inheritedPermissionCodes: ["Users.View"],
    directPermissionCodes: ["Users.Edit"],
    effectivePermissionCodes: ["Users.View", "Users.Edit"],
    inheritedSources: [{ roleName: "StoreStaff", permissionCodes: ["Users.View"] }],
  },
  mobile: { ...mobile, bottom: [...mobile.bottom], store: [...mobile.store], operations: [], reports: [...mobile.reports] },
});

assert.equal(hasAuthorizationDraftChanges(draft), false);
draft.roles.push("role-2");
assert.equal(hasAuthorizationDraftChanges(draft), true);
assert.equal(
  areMobileDraftsEqual(
    { ...mobile, bottom: [...mobile.bottom], store: [...mobile.store], operations: [], reports: [...mobile.reports] },
    { ...mobile, bottom: [...mobile.bottom], store: [...mobile.store], operations: [], reports: [...mobile.reports] },
  ),
  true,
);

const sections = splitMobileMenuItems([
  { key: "dashboard", title: "Dashboard", permissionCodes: [], visible: true },
  { key: "store-orders", title: "Orders", permissionCodes: [], visible: true },
  { key: "reports-sales", title: "Sales", permissionCodes: [], visible: true },
  { key: "store-settings", title: "Settings", permissionCodes: [], visible: true },
]);
assert.deepEqual(sections.bottom.map((item) => item.key), ["dashboard"]);
assert.deepEqual(sections.operations.map((item) => item.key), ["store-orders"]);
assert.deepEqual(sections.reports.map((item) => item.key), ["reports-sales"]);
assert.deepEqual(sections.store.map((item) => item.key), ["store-settings"]);

const menuDraft = draft.permissions!;
const menuChanged = togglePermissionCodes(menuDraft, ["Orders.View", "Orders.Edit"], true);
assert.deepEqual(menuChanged.selectedCodes.sort(), ["Orders.View", "Users.Edit"].sort(), "开启 OR 菜单只能授予一个必要权限");
assert.deepEqual(
  togglePermissionCodes(menuDraft, ["Users.View"], false).selectedCodes,
  ["Users.Edit"],
  "角色继承权限在菜单批量关闭时仍必须保留",
);

assert.deepEqual(togglePermissionCodes(menuDraft, ["OrderFront", "Orders.View", "Warehouse.Manage"], true, ["Orders.View", "Warehouse.Manage"]).selectedCodes.sort(), ["Users.Edit", "Orders.View"].sort());
assert.deepEqual(togglePermissionCodes(menuDraft, ["Orders.View"], true, []).selectedCodes, menuDraft.selectedCodes, "目录外权限不可授予");
const aliasDraft = { ...menuDraft, selectedCodes: ["LocalInvocie.View", "Users.Edit"] };
assert.equal(userMenuPermissionState({ permissionCodes: ["LocalPurchase.View"] }, aliasDraft, ["LocalInvocie.View"], { isSuperAdmin: false, implicitAllPermissions: false }).visible, true);
assert.deepEqual(togglePermissionCodes(aliasDraft, ["LocalPurchase.View"], false, ["LocalInvocie.View"]).selectedCodes, ["Users.Edit"], "关闭菜单清理可分配的历史别名");
assert.equal(userMenuPermissionState({ permissionCodes: [], requireAdmin: true }, menuDraft, [], { isSuperAdmin: false, implicitAllPermissions: true }).visible, false, "隐式全权限不等同管理员身份");
assert.equal(userMenuPermissionState({ permissionCodes: ["Users.View"] }, menuDraft, ["Users.View"], { isSuperAdmin: false, implicitAllPermissions: false }).locked, true, "角色继承菜单不能通过账号开关关闭");

console.log("user-authorization-logic.test.ts: ok");
