## 修复 ScheduledTaskLog 插入失败问题

### 问题分析
用户报告在执行定时任务时出现 `SqlException`，提示无法将 `NULL` 值插入 `ScheduledTaskLog` 表的 `ErrorMessage` 列。
虽然代码中 `ScheduledTaskLog` 实体的 `ErrorMessage` 属性标记为 `IsNullable = true`，但实际数据库表结构中该列似乎被定义为 `NOT NULL`。

### 解决方案
为了解决这个问题并防止插入失败，我将在 `ScheduledTaskLogService.LogTaskStartAsync` 方法中初始化 `ScheduledTaskLog` 对象时，显式将 `ErrorMessage` 设置为 `string.Empty`（空字符串），而不是默认的 `null`。

### 修改计划
1.  编辑 `D:\Development\cline\blazor\BlazorApp.Api\Services\Background\ScheduledTaskLogService.cs` 文件。
2.  在 `LogTaskStartAsync` 方法中，创建 `ScheduledTaskLog` 实例时，添加 `ErrorMessage = string.Empty` 初始化。

这样可以确保插入数据库时 `ErrorMessage` 列有值，避免违反非空约束。