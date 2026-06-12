using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IDataSyncIncrementalService
    {
        Task<SyncResult> SyncProductsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncStoreRetailPricesFromHqIncrementalAsync(
          List<string>? selectedStoreCodes = null,
          DateTime? startDateFromRequest = null
        );

        Task<SyncResult> SyncStoreMultiCodeProductsFromHqIncrementalAsync(
          List<string>? selectedStoreCodes = null,
          DateTime? startDateFromRequest = null
        );

        Task<SyncResult> SyncProductSetCodesFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncStoreClearancePricesFromHqIncrementalAsync(
          List<string>? selectedStoreCodes = null,
          DateTime? startDateFromRequest = null
        );

        Task<SyncResult> SyncDomesticProductsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncDomesticSetProductsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncProductPrefixCodesFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncChinaSuppliersFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncWarehouseCategoriesFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncContainersFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncContainerDetailsFromHqIncrementalAsync(
          List<string>? masterGuids = null,
          DateTime? startDateFromRequest = null
        );

        Task<SyncResult> SyncWarehouseProductsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncStoreLocalSupplierInvoicesFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncStoreLocalSupplierInvoiceDetailsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncWareHouseOrdersFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncWareHouseOrderDetailsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncLocationsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncProductLocationsFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncCashRegisterUsersFromHqIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncPosmProductSupplierMappingsIncrementalAsync(DateTime? startDateFromRequest = null);

        Task<SyncResult> SyncProductCategoriesFromHqIncrementalAsync(DateTime? startDateFromRequest = null);
    }
}
