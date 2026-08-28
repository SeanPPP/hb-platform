using BlazorApp.Api.Data;
using BlazorApp.Api.Features.DataSync.Common;
using BlazorApp.Api.Features.DataSync.Full.Products;
using BlazorApp.Api.Features.DataSync.Full.Stores;
using BlazorApp.Api.Features.DataSync.Full.Warehouse;
using BlazorApp.Api.Features.DataSync.Incremental;
using BlazorApp.Api.Features.DataSync.Locations;
using BlazorApp.Api.Features.DataSync.Queries;
using BlazorApp.Api.Features.DataSync.ReverseSync;
using BlazorApp.Api.Features.DataSync.Translation;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services;

/// <summary>
/// 兼容旧调用点的数据同步 facade。实际实现按业务切片位于 Features/DataSync，
/// 本类只负责保持公开入口和构造函数兼容。
/// </summary>
public class DataSyncService
{
    private readonly DataSyncLocationsStore _locations;
    private readonly DataSyncMasterDataStore _masterData;
    private readonly DataSyncProductsStore _products;
    private readonly DataSyncWarehouseStore _warehouse;
    private readonly DataSyncIncrementalStore _incremental;
    private readonly DataSyncTranslationStore _translation;
    private readonly DataSyncDomesticProductsStore _domesticProducts;
    private readonly DataSyncContainersStore _containers;
    private readonly DataSyncStorePricesStore _storePrices;
    private readonly DataSyncStorePricesConcurrentStore _concurrentStorePrices;
    private readonly DataSyncStoreProductsStore _storeProducts;
    private readonly DataSyncConnectionQuery _queries;
    private readonly DataSyncReverseSyncStore _reverseSync;

    public DataSyncService(SqlSugarContext localContext, HqSqlSugarContext hqContext, HBSalesSqlSugarContext hbSalesContext, ILogger<DataSyncService> logger, AutoMapper.IMapper mapper, ITranslationService translationService, IConfiguration configuration, IWarehouseProductChangeHistoryService changeHistoryService, ICurrentUserService currentUserService)
    {
        ArgumentNullException.ThrowIfNull(changeHistoryService);
        ArgumentNullException.ThrowIfNull(currentUserService);
        var context = new DataSyncSliceContext(localContext, hqContext, hbSalesContext, logger, mapper, translationService, configuration, changeHistoryService, currentUserService);
        _locations = new(context);
        _masterData = new(context);
        _products = new(context);
        _warehouse = new(context);
        _incremental = new(context);
        _translation = new(context);
        _domesticProducts = new(context);
        _containers = new(context);
        _storePrices = new(context);
        _concurrentStorePrices = new(context);
        _storeProducts = new(context);
        _queries = new(context);
        _reverseSync = new(context);
    }

    public Task<SyncResult> SyncLocationsFromHqAsync() => _locations.SyncLocationsFromHqAsync();
    public Task<SyncResult> SyncProductLocationsFromHqAsync() => _locations.SyncProductLocationsFromHqAsync();
    public Task<SyncResult> SyncSuppliersFromHqAsync() => _masterData.SyncSuppliersFromHqAsync();
    public Task<SyncResult> SyncCategoriesFromHqAsync() => _masterData.SyncCategoriesFromHqAsync();
    public Task<SyncResult> SyncProductsFromHqAsync() => _products.SyncProductsFromHqAsync();
    public Task<SyncResult> SyncProductStocksFromHqAsync() => _warehouse.SyncProductStocksFromHqAsync();
    public Task<List<WarehouseProduct>> ConvertHqStocksToWarehouseProductsAsync(List<string> productCodes) => _warehouse.ConvertHqStocksToWarehouseProductsAsync(productCodes);
    public Task<CPT_DIC_商品信息字典表?> GetProductWithStockInfoAsync(string productCode) => _queries.GetProductWithStockInfoAsync(productCode);
    public Task<List<CBP_DIC_商品库存表>> GetLowStockProductsAsync(decimal minStockThreshold = 100) => _queries.GetLowStockProductsAsync(minStockThreshold);
    public Task<SyncResult> SyncProductsIncrementalFromHqAsync(DateTime lastUpdateDate) => _incremental.SyncProductsIncrementalFromHqAsync(lastUpdateDate);
    public Task<SyncResult> SyncProductStocksIncrementalFromHqAsync(DateTime lastUpdateDate) => _incremental.SyncProductStocksIncrementalFromHqAsync(lastUpdateDate);
    public Task<SyncResult> TranslateAllProductNamesAsync() => _translation.TranslateAllProductNamesAsync();
    public Task<SyncResult> TranslateProductNamesAsync(string mode, string? productCodeFilter = null) => _translation.TranslateProductNamesAsync(mode, productCodeFilter);
    public Task<SyncResult> SyncDomesticProductsFromHqAsync() => _domesticProducts.SyncDomesticProductsFromHqAsync();
    public Task<SyncResult> SyncProductPrefixCodesFromHqAsync() => _domesticProducts.SyncProductPrefixCodesFromHqAsync();
    public Task<SyncResult> SyncDomesticSetProductsFromHqAsync() => _domesticProducts.SyncDomesticSetProductsFromHqAsync();
    public Task<SyncResult> SyncContainersFromHqAsync() => _containers.SyncContainersFromHqAsync();
    public Task<SyncResult> SyncContainersIncrementalFromHqAsync(DateTime lastUpdateDate) => _containers.SyncContainersIncrementalFromHqAsync(lastUpdateDate);
    public Task<SyncResult> SyncStoreRetailPricesFromHqAsync(List<string>? selectedStoreCodes = null) => _storePrices.SyncStoreRetailPricesFromHqAsync(selectedStoreCodes);
    public Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(List<string>? selectedStoreCodes = null, int maxConcurrency = 15, int batchSize = 200000) => _concurrentStorePrices.SyncStoreRetailPricesFromHqConcurrentAsync(selectedStoreCodes, maxConcurrency, batchSize);
    public Task<SyncResult> SyncStoreClearancePricesFromHqAsync(List<string>? selectedStoreCodes = null) => _storeProducts.SyncStoreClearancePricesFromHqAsync(selectedStoreCodes);
    public Task<SyncResult> SyncStoreMultiCodeProductsFromHqAsync(List<string>? selectedStoreCodes = null) => _storeProducts.SyncStoreMultiCodeProductsFromHqAsync(selectedStoreCodes);
    public Task<SyncResult> TestPostgresConnectionAsync() => _queries.TestPostgresConnectionAsync();
    public Task<SyncResult> SyncDomesticProductsToHqAsync(DateTime lastUpdateDate) => _reverseSync.SyncDomesticProductsToHqAsync(lastUpdateDate);
}
