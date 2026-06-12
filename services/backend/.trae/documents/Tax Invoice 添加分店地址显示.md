## 修改目标
修正 GST 计算公式从 `* 10 / 11` 改为 `* 10 / 110`

## 实施步骤

### 步骤 1: 修改 GST 计算公式
在 [TaxInvoiceService.cs#L390](file:///d:/Development/cline/blazor/BlazorApp.Api/Services/TaxInvoiceService.cs#L390) 修改：

```csharp
// 从：
var gstAmount = (order.ActualAmount ?? 0) * 10 / 11;

// 改为：
var gstAmount = (order.ActualAmount ?? 0) * 10 / 110;
```

### 步骤 2: 测试验证
运行 `dotnet build` 确保编译成功

## 计算说明
- 澳大利亚 GST 税率：10%
- 含税金额 = 不含税金额 × 1.1
- GST 金额 = 含税金额 × 10 / 110
- 示例：$110.00 × 10 / 110 = $10.00 GST