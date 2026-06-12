using BlazorApp.Api.Data;
using BlazorApp.Shared;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public class ProductStoreSyncService : IProductStoreSyncService
    {
        private readonly SqlSugarContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProductStoreSyncService> _logger;

        public ProductStoreSyncService(
            SqlSugarContext db,
            IConfiguration configuration,
            ILogger<ProductStoreSyncService> logger
        )
        {
            _db = db;
            _configuration = configuration;
            _logger = logger;
        }

        private ISqlSugarClient CreateIndependentConnection()
        {
            return SqlSugarContext.CreateConcurrentConnection(_configuration);
        }

        /// <summary>
        /// 同步商品到分店
        /// 将选中的商品（进货价、零售价、是否自动定价、是否特殊商品、折扣率）同步到指定分店
        /// 同时同步 StoreMultiCodeProduct 和 StoreRetailPrice 两张表
        /// StoreMultiCodeProduct 数据来自 ProductSetCode（产品套装多码表）
        /// StoreRetailPrice 数据来自 Product（产品表）
        /// 如果目标分店不存在该商品记录，则创建包含所有字段的新记录
        /// 如果目标分店已存在该商品记录，则只更新选中的字段
        /// </summary>
        /// <param name="request">同步请求参数</param>
        /// <returns>同步结果</returns>
        public async Task<ApiResponse<SyncProductsToStoresResult>> SyncProductsToStoresAsync(
            SyncProductsToStoresRequest request
        )
        {
            try
            {
                // 关键位置：兼容前端只传 fields 的新协议，避免 bool 默认值导致误同步。
                request.NormalizeFieldSelection();

                if (request.ProductCodes == null || request.ProductCodes.Count == 0)
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "商品编码列表不能为空",
                        "VALIDATION_ERROR"
                    );
                }

                if (request.StoreCodes == null || request.StoreCodes.Count == 0)
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "目标分店编码列表不能为空",
                        "VALIDATION_ERROR"
                    );
                }

                if (
                    !request.SyncPurchasePrice
                    && !request.SyncRetailPrice
                    && !request.SyncIsAutoPricing
                    && !request.SyncIsSpecialProduct
                    && !request.SyncDiscountRate
                )
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "请至少选择一个要同步的字段",
                        "VALIDATION_ERROR"
                    );
                }

                var result = new SyncProductsToStoresResult
                {
                    TotalProducts = request.ProductCodes.Count,
                    TotalStores = request.StoreCodes.Count,
                };

                var productDb = _db.ProductDb;
                var productSetCodeDb = _db.ProductSetCodeDb;

                var products = await productDb
                    .AsQueryable()
                    .Where(p => request.ProductCodes.Contains(p.ProductCode))
                    .Where(p => p.IsDeleted == false)
                    .ToListAsync();

                if (products.Count == 0)
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "未找到有效的商品",
                        "NOT_FOUND"
                    );
                }

                var productCodes = products.Select(p => p.ProductCode).ToList();

                var productsNeedMultiCodeSync = products
                    .Where(p => p.ProductType != null && p.ProductType != 0)
                    .ToList();

                List<ProductSetCode> productSetCodes = new();
                if (productsNeedMultiCodeSync.Count > 0)
                {
                    var productCodesNeedMultiCodeSync = productsNeedMultiCodeSync
                        .Select(p => p.ProductCode)
                        .ToList();

                    productSetCodes = await productSetCodeDb
                        .AsQueryable()
                        .Where(p => productCodesNeedMultiCodeSync.Contains(p.ProductCode))
                        .Where(p => p.IsActive == true && p.IsDeleted == false)
                        .ToListAsync();
                }

                _logger.LogInformation(
                    "开始并发同步 {ProductCount} 个商品到 {StoreCount} 个分店",
                    products.Count,
                    request.StoreCodes.Count
                );

                var syncTasks = request.StoreCodes
                    .Select<string, Func<Task<StoreSyncResult>>>(storeCode =>
                        () => SyncToSingleStoreAsync(request, storeCode, products, productSetCodes)
                    )
                    .ToList();

                var storeResults = await RunStoreSyncTasksAsync(syncTasks);

                foreach (var storeResult in storeResults)
                {
                    if (storeResult.Success)
                    {
                        result.CreatedCount += storeResult.CreatedCount;
                        result.UpdatedCount += storeResult.UpdatedCount;
                        result.StoreMultiCodeProductCreatedCount += storeResult.StoreMultiCodeProductCreatedCount;
                        result.StoreMultiCodeProductUpdatedCount += storeResult.StoreMultiCodeProductUpdatedCount;
                        result.StoreRetailPriceCreatedCount += storeResult.StoreRetailPriceCreatedCount;
                        result.StoreRetailPriceUpdatedCount += storeResult.StoreRetailPriceUpdatedCount;
                    }
                    else
                    {
                        result.FailedCount += storeResult.FailedCount;
                        if (storeResult.Errors != null)
                        {
                            result.Errors.AddRange(storeResult.Errors);
                        }
                    }
                }

                _logger.LogInformation(
                    "并发同步完成。总创建: {CreatedCount}, 总更新: {UpdatedCount}, 失败: {FailedCount}",
                    result.CreatedCount,
                    result.UpdatedCount,
                    result.FailedCount
                );

                return BuildAggregateResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品到分店失败");
                return ApiResponse<SyncProductsToStoresResult>.Error(
                    "商品同步到分店失败，请稍后重试或联系管理员",
                    "DATABASE_ERROR"
                );
            }
        }

        public static ApiResponse<SyncProductsToStoresResult> BuildAggregateResponse(
            SyncProductsToStoresResult result
        )
        {
            if (result.FailedCount > 0 && result.CreatedCount + result.UpdatedCount == 0)
            {
                return new ApiResponse<SyncProductsToStoresResult>
                {
                    Success = false,
                    Message = "商品同步到分店失败，请稍后重试或联系管理员",
                    ErrorCode = "SYNC_PRODUCTS_TO_STORES_FAILED",
                    Data = result,
                    Details = result,
                    Timestamp = DateTime.UtcNow,
                };
            }

            var message = result.FailedCount > 0
                ? "商品同步到分店部分完成，部分分店失败"
                : "同步成功";
            return ApiResponse<SyncProductsToStoresResult>.OK(result, message);
        }

        public static async Task<List<T>> RunStoreSyncTasksAsync<T>(
            IReadOnlyList<Func<Task<T>>> taskFactories,
            int maxConcurrency = 3
        )
        {
            if (taskFactories.Count == 0)
            {
                return new List<T>();
            }

            var results = new T[taskFactories.Count];
            var nextIndex = -1;
            var workerCount = Math.Min(maxConcurrency, taskFactories.Count);

            async Task WorkerAsync()
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref nextIndex);
                    if (index >= taskFactories.Count)
                    {
                        return;
                    }

                    results[index] = await taskFactories[index]();
                }
            }

            var workers = Enumerable.Range(0, workerCount).Select(_ => WorkerAsync()).ToList();
            await Task.WhenAll(workers);
            return results.ToList();
        }

        private async Task<StoreSyncResult> SyncToSingleStoreAsync(
            SyncProductsToStoresRequest request,
            string storeCode,
            List<Product> products,
            List<ProductSetCode> productSetCodes
        )
        {
            var result = new StoreSyncResult();

            ISqlSugarClient? independentDb = null;
            try
            {
                independentDb = CreateIndependentConnection();

                var storeMultiCodeProductDb = new SimpleClient<StoreMultiCodeProduct>(independentDb);
                var storeRetailPriceDb = new SimpleClient<StoreRetailPrice>(independentDb);

                if (productSetCodes.Count > 0)
                {
                    var productCodesNeedMultiCodeSync = products
                        .Where(p => p.ProductType != null && p.ProductType != 0)
                        .Select(p => p.ProductCode)
                        .ToList();

                    var existingMultiCodeRecords = await storeMultiCodeProductDb
                        .AsQueryable()
                        .Where(p => p.StoreCode == storeCode)
                        .Where(p => productCodesNeedMultiCodeSync.Contains(p.ProductCode))
                        .ToListAsync();

                    var newMultiCodeRecords = new List<StoreMultiCodeProduct>();
                    var updateMultiCodeRecords = new List<StoreMultiCodeProduct>();

                    foreach (var productSetCode in productSetCodes)
                    {
                        var existingRecord = existingMultiCodeRecords.FirstOrDefault(p =>
                            p.StoreCode == storeCode
                            && p.MultiCodeProductCode == productSetCode.SetProductCode
                        );

                        if (existingRecord == null)
                        {
                            var newRecord = new StoreMultiCodeProduct
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = storeCode,
                                ProductCode = productSetCode.ProductCode,
                                MultiCodeProductCode = productSetCode.SetProductCode,
                                StoreMultiCodeProductCode = storeCode + productSetCode.SetProductCode,
                                MultiBarcode = productSetCode.SetBarcode,
                                PurchasePrice = request.SyncPurchasePrice
                                    ? productSetCode.SetPurchasePrice
                                    : null,
                                MultiCodeRetailPrice = request.SyncRetailPrice
                                    ? productSetCode.SetRetailPrice
                                    : null,
                                IsAutoPricing = false,
                                IsActive = true,
                                CreatedBy = "System",
                                CreatedAt = DateTime.Now,
                            };
                            newMultiCodeRecords.Add(newRecord);
                        }
                        else
                        {
                            if (request.SyncPurchasePrice)
                                existingRecord.PurchasePrice = productSetCode.SetPurchasePrice;
                            if (request.SyncRetailPrice)
                                existingRecord.MultiCodeRetailPrice = productSetCode.SetRetailPrice;

                            existingRecord.MultiBarcode = productSetCode.SetBarcode;
                            existingRecord.UpdatedBy = "System";
                            existingRecord.UpdatedAt = DateTime.Now;
                            updateMultiCodeRecords.Add(existingRecord);
                        }
                    }

                    if (newMultiCodeRecords.Count > 0)
                    {
                        await independentDb
                            .Fastest<StoreMultiCodeProduct>()
                            .PageSize(2000)
                            .BulkCopyAsync(newMultiCodeRecords);
                        result.StoreMultiCodeProductCreatedCount = newMultiCodeRecords.Count;
                        result.CreatedCount += newMultiCodeRecords.Count;
                    }

                    if (updateMultiCodeRecords.Count > 0)
                    {
                        await independentDb
                            .Fastest<StoreMultiCodeProduct>()
                            .BulkUpdateAsync(updateMultiCodeRecords);
                        result.StoreMultiCodeProductUpdatedCount = updateMultiCodeRecords.Count;
                        result.UpdatedCount += updateMultiCodeRecords.Count;
                    }
                }

                var productCodes = products.Select(p => p.ProductCode).ToList();
                var existingRetailPriceRecords = await storeRetailPriceDb
                    .AsQueryable()
                    .Where(p => p.StoreCode == storeCode)
                    .Where(p => productCodes.Contains(p.ProductCode))
                    .ToListAsync();

                var newRetailPriceRecords = new List<StoreRetailPrice>();
                var updateRetailPriceRecords = new List<StoreRetailPrice>();

                foreach (var product in products)
                {
                    var storeProductCode = storeCode + product.ProductCode;

                    var existingRecord = existingRetailPriceRecords.FirstOrDefault(p =>
                        p.StoreCode == storeCode && p.ProductCode == product.ProductCode
                    );

                    if (existingRecord == null)
                    {
                        var newRecord = new StoreRetailPrice
                        {
                            UUID = UuidHelper.GenerateUuid7(),
                            StoreCode = storeCode,
                            ProductCode = product.ProductCode,
                            StoreProductCode = storeProductCode,
                            SupplierCode = product.LocalSupplierCode,
                            PurchasePrice = request.SyncPurchasePrice
                                ? product.PurchasePrice
                                : null,
                            StoreRetailPriceValue = request.SyncRetailPrice
                                ? product.RetailPrice
                                : null,
                            IsAutoPricing = request.SyncIsAutoPricing
                                ? product.IsAutoPricing
                                : false,
                            IsSpecialProduct = request.SyncIsSpecialProduct
                                ? product.IsSpecialProduct
                                : false,
                            DiscountRate = request.SyncDiscountRate ? null : null,
                            IsActive = true,
                            CreatedBy = "System",
                            CreatedAt = DateTime.Now,
                        };
                        newRetailPriceRecords.Add(newRecord);
                    }
                    else
                    {
                        if (request.SyncPurchasePrice)
                            existingRecord.PurchasePrice = product.PurchasePrice;
                        if (request.SyncRetailPrice)
                            existingRecord.StoreRetailPriceValue = product.RetailPrice;
                        if (request.SyncIsAutoPricing)
                            existingRecord.IsAutoPricing = product.IsAutoPricing;
                        if (request.SyncIsSpecialProduct)
                            existingRecord.IsSpecialProduct = product.IsSpecialProduct;
                        existingRecord.UpdatedBy = "System";
                        existingRecord.UpdatedAt = DateTime.Now;
                        updateRetailPriceRecords.Add(existingRecord);
                    }
                }

                if (newRetailPriceRecords.Count > 0)
                {
                    await independentDb
                        .Fastest<StoreRetailPrice>()
                        .PageSize(2000)
                        .BulkCopyAsync(newRetailPriceRecords);
                    result.StoreRetailPriceCreatedCount = newRetailPriceRecords.Count;
                    result.CreatedCount += newRetailPriceRecords.Count;
                }

                if (updateRetailPriceRecords.Count > 0)
                {
                    await independentDb
                        .Fastest<StoreRetailPrice>()
                        .BulkUpdateAsync(updateRetailPriceRecords);
                    result.StoreRetailPriceUpdatedCount = updateRetailPriceRecords.Count;
                    result.UpdatedCount += updateRetailPriceRecords.Count;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品到分店 {StoreCode} 失败", storeCode);
                result.Success = false;
                result.FailedCount = 1;
                result.Errors = new List<string> { $"分店 {storeCode} 同步失败，请稍后重试或联系管理员" };
            }
            finally
            {
                if (independentDb != null)
                {
                    independentDb.Dispose();
                }
            }

            return result;
        }

        private class StoreSyncResult
        {
            public bool Success { get; set; }
            public int CreatedCount { get; set; }
            public int UpdatedCount { get; set; }
            public int FailedCount { get; set; }
            public int StoreMultiCodeProductCreatedCount { get; set; }
            public int StoreMultiCodeProductUpdatedCount { get; set; }
            public int StoreRetailPriceCreatedCount { get; set; }
            public int StoreRetailPriceUpdatedCount { get; set; }
            public List<string> Errors { get; set; } = new();
        }
    }
}
