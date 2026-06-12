## 改造 UpdateSupplierStatistics 方法支持日期区间

### 核心优化
避免逐日循环（超过5分钟），改为一次性查询日期区间，在 GroupBy 中保留日期维度。

### 具体步骤

1. **修改方法签名**

   - 从 `UpdateSupplierStatistics(DateTime? date = null)`
   - 改为 `UpdateSupplierStatistics(DateTime? startDate = null, DateTime? endDate = null, List<string>? supplierCodes = null)`
   - 如果只传 startDate，endDate 默认为 startDate（向后兼容单日期调用）

2. **修改日期查询条件**

   - 单日期：`o.OrderTime.Value.Date == startDate`
   - 日期区间：`o.OrderTime.Value.Date >= startDate && o.OrderTime.Value.Date <= endDate`

3. **GroupBy 保持日期维度**

   - 保留 `GroupBy((o, d) => new { Date = o.OrderTime.Value.Date, d.SupplierCode })`
   - 一次查询返回所有日期和供应商的统计

4. **调整现有记录查询条件**

   - 从 `Where(s => s.Date == targetDate && ...)`
   - 改为 `Where(s => s.Date >= startDate && s.Date <= endDate && ...)`

5. **更新日志和参数验证**

   - 记录日期区间信息
   - 验证 startDate <= endDate

6. **保持向后兼容**

   - 原有单日期调用方式依然有效

