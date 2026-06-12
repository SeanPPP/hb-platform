## 总体策略
- 数据量 ≤10 万，采用“全量分页读取 + 单事务批量写入”，默认 `hqBatchSize=50,000`、`writePageSize=10,000`
- 不用并发；每表独立事务：`BeginTran → 清理 → 分页拉取 → AutoMapper 映射 → Fastest<T>.BulkCopy(PageSize) → CommitTran`；失败回滚
- 统一返回 `ApiResponse<SyncResult>`：`isSuccess/message/addedCount/errorCount/start/end/duration`
- 统一日志：每批拉取 `页/条数/耗时`；插入失败 `批大小/异常信息`

## 后端实现
### 控制器端点（DataSyncReactController）
- `POST /api/react-sync/domestic-products`
- `POST /api/react-sync/domestic-set-products`
- `POST /api/react-sync/product-prefix-codes`
- `POST /api/react-sync/container-details`（可选 `selectedMasterGuids?: string[]` 过滤主表 GUID）
- `POST /api/react-sync/china-suppliers`
- `POST /api/react-sync/warehouse-categories`

### 服务接口（IDataSyncReactService）方法
- `SyncDomesticProductsFromHqAsync(hqBatchSize=50000, writePageSize=10000)`
- `SyncDomesticSetProductsFromHqAsync(hqBatchSize=50000, writePageSize=10000)`
- `SyncProductPrefixCodesFromHqAsync(hqBatchSize=50000, writePageSize=10000)`
- `SyncContainerDetailsFromHqAsync(List<string>? masterGuids=null, hqBatchSize=50000, writePageSize=10000)`
- `SyncChinaSuppliersFromHqAsync(hqBatchSize=50000, writePageSize=10000)`
- `SyncWarehouseCategoriesFromHqAsync(hqBatchSize=50000, writePageSize=10000)`

### 服务实现（DataSyncReactService）与过滤
- HQ 过滤统一：必备主键/关键字段非空，启用状态（如有）；字符串 `Trim + 截断`；必要字段回退
- 写入表前先清空对应目标表（或按筛选条件清理），保持“全量重建”一致性

### React 专用映射 Profile（BlazorApp.Api/Mappings/Profiles/React）
- `ReactDomesticProductProfile`（CPT_DIC_商品信息字典表 → DomesticProduct）
  - `ProductCode ← 商品编码`（为空则 UUID7）
  - `SupplierCode ← 供应商编码`；`ProductName ← 中文名称`；`EnglishProductName ← 英文名称`
  - `HBProductNo ← HB货号`；`Barcode ← 条形码`
  - `ProductType ← 商品类型`；`DomesticPrice/ImportPrice/OEMPrice`
  - `PackingQuantity ← 单件装箱数`；`UnitVolume ← 单件体积`；`MiddlePackQuantity ← 中包数量`
  - `ProductImage ← 商品图片`；`IsActive ← 使用状态==1`
- `ReactDomesticSetProductProfile`（CPT_DIC_商品套装信息表 → DomesticSetProduct）
  - `ProductCode ← 商品编码`
  - `SetProductNo ← 商品小货号`（为空则回退 `条形码` → `商品编码`）
  - `SetBarcode ← 条形码`；`DomesticPrice/ImportPrice/OEMPrice`；`Remarks ← 备注`
- `ReactProductPrefixCodeProfile`（CPT_DIC_货号前缀信息表 → ProductPrefixCode）
  - `SupplierCode ← 供应商编码`；`PrefixName ← HB货号前缀码`；`PrefixDescription ← 前缀描述`
- `ReactChinaSupplierProfile`（CBP_DIC_国内供应商信息表 → ChinaSupplier）
  - `SupplierCode/Name/ShopNumber/ContactPerson/Phone/Email/StorefrontPhoto/Remarks/Status`
  - FGC 字段映射到 `CreatedBy/CreatedAt/UpdatedBy/UpdatedAt`（若存在对应字段）
- `ReactWarehouseCategoryProfile`（CBP_DIC_商品分类码表 → WarehouseCategory）
  - `CategoryGUID ← HGUID`（为空则 UUID）；`ParentGUID ← H父级GUID`
  - `CategoryName ← H类别名称`；`ChineseName ← H中文名称`；`IsActive ← true`
- `ReactContainerDetailProfile`（CPT_RED_货柜单详情表 → ContainerDetail）
  - `DetailCode ← UUID7`；`ContainerCode ← 主表GUID`；`ProductCode ← 商品编码`
  - `LoadingType ← 装柜类型`；`MixedGroupCode ← 混装GUID`；`ProductType ← 商品类型`
  - 数量/价格/体积/成本/状态/备注对应映射
  - 额外：以 `主表GUID` 分组生成 `Container` 若本地缺失（最小字段：`ContainerCode`，其余允许空），在同事务内先清空后重建

## 前端集成（紧凑布局）
- 在现有“数据同步”页继续新增 6 张小卡片与按钮（已做紧凑化样式），每卡片仅保留一句说明与一个主按钮；`ContainerDetail` 可选主表 GUID（多选）
- 服务层新增对应方法，默认超时 10–20 分钟；错误分支返回后端 `ApiResponse` 并在卡片底部显示摘要

## 验证
- 逐项执行 6 个同步：观察新增/错误条数与耗时；检查日志中每批拉取/插入统计
- 特别验证 `ContainerDetail` 与衍生 `Container` 的数量与关联关系正确

## 交付
- 控制器 6 个端点 + 服务接口 6 方法 + 服务实现逻辑 + 6 个映射 Profile
- 前端服务方法 + 紧凑页面卡片与交互更新

如确认，我将按此方案开始编码与集成，并在完成后提供编译与运行验证说明。