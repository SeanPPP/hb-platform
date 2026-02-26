# 前端集成 Hangfire 仪表盘实施计划

## 概述
在 React 前端集成后端的 Hangfire 仪表盘，允许管理员通过 iframe 访问和管理后台任务。

## 实施步骤

### 1. 创建 Hangfire 仪表盘页面
- 路径: `ReactUmi/my-app/src/pages/HangfireDashboard/index.tsx`
- 使用 iframe 嵌入后端 `/hangfire` 路径
- 自动携带当前用户的 JWT 令牌
- 添加页面标题和说明

### 2. 配置路由
- 在 `.umirc.ts` 的系统设置路由下添加 Hangfire 仪表盘路由
- 路径: `/system/hangfire-dashboard`
- 图标: `DashboardOutlined`
- 权限: `isAdmin`

### 3. 添加国际化翻译
- 在 `locales/zh-CN.ts` 添加菜单项翻译
- 在 `locales/en-US.ts` 添加英文翻译

### 4. 更新权限系统
- 在 `access.ts` 中确认 `isAdmin` 权限已正确配置
- 确保只有 Admin 角色用户可以访问

### 5. 更新 app.tsx 路由映射
- 在 `menuItemRender` 的路径组件映射中添加 Hangfire 仪表盘

## 技术要点

- **认证处理**: iframe 需要携带 JWT 令牌，通过 URL 参数或 cookie 方式传递
- **安全性**: 严格控制访问权限，仅 Admin 角色可访问
- **CORS**: 确保 Hangfire 路径允许前端 iframe 嵌入

## 涉及文件
1. `ReactUmi/my-app/src/pages/HangfireDashboard/index.tsx` (新建)
2. `ReactUmi/my-app/.umirc.ts` (修改)
3. `ReactUmi/my-app/src/locales/zh-CN.ts` (修改)
4. `ReactUmi/my-app/src/locales/en-US.ts` (修改)
5. `ReactUmi/my-app/src/app.tsx` (修改)
