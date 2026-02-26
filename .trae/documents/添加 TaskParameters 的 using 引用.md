## 修复编译错误

### 问题
`HBSalesRecordStatisticsService.cs` 缺少 `using BlazorApp.Shared.Models.HBweb;` 引用，导致 `TaskParameters` 类型找不到。

### 解决方案
在 `HBSalesRecordStatisticsService.cs` 文件顶部的 using 部分添加：
```csharp
using BlazorApp.Shared.Models.HBweb;
```

这样就可以正确引用 `TaskParameters` 类了。