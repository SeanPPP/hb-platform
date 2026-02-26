## 修改计划

### 1. 修改后端 DTO - `PosmSalesOrderDtos.cs`
为 `PosmSalesOrderDto` 添加可选属性：
- `ABN` - 澳大利亚商业号码
- `BrandName` - 品牌名称

### 2. 修改后端服务 - `PosmSalesOrderReactService.cs`
- 添加 `SqlSugarContext` 依赖注入（用于查询主数据库的 Store 表）
- 在 `GetSalesOrderListAsync` 方法中：
  - 查询完 POSM 数据库订单后，提取所有 `BranchCode`
  - 从主数据库查询对应的 Store 信息（使用异常捕获）
  - 用字典匹配填充 `BranchName`、`ABN`、`BrandName`
- 在 `GetSalesOrderDetailAsync` 方法中：
  - 同样填充订单的 Store 相关信息

### 3. 修改 PDF 生成服务 - `TaxInvoiceService.cs`
在 PDF 中添加 BrandName 显示（在 ABN 附近或 ABN 下方）