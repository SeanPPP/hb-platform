# 修复 Hangfire Dashboard 加载问题

## 问题分析
1. 中间件顺序错误：`UseHangfireDashboard` 在 `UseCors` 之前
2. Hangfire Dashboard 缺少 CORS 和 iframe 安全头配置
3. URL 参数传递 token 方式不可靠

## 修复步骤

### 1. 调整后端中间件顺序
- 将 `UseHangfireDashboard` 移到 `UseCors` 之后
- 确保 CORS 中间件先处理跨域请求

### 2. 为 Hangfire Dashboard 添加自定义 CORS 中间件
- 创建专门的中间件为 Hangfire Dashboard 添加 CORS 头
- 设置 `X-Frame-Options: ALLOWALL` 允许 iframe 嵌入
- 设置 `Content-Security-Policy` 允许嵌入

### 3. 优化前端 token 传递方式
- 改用 cookie 方式传递 token（可选，更安全）
- 或者保持当前 URL 参数方式但添加调试日志

### 4. 添加调试日志
- 在 HangfireAuthorizationFilter 中添加日志
- 在前端 iframe 中添加错误捕获

## 涉及文件
- `BlazorApp.Api/Program.cs` - 调整中间件顺序，添加 Hangfire CORS 配置
- `BlazorApp.Api/Hangfire/HangfireAuthorizationFilter.cs` - 添加调试日志
- `ReactUmi/my-app/src/pages/HangfireDashboard/index.tsx` - 改进错误处理和调试
