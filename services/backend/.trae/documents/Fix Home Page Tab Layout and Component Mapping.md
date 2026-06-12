我将修复路由配置，确保首页 (Home) 在 Tab 布局中正确渲染，并指向正确的组件。

### 1. 修复 `.umirc.ts`
*   **问题**: `/home` 路由目前是顶级路由，没有被包裹在 `KeepAliveTabLayout` 中，导致进入首页时 Tab 栏消失。同时，它当前指向的是 `StoreOrder` 组件，而不是 `Home` 组件。
*   **操作**:
    *   重构 `/home` 路由结构：将其作为一个使用 `@/layouts/KeepAliveTabLayout` 的父级路由的子路由。
    *   修正组件映射：将 `/home` 的组件路径从 `./StoreOrder` 改回 `./Home`，以匹配 `app.ts` 中的配置和标准项目结构。

### 2. 验证 `src/app.ts`
*   **检查**: 确认 `pathComponentMap` 中 `/home` 的配置是否正确指向 `Home` 组件。（根据之前的分析应该是正确的，但我会再次确认）。

### 3. 执行步骤
1.  修改 `.umirc.ts`：将 `/home` 路由包裹在 `KeepAliveTabLayout` 布局中。
2.  修改 `.umirc.ts`：将 `/home` 对应的组件更新为 `./Home`。
3.  验证：确保访问首页时能看到 Tab 栏，且导航功能正常。
