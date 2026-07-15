# Expo 分店用户 POS 终端授权 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Expo 店员管理中新增独立全屏的“POS 授权”页面，让店长按分店修改普通用户的 POS 终端业务权限，并支持保存覆盖和恢复继承。

**Architecture:** 在现有 `users` 模块内增加独立的权限领域逻辑、API 与 React Query hooks；用户列表只负责入口判断和路由跳转，授权页面负责查询、编辑、保存、恢复及未保存返回确认。所有前端判断只做保守预检，后端 `Users.ManagePosTerminalPermissions` 策略和用户/分店边界仍是最终授权依据。

**Tech Stack:** Expo Router 6、React Native 0.81、React Native Paper 5、TanStack React Query 5、Axios、TypeScript 5.9、Node + `tsx` 轻量测试。

---

## 文件结构

- Create: `apps/mobile/src/modules/users/pos-terminal-permissions.ts` — 入口资格、权限白名单、分组、选中状态及错误分类的纯业务逻辑。
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions.test.ts` — 纯逻辑与页面状态边界测试。
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-api.ts` — GET/PUT/DELETE 请求、路径编码和响应规范化。
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-api.test.ts` — API 路径、请求体和响应解包测试。
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-hooks.ts` — 查询、保存与恢复 hooks 及缓存同步。
- Create: `apps/mobile/app/users/[userGuid]/pos-terminal-permissions.tsx` — 仅负责 Expo Router 文件路由。
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-screen.tsx` — 独立全屏授权页面与离开保护。
- Modify: `apps/mobile/src/modules/users/types.ts` — 增加 POS 权限 DTO 与更新参数类型。
- Modify: `apps/mobile/src/modules/users/index.ts` — 导出新增 API、hooks、类型和纯逻辑。
- Modify: `apps/mobile/app/(tabs)/users.tsx` — 增加保守入口判断和路由跳转。
- Modify: `apps/mobile/src/shared/utils/access.ts` — 增加共享权限常量 `Users.ManagePosTerminalPermissions`。
- Modify: `apps/mobile/src/locales/zh/screens/userManagement.json` — 中文文案。
- Modify: `apps/mobile/src/locales/en/screens/userManagement.json` — 英文文案。
- Modify: `apps/mobile/package.json` — 增加本功能的测试脚本。

### Task 1: 建立权限领域模型与纯逻辑

**Files:**
- Modify: `apps/mobile/src/modules/users/types.ts`
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions.ts`
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions.test.ts`

- [ ] **Step 1: 先写失败测试**

创建 `apps/mobile/src/modules/users/pos-terminal-permissions.test.ts`：

```ts
import assert from "node:assert/strict";
import {
  buildGrantedPosPermissionCodes,
  buildPosPermissionDraft,
  buildPosPermissionGroups,
  classifyPosPermissionError,
  getEditablePosPermissionCodes,
  getPosPermissionEntryState,
  setGroupPermissionSelection,
  togglePermissionSelection,
} from "./pos-terminal-permissions";
import type { PosTerminalPermissionOption } from "./types";

const permissions: PosTerminalPermissionOption[] = [
  { code: "Permissions.PosTerminal.Sales.Sell", name: "销售", group: "POS 销售", description: "创建销售单" },
  { code: "Permissions.PosTerminal.Sales.Refund", name: "退款", group: "POS 销售", description: "处理退款" },
  { code: "Permissions.PosTerminal.Payment.Cash", name: "现金", group: "POS 收款", description: "现金收款" },
];

assert.deepEqual(
  getEditablePosPermissionCodes(
    ["Permissions.PosTerminal.Sales.Sell", "Permissions.PosTerminal.Admin.Hidden"],
    permissions,
  ),
  ["Permissions.PosTerminal.Sales.Sell"],
);
assert.deepEqual(
  buildGrantedPosPermissionCodes(
    ["Permissions.PosTerminal.Sales.Sell", "Permissions.PosTerminal.Sales.Sell", "Outside.Scope"],
    permissions,
  ),
  ["Permissions.PosTerminal.Sales.Sell"],
);
assert.deepEqual(
  buildPosPermissionDraft({
    mode: "Inherited",
    assignablePermissions: permissions,
    inheritedPermissionCodes: ["Permissions.PosTerminal.Sales.Sell", "Outside.Scope"],
    overriddenPermissionCodes: [],
    grantedPermissionCodes: [],
    effectivePermissionCodes: ["Permissions.PosTerminal.Sales.Sell", "Outside.Scope"],
  }),
  {
    baselineCodes: ["Permissions.PosTerminal.Sales.Sell"],
    selectedCodes: ["Permissions.PosTerminal.Sales.Sell"],
  },
);
assert.deepEqual(
  buildPosPermissionGroups(permissions).map((group) => group.name),
  ["POS 销售", "POS 收款"],
);
assert.deepEqual(
  togglePermissionSelection(["Permissions.PosTerminal.Sales.Sell"], "Permissions.PosTerminal.Payment.Cash", true),
  ["Permissions.PosTerminal.Sales.Sell", "Permissions.PosTerminal.Payment.Cash"],
);
assert.deepEqual(
  setGroupPermissionSelection(
    ["Permissions.PosTerminal.Payment.Cash"],
    permissions.slice(0, 2),
    true,
  ),
  [
    "Permissions.PosTerminal.Payment.Cash",
    "Permissions.PosTerminal.Sales.Sell",
    "Permissions.PosTerminal.Sales.Refund",
  ],
);

