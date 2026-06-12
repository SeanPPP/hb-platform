## 优化目标
修改 `ProcessDateRange` 方法中的 HBSales 查询逻辑，实现单据类型过滤和正负值处理。

## 修改内容

### 1. 替换第 304-374 行的 HBSales 查询和聚合逻辑

**当前逻辑**：
- 只查询 `SalesOrderDetailRecord` 明细表
- 没有关联主表
- 没有单据类型过滤

**新逻辑**：
- 使用 `LeftJoin` 关联 `SalesOrderMain` 主表和 `SalesOrderDetailRecord` 明细表
- 添加 `Where` 条件排除类型 "2"（挂单）：`m.B单据类型 != "2"`
- 在 `Select` 中包含 `DocumentType` 字段
- 在聚合时根据单据类型调整金额和数量：
  - 类型 "1", "5"：保持原值
  - 类型 "3", "4"：金额和数量取负值

### 2. 保留 OrderCount 和 CustomerCount 的简单计算逻辑
- 每条明细记录计数为 1（适用于大部分统计场景）

## 文件修改
- `BlazorApp.Api/Services/HBSalesRecordStatisticsService.cs`（第 304-374 行）