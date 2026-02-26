## 修复步骤

修改 [ReactUmi/my-app/src/pages/SalesDashboard/index.tsx](file:///d:/Development/cline/blazor/ReactUmi/my-app/src/pages/SalesDashboard/index.tsx#L64-L70) 中的 `formatDateRange` 函数：

**当前代码**：
```typescript
compareMode: range.compareMode.toUpperCase() as 'ByDate' | 'ByWeek',
```

**修改为**：
```typescript
compareMode: range.compareMode === 'by-date' ? 'ByDate' : 'ByWeek',
```

这将确保前端发送 `'ByDate'` 或 `'ByWeek'`，与后端 `CompareMode` 枚举值匹配，解决 400 验证错误。