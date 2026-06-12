完成将 UserProfile 移出一级菜单的配置。

1.  **路由配置更新**:
    - 在 `.umirc.ts` 中，已将 UserProfile 路由从 `system` 下移出，配置为顶级路由 `/profile`。
    - 添加了 `hideInMenu: true` 和 `hideInBreadcrumb: true`，使其不在侧边栏菜单中显示。

2.  **布局组件更新**:
    - 更新了 `SystemLayout.tsx` 中的下拉菜单跳转链接，指向新的 `/profile` 路径。

现在用户可以通过点击右上角头像下拉菜单中的"个人中心"访问个人信息页面，且该页面不会出现在左侧系统菜单中。