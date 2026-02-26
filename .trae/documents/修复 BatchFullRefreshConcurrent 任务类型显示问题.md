在 `ReactUmi/my-app/src/pages/ScheduledTaskHistory/index.tsx` 文件中添加对 `BatchFullRefreshConcurrent` 任务类型的支持：

1. 在 `getTaskTypeText` 函数（第181-197行）的 `typeMap` 中添加映射
2. 在 `getTaskTypeColor` 函数（第199-215行）的 `colorMap` 中添加颜色映射
3. 在 `columns` 配置的 `valueEnum`（第249-262行）中添加筛选选项

这样该任务类型将正确显示为中文名称"批量并发全部刷新（7天/5并发）"，并在表格中显示合适的颜色。