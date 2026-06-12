我将修改 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\SalesDashboard\index.tsx`，确保在选中特定分店时，能够正确获取并显示该分店的“国内供应商销售排行”数据，而不是清空数据。

### 具体修改计划：
1.  **定位 `loadDashboardData` 函数**：找到检查 `selectedBranchCode` 的逻辑块（约第 101 行）。
2.  **更新逻辑**：
    - 将 `chinaSupplierPromise = Promise.resolve({ success: true, data: [] });`
    - 修改为 `chinaSupplierPromise = getChinaSupplierSalesRank(dateRangeParams, 20, selectedBranchCode);`

这一修改会将选中的分店代码传递给后端 API，从而让“国内供应商销售排行”表格显示该分店的过滤数据。