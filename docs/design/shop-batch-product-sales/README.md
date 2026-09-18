# 订货前台「货号销量」页面：设计与验收

日期：2026-09-18。目标：让订货前台（`/shop`）用户按导入货号查看**全部分店**的销量，页面完整复用后台「批量货号销量」组件，后端沿用同一组接口。

## 设计结论

- 导航：桌面一级导航「热销商品」之后新增「货号销量 / Item Sales」，移动端进入「更多」抽屉；路由 `/shop/batch-product-sales`，套用 `shop-workspace-layout` 紧凑外壳。
- 页面：`apps/web/src/pages/ShopBatchProductSales` 仅包一层 `shop-feature-page`，内部是原样的 `BatchProductSalesAnalysisPage`，工具栏、商品范围弹窗、趋势图、分店排行和分店商品贡献全部保留。
- 文案：`shop.batchProductSales`、`shop.batchProductSalesBannerSubtitle` 中英文各一份；页面自身文案沿用 `batchProductSalesAnalysis` 命名空间。

## 权限与门店范围

- 新增前台权限码 `OrderFront.BatchProductSales.View`（分类「前台订货」），只注册权限，不写入订货员角色模板，由管理员显式授予。
- `BatchProductSalesAnalysisController` 改为 `[Authorize]`，每个请求仍在 `ResolveStoreScopeAsync` 里按实时精确权限核验：持有前台权限即返回全店范围（与热销榜口径一致）；仅持有后台 `SalesDashboard.BatchProductSales.View` 的普通用户继续受名下门店限制；两者都没有则 403。
- Web 端 `canViewShopBatchProductSales` 只认前台权限码，不参与后台入口判定，因此订货员获授权后仍默认落在 `/shop`，且不会获得后台壳入口；`/shop/batch-product-sales` 历史地址按该权限单独放行。
- 移动端权限管理页的中英文说明已登记在 `apps/mobile/src/locales/*/accessPermissions.json`。

## 验收截图（演示数据）

- [桌面 · 中文](implementation-desktop-zh.png)
- [桌面 · 英文](implementation-desktop-en.png)
- [手机 390px · 中文](implementation-mobile-zh.png)

截图由本地验收入口 `apps/web/dev/shop-batch-product-sales-preview/` 生成：真实 `ShopLayout` + 正式页面，仅在入口内模拟 fetch，`?auto=1` 自动导入货号、查询并选中首个分店。启动方式：在 `apps/web` 执行 `npm run dev -- --port 5196` 后打开 `http://localhost:5196/dev/shop-batch-product-sales-preview/?lang=zh`。

## 已执行的验证

- 后端：`dotnet test` 过滤 `BatchProductSalesAnalysisControllerTests | SeedDataServiceTests | NavigationServiceTests`，198 项通过（含新增的前台权限全店范围与后台权限门店限制用例）。
- Web：`npm run typecheck`、`npm run test:access`、`test:batch-product-sales-analysis`，以及 `shopMobileNavigation`、`shopRedesignUiContract`、`shopBannerCopy` 契约测试全部通过。
- 移动端：`access-permission-presentation.test.ts` 与 `scripts/check-locale-parity.js` 通过。
- 浏览器：桌面 1440 与手机 390 视口下真实页面挂载、导入→查询→选分店流程完成；手机视口 `scrollWidth` 等于视口宽度，矩阵表格在面板内部横向滚动。

## 未覆盖

- 未连接生产数据库验收；生产权限需管理员在角色管理中为订货角色授予「前台查看货号销量」后生效。
