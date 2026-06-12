# 修复 Hangfire Dashboard 路由配置

## 问题分析
Hangfire 仪表盘路由配置不正确，导致 Umi 无法找到组件。当前配置是独立路由，但应该作为系统设置的子路由。

## 修复步骤

### 1. 修复 .umirc.ts 路由配置
- 将 Hangfire 仪表盘从独立路由移除
- 添加到系统设置的 `routes` 数组中
- 设置 `component: './HangfireDashboard'`

### 2. 更新 app.tsx 路径组件映射
- 确认 `/system/hangfire-dashboard` 映射已存在

## 涉及文件
- `ReactUmi/my-app/.umirc.ts` - 修复路由结构
