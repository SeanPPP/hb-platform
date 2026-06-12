## 修复 SQL 参数声明错误

### 问题原因
`SalesStatisticsJobService.cs` 中使用 `IgnoreColumns(it => new { it.Date })` 导致 UPDATE 语句的 WHERE 条件参数 `@Date` 未定义。

### 修复方案
修改 `UpdateDailyStatistics` 方法中的更新逻辑：
- 将 `IgnoreColumns(it => new { it.Date })` 改为正确的方式
- 或者使用 `SetColumns` 只更新需要更新的列，保留 `Date` 列用于 WHERE 条件

### 修改文件
- `BlazorApp.Api/Services/Hangfire/SalesStatisticsJobService.cs`