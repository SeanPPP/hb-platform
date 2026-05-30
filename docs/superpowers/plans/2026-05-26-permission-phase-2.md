# 第二阶段权限系统实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在第一阶段“后端种子 + Admin 隐式全权限 + 去硬编码”基础上，完成后台权限管理体验、Web 权限消费、移动端权限消费的统一和解耦。

**Architecture:** 后端继续作为权限事实源，提供权限目录、角色模板、角色有效权限、Admin 隐式权限状态和 legacy alias 元数据。Web 后台权限页不再维护本地角色白名单，权限选择、展示和保存全部围绕后端返回的权限目录与角色有效权限；移动端只消费 `/api/navigation/app-menu` 和 `/api/Auth/current` 的 permission claims，不用角色名推导功能入口。

**Tech Stack:** .NET 9 API + SqlSugar + xUnit, React/Vite/Ant Design Web, Expo Router + React Native + TypeScript.

***

## 已完成前置条件

* 第一阶段已完成：Admin / `管理员` 隐式全权限，不写 `SysRolePermission`。

* 第一阶段已完成：后端授权层删除 StoreManager、WarehouseManager、考勤自助运行时白名单。

* 第一阶段已完成：`LocalInvocie.*` 兼容集中到 `Permissions` helper、JWT/current user 权限聚合和 `RoleService`。

* 第一阶段已验证：`dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj` 通过 `205/205`。

## 第二阶段范围

* 修改后端权限管理契约和测试。

* 修改 Web 权限管理页面、权限 access 工具和类型。

* 修改 Expo 权限消费、导航兜底和 route guard。

* 不改业务页面功能逻辑，除非页面入口权限依赖已过时。

* 不引入新权限模型表，继续基于 `SysPermission`、`SysRolePermission`、`Role`，只补 DTO/API/前端消费层。

## 子代理安排

* **GPT-5.5 主代理：后端权限契约与集成**

  * Owner: `/Users/sean/DEV/HBBblazorweb-master-vite`

  * 负责 `RolesController`、`RoleService`、权限 DTO、后端测试、最终集成验证。

* **GPT-5.4 子代理：Web 权限管理体验**

  * Owner: `/Users/sean/DEV/hbweb_rv`

  * 负责后台权限页、角色权限选择器、Web access 去角色白名单。

