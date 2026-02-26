## 销售订单查询页面实施计划

### 后端（BlazorApp.Api）
1. 创建 `PosmSalesOrderReactController` 控制器
2. 创建 `IPosmSalesOrderReactService` 接口和 `PosmSalesOrderReactService` 实现
3. 创建 DTO 类型（订单主表、明细、支付、查询参数）
4. 创建 AutoMapper 配置 `PosmSalesOrderProfile`
5. 在 `Program.cs` 注册服务

### 前端（ReactUmi/my-app）
6. 创建 API 服务 `src/services/posmSalesOrder.ts`
7. 创建类型定义 `src/types/posmSalesOrder.ts`
8. 创建页面组件 `src/pages/PosmSalesOrders/index.tsx`（ProTable + 可展开行）
9. 添加中英文翻译 `src/locales/zh-CN.ts` 和 `en-US.ts`
10. 在 `.umirc.ts` 配置路由 `/posm/sales-orders`

### 功能特性
- 日期范围查询（默认当天）
- 分店/设备号过滤
- 后端按时间升序排序
- 订单主表9列（含二维码）
- 点击展开显示订单明细表和支付明细表