assert.deepEqual(
  getPosPermissionEntryState({
    hasPermission: true,
    isManageableStore: true,
    actorUserGuid: "manager-1",
    targetUser: { userGUID: "staff-1", status: 1, roleNames: ["StoreStaff"] },
    storeGuid: "store-guid-1",
  }),
  { state: "enabled" },
);
assert.deepEqual(
  getPosPermissionEntryState({
    hasPermission: true,
    isManageableStore: true,
    actorUserGuid: "manager-1",
    targetUser: { userGUID: "manager-1", status: 1, roleNames: ["StoreManager"] },
    storeGuid: "store-guid-1",
  }),
  { state: "hidden" },
);
assert.deepEqual(
  getPosPermissionEntryState({
    hasPermission: true,
    isManageableStore: true,
    actorUserGuid: "admin-1",
    targetUser: { userGUID: "admin-2", status: 1, roleNames: ["超级管理员"] },
    storeGuid: "store-guid-1",
  }),
  { state: "hidden" },
);
assert.deepEqual(
  getPosPermissionEntryState({
    hasPermission: true,
    isManageableStore: true,
    actorUserGuid: "manager-1",
    targetUser: { userGUID: "staff-1", status: 1, roleNames: ["StoreStaff"] },
    storeGuid: undefined,
  }),
  { state: "disabled", reason: "missingStoreGuid" },
);
assert.deepEqual(
  getPosPermissionEntryState({
    hasPermission: false,
    isManageableStore: true,
    actorUserGuid: "manager-1",
    targetUser: { userGUID: "staff-1", status: 1, roleNames: ["StoreStaff"] },
    storeGuid: "store-guid-1",
  }),
  { state: "hidden" },
);
assert.equal(classifyPosPermissionError({ response: { status: 403 } }), "forbidden");
assert.equal(classifyPosPermissionError({ response: { status: 404 } }), "notFound");
assert.equal(classifyPosPermissionError(new Error("offline")), "network");
```

- [ ] **Step 2: 运行测试并确认按预期失败**

Run:

```bash
cd apps/mobile
npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts
```

Expected: FAIL，提示找不到 `./pos-terminal-permissions` 或新增类型。

- [ ] **Step 3: 增加 DTO 类型**

在 `apps/mobile/src/modules/users/types.ts` 末尾加入：

```ts
export interface PosTerminalPermissionOption {
  code: string;
  name: string;
  group: string;
  description: string;
}

export interface StoreUserPosTerminalPermissions {
  mode: string;
  assignablePermissions: PosTerminalPermissionOption[];
  inheritedPermissionCodes: string[];
  overriddenPermissionCodes: string[];
  grantedPermissionCodes: string[];
  effectivePermissionCodes: string[];
}

export interface StoreUserPosTerminalPermissionTarget {
  userGuid: string;
  storeGuid: string;
}

export interface UpdateStoreUserPosTerminalPermissionsPayload
  extends StoreUserPosTerminalPermissionTarget {
  grantedPermissionCodes: string[];
}
```

- [ ] **Step 4: 实现最小纯逻辑**

创建 `apps/mobile/src/modules/users/pos-terminal-permissions.ts`：

```ts
import type {
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
  StoreUserListItem,
} from "./types";

const HIGH_PRIVILEGE_ROLE_NAMES = new Set(
  [
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
  ].map((role) => role.toLowerCase()),
);

export interface PosPermissionGroup {
  name: string;
  permissions: PosTerminalPermissionOption[];
}

export type PosPermissionEntryState =
  | { state: "hidden" }
  | { state: "disabled"; reason: "missingStoreGuid" }
  | { state: "enabled" };

function uniqueCodes(codes: string[]) {
  return Array.from(new Set(codes.filter(Boolean)));
}

export function getEditablePosPermissionCodes(
  permissionCodes: string[],
  assignablePermissions: PosTerminalPermissionOption[],
) {
  const assignableCodes = new Set(assignablePermissions.map((permission) => permission.code));
  return uniqueCodes(permissionCodes.filter((code) => assignableCodes.has(code)));
}

export function buildGrantedPosPermissionCodes(
  selectedPermissionCodes: string[],
  assignablePermissions: PosTerminalPermissionOption[],
) {
  return getEditablePosPermissionCodes(selectedPermissionCodes, assignablePermissions);
}

export function buildPosPermissionDraft(state: StoreUserPosTerminalPermissions) {
  const selectedCodes = getEditablePosPermissionCodes(
    state.effectivePermissionCodes,
    state.assignablePermissions,
  );
  return { baselineCodes: selectedCodes, selectedCodes };
}

export function buildPosPermissionGroups(
  permissions: PosTerminalPermissionOption[],
): PosPermissionGroup[] {
  const groups = new Map<string, PosTerminalPermissionOption[]>();
  permissions.forEach((permission) => {
    const groupName = permission.group.trim() || "其他";
    groups.set(groupName, [...(groups.get(groupName) ?? []), permission]);
  });
  return Array.from(groups.entries()).map(([name, items]) => ({
    name,
    permissions: [...items].sort((left, right) =>
      left.name.localeCompare(right.name, "zh-CN", { numeric: true }),
    ),
  }));
}

export function togglePermissionSelection(
  selectedCodes: string[],
  permissionCode: string,
  checked: boolean,
) {
  const next = new Set(selectedCodes);
  if (checked) next.add(permissionCode);
  else next.delete(permissionCode);
  return Array.from(next);
}

export function setGroupPermissionSelection(
  selectedCodes: string[],
  groupPermissions: PosTerminalPermissionOption[],
  checked: boolean,
) {
  const next = new Set(selectedCodes);
  groupPermissions.forEach((permission) => {
    if (checked) next.add(permission.code);
    else next.delete(permission.code);
  });
  return Array.from(next);
}

export function arePermissionSetsEqual(left: string[], right: string[]) {
  const leftSet = new Set(left);
  const rightSet = new Set(right);
  return leftSet.size === rightSet.size && Array.from(leftSet).every((code) => rightSet.has(code));
}

export function getPosPermissionEntryState({
  hasPermission,
  isManageableStore,
  actorUserGuid,
  targetUser,
  storeGuid,
}: {
  hasPermission: boolean;
  isManageableStore: boolean;
  actorUserGuid?: string | null;
  targetUser: Pick<StoreUserListItem, "userGUID" | "status" | "roleNames">;
  storeGuid?: string;
}): PosPermissionEntryState {
  const isSelf = Boolean(actorUserGuid && actorUserGuid === targetUser.userGUID);
  const isHighPrivilege = targetUser.roleNames.some((role) =>
    HIGH_PRIVILEGE_ROLE_NAMES.has(role.toLowerCase()),
  );
  if (!hasPermission || !isManageableStore || targetUser.status !== 1 || isSelf || isHighPrivilege) {
    return { state: "hidden" };
  }
  if (!storeGuid) return { state: "disabled", reason: "missingStoreGuid" };
  return { state: "enabled" };
}

