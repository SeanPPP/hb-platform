## 修改计划

修复任务历史记录页面（ScheduledTaskHistory）无法在tab中打开的问题。

### 需要修改的文件

1. **ReactUmi/my-app/.umirc.ts**
   - 将 `/system/scheduled-task-history` 路由配置改为使用 `KeepAliveTabLayout` 布局
   - 添加子路由配置，指向 `./ScheduledTaskHistory` 组件

2. **ReactUmi/my-app/src/app.tsx**
   - 在 `pathComponentMap` 中添加 `/system/scheduled-task-history` 的路径映射
   - 配置组件名称、图标和 keepAlive 设置

3. **ReactUmi/my-app/src/layouts/KeepAliveTabLayout.tsx**
   - 在 `componentMap` 中添加 `ScheduledTaskHistory` 组件的懒加载映射
   - 确认 `iconMap` 中已有 `HistoryOutlined` 图标映射

### 修改内容

所有修改都是为了保持与其他页面（如 StatisticsJobTrigger）的一致性，确保点击菜单时能在tab中正确打开页面。