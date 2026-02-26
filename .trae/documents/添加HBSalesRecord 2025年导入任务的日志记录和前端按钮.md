## 实施计划

### 后端修改

#### 1. 修改 HBSalesRecordStatisticsService.cs
- 注入 `ScheduledTaskLogService` 依赖
- 在 `ImportAndStatistics2025Concurrent` 方法中添加任务日志记录：
  - 开始时调用 `LogTaskStartAsync` 记录任务开始
  - 成功时调用 `LogTaskSuccessAsync` 记录任务完成
  - 失败时调用 `LogTaskFailureAsync` 记录错误信息
- 返回包含 `TaskId` 的结果对象

#### 2. 修改 HBSalesRecordStatisticsController.cs
- 更新 API 响应格式，返回 `TaskId` 供前端使用

### 前端修改

#### 1. 修改 statisticsJob.ts
- 添加新的接口类型 `HBSalesRecordImport2025Request`
- 添加新的接口类型 `HBSalesRecordImport2025Response`（包含 taskId）
- 添加 API 函数 `triggerHBSalesRecordImport2025()`

#### 2. 修改 StatisticsJobTrigger/index.tsx
- 在 `JobType` 类型中添加 `'hb-sales-record-2025'`
- 在 `jobTypeOptions` 数组中添加新的选项："HBSalesRecord 2025年导入（10天/10并发）"
- 在 `handleTrigger` 函数中添加对应的 case 处理
- 在任务说明 Alert 中添加对应的说明标签

### 技术要点
- 后端任务类型字符串：`"HBSalesRecordImport2025"`
- 触发方式：`TaskTrigger.Manual`（手动触发）
- 任务参数包含：年份（2025）
- 前端 API 超时设置：300000ms（5分钟）
- 任务标签颜色：使用醒目的颜色（如 `purple` 或 `magenta`）