export type PosPermissionErrorKind = "unauthorized" | "forbidden" | "notFound" | "network";

export function classifyPosPermissionError(error: unknown): PosPermissionErrorKind {
  const status = (error as { response?: { status?: number } } | null)?.response?.status;
  if (status === 401) return "unauthorized";
  if (status === 403) return "forbidden";
  if (status === 404) return "notFound";
  return "network";
}
```

- [ ] **Step 5: 运行测试并确认通过**

Run: `cd apps/mobile && npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts`

Expected: PASS，进程退出码为 0。

- [ ] **Step 6: 提交本任务**

```bash
git add apps/mobile/src/modules/users/types.ts apps/mobile/src/modules/users/pos-terminal-permissions.ts apps/mobile/src/modules/users/pos-terminal-permissions.test.ts
git commit -m "新增移动端 POS 授权领域逻辑 reasonix"
```

### Task 2: 增加 API、响应规范化与 React Query hooks

**Files:**
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-api.ts`
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-api.test.ts`
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-hooks.ts`
- Modify: `apps/mobile/src/modules/users/index.ts`

- [ ] **Step 1: 写 API 失败测试**

创建 `apps/mobile/src/modules/users/pos-terminal-permissions-api.test.ts`。测试用动态导入并替换 `apiClient` 的三种方法，记录请求后恢复原方法：

```ts
import assert from "node:assert/strict";
import Module from "node:module";

async function run() {
  Object.assign(globalThis, { __DEV__: false });
  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };
  mockModule("expo-router", { router: { replace: () => undefined } });
  mockModule("react-native", {
    AppState: { addEventListener: () => ({ remove: () => undefined }) },
    NativeModules: {},
    Platform: { OS: "ios", select: <T>(values: { ios?: T; default?: T }) => values.ios ?? values.default },
  });
  mockModule("expo-secure-store", {
    getItemAsync: async () => null,
    setItemAsync: async () => undefined,
    deleteItemAsync: async () => undefined,
  });
  mockModule("expo-location", {
    hasStartedLocationUpdatesAsync: async () => false,
    stopLocationUpdatesAsync: async () => undefined,
  });
  mockModule("@react-native-async-storage/async-storage", {
    default: { getItem: async () => null, setItem: async () => undefined, removeItem: async () => undefined },
  });

  const { apiClient } = await import("../../shared/api/client");
  const api = await import("./pos-terminal-permissions-api");
  const requests: Array<{ method: string; path: string; body?: unknown }> = [];
  const originalGet = apiClient.get;
  const originalPut = apiClient.put;
  const originalDelete = apiClient.delete;
  const response = {
    data: {
      mode: "Inherited",
      assignablePermissions: [{ code: "P1", name: "销售", group: "POS 销售", description: "" }],
      inheritedPermissionCodes: ["P1"],
      overriddenPermissionCodes: [],
      grantedPermissionCodes: [],
      effectivePermissionCodes: ["P1"],
    },
  };
  apiClient.get = (async (path: string) => { requests.push({ method: "GET", path }); return response; }) as typeof apiClient.get;
  apiClient.put = (async (path: string, body: unknown) => { requests.push({ method: "PUT", path, body }); return response; }) as typeof apiClient.put;
  apiClient.delete = (async (path: string) => { requests.push({ method: "DELETE", path }); return response; }) as typeof apiClient.delete;

  try {
    await api.fetchStoreUserPosTerminalPermissions("user/a", "store b");
    await api.updateStoreUserPosTerminalPermissions({ userGuid: "user/a", storeGuid: "store b", grantedPermissionCodes: ["P1"] });
    await api.restoreStoreUserPosTerminalPermissions("user/a", "store b");
    assert.deepEqual(requests, [
      { method: "GET", path: "/Users/guid/user%2Fa/stores/store%20b/pos-terminal-permissions" },
      { method: "PUT", path: "/Users/guid/user%2Fa/stores/store%20b/pos-terminal-permissions", body: { grantedPermissionCodes: ["P1"] } },
      { method: "DELETE", path: "/Users/guid/user%2Fa/stores/store%20b/pos-terminal-permissions" },
    ]);
    requests.forEach(({ path }) => assert.equal(path.startsWith("/api/"), false));
  } finally {
    apiClient.get = originalGet;
    apiClient.put = originalPut;
    apiClient.delete = originalDelete;
  }
}

void run();
```

- [ ] **Step 2: 运行测试并确认失败**

Run: `cd apps/mobile && npx --yes tsx src/modules/users/pos-terminal-permissions-api.test.ts`

Expected: FAIL，提示找不到 `./pos-terminal-permissions-api`。

- [ ] **Step 3: 实现 API**

创建 `apps/mobile/src/modules/users/pos-terminal-permissions-api.ts`：

