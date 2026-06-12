## 修复计划

**问题**：第 79 行类型错误 - `undefined` 不能赋值给 `DashboardSummaryType | null`

**原因**：`summaryRes.data` 类型是 `DashboardSummary | undefined`，但 state 定义为 `DashboardSummaryType | null`

**解决方案**：使用空值合并运算符 `??` 将 `undefined` 转换为 `null`

**修改内容**：
- 文件：`ReactUmi/my-app/src/pages/SalesDashboard/index.tsx`
- 第 79 行：`setDashboardSummary(summaryRes.data);` → `setDashboardSummary(summaryRes.data ?? null);`

这是最简洁且符合 React 最佳实践的修复方案。