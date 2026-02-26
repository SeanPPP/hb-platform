## 修复 GetHourlySalesAsync 中的 SQL GROUP BY 错误

### 问题
`GetHourlySalesAsync` 方法中的 SQL 查询出现错误：
- `BranchName` 列在 `SELECT` 中但未包含在 `GROUP BY` 子句中
- 这违反了 SQL 规则：非聚合列必须包含在 GROUP BY 中

### 解决方案
在第200行的 `GROUP BY` 子句中添加 `s.BranchName`：
```csharp
.GroupBy(s => new { s.Hour, s.BranchCode, s.BranchName })
```

### 修改文件
- `BlazorApp.Api/Services/React/SalesDashboardReactService.cs`