```ts
import { apiClient } from "@/shared/api/client";
import type {
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
  UpdateStoreUserPosTerminalPermissionsPayload,
} from "./types";

function buildPath(userGuid: string, storeGuid: string) {
  return `/Users/guid/${encodeURIComponent(userGuid)}/stores/${encodeURIComponent(storeGuid)}/pos-terminal-permissions`;
}

function stringArray(value: unknown) {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];
}

export function normalizeStoreUserPosTerminalPermissions(payload: unknown): StoreUserPosTerminalPermissions {
  const record = typeof payload === "object" && payload !== null ? payload as Record<string, unknown> : {};
  const rawPermissions = Array.isArray(record.assignablePermissions) ? record.assignablePermissions : [];
  const assignablePermissions = rawPermissions.flatMap((item): PosTerminalPermissionOption[] => {
    if (typeof item !== "object" || item === null) return [];
    const option = item as Record<string, unknown>;
    const code = typeof option.code === "string" ? option.code : "";
    if (!code) return [];
    return [{
      code,
      name: typeof option.name === "string" ? option.name : code,
      group: typeof option.group === "string" ? option.group : "",
      description: typeof option.description === "string" ? option.description : "",
    }];
  });
  return {
    mode: typeof record.mode === "string" ? record.mode : "Inherited",
    assignablePermissions,
    inheritedPermissionCodes: stringArray(record.inheritedPermissionCodes),
    overriddenPermissionCodes: stringArray(record.overriddenPermissionCodes),
    grantedPermissionCodes: stringArray(record.grantedPermissionCodes),
    effectivePermissionCodes: stringArray(record.effectivePermissionCodes),
  };
}

export async function fetchStoreUserPosTerminalPermissions(userGuid: string, storeGuid: string) {
  const response = await apiClient.get(buildPath(userGuid, storeGuid));
  return normalizeStoreUserPosTerminalPermissions(response.data);
}

export async function updateStoreUserPosTerminalPermissions(payload: UpdateStoreUserPosTerminalPermissionsPayload) {
  const response = await apiClient.put(buildPath(payload.userGuid, payload.storeGuid), {
    grantedPermissionCodes: payload.grantedPermissionCodes,
  });
  return normalizeStoreUserPosTerminalPermissions(response.data);
}

export async function restoreStoreUserPosTerminalPermissions(userGuid: string, storeGuid: string) {
  const response = await apiClient.delete(buildPath(userGuid, storeGuid));
  return normalizeStoreUserPosTerminalPermissions(response.data);
}
```

- [ ] **Step 4: 实现 hooks 与导出**

创建 `apps/mobile/src/modules/users/pos-terminal-permissions-hooks.ts`：

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchStoreUserPosTerminalPermissions,
  restoreStoreUserPosTerminalPermissions,
  updateStoreUserPosTerminalPermissions,
} from "./pos-terminal-permissions-api";
import type { UpdateStoreUserPosTerminalPermissionsPayload } from "./types";

export function storeUserPosPermissionQueryKey(userGuid?: string | null, storeGuid?: string | null) {
  return ["storeUserPosTerminalPermissions", storeGuid ?? "", userGuid ?? ""] as const;
}

export function useStoreUserPosTerminalPermissions(
  userGuid?: string | null,
  storeGuid?: string | null,
  enabled = true,
) {
  return useQuery({
    queryKey: storeUserPosPermissionQueryKey(userGuid, storeGuid),
    enabled: enabled && Boolean(userGuid && storeGuid),
    retry: false,
    queryFn: () => fetchStoreUserPosTerminalPermissions(userGuid!, storeGuid!),
  });
}

export function useStoreUserPosTerminalPermissionMutations(userGuid: string, storeGuid: string) {
  const queryClient = useQueryClient();
  const setPermissionState = (data: Awaited<ReturnType<typeof fetchStoreUserPosTerminalPermissions>>) => {
    queryClient.setQueryData(storeUserPosPermissionQueryKey(userGuid, storeGuid), data);
  };
  const updateMutation = useMutation({
    mutationFn: (payload: UpdateStoreUserPosTerminalPermissionsPayload) =>
      updateStoreUserPosTerminalPermissions(payload),
    onSuccess: setPermissionState,
  });
  const restoreMutation = useMutation({
    mutationFn: () => restoreStoreUserPosTerminalPermissions(userGuid, storeGuid),
    onSuccess: setPermissionState,
  });
  return { updateMutation, restoreMutation };
}
```

在 `apps/mobile/src/modules/users/index.ts` 增加：

```ts
export * from "@/modules/users/pos-terminal-permissions";
export * from "@/modules/users/pos-terminal-permissions-api";
export * from "@/modules/users/pos-terminal-permissions-hooks";
```

- [ ] **Step 5: 运行 API 与纯逻辑测试**

Run:

```bash
cd apps/mobile
npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts
npx --yes tsx src/modules/users/pos-terminal-permissions-api.test.ts
```

Expected: 两条命令均 PASS，实际路径不含重复 `/api/api`。

- [ ] **Step 6: 提交本任务**

```bash
git add apps/mobile/src/modules/users/pos-terminal-permissions-api.ts apps/mobile/src/modules/users/pos-terminal-permissions-api.test.ts apps/mobile/src/modules/users/pos-terminal-permissions-hooks.ts apps/mobile/src/modules/users/index.ts
git commit -m "接入移动端 POS 授权接口 reasonix"
```

### Task 3: 增加用户卡片授权入口

**Files:**
- Modify: `apps/mobile/src/shared/utils/access.ts`
- Modify: `apps/mobile/app/(tabs)/users.tsx`
- Modify: `apps/mobile/src/locales/zh/screens/userManagement.json`
- Modify: `apps/mobile/src/locales/en/screens/userManagement.json`

- [ ] **Step 1: 先扩展入口资格测试**

在 `pos-terminal-permissions.test.ts` 增加只读分店、停用账号与仓库经理用例：

```ts
assert.equal(getPosPermissionEntryState({
  hasPermission: true,
  isManageableStore: false,
  actorUserGuid: "manager-1",
  targetUser: { userGUID: "staff-1", status: 1, roleNames: ["StoreStaff"] },
  storeGuid: "store-guid-1",
}).state, "hidden");
assert.equal(getPosPermissionEntryState({
  hasPermission: true,
  isManageableStore: true,
  actorUserGuid: "manager-1",
  targetUser: { userGUID: "staff-1", status: 0, roleNames: ["StoreStaff"] },
  storeGuid: "store-guid-1",
}).state, "hidden");
assert.equal(getPosPermissionEntryState({
  hasPermission: true,
  isManageableStore: true,
  actorUserGuid: "admin-1",
  targetUser: { userGUID: "warehouse-1", status: 1, roleNames: ["WarehouseManager"] },
  storeGuid: "store-guid-1",
}).state, "hidden");
```

- [ ] **Step 2: 运行测试并确认通过已有纯逻辑，随后开始入口接线**

Run: `cd apps/mobile && npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts`

Expected: PASS；这些用例固定 UI 的保守边界。

- [ ] **Step 3: 增加权限常量**

在 `apps/mobile/src/shared/utils/access.ts` 的 `PERMISSIONS` 增加：

```ts
Users: {
  ManagePosTerminalPermissions: "Users.ManagePosTerminalPermissions",
},
```

- [ ] **Step 4: 在用户卡片接入入口**

在 `apps/mobile/app/(tabs)/users.tsx`：

1. 从 `@/modules/users` 导入 `getPosPermissionEntryState`。
2. 从 `@/shared/utils/access` 导入 `PERMISSIONS`。
3. 读取当前用户：

```ts
const currentUser = useAuthStore((state) => state.user);
const canManagePosTerminalPermissions = access.hasPermission(
  PERMISSIONS.Users.ManagePosTerminalPermissions,
);
```

4. 增加跳转函数：

```ts
const openPosPermissions = useCallback(
  (user: StoreUserListItem) => {
    const entry = getPosPermissionEntryState({
      hasPermission: canManagePosTerminalPermissions,
      isManageableStore: isStoreManageable(managedStoreCode, manageableStores),
      actorUserGuid: currentUser?.userGUID,
      targetUser: user,
      storeGuid: managedStore?.storeGUID,
    });
    if (entry.state === "disabled") {
      setSnackbarMessage(t("posPermissions.messages.missingStoreGuid"));
      return;
    }
    if (entry.state !== "enabled" || !managedStore?.storeGUID) return;
    router.push({
      pathname: "/users/[userGuid]/pos-terminal-permissions",
      params: {
        userGuid: user.userGUID,
        storeGuid: managedStore.storeGUID,
        userName: user.fullName || user.username,
        storeName: managedStore.storeName || managedStore.storeCode,
      },
    } as unknown as Parameters<typeof router.push>[0]);
  },
  [canManagePosTerminalPermissions, currentUser?.userGUID, managedStore, managedStoreCode, manageableStores, router, t],
);
```

5. 在 `renderUserCard` 内计算 `posPermissionEntry`，并只对非 `hidden` 状态渲染按钮：

```tsx
const posPermissionEntry = getPosPermissionEntryState({
  hasPermission: canManagePosTerminalPermissions,
  isManageableStore: isStoreManageable(managedStoreCode, manageableStores),
  actorUserGuid: currentUser?.userGUID,
  targetUser: item,
  storeGuid: managedStore?.storeGUID,
});

