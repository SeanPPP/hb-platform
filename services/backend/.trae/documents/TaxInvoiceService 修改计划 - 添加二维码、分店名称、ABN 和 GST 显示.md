## 修改目标
为 TaxInvoiceService.cs 添加以下功能：
1. 添加二维码显示 Order No
2. Branch 字段改为显示分店名称（而非 BranchCode）
3. 在 TAX INVOICE 标题下方显示分店 ABN
4. 显示 GST 金额 (Actual Amount * 10/11)

## 实施步骤

### 步骤 1: 添加 QRCoder NuGet 包
在 `BlazorApp.Api/BlazorApp.Api.csproj` 中添加：
```xml
<PackageReference Include="QRCoder" Version="1.6.0" />
```

### 步骤 2: 修改 TaxInvoiceService.cs
- 添加 `using QRCoder;` 引用
- 注入 `SqlSugarContext`（如果尚未注入）用于查询 Store 表
- 根据 `order.BranchCode` 查询分店信息（Store 表）
- 在 TAX INVOICE 标题下方添加分店 ABN 显示
- 修改 Branch 单元格显示 `store.StoreName` 或 `order.BranchCode`
- 生成 Order No 的二维码并插入到 PDF（右上角或标题旁）
- 在 summaryTable 中添加 GST 显示行：`GST Included: $xxx`
- 添加私有方法 `GenerateQRCodeImage` 用于生成二维码图片

### 步骤 3: 测试验证
- 运行 API 服务
- 测试生成 PDF 功能，确认：
  - 二维码正确显示订单号
  - Branch 显示分店名称
  - ABN 正确显示
  - GST 计算正确（ActualAmount * 10/11）

## 数据来源
使用本地 `Store` 表（通过 `SqlSugarContext.StoreDb`）查询分店名称和 ABN，与 `BranchCode` 对应 `StoreCode`。