## 改造 UpdateSupplierStatistics 方法支持日期区间

### 核心优化
利用 GroupBy 已有的日期维度，一次查询完成日期区间的统计，避免逐日循环。

### 具体步骤

1. **修改方法签名**

   - 从 `UpdateSupplierStatistics(DateTime date, List<string>? supplierCodes = null)`
   - 改为 `UpdateSupplierStatistics(DateTime? startDate = null, DateTime? endDate = null, List<string>? supplierCodes = null)`
   - 如果只传 startDate，endDate 默认为 startDate（向后兼容单日期调用）

2. **调整日期查询条件**

   - 单日期：`o.OrderTime.Value.Date == startDate`
   - 日期区间：`o.OrderTime.Value.Date >= startDate && o.OrderTime.Value.Date <= endDate`

3. **调整现有记录查询条件**

   - 从 `Where(s => s.Date == date && ...)`
   - 改为 `Where(s => s.Date >= startDate && s.Date <= endDate && ...)`

4. **更新日志和参数验证**

   - 记录日期区间信息
   - 验证 startDate <= endDate
   - 更新成功和失败日志

5. **保持向后兼容**

   - 原有单日期调用方式依然有效
   - GroupBy 已保留日期维度，天然支持多日期统计

