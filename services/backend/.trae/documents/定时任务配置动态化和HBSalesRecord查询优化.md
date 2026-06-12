## 实现计划

### 任务1: 定时任务配置动态化

**1. 创建系统配置实体**
- 创建 `SysSystemConfig` 实体类 (存储在数据库)
- 包含字段: `HourlyTaskIntervalMinutes`, `DailyTaskHour`, `DailyTaskMinute`
- 继承 `BaseEntity` (包含 Id, CreateTime, UpdateTime 等)

**2. 修改 ScheduledTaskService**
- 改用 `IOptionsMonitor<ScheduledTaskOptions>` 替代 `IOptions<>`
- 添加 `IConfigurationMonitor` 接口实现,监听配置变更
- 配置变更时重新计算定时器间隔
- 添加 `ReloadConfiguration()` 方法手动刷新配置

**3. 创建配置服务**
- 创建 `ISystemConfigService` 接口和 `SystemConfigService` 实现
- 提供方法: `GetConfigAsync()`, `UpdateConfigAsync()`, `SyncToAppSettingsAsync()`
- 从数据库读取配置,支持缓存

**4. 创建配置管理 API**
- 创建 `SystemConfigController` (仅限管理员访问)
- 提供 API: 
  - `GET /api/SystemConfig` - 获取当前配置
  - `PUT /api/SystemConfig` - 更新配置
  - `POST /api/SystemConfig/reload` - 强制重新加载配置

**5. 初始化种子数据**
- 修改 `SeedDataService`,添加默认系统配置
- 从 `appsettings.json` 读取初始值写入数据库

**6. 前端配置管理页面**
- 创建 React 页面 `src/pages/System/Config/`
- 表单: 每小时任务间隔(分钟), 每日任务小时, 每日任务分钟
- 保存按钮调用 API 更新配置
- 实时反馈配置更新成功

---

### 任务2: HBSalesRecordStatisticsService 查询优化

**1. 修改 ProcessDateRange 方法**
- 添加 `SalesOrderMain` 主表关联查询
- 使用 `LeftJoin` 或 `InnerJoin` 关联主表和明细表

**2. 实现单据类型过滤逻辑**
```csharp
var query = hbSalesContext.Db
    .Queryable<SalesOrderMain>()
    .LeftJoin<SalesOrderDetailRecord>((m, d) => m.B销售单号 == d.B销售单号)
    .Where((m, d) => 
        d.B结账日期.HasValue
        && d.B结账日期.Value >= startDate
        && d.B结账日期.Value <= endDate
        && m.B单据类型 != "2" // 排除挂单
    )
    .Select((m, d) => new { ... })
```

**3. 处理不同单据类型的金额/数量**
- 单据类型 "1", "5": 正常统计 (金额/数量不变)
- 单据类型 "3", "4": 统计为负 (金额/数量取负值)
- 单据类型 "2": 排除不统计

**4. 更新 SalesDataAggregate 聚合逻辑**
- 根据单据类型调整 `TotalAmount` 和 `TotalQuantity`
- 确保 CustomerCount 和 OrderCount 正确计算

---

## 实现步骤

1. **数据库层**
   - 创建 `SysSystemConfig` 实体
   - 更新 `SqlSugarContext` 添加表初始化

2. **后端服务层**
   - 创建 `SystemConfigService`
   - 修改 `ScheduledTaskService` 使用 `IOptionsMonitor`
   - 修改 `HBSalesRecordStatisticsService` 添加主表关联和过滤

3. **后端 API 层**
   - 创建 `SystemConfigController`

4. **数据初始化**
   - 更新 `SeedDataService` 添加默认配置

5. **前端实现**
   - 创建系统配置管理页面

6. **测试验证**
   - 测试配置动态更新是否生效
   - 验证不同单据类型的统计准确性