import { strict as assert } from "node:assert";
import {
  arePermissionCodeSetsEqual,
  buildGrantedPosPermissionCodes,
  buildPosPermissionDraft,
  classifyPosPermissionError,
  getEffectivePosPermissionCodes,
  getPosPermissionEntryState,
  groupPosPermissions,
  setPosPermissionGroupSelection,
  togglePosPermissionCode,
} from "./pos-terminal-permissions";
import { PERMISSIONS } from "../../shared/utils/access";
import type {
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
  UpdateStoreUserPosTerminalPermissionsPayload,
} from "./types";

const assignablePermissions: PosTerminalPermissionOption[] = [
  {
    code: "PosTerminal.Sales.AddItem",
    name: "添加商品",
    group: "销售",
    description: "允许添加商品",
  },
  {
    code: "PosTerminal.Sales.RemoveLine",
    name: "删除明细",
    group: "销售",
    description: "允许删除明细",
  },
  {
    code: "PosTerminal.Payment.TakeCash",
    name: "现金收款",
    group: "支付",
    description: "允许现金收款",
  },
];

const permissionState: StoreUserPosTerminalPermissions = {
  mode: "override",
  assignablePermissions,
  inheritedPermissionCodes: ["PosTerminal.Sales.AddItem"],
  overriddenPermissionCodes: ["PosTerminal.Payment.TakeCash"],
  grantedPermissionCodes: ["PosTerminal.Payment.TakeCash"],
  effectivePermissionCodes: [
    "PosTerminal.Sales.AddItem",
    "Unknown.Permission",
    "PosTerminal.Payment.TakeCash",
    "PosTerminal.Sales.AddItem",
  ],
};

assert.deepEqual(
  getEffectivePosPermissionCodes(permissionState),
  ["PosTerminal.Sales.AddItem", "PosTerminal.Payment.TakeCash"],
  "effective 权限只能保留 assignablePermissions 白名单并去重"
);

const updatePayload: UpdateStoreUserPosTerminalPermissionsPayload = {
  userGuid: "user-guid",
  storeGuid: "store-guid",
  grantedPermissionCodes: buildGrantedPosPermissionCodes(
    [
      "PosTerminal.Payment.TakeCash",
      "Unknown.Permission",
      "PosTerminal.Payment.TakeCash",
    ],
    assignablePermissions
  ),
};

assert.deepEqual(
  updatePayload,
  {
    userGuid: "user-guid",
    storeGuid: "store-guid",
    grantedPermissionCodes: ["PosTerminal.Payment.TakeCash"],
  },
  "保存 payload 应包含目标用户、分店，并过滤去重后的白名单权限"
);

assert.deepEqual(
  buildPosPermissionDraft(permissionState),
  {
    baselineCodes: [
      "PosTerminal.Sales.AddItem",
      "PosTerminal.Payment.TakeCash",
    ],
    selectedCodes: [
      "PosTerminal.Sales.AddItem",
      "PosTerminal.Payment.TakeCash",
    ],
  },
  "草稿应以过滤后的 effectivePermissionCodes 同时初始化基线和选择"
);

const groupedPermissions = groupPosPermissions([
  {
    code: "sales-b",
    name: "乙权限",
    group: "销售",
    description: "",
  },
  {
    code: "payment-a",
    name: "甲权限",
    group: "支付",
    description: "",
  },
  {
    code: "sales-a",
    name: "甲权限",
    group: "销售",
    description: "",
  },
  {
    code: "sales-a-second",
    name: "甲权限",
    group: "销售",
    description: "",
  },
]);

assert.deepEqual(
  groupedPermissions.map((item) => item.group),
  ["销售", "支付"],
  "权限分组应保持 group 首次出现的顺序"
);
assert.deepEqual(
  groupedPermissions[0]?.permissions.map((item) => item.code),
  ["sales-a", "sales-a-second", "sales-b"],
  "同组权限应按中文名称稳定排序"
);

assert.deepEqual(
  togglePosPermissionCode(["a"], "b"),
  ["a", "b"],
  "toggle 应添加尚未选择的权限"
);
assert.deepEqual(
  togglePosPermissionCode(["a", "b"], "a"),
  ["b"],
  "toggle 应移除已经选择的权限"
);
assert.deepEqual(
  setPosPermissionGroupSelection(["outside", "a"], ["a", "b"], true),
  ["outside", "a", "b"],
  "组全选只应补齐当前组并保留其他组选择"
);
assert.deepEqual(
  setPosPermissionGroupSelection(["outside", "a", "b"], ["a", "b"], false),
  ["outside"],
  "组清空只应移除当前组"
);
assert.equal(
  arePermissionCodeSetsEqual(["a", "b", "a"], ["b", "a"]),
  true,
  "集合相等应忽略顺序和重复项"
);
assert.equal(
  arePermissionCodeSetsEqual(["a"], ["a", "b"]),
  false,
  "不同权限集合不应视为相等"
);