* **GPT-5.4 子代理：Expo 权限消费**

  * Owner: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp`

  * 负责 App 当前用户权限、动态菜单、默认路由、tab guard。

* **GPT-5.5 verifier**

  * 只读审查所有 diff，重点检查是否重新引入角色白名单、Admin 是否仍无显式权限、Web/App 是否只消费权限事实源。

## 文件结构

### 后端

* Modify: `/Users/sean/DEV/HBBblazorweb-master-vite/BlazorApp.Shared/DTOs/RoleDtos.cs`

* Modify: `/Users/sean/DEV/HBBblazorweb-master-vite/BlazorApp.Api/Interfaces/IRoleService.cs`

* Modify: `/Users/sean/DEV/HBBblazorweb-master-vite/BlazorApp.Api/Services/RoleService.cs`

* Modify: `/Users/sean/DEV/HBBblazorweb-master-vite/BlazorApp.Api/Controllers/RolesController.cs`

* Modify: `/Users/sean/DEV/HBBblazorweb-master-vite/BlazorApp.Api.Tests/RoleServicePermissionTests.cs`

* Modify: `/Users/sean/DEV/HBBblazorweb-master-vite/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj`

### Web

* Modify: `/Users/sean/DEV/hbweb_rv/src/types/permissions.ts`

* Modify: `/Users/sean/DEV/hbweb_rv/src/types/role.ts`

* Modify: `/Users/sean/DEV/hbweb_rv/src/services/roleService.ts`

* Modify: `/Users/sean/DEV/hbweb_rv/src/utils/access.ts`

* Modify: `/Users/sean/DEV/hbweb_rv/src/pages/System/Permissions/index.tsx`

* Modify: `/Users/sean/DEV/hbweb_rv/src/pages/System/Roles/RolePermissionManager.tsx`

* Modify: `/Users/sean/DEV/hbweb_rv/src/pages/System/Roles/Detail.tsx`

### Expo

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/modules/auth/types.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/modules/auth/use-current-user.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/modules/navigation/api.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/modules/navigation/store.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/modules/navigation/default-route.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/modules/navigation/default-route.test.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/app/(tabs)/_layout.tsx`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/components/navigation/tab-grouping.ts`

* Modify: `/Users/sean/DEV/HbwebExpo/HbwebExpoApp/src/components/navigation/tab-grouping.test.ts`

***

### Task 1: 后端权限目录和有效权限 API

**Owner:** GPT-5.5 主代理\
**Write scope:** `/Users/sean/DEV/HBBblazorweb-master-vite`

* [ ] **Step 1: 写后端失败测试**

在 `BlazorApp.Api.Tests/RoleServicePermissionTests.cs` 增加测试：

```csharp
[Fact]
public async Task GetPermissionCatalogAsync_ReturnsAliasesTemplatesAndSuperAdminRoles()
{
    var result = await CreateService().GetPermissionCatalogAsync();

    Assert.Contains("Admin", result.Data.SuperAdminRoleNames);
    Assert.Contains("管理员", result.Data.SuperAdminRoleNames);
    Assert.Contains(result.Data.PermissionAliases, item =>
        item.CanonicalCode == Permissions.LocalPurchase.View
        && item.AliasCodes.Contains("LocalInvocie.View"));
    Assert.Contains(result.Data.RoleTemplates, item => item.RoleName == "WarehouseManager");
    Assert.Contains(result.Data.RoleTemplates, item => item.RoleName == "StoreManager");
}

[Fact]
public async Task GetRolePermissionStateAsync_AdminReportsImplicitAllWithoutExplicitLinks()
{
    await InsertRoleAsync("role-admin", "Admin");
    await InsertPermissionAsync(Permissions.Users.View);

    var result = await CreateService().GetRolePermissionStateAsync("role-admin");

    Assert.True(result.Data.IsSuperAdmin);
    Assert.True(result.Data.ImplicitAllPermissions);
    Assert.Empty(result.Data.ExplicitPermissionCodes);
    Assert.Contains(Permissions.Users.View, result.Data.EffectivePermissionCodes);
}

[Fact]
public async Task GetRolePermissionStateAsync_NormalRoleSeparatesExplicitAndEffective()
{
    await InsertRoleAsync("role-user", "User");
    await InsertRolePermissionAsync("role-user", Permissions.Attendance.Punch.Self);

    var result = await CreateService().GetRolePermissionStateAsync("role-user");

    Assert.False(result.Data.IsSuperAdmin);
    Assert.False(result.Data.ImplicitAllPermissions);
    Assert.Contains(Permissions.Attendance.Punch.Self, result.Data.ExplicitPermissionCodes);
    Assert.Contains(Permissions.Attendance.Punch.Self, result.Data.EffectivePermissionCodes);
}
```

Run:

```bash
cd /Users/sean/DEV/HBBblazorweb-master-vite
dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj --filter RoleServicePermissionTests
```

Expected: 新增测试失败，提示接口或 DTO 尚未实现。

* [ ] **Step 2: 增加 DTO**

在 `BlazorApp.Shared/DTOs/RoleDtos.cs` 增加：

```csharp
public class PermissionAliasDto
{
    public string CanonicalCode { get; set; } = string.Empty;
    public List<string> AliasCodes { get; set; } = new();
}

public class RolePermissionTemplateDto
{
    public string RoleName { get; set; } = string.Empty;
    public List<string> PermissionCodes { get; set; } = new();
}

