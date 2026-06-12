## 修复 Hangfire Dashboard 加载问题

### 1. 前端改进
- 添加 `message` 事件监听器，接收来自 Hangfire Dashboard 的授权状态消息
- 添加超时机制（10秒），如果未收到成功消息则视为加载失败
- 改进错误提示信息

### 2. 后端改进
- 创建 Hangfire Dashboard 的自定义 HTML 模板
- 在模板中注入 JavaScript 代码，授权成功时向父窗口发送 `postMessage`
- 改进 `HangfireAuthorizationFilter` 的日志记录
- 添加授权成功和失败的日志输出

### 文件修改
- `ReactUmi/my-app/src/pages/hangfiredashboard/index.tsx` - 添加 postMessage 监听和超时处理
- `BlazorApp.Api/Program.cs` - 配置 Hangfire Dashboard 使用自定义模板
- 新建 `BlazorApp.Api/Hangfire/DashboardTemplate.cshtml` - 自定义 Dashboard HTML 模板
- `BlazorApp.Api/Hangfire/HangfireAuthorizationFilter.cs` - 改进日志记录