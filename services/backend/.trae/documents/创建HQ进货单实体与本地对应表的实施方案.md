## 问题与目标
- 在 HQ 数据源中新增两个实体：`RED_进货单主表`、`RED_进货单详情表`（字段按您列出的清单）。
- 在本地库新增对应业务表：`StoreLocalSupplierInvoice`、`StoreLocalSupplierInvoiceDetails`。
- 在 `HqSqlSugarContext` 暴露这两个 HQ 实体的 `SimpleClient`，在 `SqlSugarContext` 暴露本地两个表的 `SimpleClient` 并纳入 CodeFirst 创建列表。

## 数据模型设计
### HQ 实体（BlazorApp.Shared/Models/HqEntities）
- `RED_进货单主表Store`
  - `ID:int(Identity)`、`HGUID:string?`、`APPGUID:string?`、`PCGUID:string?`
  - `H分店代码:string?`、`H供应商编码:string?`、`H随货同行单号:string?`
  - `H单据类型:int?`、`H订单日期:DateTime?`、`H入库日期:DateTime?`
  - `H总金额:decimal?`、`H收货总金额:decimal?`、`H单据图片:string?`、`H备注:string?`、`H导入模板:string?`
  - `H流程状态:int?`、`H入库状态:int?`
  - `FGC_Creator:string?`、`FGC_CreateDate:DateTime?`、`FGC_LastModifier:string?`、`FGC_LastModifyDate:DateTime?`、`FGC_UpdateHelp:string?`
- `RED_进货单详情表Store`
  - `ID:int(Identity)`、`HGUID:string?`、`H主表GUID:string?`
  - `H分店代码:string?`、`H商品标签GUID:string?`、`H商品分类码GUID:string?`、`H供应商编码:string?`
  - `H分店商品编码:string?`、`H商品编码:string?`、`H货号:string?`、`H主条形码:string?`
  - `H商品名称:string?`、`H规格:string?`、`H单位:string?`
  - `H数量:decimal?`、`H上次进货价:decimal?`、`H进货价:decimal?`、`H零售价:decimal?`、`H合计金额:decimal?`
  - `H已存在商品数:int?`、`H商品图片:string?`
  - `H活动类型:int?`、`H折扣率:decimal?`、`H是否自动定价:bool?`、`H定价浮率:decimal?`、`H新自动零售价:decimal?`
  - `H是否特殊商品:bool?`、`H老库分店商品编码:string?`
  - `FGC_Creator:string?`、`FGC_CreateDate:DateTime?`、`FGC_LastModifier:string?`、`FGC_LastModifyDate:DateTime?`、`FGC_UpdateHelp:string?`

### 本地表（BlazorApp.Shared/Models/HBweb）
- `StoreLocalSupplierInvoice`
  - 主键：`InvoiceGUID:string`（从 HQ 的 `HGUID` 写入），其余字段按主表映射，保留业务含义（如 `StoreCode`、`SupplierCode`、`VoucherType`、`OrderDate`、`InboundDate`、金额与状态字段等）。
- `StoreLocalSupplierInvoiceDetails`
  - 主键：`DetailGUID:string`，外键关联 `InvoiceGUID`；其余字段按详情表映射（如 `ProductCode`、`ItemNumber`、`Barcode`、`Quantity`、`PurchasePrice`、`RetailPrice`、`Amount`、`Flags` 等）。
- 两表继承现有 `BaseEntity`（与其它 HBweb 表保持一致）。

## 上下文与表创建
- `HqSqlSugarContext`：新增 `SimpleClient<RED_进货单主表Store>` 与 `SimpleClient<RED_进货单详情表Store>`。
- `SqlSugarContext`：
  - 新增 `SimpleClient<StoreLocalSupplierInvoice>` 与 `SimpleClient<StoreLocalSupplierInvoiceDetails>`。
  - 在 `InitializeTablesIfNeeded` 的 `tableTypes` 数组中追加这两类，以便 CodeFirst 自动创建/更新本地表结构。

## 验证
- 构建后运行应用启动流程，观察数据库 CodeFirst 创建日志，确认两张本地表被创建。
- 可选：在 HQ 上用 `CountAsync()` 测试两 HQ 表是否能正常访问（仅开发验证）。

## 后续（可选）
- 若需要同步与映射：后续增加 AutoMapper Profile 与同步服务方法，将 HQ 进货单数据落库到本地对应表，并在前端添加触发按钮。

请确认以上方案，确认后我将开始实现以上实体与上下文改动并完成验证。