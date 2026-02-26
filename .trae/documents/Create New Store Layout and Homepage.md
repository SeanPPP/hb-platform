经过检查代码配置 (`.umirc.ts`)，`/store` 路由**目前不存在**。

现有的相关路由只有：
*   `/system/store-management` (店铺管理 - 后台)
*   `/warehouse/store-order` (店铺订货 - 仓库端)

因此，我们可以安全地创建 `/store` 作为全新的普通用户首页。

### 执行计划：

1.  **新建路由**: 在 `.umirc.ts` 中添加 `/store`，配置为不使用默认的后台 Layout。
2.  **创建布局**: 实现 `StoreLayout.tsx` (无左侧菜单，仿 Brazco 设计)。
3.  **创建页面**: 实现 `StoreHome/index.tsx` (商品展示、购物车、分页)。
4.  **实现跳转**: 修改登录逻辑，让普通用户登录后自动跳转到 `/store`。

请确认执行。