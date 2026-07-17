import assert from "node:assert/strict";
import {
  areAccessCodeSetsEqual,
  buildAssignableRoleGuids,
  buildDirectPermissionDraft,
  buildUserAccessStoreAssignments,
  classifyUserAccessError,
  filterUserAccessRoles,
  getAccessPermissionSelectionState,
  getAccessRoleSelectionState,
  getUserAccessEligibility,
  getUserAccessStoreState,
  hasPrivilegedAccessRole,
  isAccessAdminRoleName,
  isStoreManagerRoleName,
  setUserAccessStoreState,
  toggleDirectPermission,
} from "./access-management";
import type {
  UserAccessPermissionState,
  UserAccessRole,
  UserAccessStore,
} from "./access-management-types";

const adminAliases = ["Admin", "管理员", "SuperAdmin", "超级管理员"];

adminAliases.forEach((roleName) => {
  assert.equal(isAccessAdminRoleName(` ${roleName.toUpperCase()} `), true);
});
assert.equal(isAccessAdminRoleName("StoreManager"), false);

[
  ...adminAliases,
  "StoreManager",
  "店长",
  "经理",
  "WarehouseManager",
  "Warehouse",
  "仓库经理",
  "仓库管理员",
  "WarehouseAdmin",
].forEach((roleName) => {
  assert.equal(
    hasPrivilegedAccessRole([roleName]),
    true,
    `${roleName} 应视为高权限角色`,
  );
});
assert.equal(hasPrivilegedAccessRole(["StoreStaff"]), false);
assert.equal(isStoreManagerRoleName("StoreManager"), true);
assert.equal(isStoreManagerRoleName("店长"), true);
assert.equal(isStoreManagerRoleName("经理"), true);
assert.equal(isStoreManagerRoleName("WarehouseManager"), false);

const assignedStores: UserAccessStore[] = [
  {
    storeGUID: "store-view",
    storeCode: "VIEW",
    storeName: "View store",
    isManageable: false,
  },
  {
    storeGUID: "store-manage",
    storeCode: "MANAGE",
    storeName: "Manage store",
    isManageable: true,
  },
];
const initialAssignments = buildUserAccessStoreAssignments(assignedStores);
assert.deepEqual(initialAssignments, [
  { storeGUID: "store-view", isPrimary: false },
  { storeGUID: "store-manage", isPrimary: true },
]);
assert.equal(
  getUserAccessStoreState(initialAssignments, "missing"),
  "unassigned",
);
assert.equal(getUserAccessStoreState(initialAssignments, "store-view"), "view");
assert.equal(
  getUserAccessStoreState(initialAssignments, "store-manage"),
  "manage",
);

const withNewViewStore = setUserAccessStoreState(
  initialAssignments,
  "store-new",
  "view",
);
assert.deepEqual(withNewViewStore.at(-1), {
  storeGUID: "store-new",
  isPrimary: false,
});
assert.deepEqual(
  setUserAccessStoreState(withNewViewStore, "store-view", "manage")[0],
  { storeGUID: "store-view", isPrimary: true },
);
assert.equal(
  setUserAccessStoreState(withNewViewStore, "store-new", "unassigned").some(
    (item) => item.storeGUID === "store-new",
  ),
  false,
);
assert.deepEqual(
  setUserAccessStoreState(initialAssignments, "  ", "manage"),
  initialAssignments,
  "空白 GUID 不应污染保存草稿",
);

const permissionState: UserAccessPermissionState = {
  userGuid: "user-1",
  isSuperAdmin: false,
  implicitAllPermissions: false,
  inheritedPermissionCodes: ["Users.View", "Reports.View"],
  directPermissionCodes: ["Users.Edit"],
  effectivePermissionCodes: ["Users.View", "Reports.View", "Users.Edit"],
  inheritedSources: [
    { roleName: "StoreStaff", permissionCodes: ["Users.View", "Reports.View"] },
  ],
};
const directDraft = buildDirectPermissionDraft(permissionState);
assert.deepEqual(directDraft, {
  baselineCodes: ["Users.Edit"],
  selectedCodes: ["Users.Edit"],
  inheritedPermissionCodes: ["Users.View", "Reports.View"],
});
assert.deepEqual(getAccessPermissionSelectionState(directDraft, "Users.View"), {
  checked: true,
  direct: false,
  inherited: true,
  locked: true,
});
assert.deepEqual(getAccessPermissionSelectionState(directDraft, "Users.Edit"), {
  checked: true,
  direct: true,
  inherited: false,
  locked: false,
});
assert.deepEqual(
  toggleDirectPermission(directDraft, "Users.View", false),
  directDraft,
  "角色继承权限必须锁定，不能从直接权限草稿中关闭",
);
assert.deepEqual(
  toggleDirectPermission(directDraft, "Orders.View", true).selectedCodes,
  ["Users.Edit", "Orders.View"],
);
assert.deepEqual(
  toggleDirectPermission(directDraft, "Users.Edit", false).selectedCodes,
  [],
);

