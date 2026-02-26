## 修复方案

### 问题根源
`GroupBy(s => 1)` + 多个聚合函数的组合在 SqlSugar 中可能导致 SQL 生成异常，引发空引用异常。

### 修复步骤

**方案选择**：采用**方案 1（移除 GroupBy）** - 最简单且性能最优

1. **修复 GetDashboardSummaryAsync 方法**（第 82-99 行）
   - 移除 `GroupBy(s => 1)` 
   - 聚合函数可以直接在 Select 中使用，不需要 GroupBy

2. **修复所有类似的查询模式**
   - `GetHourlySalesAsync`（第 262-271 行）
   - `GetStoreSalesRankAsync`（第 372-375 行）
   - `GetSupplierSalesRankAsync`（第 472-475 行）
   - `GetChinaSupplierSalesRankAsync`（第 573-576 行）
   - `GetSupplierStoreSalesAsync`（第 677-680 行）
   - `GetStoreSupplierSalesAsync`（第 770-772 行）

3. **测试验证**
   - 运行 API 测试相关端点
   - 确认数据正确返回且无异常

### 预期效果
- 消除 NullReferenceException
- 查询性能保持或提升（减少 SQL 复杂度）
- 功能完全一致