我将执行以下修改，以隐藏部分组件的代码列并将前端的默认显示行数增加到 100：

### 1. 隐藏代码列（仅针对指定组件）
- **`StoreSalesRankTable.tsx`**: 移除 "店铺代码" (`branchCode`) 列。
- **`SupplierSalesRankTable.tsx`**: 移除 "供应商代码" (`supplierCode`) 列。
- **`ChinaSupplierSalesRankTable.tsx`**: **保留** "供应商代码" 列（根据您的最新指示）。

### 2. 调整默认显示行数 (Top 20/50 -> Top 100)
修改 `src/pages/SalesDashboard/index.tsx` 中的 `loadDashboardData` 函数：
- 将所有 API 调用中的 `topN` 参数（如 `getStoreSalesRank`, `getSupplierSalesRank`, `getChinaSupplierSalesRank` 等）从默认的 `20` 或 `50` 统一修改为 **100**。
- 确保前端请求的数据量与后端一致，展示前 100 条记录。