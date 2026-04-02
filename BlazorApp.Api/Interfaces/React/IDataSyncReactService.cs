using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 数据同步服务接口
    /// 负责将 HQ 数据库中的各类业务数据（商品、价格、仓库、订单等）
    /// 分页读取并批量写入到本地数据库，返回统一的同步结果。
    /// 参数：hqBatchSize 为 HQ 读取批大小；writePageSize 为本地批量写入分页大小。
    /// </summary>
    public interface IDataSyncReactService
    {
        /// <summary>
        /// 全量同步商品（HQ 商品字典 → 本地 Product）。
        /// </summary>
        Task<SyncResult> SyncProductsFromHqAsync();

        /// <summary>
        /// 分店零售价并发同步（HQ 零售价 → 本地 StoreRetailPrice）。
        /// </summary>
        Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(
          List<string>? selectedStoreCodes = null
        );

        /// <summary>
        /// 分店一品多码并发同步（HQ → 本地 StoreMultiCodeProduct）。
        /// </summary>
        Task<SyncResult> SyncStoreMultiCodeProductsFromHqConcurrentAsync(
          List<string>? selectedStoreCodes = null,
          int maxConcurrency = 12,
          int batchSize = 200000
        );

        /// <summary>
        /// 套装多码同步（HQ 一品多码 → 本地 ProductSetCode）。包含校验与详细日志。
        /// </summary>
        Task<SyncResult> SyncProductSetCodesFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000,
          int maxReadConcurrency = 8
        );

        /// <summary>
        /// 分店清货价同步（HQ → 本地 StoreClearancePrice）。全量串行写入。
        /// </summary>
        Task<SyncResult> SyncStoreClearancePricesFromHqConcurrentAsync(
          List<string>? selectedStoreCodes = null,
          int maxConcurrency = 12,
          int batchSize = 200000
        );

        /// <summary>
        /// 国内商品同步（HQ → 本地 DomesticProduct）。
        /// </summary>
        Task<SyncResult> SyncDomesticProductsFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 国内套装同步（HQ → 本地 DomesticSetProduct）。
        /// </summary>
        Task<SyncResult> SyncDomesticSetProductsFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 货号前缀同步（HQ → 本地 ProductPrefixCode）。
        /// </summary>
        Task<SyncResult> SyncProductPrefixCodesFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 国内供应商同步（HQ → 本地 ChinaSupplier）。
        /// </summary>
        Task<SyncResult> SyncChinaSuppliersFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 仓库分类码同步（HQ → 本地 WarehouseCategory）。
        /// </summary>
        Task<SyncResult> SyncWarehouseCategoriesFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 货柜详情同步（HQ → 本地 ContainerDetail，支持主表GUID筛选）。
        /// </summary>
        Task<SyncResult> SyncContainerDetailsFromHqAsync(
          List<string>? masterGuids = null
        );

        /// <summary>
        /// 货柜主表同步（HQ → 本地 Container）。
        /// </summary>
        Task<SyncResult> SyncContainersFromHqAsync(int hqBatchSize = 50000, int writePageSize = 10000);

        /// <summary>
        /// 仓库商品同步（HQ 商品库存 → 本地 WarehouseProduct）。
        /// </summary>
        Task<SyncResult> SyncWarehouseProductsFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 分店本地进货单主表同步（HQ → 本地 StoreLocalSupplierInvoice）。
        /// </summary>
        Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 分店本地进货单详情同步（HQ → 本地 StoreLocalSupplierInvoiceDetails）。并发写入。
        /// </summary>
        Task<SyncResult> SyncStoreLocalSupplierInvoiceDetailsFromHqAsync();

        /// <summary>
        /// 分店本地进货单主+详情顺序同步，返回汇总结果。
        /// </summary>
        Task<SyncResult> SyncStoreLocalSupplierInvoicesAndDetailsFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 分店订货单主表同步（HQ → 本地 WareHouseOrder）。
        /// </summary>
        Task<SyncResult> SyncWareHouseOrdersFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 分店订货单详情同步（HQ → 本地 WareHouseOrderDetails）。并发写入。
        /// </summary>
        Task<SyncResult> SyncWareHouseOrderDetailsFromHqAsync();

        /// <summary>
        /// 分店订货单主+详情顺序同步，返回汇总结果。
        /// </summary>
        Task<SyncResult> SyncWareHouseOrdersAllFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 全量同步货位信息：HQ 货位编码表 → 本地 Location
        /// </summary>
        Task<SyncResult> SyncLocationsFromHqAsync(int hqBatchSize = 50000, int writePageSize = 10000);

        /// <summary>
        /// 全量同步货位商品关联：HQ 存货/配货信息 → 本地 ProductLocation
        /// </summary>
        Task<SyncResult> SyncProductLocationsFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 全量同步收银用户：HQ 收银用户信息表 → 本地 CashRegisterUser
        /// </summary>
        Task<SyncResult> SyncCashRegisterUsersFromHqAsync(
          int hqBatchSize = 50000,
          int writePageSize = 10000
        );

        /// <summary>
        /// 同步商品-供应商映射表：主数据库 → POSM 数据库
        /// </summary>
        Task<SyncResult> SyncPosmProductSupplierMappingsAsync();

        /// <summary>
        /// 增量同步商品-供应商映射表：主数据库 → POSM 数据库
        /// 根据商品的最后更新时间同步变更的数据，适用于每小时执行
        /// </summary>
        Task<SyncResult> SyncPosmProductSupplierMappingsIncrementalAsync();

        /// <summary>
        /// 增量同步分店本地进货单主表：HQ → 本地 StoreLocalSupplierInvoice
        /// 基于最近一次成功同步的时间点，默认100天内
        /// </summary>
        Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqIncrementalAsync();

        /// <summary>
        /// 增量同步货柜主表：HQ → 本地 Container
        /// 基于最近一次成功同步的时间点，默认100天内
        /// </summary>
        Task<SyncResult> SyncContainersFromHqIncrementalAsync();

        /// <summary>
        /// 增量同步货柜详情：HQ → 本地 ContainerDetail
        /// 基于最近一次成功同步的时间点，默认100天内，支持主表GUID筛选
        /// </summary>
        Task<SyncResult> SyncContainerDetailsFromHqIncrementalAsync(
          List<string>? masterGuids = null
        );

        /// <summary>
        /// 增量同步分店订货单主表：HQ → 本地 WareHouseOrder
        /// 基于最近一次成功同步的时间点，默认100天内
        /// </summary>
        Task<SyncResult> SyncWareHouseOrdersFromHqIncrementalAsync();
    }
}
