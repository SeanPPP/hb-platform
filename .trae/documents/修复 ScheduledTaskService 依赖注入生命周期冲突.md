## 修复 ScheduledTaskService 依赖注入生命周期冲突

### 问题
`ScheduledTaskService` (Singleton) 在构造函数中直接注入了 `SalesStatisticsJobService` (Scoped),违反了依赖注入生命周期规则。

### 解决方案
使用 `IServiceScopeFactory` 在后台任务中手动创建服务作用域来获取 Scoped 服务。

### 修改内容

**文件**: `BlazorApp.Api/Services/Background/ScheduledTaskService.cs`

1. 修改构造函数注入:
   - 移除 `SalesStatisticsJobService` 直接注入
   - 添加 `IServiceScopeFactory` 注入

2. 修改 `ExecuteHourlyTask()` 方法:
   - 使用 `CreateScope()` 创建作用域
   - 在作用域内获取 `SalesStatisticsJobService`
   - 使用完毕后释放作用域

3. 修改 `ExecuteDailyTask()` 方法:
   - 同样使用作用域模式获取服务

### 代码改动
```csharp
// 修改前
private readonly SalesStatisticsJobService _statisticsJobService;

// 修改后  
private readonly IServiceScopeFactory _scopeFactory;

// ExecuteHourlyTask/ExecuteDailyTask 中
using (var scope = _scopeFactory.CreateScope())
{
    var statisticsJobService = scope.ServiceProvider
        .GetRequiredService<SalesStatisticsJobService>();
    await statisticsJobService.UpdateCurrentHourStatistics();
}
```

### 优点
- ✅ 正确管理服务生命周期
- ✅ 数据库连接正确释放
- ✅ 避免内存泄漏
- ✅ 符合 ASP.NET Core 最佳实践