{posPermissionEntry.state !== "hidden" ? (
  <Button
    compact
    mode="contained-tonal"
    icon="shield-account-outline"
    onPress={() => openPosPermissions(item)}
    disabled={posPermissionEntry.state === "disabled"}
  >
    {t("posPermissions.actions.open")}
  </Button>
) : null}
```

在卡片操作区之后显示缺少分店 GUID 的静态提示，保证禁用按钮不依赖 `onPress` 才能解释原因：

```tsx
{posPermissionEntry.state === "disabled" ? (
  <Text variant="bodySmall" style={styles.readOnlyHint}>
    {t("posPermissions.messages.missingStoreGuid")}
  </Text>
) : null}
```

把新增依赖加入 `renderUserCard` 的 `useCallback` dependency list，避免闭包读取旧分店。

- [ ] **Step 5: 增加双语入口文案**

在中英文 `userManagement.json` 根对象增加同名 `posPermissions` 对象；Task 4 会补齐页面文案。本任务先加入：

```json
{
  "actions": { "open": "POS 授权" },
  "messages": { "missingStoreGuid": "分店资料缺少授权标识，请刷新分店资料或重新登录" }
}
```

英文对应：

```json
{
  "actions": { "open": "POS access" },
  "messages": { "missingStoreGuid": "The store access identifier is missing. Refresh stores or sign in again." }
}
```

- [ ] **Step 6: 运行入口逻辑、语言包与类型检查**

Run:

```bash
cd apps/mobile
npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts
npm run test:i18n-locales
npx tsc --noEmit
```

Expected: 三条命令均退出码 0。

- [ ] **Step 7: 提交本任务**

```bash
git add apps/mobile/src/shared/utils/access.ts 'apps/mobile/app/(tabs)/users.tsx' apps/mobile/src/locales/zh/screens/userManagement.json apps/mobile/src/locales/en/screens/userManagement.json apps/mobile/src/modules/users/pos-terminal-permissions.test.ts
git commit -m "增加店员 POS 授权入口 reasonix"
```

### Task 4: 实现独立全屏授权页面

**Files:**
- Create: `apps/mobile/app/users/[userGuid]/pos-terminal-permissions.tsx`
- Create: `apps/mobile/src/modules/users/pos-terminal-permissions-screen.tsx`
- Modify: `apps/mobile/src/locales/zh/screens/userManagement.json`
- Modify: `apps/mobile/src/locales/en/screens/userManagement.json`

- [ ] **Step 1: 为页面状态补失败测试**

在 `pos-terminal-permissions.test.ts` 增加：

```ts
import { arePermissionSetsEqual } from "./pos-terminal-permissions";

assert.equal(arePermissionSetsEqual(["P1", "P2"], ["P2", "P1"]), true);
assert.equal(arePermissionSetsEqual(["P1"], ["P1", "P2"]), false);
assert.deepEqual(setGroupPermissionSelection(["P1", "P2"], [{ code: "P1", name: "A", group: "G", description: "" }], false), ["P2"]);
```

- [ ] **Step 2: 运行测试确认通过领域逻辑，并创建页面**

Run: `cd apps/mobile && npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts`

Expected: PASS；页面只组合已测试的状态函数。

- [ ] **Step 3: 创建全屏页面**

创建 `apps/mobile/src/modules/users/pos-terminal-permissions-screen.tsx`，实现以下完整结构：

```tsx
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, ScrollView, StyleSheet, View } from "react-native";
import { useLocalSearchParams, useNavigation, useRouter } from "expo-router";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Divider,
  IconButton,
  Snackbar,
  Switch,
  Text,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  arePermissionSetsEqual,
  buildGrantedPosPermissionCodes,
  buildPosPermissionDraft,
  buildPosPermissionGroups,
  classifyPosPermissionError,
  setGroupPermissionSelection,
  togglePermissionSelection,
  useStoreUserPosTerminalPermissionMutations,
  useStoreUserPosTerminalPermissions,
  type StoreUserPosTerminalPermissions,
} from "@/modules/users";
import { PERMISSIONS } from "@/shared/utils/access";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

