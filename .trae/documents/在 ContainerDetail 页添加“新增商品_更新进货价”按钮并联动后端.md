## 目标与新增要求
- 在 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\ContainerDetail\index.tsx`：
  - 新增按钮：`添加新商品（数量）`、`更新已有商品（进货价/数量）`
  - 新增列：`是否上架`（开关控件，默认上架）
- 规则补充：英文名称不能为空；若缺失需自动补齐或阻止创建。
- 参考默认上架：`d:\Development\cline\blazor\BlazorApp.Shared\Models\HBweb\WarehouseProduct.cs:66-72`（`IsActive` 默认 `true`）。

## 前端交互与作用域
- 选中优先：有选中行（`selectedRows`）则处理选中；否则处理全部筛选行（`filteredProductDetails`）。
- 批量调用采用分片并发（每批 200、并发 4 组），统一提示与错误收集，参考现有实现 `index.tsx:1339-1364`。

## 新增商品（英文名称必填，自动定价=否，零售价>0）
- 行筛选：`row.是否新商品 === true`。
- 校验：
  - `row.商品信息?.零售价格 > 0`，`row.进口价格 > 0`。
  - 英文名称不能为空：若 `row.商品信息?.英文名称` 为空，先以 `row.商品信息?.商品名称` 调用 `batchTranslate` 自动生成英文名并回填；若仍为空则跳过并提示。
- 调用与映射：
  1) `createProductWithPrices`（`@/services/product.ts`）：
     - `productName`、`itemNumber`、`barcode`
     - `purchasePrice = row.进口价格`、`retailPrice = row.商品信息?.零售价格`
     - `isAutoPricing = false`
  2) `batchCreateProducts`（`@/services/productWarehouse.ts`）：
     - `ChineseName`、`EnglishName`（确保非空）、`ItemNumber/Barcode`
     - `ImportPrice`、`OEMPrice`（若有）、`DomesticPrice`（若有）、`Volume`、`IsSetProduct`
     - `IsActive = true`
  3) `getActiveStores` + `storeRetailPriceService.batchUpsert`：
     - 各分店 `StoreCode`、`ProductCode`、`PurchasePrice = row.进口价格`、`StoreRetailPriceValue = row.商品信息?.零售价格`、`IsAutoPricing=false`、`IsActive=true`。
  4) 多码/套装：
     - 若 `row.商品类型 in {'套装商品','套装子商品'}` 或国内商品为 `SET/MULTICODE`，使用 `@/services/domesticProduct` 获取 `setItems`，并对每个分店 `storeMultiCodePriceService.batchUpsert`：
     - `ProductCode`、`MultiBarcode`（若有）、`PurchasePrice = row.进口价格`、`MultiCodeRetailPrice`（取子项或页面策略）、`IsAutoPricing=false`、`IsActive=true`。
- 结果：统计成功/失败/跳过数量，提示后刷新 `loadProductDetails()`。

## 更新已有商品（当进货价不同）
- 行筛选：`row.是否新商品 !== true` 且具备 `ProductCode`。
- 差异检测：`detectProducts` 拉取 `WarehouseImportPrice`，与 `row.进口价格` 比较。
- 更新顺序：
  1) `batchUpdateWarehouseProducts`：`{ ProductCode, ImportPrice, IsActive = true }`。
  2) `updateProduct(productCode, { purchasePrice: row.进口价格 })`。
  3) `getActiveStores` + `storeRetailPriceService.batchUpsert`：更新各分店 `PurchasePrice = row.进口价格`，不覆盖原 `StoreRetailPriceValue`。
  4) 若多码/套装：`storeMultiCodePriceService.batchUpsert` 同步多码 `PurchasePrice`，保留 `MultiCodeRetailPrice`。
- 分片并发与统一提示与错误处理，同新增。

## 新增列：是否上架（开关控件）
- 在 `columns` 中增加一列：
  - 名称：`是否上架`
  - 渲染：使用 `antd Switch`，默认 `checked = true`（与 `WarehouseProduct.IsActive` 默认一致）。
  - 值来源：若行无仓库状态，默认显示启用；切换时针对该行 `ProductCode` 调用：
    - 单行：`bulkSetStatus([productCode], isActive)`（`@/services/productWarehouse.ts`），成功后更新本地行状态；必要时也可用 `batchUpdateWarehouseProducts([{ ProductCode, IsActive }])`。
  - 批量：保留已选中行后续支持批量上下架（可在按钮区域加“批量上/下架”）。

## 字段与规则
- 英文名称：新增时必填，缺失则先翻译自动补齐；翻译失败则跳过。
- 自动定价：新增商品与分店价统一 `IsAutoPricing = false`。
- 上架状态：新增与更新均默认 `IsActive = true`；用户可通过新列开关即时切换。

## 验证与回滚
- 全流程提示：插入/更新/失败/跳过统计；错误首条展示并记录。
- 完成后刷新明细，更新本地基线；失败行保持原值。

## 代码参考
- 批量保存与分片：`ReactUmi/my-app/src/pages/ContainerDetail/index.tsx:1339-1364`
- 批量更新明细：`index.tsx:224-247`、`index.tsx:901-943`
- 上架默认值：`BlazorApp.Shared/Models/HBweb/WarehouseProduct.cs:66-72`
- 相关服务：
  - 商品：`ReactUmi/my-app/src/services/product.ts`
  - 仓库：`ReactUmi/my-app/src/services/productWarehouse.ts`
  - 分店零售价：`ReactUmi/my-app/src/services/storeRetailPriceService.ts`
  - 分店多码价：`ReactUmi/my-app/src/services/storeMultiCodePriceService.ts`
  - 国内商品/套装：`ReactUmi/my-app/src/services/domesticProduct.ts`

请确认此更新后的方案，确认后我将实现按钮、列与服务接入，并进行端到端验证。