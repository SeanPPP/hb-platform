using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// React数据同步门面服务
    /// 负责协调全量同步和增量同步服务，提供统一的数据同步入口
    /// </summary>
    public class DataSyncReactService : IDataSyncReactService
    {
        private readonly IDataSyncFullService _fullSyncService; // 全量同步服务
        private readonly IDataSyncIncrementalService _incrementalSyncService; // 增量同步服务
        private readonly IContainerHqSyncService _containerHqSyncService; // 货柜 HQ 同步核心
        private readonly ILogger<DataSyncReactService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public DataSyncReactService(
            IDataSyncFullService fullSyncService,
            IDataSyncIncrementalService incrementalSyncService,
            IContainerHqSyncService containerHqSyncService,
            ILogger<DataSyncReactService> logger
        )
        {
            _fullSyncService = fullSyncService;
            _incrementalSyncService = incrementalSyncService;
            _containerHqSyncService = containerHqSyncService;
            _logger = logger;
        }

        /// <summary>
        /// 全量同步：商品信息
        /// 数据源：DIC_商品信息字典表 → 目标：Product
        /// </summary>
        public async Task<SyncResult> SyncProductsFromHqAsync()
        {
            return await _fullSyncService.SyncProductsFromHqAsync();
        }

        /// <summary>
        /// 全量同步：分店零售价（并发）
        /// 数据源：DIC_商品零售价表 → 目标：StoreRetailPrice
        /// 支持按分店筛选：selectedStoreCodes
        /// </summary>
        public async Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            return await _fullSyncService.SyncStoreRetailPricesFromHqConcurrentAsync(
                selectedStoreCodes
            );
        }

        /// <summary>
        /// 全量同步：一品多码（并发）
        /// 数据源：DIC_分店一品多码表 → 目标：StoreMultiCodeProduct
        /// 支持按分店筛选：selectedStoreCodes
        /// </summary>
        public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int maxConcurrency = 12,
            int batchSize = 200000
        )
        {
            return await _fullSyncService.SyncStoreMultiCodeProductsFromHqConcurrentAsync(
                selectedStoreCodes,
                maxConcurrency,
                batchSize
            );
        }

        /// <summary>
        /// 全量同步：套装码
        /// 数据源：DIC_一品多码表 → 目标：ProductSetCode
        /// </summary>
        public async Task<SyncResult> SyncProductSetCodesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000,
            int maxReadConcurrency = 8
        )
        {
            return await _fullSyncService.SyncProductSetCodesFromHqAsync(
                hqBatchSize,
                writePageSize,
                maxReadConcurrency
            );
        }

        /// <summary>
        /// 全量同步：分店清货价（并发）
        /// 数据源：DIC_商品清货价表 → 目标：StoreClearancePrice
        /// 支持按分店筛选：selectedStoreCodes
        /// </summary>
        public async Task<SyncResult> SyncStoreClearancePricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int maxConcurrency = 12,
            int batchSize = 200000
        )
        {
            return await _fullSyncService.SyncStoreClearancePricesFromHqConcurrentAsync(
                selectedStoreCodes,
                maxConcurrency,
                batchSize
            );
        }

        /// <summary>
        /// 全量同步：国货商品
        /// 数据源：CPT_DIC_商品信息字典表 → 目标：DomesticProduct
        /// </summary>
        public async Task<SyncResult> SyncDomesticProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncDomesticProductsFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：国货套装
        /// 数据源：CPT_DIC_商品套装信息表 → 目标：DomesticSetProduct
        /// </summary>
        public async Task<SyncResult> SyncDomesticSetProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncDomesticSetProductsFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：货号前缀码
        /// 数据源：CPT_DIC_货号前缀信息表 → 目标：ProductPrefixCode
        /// </summary>
        public async Task<SyncResult> SyncProductPrefixCodesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncProductPrefixCodesFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：国内供应商
        /// 数据源：CBP_DIC_国内供应商信息表 → 目标：ChinaSupplier
        /// </summary>
        public async Task<SyncResult> SyncChinaSuppliersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncChinaSuppliersFromHqAsync(hqBatchSize, writePageSize);
        }

        /// <summary>
        /// 全量同步：仓库分类
        /// 数据源：CBP_DIC_商品分类码表 → 目标：WarehouseCategory
        /// </summary>
        public async Task<SyncResult> SyncWarehouseCategoriesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncWarehouseCategoriesFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：货柜详情
        /// 数据源：CPT_RED_货柜单详情表 → 目标：ContainerDetail
        /// 支持按主表GUID筛选：masterGuids
        /// </summary>
        public async Task<SyncResult> SyncContainerDetailsFromHqAsync(
            List<string>? masterGuids = null
        )
        {
            return await _fullSyncService.SyncContainerDetailsFromHqAsync(masterGuids);
        }

        /// <summary>
        /// 全量同步：货柜
        /// 数据源：CPT_RED_货柜单主表 → 目标：Container
        /// </summary>
        public async Task<SyncResult> SyncContainersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncContainersFromHqAsync(hqBatchSize, writePageSize);
        }

        /// <summary>
        /// 全量同步：仓库商品
        /// 数据源：CBP_DIC_商品库存表 → 目标：WarehouseProduct
        /// </summary>
        public async Task<SyncResult> SyncWarehouseProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncWarehouseProductsFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：分店供应商发票
        /// 数据源：RED_进货单主表 → 目标：StoreLocalSupplierInvoice
        /// </summary>
        public async Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncStoreLocalSupplierInvoicesFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：分店供应商发票详情
        /// 数据源：RED_进货单详情表 → 目标：StoreLocalSupplierInvoiceDetails
        /// </summary>
        public async Task<SyncResult> SyncStoreLocalSupplierInvoiceDetailsFromHqAsync()
        {
            return await _fullSyncService.SyncStoreLocalSupplierInvoiceDetailsFromHqAsync();
        }

        /// <summary>
        /// 全量同步：分店供应商发票及详情（统一同步）
        /// 数据源：RED_进货单主表 + RED_进货单详情表 → 目标：StoreLocalSupplierInvoice + StoreLocalSupplierInvoiceDetails
        /// </summary>
        public async Task<SyncResult> SyncStoreLocalSupplierInvoicesAndDetailsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncStoreLocalSupplierInvoicesAndDetailsFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：仓库订单
        /// 数据源：CBP_RED_分店订货单主表 → 目标：WareHouseOrder
        /// </summary>
        public async Task<SyncResult> SyncWareHouseOrdersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncWareHouseOrdersFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：仓库订单详情
        /// 数据源：CBP_RED_分店订单详情表 → 目标：WareHouseOrderDetails
        /// </summary>
        public async Task<SyncResult> SyncWareHouseOrderDetailsFromHqAsync()
        {
            return await _fullSyncService.SyncWareHouseOrderDetailsFromHqAsync();
        }

        /// <summary>
        /// 全量同步：仓库全部订单（主表+详情）
        /// 数据源：CBP_RED_分店订货单主表 + CBP_RED_分店订单详情表 → 目标：WareHouseOrder + WareHouseOrderDetails
        /// </summary>
        public async Task<SyncResult> SyncWareHouseOrdersAllFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncWareHouseOrdersAllFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：库位
        /// 数据源：CPT_DIC_货位编码信息表 → 目标：Location
        /// </summary>
        public async Task<SyncResult> SyncLocationsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncLocationsFromHqAsync(hqBatchSize, writePageSize);
        }

        /// <summary>
        /// 全量同步：商品库位
        /// 数据源：CPT_RED_货位存货信息表 → 目标：ProductLocation
        /// </summary>
        public async Task<SyncResult> SyncProductLocationsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncProductLocationsFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：收银用户
        /// 数据源：DIC_收银用户信息表 → 目标：CashRegisterUser
        /// </summary>
        public async Task<SyncResult> SyncCashRegisterUsersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncCashRegisterUsersFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        /// <summary>
        /// 全量同步：POSM商品供应商映射
        /// 数据源：PosmProductSupplierMapping → 目标：PosmProductSupplierMapping
        /// </summary>
        public async Task<SyncResult> SyncPosmProductSupplierMappingsAsync()
        {
            return await _fullSyncService.SyncPosmProductSupplierMappingsAsync();
        }

        /// <summary>
        /// 增量同步：POSM商品供应商映射
        /// 数据源：PosmProductSupplierMapping → 目标：PosmProductSupplierMapping
        /// 按 FGC_LastModifyDate 字段增量同步
        /// </summary>
        public async Task<SyncResult> SyncPosmProductSupplierMappingsIncrementalAsync()
        {
            return await _incrementalSyncService.SyncPosmProductSupplierMappingsIncrementalAsync();
        }

        /// <summary>
        /// 增量同步：分店供应商发票
        /// 数据源：RED_进货单主表 → 目标：StoreLocalSupplierInvoice
        /// 按 FGC_LastModifyDate 字段增量同步
        /// </summary>
        public async Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqIncrementalAsync()
        {
            return await _incrementalSyncService.SyncStoreLocalSupplierInvoicesFromHqIncrementalAsync();
        }

        /// <summary>
        /// 货柜主表+明细同步组合门面。
        /// 后台同步入口统一委托货柜 HQ 同步核心，避免前后端链路各自实现。
        /// </summary>
        public async Task<SyncResult> SyncContainersWithDetailsFromHqAsync(
            DateTime? startDate = null
        )
        {
            _logger.LogInformation("[ContainerSyncFacade] 开始同步货柜主表和明细，开始日期: {StartDate}", startDate);
            return await _containerHqSyncService.SyncIncrementalAsync(startDate);
        }

        /// <summary>
        /// 增量同步：货柜
        /// 数据源：CPT_RED_货柜单主表 → 目标：Container
        /// 按 FGC_LastModifyDate 字段增量同步
        /// </summary>
        public async Task<SyncResult> SyncContainersFromHqIncrementalAsync()
        {
            return await _incrementalSyncService.SyncContainersFromHqIncrementalAsync();
        }

        /// <summary>
        /// 增量同步：货柜详情
        /// 数据源：CPT_RED_货柜单详情表 → 目标：ContainerDetail
        /// 支持按主表GUID筛选：masterGuids
        /// 按 FGC_LastModifyDate 字段增量同步
        /// </summary>
        public async Task<SyncResult> SyncContainerDetailsFromHqIncrementalAsync(
            List<string>? masterGuids = null
        )
        {
            return await _incrementalSyncService.SyncContainerDetailsFromHqIncrementalAsync(
                masterGuids
            );
        }

        /// <summary>
        /// 增量同步：仓库订单
        /// 数据源：CBP_RED_分店订货单主表 → 目标：WareHouseOrder
        /// 按 FGC_LastModifyDate 字段增量同步
        /// </summary>
        public async Task<SyncResult> SyncWareHouseOrdersFromHqIncrementalAsync()
        {
            return await _incrementalSyncService.SyncWareHouseOrdersFromHqIncrementalAsync();
        }

        public async Task<SyncResult> SyncProductCategoriesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        )
        {
            return await _fullSyncService.SyncProductCategoriesFromHqAsync(
                hqBatchSize,
                writePageSize
            );
        }

        public async Task<SyncResult> SyncProductCategoriesFromHqIncrementalAsync()
        {
            return await _incrementalSyncService.SyncProductCategoriesFromHqIncrementalAsync();
        }
    }
}
