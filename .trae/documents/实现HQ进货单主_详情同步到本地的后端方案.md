## 目标
- 实现两个表的后端同步：
  - RED_进货单主表 → StoreLocalSupplierInvoice
  - RED_进货单详情表 → StoreLocalSupplierInvoiceDetails
- 使用 HqSqlSugarContext 作为数据源，支持全量分页拉取与批量写入，事务包裹。

## 实施要点
- 映射 Profiles：
  - ReactStoreLocalSupplierInvoiceProfile：主表字段完整映射与必要的截断/回退（InvoiceGUID ← HGUID，回退生成）。
  - ReactStoreLocalSupplierInvoiceDetailProfile：详情字段完整映射（DetailGUID ← HGUID，InvoiceGUID ← H主表GUID）。
- 服务接口与方法：
  - IDataSyncReactService 新增三方法：
    - SyncStoreLocalSupplierInvoicesFromHqAsync
    - SyncStoreLocalSupplierInvoiceDetailsFromHqAsync
    - SyncStoreLocalSupplierInvoicesAndDetailsFromHqAsync（顺序执行主→详情）
  - DataSyncReactService 实现上述方法：
    - 事务：清空目标表 → HQ 分页查询 → AutoMapper 映射 → Fastest<T>.BulkCopyAsync → 日志汇总。
    - 过滤：主表 `HGUID` 非空，详情 `H主表GUID` 非空，关键字段非空保留。
- 控制器端点：
  - POST /api/react-sync/store-local-supplier-invoices
  - POST /api/react-sync/store-local-supplier-invoice-details
  - POST /api/react-sync/store-local-supplier-invoices-all（顺序同步两表）

## 验证
- 构建通过；调用三个端点分别/整体同步；观察日志新增/错误/耗时。

## 影响
- 仅新增后端代码，不影响既有同步；可后续补充前端按钮。