export default function PosPermissionsScreen() {
  const router = useRouter();
  const navigation = useNavigation();
  const params = useLocalSearchParams();
  const { t, language } = useAppTranslation(["userManagement", "common"]);
  const access = useAuthStore((state) => state.access);
  const currentUser = useAuthStore((state) => state.user);
  const userGuid = firstParam(params.userGuid) ?? "";
  const storeGuid = firstParam(params.storeGuid) ?? "";
  const userName = firstParam(params.userName) || t("posPermissions.unknownUser");
  const storeName = firstParam(params.storeName) || t("posPermissions.unknownStore");
  const canManage = access.hasPermission(PERMISSIONS.Users.ManagePosTerminalPermissions);
  const isSelf = Boolean(currentUser?.userGUID && currentUser.userGUID === userGuid);
  const query = useStoreUserPosTerminalPermissions(userGuid, storeGuid, canManage && !isSelf);
  const { updateMutation, restoreMutation } = useStoreUserPosTerminalPermissionMutations(userGuid, storeGuid);
  const [selectedCodes, setSelectedCodes] = useState<string[]>([]);
  const [baselineCodes, setBaselineCodes] = useState<string[]>([]);
  const [appliedScope, setAppliedScope] = useState("");
  const [snackbarMessage, setSnackbarMessage] = useState("");
  const allowNextRemove = useRef(false);
  const scopeKey = `${storeGuid}:${userGuid}`;
  const permissionState = query.data;
  const groups = useMemo(
    () => buildPosPermissionGroups(permissionState?.assignablePermissions ?? []),
    [permissionState?.assignablePermissions],
  );
  const isDirty = !arePermissionSetsEqual(selectedCodes, baselineCodes);
  const isBusy = updateMutation.isPending || restoreMutation.isPending;

  const applyServerState = useCallback((state: StoreUserPosTerminalPermissions) => {
    const next = buildPosPermissionDraft(state);
    setSelectedCodes(next.selectedCodes);
    setBaselineCodes(next.baselineCodes);
    setAppliedScope(scopeKey);
  }, [scopeKey]);

  useEffect(() => {
    if (permissionState && appliedScope !== scopeKey) applyServerState(permissionState);
  }, [appliedScope, applyServerState, permissionState, scopeKey]);

  const leaveScreen = useCallback(() => {
    if (router.canGoBack()) router.back();
    else router.replace("/(tabs)/users" as unknown as Parameters<typeof router.replace>[0]);
  }, [router]);

  useEffect(() => {
    return navigation.addListener("beforeRemove", (event) => {
      if (!isDirty || allowNextRemove.current) {
        allowNextRemove.current = false;
        return;
      }
      event.preventDefault();
      Alert.alert(t("posPermissions.unsaved.title"), t("posPermissions.unsaved.description"), [
        { text: t("common:actions.cancel"), style: "cancel" },
        {
          text: t("posPermissions.unsaved.discard"),
          style: "destructive",
          onPress: () => {
            allowNextRemove.current = true;
            navigation.dispatch(event.data.action);
          },
        },
      ]);
    });
  }, [isDirty, navigation, t]);

  const save = useCallback(async () => {
    if (!permissionState || !isDirty) return;
    try {
      const next = await updateMutation.mutateAsync({
        userGuid,
        storeGuid,
        grantedPermissionCodes: buildGrantedPosPermissionCodes(selectedCodes, permissionState.assignablePermissions),
      });
      applyServerState(next);
      setSnackbarMessage(t("posPermissions.messages.saved"));
    } catch (error) {
      setSnackbarMessage(resolveLocalizedErrorMessage(error, {
        t,
        language,
        fallbackKey: "posPermissions.messages.saveFailed",
      }));
    }
  }, [applyServerState, isDirty, language, permissionState, selectedCodes, storeGuid, t, updateMutation, userGuid]);

  const restore = useCallback(() => {
    Alert.alert(t("posPermissions.restore.title"), t("posPermissions.restore.description"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("posPermissions.actions.restore"),
        style: "destructive",
        onPress: async () => {
          try {
            const next = await restoreMutation.mutateAsync();
            applyServerState(next);
            setSnackbarMessage(t("posPermissions.messages.restored"));
          } catch (error) {
            setSnackbarMessage(resolveLocalizedErrorMessage(error, {
              t,
              language,
              fallbackKey: "posPermissions.messages.restoreFailed",
            }));
          }
        },
      },
    ]);
  }, [applyServerState, language, restoreMutation, t]);

  let content: React.ReactNode;
  if (!userGuid || !storeGuid) {
    content = <EmptyState title={t("posPermissions.errors.invalidTitle")} description={t("posPermissions.errors.invalidDescription")} />;
  } else if (!canManage || isSelf) {
    content = <EmptyState title={t("posPermissions.errors.forbiddenTitle")} description={t("posPermissions.errors.forbiddenDescription")} />;
  } else if (query.isLoading) {
    content = <View style={styles.center}><ActivityIndicator size="large" /><Text>{t("posPermissions.loading")}</Text></View>;
  } else if (query.isError) {
    const kind = classifyPosPermissionError(query.error);
    content = (
      <EmptyState
        title={t(`posPermissions.errors.${kind}Title`)}
        description={t(`posPermissions.errors.${kind}Description`)}
        primaryAction={kind === "forbidden" || kind === "notFound" ? {
          label: t("posPermissions.actions.back"), icon: "arrow-left", onPress: leaveScreen,
        } : {
          label: t("common:actions.retry"), icon: "refresh", onPress: () => void query.refetch(),
        }}
      />
    );
  } else if (!permissionState) {
    content = <EmptyState title={t("posPermissions.errors.networkTitle")} description={t("posPermissions.errors.networkDescription")} />;
  } else {
    content = (
      <>
        <ScrollView contentContainerStyle={styles.content}>
          <View style={styles.summary}>
            <Text variant="titleLarge">{userName}</Text>
            <Text variant="bodyMedium" style={styles.muted}>{storeName}</Text>
            <Chip compact icon={permissionState.mode.toLowerCase().startsWith("inherit") ? "source-branch" : "shield-edit-outline"}>
              {permissionState.mode.toLowerCase().startsWith("inherit")
                ? t("posPermissions.mode.inherited")
                : t("posPermissions.mode.override")}
            </Chip>
          </View>
          {groups.map((group) => {
            const groupCodes = group.permissions.map((permission) => permission.code);
            const selectedCount = groupCodes.filter((code) => selectedCodes.includes(code)).length;
            return (
              <Card key={group.name} mode="outlined" style={styles.groupCard}>
                <Card.Title title={group.name} subtitle={t("posPermissions.selectedCount", { selected: selectedCount, total: groupCodes.length })} />
                <Card.Actions>
                  <Button compact onPress={() => setSelectedCodes(setGroupPermissionSelection(selectedCodes, group.permissions, false))}>{t("posPermissions.actions.clearGroup")}</Button>
                  <Button compact onPress={() => setSelectedCodes(setGroupPermissionSelection(selectedCodes, group.permissions, true))}>{t("posPermissions.actions.selectGroup")}</Button>
                </Card.Actions>
                <Card.Content>
                  {group.permissions.map((permission, index) => (
                    <View key={permission.code}>
                      {index > 0 ? <Divider /> : null}
                      <View style={styles.permissionRow}>
                        <View style={styles.permissionCopy}>
                          <Text variant="bodyLarge">{permission.name}</Text>
                          {permission.description ? <Text variant="bodySmall" style={styles.muted}>{permission.description}</Text> : null}
                        </View>
                        <Switch
                          value={selectedCodes.includes(permission.code)}
                          onValueChange={(checked) => setSelectedCodes(togglePermissionSelection(selectedCodes, permission.code, checked))}
                          disabled={isBusy}
                        />
                      </View>
                    </View>
                  ))}
                </Card.Content>
              </Card>
            );
          })}
        </ScrollView>
        <View style={styles.footer}>
          {!permissionState.mode.toLowerCase().startsWith("inherit") ? (
            <Button mode="outlined" icon="restore" onPress={restore} disabled={isBusy}>{t("posPermissions.actions.restore")}</Button>
          ) : null}
          <Button mode="contained" icon="content-save-outline" onPress={() => void save()} disabled={!isDirty || isBusy} loading={updateMutation.isPending}>
            {t("posPermissions.actions.save")}
          </Button>
        </View>
      </>
    );
  }

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right", "bottom"]}>
      <View style={styles.header}>
        <IconButton icon="arrow-left" accessibilityLabel={t("posPermissions.actions.back")} onPress={leaveScreen} />
        <Text variant="titleMedium" style={styles.headerTitle}>{t("posPermissions.title")}</Text>
        <View style={styles.headerSpacer} />
      </View>
      {content}
      <Snackbar visible={Boolean(snackbarMessage)} onDismiss={() => setSnackbarMessage("")} duration={3500}>{snackbarMessage}</Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: "#F5F7FA" },
  header: { minHeight: 56, flexDirection: "row", alignItems: "center", borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: "#D9DEE7", backgroundColor: "#FFFFFF" },
  headerTitle: { flex: 1, textAlign: "center" },
  headerSpacer: { width: 48 },
  center: { flex: 1, alignItems: "center", justifyContent: "center", gap: 12 },
  content: { padding: 16, gap: 12, paddingBottom: 28 },
  summary: { gap: 6, alignItems: "flex-start" },
  muted: { color: "#667085" },
  groupCard: { backgroundColor: "#FFFFFF" },
  permissionRow: { minHeight: 64, flexDirection: "row", alignItems: "center", gap: 12, paddingVertical: 10 },
  permissionCopy: { flex: 1, gap: 3 },
  footer: { flexDirection: "row", justifyContent: "flex-end", gap: 10, padding: 12, borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: "#D9DEE7", backgroundColor: "#FFFFFF" },
});
```

创建薄路由 `apps/mobile/app/users/[userGuid]/pos-terminal-permissions.tsx`：

```tsx
export { default } from "@/modules/users/pos-terminal-permissions-screen";
```

- [ ] **Step 4: 补齐页面双语文案**

把中文 `userManagement.json` 中 Task 3 的 `posPermissions` 扩展为：

```json
{
  "title": "POS 终端业务授权",
  "unknownUser": "未知用户",
  "unknownStore": "未知分店",
  "loading": "正在加载 POS 权限...",
  "selectedCount": "已选 {{selected}} / {{total}}",
  "mode": { "inherited": "继承全局权限", "override": "分店单独覆盖" },
  "actions": { "open": "POS 授权", "back": "返回店员列表", "save": "保存授权", "restore": "恢复继承", "selectGroup": "全选", "clearGroup": "清空" },
  "unsaved": { "title": "放弃未保存修改？", "description": "当前 POS 权限尚未保存，离开后修改会丢失。", "discard": "放弃修改" },
  "restore": { "title": "恢复全局权限继承？", "description": "这会删除当前分店的权限覆盖，并重新使用该用户的全局有效权限。" },
  "messages": { "missingStoreGuid": "分店资料缺少授权标识，请刷新分店资料或重新登录", "saved": "POS 权限已保存", "restored": "已恢复全局权限继承", "saveFailed": "POS 权限保存失败", "restoreFailed": "恢复继承失败" },
  "errors": {
    "invalidTitle": "授权范围无效", "invalidDescription": "缺少用户或分店标识，请返回店员列表重新进入。",
    "unauthorizedTitle": "登录已失效", "unauthorizedDescription": "请重新登录后再试。",
    "forbiddenTitle": "无权修改该用户", "forbiddenDescription": "当前账号不能管理该用户或分店的 POS 权限。",
    "notFoundTitle": "用户或分店已变化", "notFoundDescription": "用户、分店或关联关系已变化，请返回刷新列表。",
    "networkTitle": "POS 权限加载失败", "networkDescription": "请检查网络后重试。"
  }
}
```

把英文 `userManagement.json` 中的 `posPermissions` 扩展为：

```json
{
  "title": "POS terminal business access",
  "unknownUser": "Unknown user",
  "unknownStore": "Unknown store",
  "loading": "Loading POS permissions...",
  "selectedCount": "Selected {{selected}} / {{total}}",
  "mode": { "inherited": "Inherits global access", "override": "Store override" },
  "actions": { "open": "POS access", "back": "Back to staff", "save": "Save access", "restore": "Restore inheritance", "selectGroup": "Select all", "clearGroup": "Clear" },
  "unsaved": { "title": "Discard unsaved changes?", "description": "The current POS permission changes have not been saved and will be lost.", "discard": "Discard changes" },
  "restore": { "title": "Restore global permission inheritance?", "description": "This removes the store override and uses the user's effective global permissions again." },
  "messages": { "missingStoreGuid": "The store access identifier is missing. Refresh stores or sign in again.", "saved": "POS permissions saved", "restored": "Global permission inheritance restored", "saveFailed": "Failed to save POS permissions", "restoreFailed": "Failed to restore inheritance" },
  "errors": {
    "invalidTitle": "Invalid permission scope", "invalidDescription": "The user or store identifier is missing. Return to Staff Management and open it again.",
    "unauthorizedTitle": "Session expired", "unauthorizedDescription": "Sign in again and retry.",
    "forbiddenTitle": "Access denied", "forbiddenDescription": "This account cannot manage POS permissions for the selected user or store.",
    "notFoundTitle": "User or store changed", "notFoundDescription": "The user, store, or assignment changed. Return and refresh the list.",
    "networkTitle": "Failed to load POS permissions", "networkDescription": "Check the network connection and retry."
  }
}
```

- [ ] **Step 5: 运行测试、语言包校验和 TypeScript 检查**

Run:

```bash
cd apps/mobile
npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts
npx --yes tsx src/modules/users/pos-terminal-permissions-api.test.ts
npm run test:i18n-locales
npx tsc --noEmit
```

Expected: 全部退出码 0；页面没有未使用导入或缺失翻译键。

- [ ] **Step 6: 提交本任务**

```bash
git add 'apps/mobile/app/users/[userGuid]/pos-terminal-permissions.tsx' apps/mobile/src/modules/users/pos-terminal-permissions-screen.tsx apps/mobile/src/locales/zh/screens/userManagement.json apps/mobile/src/locales/en/screens/userManagement.json apps/mobile/src/modules/users/pos-terminal-permissions.test.ts
git commit -m "实现移动端 POS 授权页面 reasonix"
```

### Task 5: 集成测试脚本、双轮审查与验收

**Files:**
- Modify: `apps/mobile/package.json`

- [ ] **Step 1: 增加功能测试脚本**

在 `apps/mobile/package.json` 的 `scripts` 增加：

```json
"test:user-pos-permissions": "npx --yes tsx src/modules/users/pos-terminal-permissions.test.ts && npx --yes tsx src/modules/users/pos-terminal-permissions-api.test.ts"
```

- [ ] **Step 2: 运行完整的移动端静态与相关回归验证**

Run:

```bash
cd apps/mobile
npm run test:user-pos-permissions
npm run test:i18n-locales
npx tsc --noEmit
```

Expected: 全部退出码 0。

- [ ] **Step 3: 做第一轮独立代码审查**

审查实际 diff，重点核对：

- API 路径只出现 `/Users/...`，由 `apiClient.baseURL` 提供 `/api`。
- `storeGuid` 只来自 `Store.storeGUID`，不以 `storeCode` 降级。
- 普通用户、自己、高权限账号、停用账号和只读分店边界符合规格。
- 保存 payload 只能包含 `assignablePermissions` 白名单代码。
- 403/404 不被当成普通网络错误。
- 现有 `apps/web` 用户改动未进入本功能提交。

发现问题后先修复，再重跑 Step 2。

- [ ] **Step 4: 做第二轮独立验证审查**

由另一个 reviewer/verifier 从设计规格逐项追踪到代码与测试，特别检查：未保存返回、空权限保存、防重复提交、恢复继承确认、缓存响应回填及窄屏滚动。发现问题后修复并再次运行 Step 2。

- [ ] **Step 5: 运行 GitNexus 变更影响检查**

Run:

```bash
node .gitnexus/run.cjs detect_changes --repo hb-platform --scope staged
```

Expected: 仅影响 Expo 用户管理、权限 API 与新授权路由。若本地 LadybugDB 再次以退出码 139 失败，保留原始错误证据，并用 `git diff --cached --stat`、`git diff --cached --check` 和相关测试作为降级验证，不删除 `.gitnexus` WAL 文件。

- [ ] **Step 6: iOS 与 Android 人工 smoke test**

在两个平台分别验证：普通用户入口、高权限用户无入口、缺少 `storeGUID` 禁用提示、继承状态加载、分组全选/清空、空权限保存、恢复继承、保存失败保留草稿、Android 返回键确认、iOS 脏状态禁用侧滑、长列表和窄屏滚动。

- [ ] **Step 7: 提交验证脚本与最终修复**

```bash
git add apps/mobile/package.json apps/mobile
git diff --cached --check
git commit -m "完善移动端 POS 授权验证 reasonix"
```

提交前必须先用 `git diff --cached --name-only` 确认不包含 `apps/web/src/pages/System/Users/` 下的用户现有修改。