public class PermissionCatalogDto
{
    public List<PermissionCategoryDto> Categories { get; set; } = new();
    public List<PermissionAliasDto> PermissionAliases { get; set; } = new();
    public List<RolePermissionTemplateDto> RoleTemplates { get; set; } = new();
    public List<string> SuperAdminRoleNames { get; set; } = new();
}

public class RolePermissionStateDto
{
    public string RoleGuid { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; }
    public bool ImplicitAllPermissions { get; set; }
    public List<string> ExplicitPermissionCodes { get; set; } = new();
    public List<string> EffectivePermissionCodes { get; set; } = new();
}
```

* [ ] **Step 3: 增加服务接口**

在 `BlazorApp.Api/Interfaces/IRoleService.cs` 增加：

```csharp
Task<ApiResponse<PermissionCatalogDto>> GetPermissionCatalogAsync();
Task<ApiResponse<RolePermissionStateDto>> GetRolePermissionStateAsync(string roleGuid);
```

* [ ] **Step 4: 实现 RoleService**

在 `BlazorApp.Api/Services/RoleService.cs` 实现：

```csharp
public async Task<ApiResponse<PermissionCatalogDto>> GetPermissionCatalogAsync()
{
    var permissions = await GetPermissionsAsync();
    var catalog = new PermissionCatalogDto
    {
        Categories = permissions.Data ?? new List<PermissionCategoryDto>(),
        PermissionAliases = Permissions.GetPermissionAliases()
            .Select(item => new PermissionAliasDto
            {
                CanonicalCode = item.Key,
                AliasCodes = item.Value.ToList(),
            })
            .ToList(),
        RoleTemplates = PermissionSeedData.RolePermissionTemplates
            .Select(item => new RolePermissionTemplateDto
            {
                RoleName = item.RoleName,
                PermissionCodes = item.PermissionCodes.ToList(),
            })
            .ToList(),
        SuperAdminRoleNames = Permissions.SuperAdminRoleNames.ToList(),
    };

    return ApiResponse<PermissionCatalogDto>.OK(catalog, "获取权限目录成功");
}

public async Task<ApiResponse<RolePermissionStateDto>> GetRolePermissionStateAsync(string roleGuid)
{
    var db = _context.Db;
    var role = await db.Queryable<Role>()
        .Where(item => item.RoleGUID == roleGuid && !item.IsDeleted)
        .FirstAsync();

    if (role == null)
    {
        return ApiResponse<RolePermissionStateDto>.Error("角色不存在", "ROLE_NOT_FOUND");
    }

    var explicitCodes = await db.Queryable<SysRolePermission>()
        .Where(item => item.RoleGuid == roleGuid && !item.IsDeleted)
        .Select(item => item.PermissionCode)
        .ToListAsync();

    var rolePermissions = await GetRolePermissionsAsync(roleGuid);
    var isSuperAdmin = Permissions.IsSuperAdminRole(role.RoleName);

    return ApiResponse<RolePermissionStateDto>.OK(new RolePermissionStateDto
    {
        RoleGuid = role.RoleGUID,
        RoleName = role.RoleName,
        IsSuperAdmin = isSuperAdmin,
        ImplicitAllPermissions = isSuperAdmin,
        ExplicitPermissionCodes = explicitCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        EffectivePermissionCodes = rolePermissions.Data ?? new List<string>(),
    }, "获取角色权限状态成功");
}
```

`Permissions.GetPermissionAliases()` 不存在时，在 `Permissions.cs` 增加只读 accessor：

```csharp
public static IReadOnlyDictionary<string, string[]> GetPermissionAliases()
{
    return PermissionAliases;
}
```

* [ ] **Step 5: 增加 Controller endpoints**

在 `BlazorApp.Api/Controllers/RolesController.cs` 增加：

```csharp
[HttpGet("permissions/catalog")]
[Authorize(Policy = Permissions.Roles.View)]
public async Task<ApiResponse<PermissionCatalogDto>> GetPermissionCatalog()
{
    return await _roleService.GetPermissionCatalogAsync();
}