const storeManagerRole: UserAccessRole = {
  roleGUID: "role-store-manager",
  roleName: "StoreManager",
  isActive: true,
};
assert.deepEqual(
  getAccessRoleSelectionState({
    role: storeManagerRole,
    selectedRoleGuids: [],
    hasManagedStoreAssignment: true,
  }),
  { selected: true, locked: true, derived: true },
  "任一管理分店应派生并锁定店长角色",
);
assert.deepEqual(
  getAccessRoleSelectionState({
    role: storeManagerRole,
    selectedRoleGuids: [storeManagerRole.roleGUID],
    hasManagedStoreAssignment: false,
  }),
  { selected: false, locked: true, derived: true },
  "店长角色不能通过角色全量替换直接保留",
);
const ordinaryRole: UserAccessRole = {
  roleGUID: "role-staff",
  roleName: "StoreStaff",
};
assert.deepEqual(
  filterUserAccessRoles(
    [
      storeManagerRole,
      { ...ordinaryRole, description: "Regular shop account" },
    ],
    "shop",
  ).map((role) => role.roleGUID),
  [ordinaryRole.roleGUID],
  "角色搜索同时匹配名称和说明",
);
assert.deepEqual(
  filterUserAccessRoles([storeManagerRole, ordinaryRole], "  "),
  [storeManagerRole, ordinaryRole],
  "空搜索词保留全部角色",
);
assert.deepEqual(
  getAccessRoleSelectionState({
    role: ordinaryRole,
    selectedRoleGuids: [ordinaryRole.roleGUID],
    hasManagedStoreAssignment: false,
  }),
  { selected: true, locked: false, derived: false },
);
assert.deepEqual(
  buildAssignableRoleGuids(
    [storeManagerRole, ordinaryRole],
    [storeManagerRole.roleGUID, ordinaryRole.roleGUID],
  ),
  [ordinaryRole.roleGUID],
  "角色全量替换必须排除由分店管理关系派生的店长角色",
);

