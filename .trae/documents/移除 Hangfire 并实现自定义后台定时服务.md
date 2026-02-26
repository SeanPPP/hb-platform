# 移除 Hangfire 并实现自定义后台定时服务

## 1. 移除 Hangfire NuGet 包
从 `BlazorApp.Api.csproj` 删除：
- Hangfire.AspNetCore
- Hangfire.Core  
- Hangfire.SqlServer

## 2. 创建自定义后台定时服务
创建 `Services/Background/ScheduledTaskService.cs`：
- 实现 `BackgroundService` 基类
- 使用 `System.Threading.Timer` 实现定时任务
- 复现两个定时任务逻辑：
  - 每小时执行 `UpdateCurrentHourStatistics`
  - 每天 3 点执行 `FullRefreshPreviousDay`
- 注入 `SalesStatisticsJobService` 并调用其方法
- 添加配置化的执行间隔和执行时间
- 支持手动触发（通过公共方法）

## 3. 重构 StatisticsJobTriggerController
修改 `Controllers/StatisticsJobTriggerController.cs`：
- 移除 `IBackgroundJobClient` 依赖
- 直接注入 `SalesStatisticsJobService`
- 同步调用统计方法（改为 await）
- 保留原有 API 端点接口

## 4. 修改 Program.cs
- 移除 Hangfire 相关引用和命名空间
- 移除 Hangfire 服务注册（AddHangfire、AddHangfireServer）
- 移除 Hangfire Dashboard 中间件配置（Line 374-456）
- 移除 `app.UseHangfireDashboard`（Line 458-471）
- 移除 `RecurringJob.AddOrUpdate` 注册（Line 540-552）
- 注册 `ScheduledTaskService` 为 `HostedService`
- 移除 Hangfire 授权过滤器相关代码

## 5. 清理和删除文件
- 删除 `Hangfire/HangfireAuthorizationFilter.cs`
- 将 `SalesStatisticsJobService.cs` 从 `Services/Hangfire/` 移动到 `Services/`（或保持原位置但移除 Hangfire 命名空间）

## 6. 更新命名空间和引用
- 修改 `SalesStatisticsJobService.cs` 命名空间为 `BlazorApp.Api.Services`
- 更新所有引用该文件的地方

## 预期效果
- 完全移除 Hangfire 依赖
- 使用 .NET 原生 BackgroundService 实现定时任务
- 保持原有功能不变
- 更轻量、更易维护的解决方案