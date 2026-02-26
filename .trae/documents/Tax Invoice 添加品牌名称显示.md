## 修改目标
在 Tax Invoice PDF 中显示分店的品牌名称（BrandName）

### 实施步骤

1. **更新日志输出** - 在找到分店时记录 BrandName
   - 位置：[TaxInvoiceService.cs#L76-L78](file:///d:/Development/cline/blazor/BlazorApp.Api/Services/TaxInvoiceService.cs#L76-L78)
   - 添加 `BrandName = {store.BrandName}` 到日志中

2. **添加品牌名称显示** - 在 ABN 显示前添加品牌名称
   - 位置：[TaxInvoiceService.cs#L158](file:///d:/Development/cline/blazor/BlazorApp.Api/Services/TaxInvoiceService.cs#L158)
   - 在 ABN 显示之前插入品牌名称显示代码

### PDF 布局效果
```
┌─────────────────────────────────────┐
│  TAX INVOICE       [二维码]     │
│     Brand: [品牌名称]             │ ← 新增
│     ABN: 12345678901              │
│     Address: 123 Main St, Sydney    │
├─────────────────────────────────────┤
│ Order No: ...     Date: ...         │
│ Branch: 分店名称    Device: ...     │
└─────────────────────────────────────┘
```