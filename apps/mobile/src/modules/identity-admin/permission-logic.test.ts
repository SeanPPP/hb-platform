import assert from "node:assert/strict";
import {
  areRoleGuidsEqual,
  buildPermissionListItems,
  createEmptyPermissionDraft,
  createRoleAssignmentDraft,
  filterPermissionListItems,
  findPermissionListItem,
  formatPermissionUserName,
  getPermissionCapabilities,
  getRoleAssignmentDelta,
  groupPermissionsByCategory,
  isImplicitAllRoleName,
  isPermissionCreationVerified,
  isPermissionUserDeltaApplied,
  isRoleAssignmentDirty,
  listPermissionCategoryOptions,
  previewGeneratedPermissions,
  toCreatePermissionInput,
  togglePermissionAction,
  toggleRoleAssignment,
  validatePermissionDraft,
} from "./permission-logic";

// ---- 能力判定：写操作必须同时是管理员，和 RoleService 的管理员二次校验保持一致 ----
const hasAll = () => true;
assert.deepEqual(getPermissionCapabilities({ isDeviceMode: true, isAdmin: true, hasPermission: hasAll }), { canView: false, canManage: false });
assert.deepEqual(getPermissionCapabilities({ isDeviceMode: false, isAdmin: false, hasPermission: hasAll }), { canView: true, canManage: false });
assert.deepEqual(getPermissionCapabilities({ isDeviceMode: false, isAdmin: true, hasPermission: () => false }), { canView: true, canManage: true });
assert.deepEqual(getPermissionCapabilities({ isDeviceMode: false, isAdmin: false, hasPermission: (code) => code === "Roles.View" }), { canView: true, canManage: false });

// ---- 目录 + 数据库表合并，规则移植自 Web 端 buildPermissionTableItems ----
const catalog = [
  {
    category: "Users",
    displayName: "用户管理",
    permissions: [
      { name: "Users.View", displayName: "查看用户", category: "Users", isSystemPermission: true, createdBy: "seed" },
      { name: "Users.Create", displayName: "创建用户", category: "Users", isSystemPermission: true },
    ],
  },
  {
    category: "StoreEvents",
    displayName: "门店活动",
    permissions: [
      { name: "StoreEvents.View", displayName: "查看门店活动", category: "StoreEvents", isSystemPermission: false, description: "目录说明" },
    ],
  },
];
const sysPermissions = [
  { id: "p1", code: "Users.View", name: "Users.View 表名", category: "Users" },
  { id: "p2", code: "StoreEvents.View", name: "表内名称", category: "StoreEvents", description: "表内说明" },
  { id: "p3", code: "Legacy.Only", name: "仅表内", category: "Legacy", description: "只在数据库表中存在" },
];
const items = buildPermissionListItems(catalog, sysPermissions);
assert.equal(items.length, 4);
const usersView = findPermissionListItem(items, "users.view");
assert.ok(usersView);
assert.equal(usersView.id, "p1", "已落库的系统权限沿用数据库 id");
assert.equal(usersView.name, "查看用户", "目录展示名优先于数据库表名称");
assert.equal(usersView.isSystem, true);
assert.equal(usersView.deletable, false, "系统权限即使已落库也不可删除");
assert.equal(usersView.createdBy, "seed");
const usersCreate = findPermissionListItem(items, "Users.Create");
assert.equal(usersCreate?.id, "Users.Create", "未落库的系统权限用代码作为 id");
assert.equal(usersCreate?.deletable, false);
const storeEventsView = findPermissionListItem(items, "StoreEvents.View");
assert.equal(storeEventsView?.deletable, true, "非系统且已落库的权限可删除");
assert.equal(storeEventsView?.description, "目录说明", "目录说明优先于表内说明");
const legacy = findPermissionListItem(items, "Legacy.Only");
assert.equal(legacy?.deletable, true);
assert.equal(legacy?.categoryName, "Legacy", "仅表内权限的分类名回退为分类键");
assert.equal(findPermissionListItem(items, "Missing.Code"), null);

// ---- 分组与自定义分类判定 ----
const groups = groupPermissionsByCategory(items);
assert.deepEqual(groups.map((group) => group.key), ["Legacy", "StoreEvents", "Users"].sort((a, b) => {
  const label = (key: string) => groups.find((group) => group.key === key)?.displayName ?? key;
  return label(a).localeCompare(label(b));
}));
assert.equal(groups.find((group) => group.key === "Users")?.isCustom, false);
assert.equal(groups.find((group) => group.key === "StoreEvents")?.isCustom, true);
assert.deepEqual(groups.find((group) => group.key === "Users")?.items.map((item) => item.code), ["Users.Create", "Users.View"], "分类内按名称排序");

// ---- 筛选 ----
assert.deepEqual(filterPermissionListItems(items, { keyword: "users" }).map((item) => item.code).sort(), ["Users.Create", "Users.View"]);
assert.deepEqual(filterPermissionListItems(items, { keyword: "门店" }).map((item) => item.code), ["StoreEvents.View"], "支持按分类展示名搜索");
assert.deepEqual(filterPermissionListItems(items, { categoryKey: "Legacy" }).map((item) => item.code), ["Legacy.Only"]);
assert.deepEqual(filterPermissionListItems(items, { categoryKey: "Users", keyword: "create" }).map((item) => item.code), ["Users.Create"]);
assert.equal(filterPermissionListItems(items, {}).length, 4);

