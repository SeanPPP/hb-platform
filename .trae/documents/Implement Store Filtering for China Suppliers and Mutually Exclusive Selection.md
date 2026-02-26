我将执行以下步骤来实现国内供应商的分店筛选功能，并确保供应商选择互斥：

### 1. 更新后端 API
修改 `BlazorApp.Api/Controllers/React/SalesDashboardController.cs`：
- 更新 `GetChinaSupplierSalesRankAsync` 方法，接受可选的 `branchCode` 查询参数。
- 实现权限合并逻辑：如果提供了 `branchCode`，需检查其是否在用户允许的分店列表中（管理员除外）。

### 2. 更新前端服务
修改 `ReactUmi/my-app/src/services/salesDashboard.ts`：
- 更新 `getChinaSupplierSalesRank` 函数，接受可选的 `branchCode` 参数并传递给 API。

### 3. 更新销售仪表板逻辑
修改 `ReactUmi/my-app/src/pages/SalesDashboard/index.tsx`：
- **互斥选择逻辑**:
  - 修改 `handleSupplierRowClick`：选中普通供应商时，将 `selectedChinaSupplier` 设为 `null`。
  - 修改 `handleChinaSupplierRowClick`：选中国内供应商时，将 `selectedSupplier` 设为 `null`。
- **数据加载 (`loadDashboardData`)**:
  - 当 `selectedBranchCode` 有值时，将其传递给 `getChinaSupplierSalesRank`。
  - 移除之前“选中分店时清空国内供应商数据”的逻辑。
  - 移除 JSX 中“选中分店时隐藏国内供应商表格”的条件渲染，使其始终显示。