assert.deepEqual(
  getUserAccessEligibility({
    isDeviceMode: false,
    isAdmin: true,
    isStoreManager: false,
    canManageStores: false,
    canManageRoles: false,
    canManagePos: false,
    currentUserGuid: "admin-1",
    targetUserGuid: "manager-1",
    targetStatus: 1,
    targetRoleNames: ["StoreManager"],
    hasManageableStores: true,
  }),
  {
    canOpen: true,
    storesMode: "editable",
    rolesMode: "editable",
    permissionsMode: "editable",
    canManagePos: true,
    canGrantStoreManagement: true,
    reason: null,
  },
  "Admin 可以管理高权限目标并拥有全部分段能力",
);
assert.deepEqual(
  getUserAccessEligibility({
    isDeviceMode: false,
    isAdmin: false,
    isStoreManager: true,
    canManageStores: false,
    canManageRoles: false,
    canManagePos: true,
    currentUserGuid: "manager-1",
    targetUserGuid: "staff-1",
    targetStatus: 1,
    targetRoleNames: ["StoreStaff"],
    hasManageableStores: true,
  }),
  {
    canOpen: true,
    storesMode: "readOnly",
    rolesMode: "hidden",
    permissionsMode: "editable",
    canManagePos: true,
    canGrantStoreManagement: false,
    reason: null,
  },
);
assert.equal(
  getUserAccessEligibility({
    isDeviceMode: false,
    isAdmin: false,
    isStoreManager: true,
    canManageStores: true,
    canManageRoles: true,
    canManagePos: true,
    currentUserGuid: "manager-1",
    targetUserGuid: "manager-1",
    targetStatus: 1,
    targetRoleNames: ["StoreManager"],
    hasManageableStores: true,
  }).reason,
  "self",
);
assert.equal(
  getUserAccessEligibility({
    isDeviceMode: true,
    isAdmin: true,
    isStoreManager: false,
    canManageStores: true,
    canManageRoles: true,
    canManagePos: true,
    currentUserGuid: "admin-1",
    targetUserGuid: "staff-1",
    targetStatus: 1,
    targetRoleNames: ["StoreStaff"],
    hasManageableStores: true,
  }).reason,
  "deviceMode",
);
assert.equal(
  getUserAccessEligibility({
    isDeviceMode: false,
    isAdmin: false,
    isStoreManager: true,
    canManageStores: true,
    canManageRoles: true,
    canManagePos: true,
    currentUserGuid: "manager-1",
    targetUserGuid: "manager-2",
    targetStatus: 1,
    targetRoleNames: ["StoreManager"],
    hasManageableStores: true,
  }).reason,
  "privilegedTarget",
);
const disabledTargetEligibility = getUserAccessEligibility({
  isDeviceMode: false,
  isAdmin: false,
  isStoreManager: true,
  canManageStores: true,
  canManageRoles: false,
  canManagePos: true,
  currentUserGuid: "manager-1",
  targetUserGuid: "staff-disabled",
  targetStatus: 0,
  targetRoleNames: ["StoreStaff"],
  hasManageableStores: true,
});
assert.equal(disabledTargetEligibility.canOpen, true);
assert.equal(disabledTargetEligibility.canManagePos, false);

assert.equal(
  getUserAccessEligibility({
    isDeviceMode: false,
    isAdmin: false,
    isStoreManager: true,
    canManageStores: false,
    canManageRoles: true,
    canManagePos: false,
    currentUserGuid: "delegated-user",
    targetUserGuid: "staff-1",
    targetStatus: 1,
    targetRoleNames: ["StoreStaff"],
    hasManageableStores: false,
  }).reason,
  "noPermission",
  "非管理员即使具备角色委派权限，也必须先拥有可管理分店范围",
);

assert.equal(areAccessCodeSetsEqual(["b", "a", "a"], ["a", "b"]), true);
assert.equal(areAccessCodeSetsEqual(["a"], ["a", "b"]), false);
assert.equal(
  classifyUserAccessError({ response: { status: 401 } }),
  "unauthorized",
);
assert.equal(
  classifyUserAccessError({ response: { status: 403 } }),
  "forbidden",
);
assert.equal(classifyUserAccessError({ status: 404 }), "notFound");
assert.equal(classifyUserAccessError(new Error("offline")), "network");

const storeManagerAccess = getUserAccessEligibility({
  isDeviceMode: false,
  isAdmin: false,
  isStoreManager: true,
  canManageStores: true,
  canManageRoles: true,
  canManagePos: true,
  currentUserGuid: "manager-1",
  targetUserGuid: "staff-1",
  targetStatus: 1,
  targetRoleNames: ["StoreStaff"],
  hasManageableStores: true,
});
assert.deepEqual(
  storeManagerAccess,
  {
    canOpen: true,
    storesMode: "readOnly",
    rolesMode: "hidden",
    permissionsMode: "editable",
    canManagePos: true,
    canGrantStoreManagement: false,
    reason: null,
  },
  "店长只能查看分店并维护 POS 与自己范围内的员工权限，不能分配分店或角色",
);

const delegatedStaffAccess = getUserAccessEligibility({
  isDeviceMode: false,
  isAdmin: false,
  isStoreManager: false,
  canManageStores: true,
  canManageRoles: true,
  canManagePos: true,
  currentUserGuid: "staff-operator",
  targetUserGuid: "staff-1",
  targetStatus: 1,
  targetRoleNames: ["StoreStaff"],
  hasManageableStores: true,
});
assert.equal(
  delegatedStaffAccess.canOpen,
  false,
  "普通员工即使误带管理权限码，也不能看到入口或通过深链加载授权数据",
);
assert.equal(delegatedStaffAccess.reason, "noPermission");

console.log("user access management pure logic tests passed");