assert.deepEqual(listPermissionCategoryOptions(items).map((option) => option.key).sort(), ["Legacy", "StoreEvents", "Users"]);

// ---- 新建草稿校验与批量生成预览 ----
const empty = createEmptyPermissionDraft();
assert.deepEqual(validatePermissionDraft(empty), { code: "required", name: "required", category: "required" });
assert.equal(validatePermissionDraft({ ...empty, code: "Store Events", name: "x", category: "y" }).code, "invalid", "代码不允许空格");
assert.equal(validatePermissionDraft({ ...empty, code: "1Bad", name: "x", category: "y" }).code, "invalid", "代码须以字母开头");
assert.equal(validatePermissionDraft({ ...empty, code: "Users.View", name: "x", category: "y" }, ["users.view"]).code, "exists", "重复代码不区分大小写");
assert.equal(validatePermissionDraft({ ...empty, code: "Users", name: "x", category: "y", actions: ["View"] }, ["Users.View"]).code, "exists", "批量生成后的代码也要查重");
assert.deepEqual(validatePermissionDraft({ ...empty, code: "StoreEvents.View", name: "门店活动", category: "StoreEvents" }, ["Users.View"]), {});
assert.equal(validatePermissionDraft({ ...empty, code: "a".repeat(101), name: "x", category: "y" }).code, "tooLong");
assert.equal(validatePermissionDraft({ ...empty, code: "A", name: "x".repeat(101), category: "y" }).name, "tooLong");
assert.equal(validatePermissionDraft({ ...empty, code: "A", name: "x", category: "y".repeat(51) }).category, "tooLong");
assert.equal(validatePermissionDraft({ ...empty, code: "A", name: "x", category: "y", description: "d".repeat(501) }).description, "tooLong");

let draft = { ...empty, code: " StoreEvents ", name: " 门店活动 ", category: "StoreEvents", description: "" };
assert.deepEqual(previewGeneratedPermissions(draft), [{ code: "StoreEvents", name: "门店活动" }], "未勾选动作时只创建一条");
draft = togglePermissionAction(draft, "Edit");
draft = togglePermissionAction(draft, "Create");
assert.deepEqual(draft.actions, ["Create", "Edit"], "动作顺序固定为 Create/View/Edit/Delete");
assert.deepEqual(previewGeneratedPermissions(draft), [
  { code: "StoreEvents.Create", name: "门店活动 - 创建" },
  { code: "StoreEvents.Edit", name: "门店活动 - 编辑" },
], "预览名称必须与服务端生成的中文后缀一致");
draft = togglePermissionAction(draft, "Edit");
assert.deepEqual(draft.actions, ["Create"]);
assert.deepEqual(toCreatePermissionInput(draft), { code: "StoreEvents", name: "门店活动", category: "StoreEvents", description: undefined, actions: ["Create"] });
assert.deepEqual(toCreatePermissionInput({ ...draft, actions: [], description: " 说明 " }), { code: "StoreEvents", name: "门店活动", category: "StoreEvents", description: "说明", actions: undefined });
assert.equal(isPermissionCreationVerified(draft, ["storeevents.create", "Users.View"]), true, "读回核验不区分大小写");
assert.equal(isPermissionCreationVerified(draft, ["Users.View"]), false);
assert.equal(previewGeneratedPermissions(empty).length, 0);

// ---- 分配角色草稿 ----
let assignment = createRoleAssignmentDraft(["r2", " r1 ", "r1", ""]);
assert.deepEqual(assignment, { selected: ["r1", "r2"], baseline: ["r1", "r2"] });
assert.equal(isRoleAssignmentDirty(assignment), false);
assignment = toggleRoleAssignment(assignment, "r3");
assignment = toggleRoleAssignment(assignment, "r1");
assert.deepEqual(getRoleAssignmentDelta(assignment), { added: ["r3"], removed: ["r1"] });
assert.equal(isRoleAssignmentDirty(assignment), true);
assert.equal(areRoleGuidsEqual(["b", "a"], ["a", "b", "a"]), true);
assert.equal(areRoleGuidsEqual(["a"], ["a", "b"]), false);

assert.equal(isImplicitAllRoleName(" admin "), true);
assert.equal(isImplicitAllRoleName("超级管理员", ["Admin", "超级管理员"]), true);
assert.equal(isImplicitAllRoleName("StoreManager"), false);

// ---- 直接授权用户：增量读回核验只看本次涉及的用户 ----
assert.equal(isPermissionUserDeltaApplied({ added: ["U1"], removed: ["u2"] }, ["u1", "u3"]), true, "他人并发新增的 u3 不影响核验，GUID 不区分大小写");
assert.equal(isPermissionUserDeltaApplied({ added: ["u1"], removed: [] }, ["u3"]), false, "新增未落库必须判定失败");
assert.equal(isPermissionUserDeltaApplied({ added: [], removed: ["u2"] }, ["u2"]), false, "移除未生效必须判定失败");
assert.equal(formatPermissionUserName({ username: "alice", fullName: " Alice Wang " }), "Alice Wang（alice）");
assert.equal(formatPermissionUserName({ username: "bob", fullName: "bob" }), "bob");
assert.equal(formatPermissionUserName({ username: "carol" }), "carol");

console.log("permission-logic tests passed");
