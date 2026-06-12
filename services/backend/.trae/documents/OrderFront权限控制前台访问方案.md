# 新增 OrderFront 权限码控制前台访问 方案

## 现状

当前前台（`/shop`）对所有已登录用户开放，用 `onlyOrder`（纯订货员角色）控制默认跳转目标。**但任何登录用户都能访问 `/shop` 页面**，只是菜单/按钮做了条件渲染。

## 改造目标

新增权限码 `OrderFront`，**只有持有此权限的用户才能进入前台页面**。无此权限的用户访问 `/shop` 将被重定向回 `/dashboard` 或显示 403。

---

## 步骤 1：后端新增权限码

**文件**：`BlazorApp.Shared/Constants/Permissions.cs`

```csharp
public static class OrderFront
{
    public const string Access = "OrderFront.Access";  // 访问前台（订货页面）
}
```

在 `GetAllPermissions()` 中新增种子数据：

```csharp
// 前台访问
yield return (OrderFront.Access, "访问前台", "前台订货");
```

---

## 步骤 2：前端 AccessControl 新增 accessKey

**文件**：`hbweb_rv/src/types/auth.ts`

在 `AccessControl` 接口末尾（`managedStoreCodes` 之前）新增：

```typescript
canAccessShop: boolean  // OrderFront.Access — 前台访问权限
```

---

## 步骤 3：前端 access.ts 新增计算逻辑

**文件**：`hbweb_rv/src/utils/access.ts`

```typescript
// 在 createEmptyAccess 中添加默认值
canAccessShop: false,

// 在 buildAccess 中添加计算
const canAccessShop = isAdmin || hasPermission('OrderFront.Access');

// 在 return 中添加
canAccessShop,
```

---

## 步骤 4：App.tsx 保护 `/shop` 路由

**文件**：`hbweb_rv/src/App.tsx`

### 4.1 修正 homePage 判断

```diff
- const isOnlyOrder = access.onlyOrder
- const homePage = isOnlyOrder ? '/shop' : '/dashboard'
+ const homePage = access.canAccessShop ? '/shop' : '/dashboard'
```

### 4.2 保护 /shop 路由入口

```diff
  <Route
    path="/shop"
-   element={currentUser ? <ShopLayout /> : <Navigate to="/login" replace />}
+   element={
+     currentUser
+       ? (access.canAccessShop ? <ShopLayout /> : <Navigate to="/dashboard" replace />)
+       : <Navigate to="/login" replace />
+   }
  >
```

---

## 步骤 5：Login 页修正跳转逻辑

**文件**：`hbweb_rv/src/pages/Login/index.tsx`

```diff
- const defaultPage = useAuthStore.getState().access.onlyOrder ? '/shop' : '/dashboard'
+ const defaultPage = useAuthStore.getState().access.canAccessShop ? '/shop' : '/dashboard'
```

---

## 步骤 6：后端导航服务补充权限

**文件**：`BlazorApp.Api/Services/NavigationService.cs`

不需要改动——`/shop` 不在管理后台导航中。

但需要在 API 控制器中保护 Shop 相关端点（如果存在独立的 Shop Controller）。搜索并确认。

---

## 步骤 7：编译验证 + Docker 部署

1. `dotnet build` 后端
2. `npx tsc --noEmit` + `npm run build` 前端
3. `docker compose up -d --build` 两边部署

---

## 影响范围总结

| 文件 | 改动 |
|------|------|
| `Permissions.cs` | 新增 `OrderFront.Access` |
| `types/auth.ts` | 新增 `canAccessShop: boolean` |
| `access.ts` | 新增 `canAccessShop` 计算 |
| `App.tsx` | `/shop` 路由加权限守卫，修正 `homePage` |
| `Login/index.tsx` | 修正登录后跳转默认页 |

## 行为变化

| 场景 | 旧行为 | 新行为 |
|------|--------|--------|
| 无权限用户访问 `/shop` | ✅ 正常进入 | ❌ 重定向到 `/dashboard` |
| 拥有 `OrderFront` 用户登录 | → `/dashboard`（除非 onlyOrder） | → `/shop` |
| 无 `OrderFront` 用户登录 | → `/dashboard` | → `/dashboard` |
