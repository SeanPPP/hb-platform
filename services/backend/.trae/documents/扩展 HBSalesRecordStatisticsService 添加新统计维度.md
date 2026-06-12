# 扩展 HBSalesRecordStatisticsService 添加5张统计表的数据生成

## 背景
参考 `SalesStatisticsJobService.cs` 的 `FullRefreshDateRangeWithContext` 方法（L2079-2127），一共有5张表需要更新统计数据。当前 `HBSalesRecordStatisticsService` 只生成了 `SupplierSalesStatistic`（供应商日期统计），需要补充其余4张表的统计逻辑。

## 需要更新的5张统计表

1. **DailySalesStatistic** - 每日统计（按日期汇总所有销售）
2. **HourlySalesStatistic** - 分时分店统计（按日期+小时+分店汇总）
3. **StoreSalesStatistic** - 分店日期统计（按日期+分店汇总）
4. **SupplierSalesStatistic** - 供应商日期统计（已实现，按日期+供应商汇总）
5. **StoreSupplierSalesDetail** - 供应商分店日期统计（按日期+分店+供应商汇总）

## 实施步骤

### 1. 修改 `HBSalesRecordStatisticsService.cs`

#### 添加新的私有方法
- `ProcessDailyStatistics()` - 生成每日统计（DailySalesStatistic）
- `ProcessStoreDateStatistics()` - 生成分店日期统计（StoreSalesStatistic）
- `ProcessHourlyStoreStatistics()` - 生成分时分店统计（HourlySalesStatistic）
- `ProcessStoreSupplierStatistics()` - 生成供应商分店日期统计（StoreSupplierSalesDetail）

#### 添加批量更新方法
- `UpdateDailyStatisticsBatch()` - 批量更新每日统计
- `UpdateStoreDateStatisticsBatch()` - 批量更新分店日期统计
- `UpdateHourlyStoreStatisticsBatch()` - 批量更新分时分店统计
- `UpdateStoreSupplierStatisticsBatch()` - 批量更新供应商分店统计

#### 修改 `ProcessDateRange()` 方法
- 返回值从 `List<SupplierSalesStatistic>` 改为包含5种统计类型的自定义结果类
- 同时生成5种统计数据

#### 修改 `ImportAndStatistics2025Concurrent()` 方法
- 修改批量写入逻辑，处理5种统计类型
- 更新日志和结果信息

### 2. 修改 `BatchStatisticsUpdateResult` 类（如需要）
- 添加字段以跟踪不同统计类型的处理情况

## 数据处理逻辑（参考 SalesStatisticsJobService）

### 每日统计（DailySalesStatistic）
- 按 `Date` 分组
- 聚合：`TotalAmount`、`TotalQuantity`、`OrderCount`、`SkuCount`、`CustomerCount`、`AverageOrderValue`

### 分时分店统计（HourlySalesStatistic）
- 按 `Date` + `Hour` + `BranchCode` 分组
- 聚合：`TotalAmount`、`TotalQuantity`、`CustomerCount`、`AverageOrderValue`
- 从 `Store` 表获取 `BranchName`
- 每小时生成一条"ALL"分店的汇总记录

### 分店日期统计（StoreSalesStatistic）
- 按 `Date` + `BranchCode` 分组
- 聚合：`TotalAmount`、`TotalQuantity`、`OrderCount`、`CustomerCount`、`AverageOrderValue`
- 从 `Store` 表获取 `BranchName`

### 供应商日期统计（SupplierSalesStatistic，已有）
- 保持现有逻辑
- 按 `Date` + `SupplierCode` 分组
- 聚合：`TotalAmount`、`TotalQuantity`、`StoreCount`
- 标记 `IsDomestic`（是否为国内供应商）

### 供应商分店日期统计（StoreSupplierSalesDetail）
- 按 `Date` + `BranchCode` + `SupplierCode` 分组
- 聚合：`TotalAmount`、`TotalQuantity`
- 获取 `SupplierName` 和 `IsDomestic` 标记

## 修改文件
- `BlazorApp.Api/Services/HBSalesRecordStatisticsService.cs`

## 预期效果
运行 `ImportAndStatistics2025Concurrent()` 时，将同时生成5种统计类型的数据，实现与 `SalesStatisticsJobService` 相同的统计维度覆盖。