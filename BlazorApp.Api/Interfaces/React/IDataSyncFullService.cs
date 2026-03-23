using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IDataSyncFullService
    {
        Task<SyncResult> SyncProductsFromHqAsync();

        Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int? maxConcurrency = null,
            int? batchSize = null
        );

        Task<SyncResult> SyncStoreMultiCodeProductsFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int? maxConcurrency = null,
            int? batchSize = null
        );

        Task<SyncResult> SyncProductSetCodesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000,
            int maxReadConcurrency = 8
        );

        Task<SyncResult> SyncStoreClearancePricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int? maxConcurrency = null,
            int? batchSize = null
        );

        Task<SyncResult> SyncDomesticProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncDomesticSetProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncProductPrefixCodesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncChinaSuppliersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncWarehouseCategoriesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncContainerDetailsFromHqAsync(
            List<string>? masterGuids = null,
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncContainersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncWarehouseProductsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncStoreLocalSupplierInvoiceDetailsFromHqAsync(
            int hqBatchSize = 100000,
            int writePageSize = 50000
        );

        Task<SyncResult> SyncStoreLocalSupplierInvoicesAndDetailsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncWareHouseOrdersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncWareHouseOrderDetailsFromHqAsync(
            int hqBatchSize = 100000,
            int writePageSize = 50000
        );

        Task<SyncResult> SyncWareHouseOrdersAllFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncLocationsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncProductLocationsFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncCashRegisterUsersFromHqAsync(
            int hqBatchSize = 50000,
            int writePageSize = 10000
        );

        Task<SyncResult> SyncPosmProductSupplierMappingsAsync();
    }
}