[HttpGet("guid/{guid}/permissions/state")]
[Authorize(Policy = Permissions.Roles.View)]
public async Task<ApiResponse<RolePermissionStateDto>> GetRolePermissionState(string guid)
{
    return await _roleService.GetRolePermissionStateAsync(guid);
}
```

* [ ] **Step 6: 后端验证**

Run:

```bash
cd /Users/sean/DEV/HBBblazorweb-master-vite
dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj --filter RoleServicePermissionTests
dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj
```

Expected: all tests pass.

### Task 2: Web 权限管理页改为后端事实源

**Owner:** GPT-5.4 Web 子代理\
**Write scope:** `/Users/sean/DEV/hbweb_rv`\
**Depends on:** Task 1 backend endpoints.

* [ ] **Step 1: 删除 Web 角色权限白名单测试预期**

在 `src/utils/access.ts` 相关测试缺失时，新增一个轻量脚本测试文件 `src/utils/access.permission.test.ts`，并在 `package.json` 增加脚本：

```json
{
  "scripts": {
    "test:access": "npx --yes tsx src/utils/access.permission.test.ts"
  }
}
```

测试内容：

```ts
import { buildAccess } from './access'

const access = buildAccess({
  userGUID: 'u1',
  username: 'wm',
  roleNames: ['WarehouseManager'],
  roles: [],
  permissions: [],
  stores: [],
})

if (access.hasPermission('Warehouse.ManageOrders')) {
  throw new Error('WarehouseManager role must not grant Warehouse.ManageOrders without permission claim')
}
```

Run:

```bash
cd /Users/sean/DEV/hbweb_rv
npm run test:access
```

Expected: 旧实现失败，因为 `WAREHOUSE_MANAGER_PERMISSION_CODES` 仍会给角色放行。

* [ ] **Step 2: 去掉 Web 本地 WarehouseManager 权限白名单**

修改 `src/types/permissions.ts`：

```ts
export const ALL_PERMISSIONS: string[] = Object.values(P).flatMap((group) =>
  Object.values(group),
)
```

删除：

```ts
export const WAREHOUSE_MANAGER_PERMISSION_CODES = [...]
```

修改 `src/utils/access.ts`：

```ts
import type { AccessControl, CurrentUser } from '../types/auth'

const PERMISSION_ALIASES: Record<string, string[]> = {
  'LocalPurchase.View': ['LocalInvocie.View'],
  'LocalPurchase.Edit': ['LocalInvocie.Edit'],
}

function permissionMatches(actual: string, expected: string) {
  if (actual.toLowerCase() === expected.toLowerCase()) return true
  return (PERMISSION_ALIASES[expected] ?? []).some(
    (alias) => alias.toLowerCase() === actual.toLowerCase(),
  )
}
```

`hasPermission` 改为：

```ts
const hasPermission = (permission: string) => {
  if (isAdmin) return true
  return currentUser.permissions?.some((item) => permissionMatches(item, permission)) ?? false
}
```

* [ ] **Step 3: 接入权限目录和角色权限状态接口**

修改 `src/types/role.ts` 增加：

```ts
export interface PermissionAliasDto {
  canonicalCode: string
  aliasCodes: string[]
}

export interface RolePermissionTemplateDto {
  roleName: string
  permissionCodes: string[]
}

export interface PermissionCatalogDto {
  categories: PermissionCategoryDto[]
  permissionAliases: PermissionAliasDto[]
  roleTemplates: RolePermissionTemplateDto[]
  superAdminRoleNames: string[]
}

export interface RolePermissionStateDto {
  roleGuid: string
  roleName: string
  isSuperAdmin: boolean
  implicitAllPermissions: boolean
  explicitPermissionCodes: string[]
  effectivePermissionCodes: string[]
}
```

修改 `src/services/roleService.ts` 增加：

```ts
export async function getPermissionCatalog(): Promise<PermissionCatalogDto> {
  const response = await request.get<ApiResponse<PermissionCatalogDto>>('/api/Roles/permissions/catalog')
  return unwrapApiData(response)
}

