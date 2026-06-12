## 修改 `SalesDashboard/index.tsx`

**修改内容**：
1. 在第6行的 import 语句中添加 `useAccess` hook
2. 在组件内部添加 `const access = useAccess();` 获取权限
3. 将第251行的 `<DashboardSummary />` 改为条件渲染：
   ```tsx
   {(access.isAdmin || access.isWarehouseManager) && (
     <DashboardSummary data={dashboardSummary} loading={loading} />
   )}
   ```

这样，只有管理员和仓库管理员能看到销售汇总统计卡片，其他用户将看不到这个组件。