修改 `SalesStatisticsJobService.cs` 中 `UpdateHourlyStatistics` 方法：

1. 将 `BranchCode = null` 改为 `BranchCode = "ALL"`（或空字符串 `""`）
2. 同样修改 `BranchName = null` 改为 `BranchName = "All Stores"`（或 `"全部门店"`）
3. 更新查询逻辑，将 `BranchCode == null` 的条件改为 `BranchCode == "ALL"`

这样可以确保插入的数据满足数据库的 NOT NULL 约束，同时保持语义清晰。