export async function getRolePermissionState(guid: string): Promise<RolePermissionStateDto> {
  const response = await request.get<ApiResponse<RolePermissionStateDto>>(`/api/Roles/guid/${guid}/permissions/state`)
  return unwrapApiData(response)
}
```

* [ ] **Step 4: 权限页显示 Admin 隐式全权限**

修改 `src/pages/System/Permissions/index.tsx` 和 `src/pages/System/Roles/RolePermissionManager.tsx`：

```text
当 rolePermissionState.isSuperAdmin === true:
- 显示提示文案：Admin 默认拥有所有权限，无需分配
- 权限勾选列表以 effectivePermissionCodes 全选展示
- 保存按钮 disabled
- 不调用 assignPermissionsToRole

当普通角色:
- 勾选值使用 explicitPermissionCodes
- 可显示 template 权限徽标，但保存仍只提交用户勾选结果
```

* [ ] **Step 5: Web 验证**

Run:

```bash
cd /Users/sean/DEV/hbweb_rv
npm run test:access
npm run build
rg "WAREHOUSE_MANAGER_PERMISSION_CODES|WarehouseManager role-level|LocalInvocie.View\\)|LocalInvocie.Edit\\)" src
```

Expected:

* `npm run test:access` passes.

* `npm run build` passes.

* `WAREHOUSE_MANAGER_PERMISSION_CODES` 无匹配。

* `LocalInvocie` 只允许出现在 centralized alias helper 或文案/测试里。

### Task 3: Expo 只消费权限 claims 和 app-menu

**Owner:** GPT-5.4 Expo 子代理\
**Write scope:** `/Users/sean/DEV/HbwebExpo/HbwebExpoApp`\
**Depends on:** 第一阶段 backend app-menu behavior; Task 1 current user permission expansion.

* [ ] **Step 1: 增加 route guard 测试**

修改 `src/modules/navigation/default-route.test.ts` 或新增同目录测试，覆盖：

```ts
import { getDefaultRouteName } from './default-route'

const route = getDefaultRouteName({
  roleNames: ['WarehouseManager'],
  permissions: [],
  appMenu: [],
})

