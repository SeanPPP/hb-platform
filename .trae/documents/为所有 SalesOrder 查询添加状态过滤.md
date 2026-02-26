## 为所有 SalesOrder 查询添加状态过滤

**目标**：为所有 `SalesOrder` 查询添加状态过滤 `o.Status != null && (o.Status == 1 || o.Status == 4)`

**修改位置**（共 10 处）：
1. `UpdateDailyStatistics` - 第 109-110 行
2. `UpdateHourlyStatistics` - 第 178-181 行
3. `UpdateStoreStatistics` (第 1 个) - 第 350-352 行
4. `UpdateStoreStatistics` (第 2 个) - 第 548-550 行
5. `UpdateSupplierStatistics` - 第 690-696 行
6. `UpdateSupplierStatisticsWithContext` - 第 1163-1169 行
7. `UpdateDailyStatisticsWithContext` - 第 2182-2184 行
8. `UpdateHourlyStatisticsWithContext` - 第 2256-2262 行
9. `UpdateStoreStatisticsWithContext` - 第 2438-2440 行
10. `UpdateStoreSupplierStatisticsWithContext` - 第 2575-2581 行

**注意**：`UpdateStoreSupplierStatistics` (第 1499-1504 行) 已有此过滤条件，无需修改。