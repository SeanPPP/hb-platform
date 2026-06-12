# 修复路由配置错误

`flattenRoutes` 运行时错误是由于将绝对路径 `/store-order/invoice/:id` 嵌套在 `/warehouse` 路由组中引起的，这违反了路由嵌套规则（子路由路径通常应以父路径 `/warehouse` 开头）。

我将通过把 `storeOrderInvoice` 路由移动到 `.umirc.ts` 中的顶层 `routes` 配置来修复此问题。这将同时允许我们为 Invoice 页面禁用标准布局（`layout: false`），从而确保打印视图干净整洁（无侧边栏或标签页）。

## 1. 修改 .umirc.ts
- 从 `warehouse` 子路由中移除 `storeOrderInvoice` 路由。
- 将 `storeOrderInvoice` 路由添加到主 `routes` 数组中（例如，在 404 路由之前）。
- 为该路由设置 `layout: false`，确保全屏渲染以便打印。

## 2. 验证
- 路由路径 `/store-order/invoice/:id` 与 `OrderDetails` 页面中使用的链接一致，因此无需修改业务代码。
- 此更改应解决 `flattenRoutes` 错误并使应用恢复正常。