if (route === 'warehouse') {
  throw new Error('WarehouseManager role alone must not default to warehouse route')
}
```

Run:

```bash
cd /Users/sean/DEV/HbwebExpo/HbwebExpoApp
npx --yes tsx src/modules/navigation/default-route.test.ts
```

Expected: 旧实现如果通过角色名推导 warehouse，则失败。

* [ ] **Step 2: 统一 auth current user 权限类型**

修改 `src/modules/auth/types.ts`，确保 `CurrentUser` 有：

```ts
export type CurrentUser = {
  userGuid: string
  username: string
  roleNames: string[]
  permissions: string[]
  stores: Array<{ storeCode: string; storeName: string }>
}
```

修改 `src/modules/auth/use-current-user.ts`：

```text
从 /api/Auth/current 读取 permissions。
不在客户端根据 roleNames 补权限。
Admin 只作为 hasPermission 的快捷判断，不生成 fake permission list。
```

* [ ] **Step 3: App tab 和默认路由只信任 app-menu**

修改：

* `src/modules/navigation/api.ts`

* `src/modules/navigation/store.ts`

* `src/modules/navigation/default-route.ts`

* `app/(tabs)/_layout.tsx`

* `src/components/navigation/tab-grouping.ts`

规则：

```text
优先使用 /api/navigation/app-menu 返回的 routeName 列表。
如果 app-menu 为空，只显示 settings 或登录后安全兜底页。
不要因为 roleNames 包含 StoreManager/WarehouseManager/Admin 以外的普通角色而追加功能入口。
Admin 可以通过 app-menu 全量返回实现全入口，不需要客户端拼全量菜单。
```

* [ ] **Step 4: LocalInvocie legacy 不在页面散落判断**

检查：

```bash
cd /Users/sean/DEV/HbwebExpo/HbwebExpoApp
rg "LocalInvocie|WarehouseManager|StoreManager" src app
```

Allowed:

```text
LocalInvocie 只允许存在于 local-supplier-invoices navigation 兼容测试或 centralized permission helper。
WarehouseManager/StoreManager 只允许作为显示身份、结构性范围判断、测试数据；不能用于入口授权。
```

* [ ] **Step 5: Expo 验证**

Run:

```bash
cd /Users/sean/DEV/HbwebExpo/HbwebExpoApp
npx tsc --noEmit
npx --yes tsx src/modules/navigation/default-route.test.ts
npx --yes tsx src/components/navigation/tab-grouping.test.ts
```

Expected: typecheck and navigation tests pass.

### Task 4: 端到端权限一致性和发布检查

**Owner:** GPT-5.5 verifier\
**Write scope:** 只读优先；只有发现阻断问题才交回对应 owner 修复。

* [ ] **Step 1: 全仓库残留扫描**

Run:

```bash
cd /Users/sean/DEV
rg "IsWarehouseManagerGranted|IsStoreManagerGranted|IsAttendanceSelfServiceGranted|WarehouseManagerGrantedPermissions|StoreManagerGrantedPermissions|AttendanceSelfServicePermissions" HBBblazorweb-master-vite hbweb_rv HbwebExpo
rg "WAREHOUSE_MANAGER_PERMISSION_CODES|role-level permission grants" hbweb_rv HbwebExpo
```

Expected: 无运行时白名单残留。

* [ ] **Step 2: Admin 无显式权限验证**

Run backend tests:

```bash
cd /Users/sean/DEV/HBBblazorweb-master-vite
dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj --filter "RoleServicePermissionTests|SeedDataServiceTests|PermissionAuthorizationHandlerTests|NavigationServiceTests|AuthServiceTests"
```

Expected: all selected tests pass.

* [ ] **Step 3: 三端 build**

Run:

```bash
cd /Users/sean/DEV/HBBblazorweb-master-vite
dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj

cd /Users/sean/DEV/hbweb_rv
npm run build

cd /Users/sean/DEV/HbwebExpo/HbwebExpoApp
npx tsc --noEmit
```

Expected:

```text
Backend tests pass.
Web build passes.
Expo typecheck passes.
```

* [ ] **Step 4: 人工冒烟场景**

验证场景：

```text
Admin:
- Web 权限页显示 Admin 默认拥有所有权限
- Admin 保存权限按钮禁用或提示无需分配
- Web 菜单全量可见
- App 菜单全量来自 /api/navigation/app-menu

WarehouseManager:
- 没有 SysRolePermission 时，Web/App 不显示仓库入口
- 有 Warehouse.ManageOrders 权限时，显示对应仓库订单入口

StoreManager:
- 仅 role claim 不显示用户管理入口
- 有 Users.View 权限且 role 为 StoreManager 时，App users 入口可见

Legacy:
- 仅有 LocalInvocie.View 显式权限时，授权 LocalPurchase.View 通过
- /api/Auth/current permissions 同时返回 LocalInvocie.View 和 LocalPurchase.View
- Web/App 本地进货入口可见
```

## 成功标准

* 后端权限目录 API 可表达：权限分组、legacy alias、角色模板、超级管理员角色名、角色有效权限。

* Web 权限页直观看出 Admin 是隐式全权限，并且不会给 Admin 写 `SysRolePermission`。

* Web `access.ts` 不再因为 `WarehouseManager` role 自动授予仓库权限。

* Expo 不再通过角色名推导功能入口，入口来自 app-menu 和 permission claims。

* `LocalInvocie.*` 兼容只存在于集中 helper、种子、测试或兼容文案中。

* 三端验证命令全部通过。

⠀