const eligibleEntry = {
  canManagePosTerminalPermissions: true,
  canManageStore: true,
  currentUserGuid: "current-user",
  targetUserGuid: "target-user",
  targetStatus: 1,
  targetRoleNames: ["StoreStaff"],
  storeGuid: "store-guid",
};

assert.equal(
  PERMISSIONS.Users.ManagePosTerminalPermissions,
  "Users.ManagePosTerminalPermissions",
  "POS 授权入口应使用后端定义的显式权限代码"
);

assert.equal(
  getPosPermissionEntryState(eligibleEntry),
  "enabled",
  "普通启用用户应显示可用入口"
);
assert.equal(
  getPosPermissionEntryState({
    ...eligibleEntry,
    canManagePosTerminalPermissions: false,
  }),
  "hidden",
  "没有 POS 授权管理权限时应隐藏入口"
);
assert.equal(
  getPosPermissionEntryState({ ...eligibleEntry, canManageStore: false }),
  "hidden",
  "无法管理目标分店时应隐藏入口"
);
assert.equal(
  getPosPermissionEntryState({
    ...eligibleEntry,
    targetUserGuid: "CURRENT-USER",
  }),
  "hidden",
  "本人账号应隐藏入口"
);
assert.equal(
  getPosPermissionEntryState({
    ...eligibleEntry,
    currentUserGuid: " current-user",
    targetUserGuid: "CURRENT-USER ",
  }),
  "hidden",
  "本人账号 GUID 首尾有空白时仍应隐藏入口"
);
assert.equal(
  getPosPermissionEntryState({ ...eligibleEntry, targetStatus: 0 }),
  "hidden",
  "停用账号应隐藏入口"
);

const privilegedRoles = [
  "Admin",
  "管理员",
  "SuperAdmin",
  "超级管理员",
  "StoreManager",
  "店长",
  "经理",
  "WarehouseManager",
  "仓库经理",
  "仓库管理员",
  "WarehouseAdmin",
];

privilegedRoles.forEach((roleName, index) => {
  const roleWithDifferentCase = index % 2 === 0 ? roleName.toUpperCase() : roleName;
  assert.equal(
    getPosPermissionEntryState({
      ...eligibleEntry,
      targetRoleNames: [roleWithDifferentCase],
    }),
    "hidden",
    `高权限角色 ${roleName} 应隐藏入口`
  );
});

assert.equal(
  getPosPermissionEntryState({
    ...eligibleEntry,
    targetRoleNames: ["StoreManagerAssistant"],
  }),
  "enabled",
  "高权限角色应精确匹配，不能命中相似角色名"
);
assert.equal(
  getPosPermissionEntryState({
    ...eligibleEntry,
    targetRoleNames: [" StoreManager "],
  }),
  "hidden",
  "高权限角色首尾有空白时仍应隐藏入口"
);
assert.equal(
  getPosPermissionEntryState({ ...eligibleEntry, storeGuid: null }),
  "disabled",
  "符合资格但缺少 storeGuid 时应显示禁用入口"
);
assert.equal(
  getPosPermissionEntryState({ ...eligibleEntry, storeGuid: "   " }),
  "disabled",
  "storeGuid 只有空白时应视为缺少分店标识并禁用入口"
);

assert.equal(
  classifyPosPermissionError({ response: { status: 401 } }),
  "unauthorized",
  "401 应分类为 unauthorized"
);
assert.equal(
  classifyPosPermissionError({ response: { status: 403 } }),
  "forbidden",
  "403 应分类为 forbidden"
);
assert.equal(
  classifyPosPermissionError({ response: { status: 404 } }),
  "notFound",
  "404 应分类为 notFound"
);
assert.equal(
  classifyPosPermissionError({ response: { status: 500 } }),
  "network",
  "其他 HTTP 错误应分类为 network"
);
assert.equal(
  classifyPosPermissionError(new Error("offline")),
  "network",
  "无 HTTP 状态的错误应分类为 network"
);

console.log("pos-terminal-permissions.test.ts: ok");
