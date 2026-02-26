我将通过更新后端服务和前端组件来实现您的需求，包括过滤、排序、分页以及任务类型的彩色显示。

**后端实现：**

1. **更新** **`ScheduledTaskLogService.cs`**：

   * 添加 `GetPagedTasksAsync` 方法，支持：

     * 分页 (`pageIndex`, `pageSize`)

     * 过滤 (`taskType`, `status`, `triggeredBy`, `startDate`, `endDate`)

     * 排序 (`sortBy`, `sortDirection`)

   * 修改 `GetTaskStatisticsAsync` 方法，使其在未提供日期时返回所有历史数据的统计，而不仅仅是当天的。

2. **更新** **`ScheduledTaskRetryController.cs`**：

   * 创建 `ScheduledTaskQueryDto` 类用于接收查询参数。

   * 添加新的 API 接口 `GET /api/ScheduledTaskRetry/list`，调用新的服务方法进行分页查询。

**前端实现：**

1. **更新** **`src/services/scheduledTaskRetry.ts`**：

   * 添加 `getTaskList` 函数以调用新的 API 接口。

2. **更新** **`src/pages/ScheduledTaskHistory/index.tsx`**：

   * **任务类型彩色显示**：根据您提供的截图，为“任务类型”列配置对应的颜色（Tag）：

     * 分店统计: `blue`

     * 供应商统计: `green`

     * 门店供应商统计: `lime`

     * 每日统计: `orange`

     * 全量刷新: `red`

     * 批量分店: `purple`

     * 批量供应商: `cyan`

     * 批量门店供应商: `volcano`

     * 批量每日: `geekblue`

     * 批量分时: `magenta`

   * **ProTable 升级**：

     * 启用查询栏 (`search={{ labelWidth: 'auto' }}`)。

     * 设置默认每页 50 条 (`pagination={{ defaultPageSize: 50 }}`)。

     * 为“任务类型”、“状态”、“触发方式”列添加筛选功能 (`valueEnum`)。

     * 启用列排序 (`sorter`)。

   * **数据请求**：

     * 更新 `request` 属性，将前端的筛选和排序参数正确映射到后端 API。

   * **统计卡片**：

     * 加载页面时获取全量统计数据，解决“信息没有”的问题。

**验证：**

* 确认任务列表每页显示 50 条。

* 确认可以通过任务类型、状态、触发方式进行筛选。

* 确认各列可以点击排序。

* 确认任务类型列显示为指定的颜色。

* 确认顶部的统计卡片显示了正确的总数。

