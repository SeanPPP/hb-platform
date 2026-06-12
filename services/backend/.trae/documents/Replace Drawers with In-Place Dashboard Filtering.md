我将通过以下步骤修改代码，实现点击组件后在当前页面显示详细数据，而不再弹出抽屉：

### 1. 修改类型定义
修改 `src/types/salesDashboard.ts`，将部分字段设为可选，以兼容不同的数据源：
- `StoreSalesRank`: 将 `orderCount` (订单数), `customerCount` (客流数), `averageOrderValue` (客单价) 设为可选。
- `SupplierSalesRank`: 将 `storeCount` (门店数) 设为可选。

### 2. 更新表格组件
修改 `StoreSalesRankTable.tsx` 和 `SupplierSalesRankTable.tsx`：
- 添加 `title` 属性，允许父组件覆盖默认标题。
- 优化列渲染逻辑，当上述可选字段为空时，安全地显示占位符（如 `-`），防止报错。

### 3. 重构销售仪表板 (index.tsx)
- **移除抽屉相关代码**: 删除 `drawerVisible`、`drawerType` 状态以及 `SupplierStoreSalesDrawer` 和 `StoreSupplierSalesDrawer` 组件。
- **优化数据加载逻辑 (loadDashboardData)**:
  - **选中供应商时**: 调用 `getSupplierStoreSales` 获取该供应商在各门店的销售数据，并填充到“门店销售排名”表格中。
  - **选中门店时**: 调用 `getStoreSupplierSales` 获取该门店的各供应商销售数据，并填充到“供应商销售排名”表格中。同时清空“中国供应商排名”表（避免数据混淆）。
- **更新交互逻辑**:
  - 点击表格行时，不再打开抽屉，而是直接更新筛选状态 (`selectedSupplier` 或 `selectedBranchCode`)，从而触发仪表板数据刷新。
- **动态标题**:
  - 根据选中状态动态设置表格标题。例如，选中某供应商时，门店表格标题显示为“{供应商} 的门店销售详情”。