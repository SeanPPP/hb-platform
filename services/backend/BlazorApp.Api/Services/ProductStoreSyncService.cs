using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
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
                var failureDetails = new List<BatchOperationFailureDto>();

                var productDb = _db.ProductDb;
                var productSetCodeDb = _db.ProductSetCodeDb;

                var products = await productDb
                    .AsQueryable()
                    .Where(p => p.ProductCode != null && request.ProductCodes.Contains(p.ProductCode))
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

                var allProductCodes = products.Select(p => p.ProductCode).ToList();

                List<ProductSetCode> productSetCodes = new();
                if (allProductCodes.Count > 0)
                {
                    productSetCodes = await productSetCodeDb
                        .AsQueryable()
                        // 门店多码关系的唯一事实来源是有效 ProductSetCode，不能依赖主商品 ProductType。
                        .Where(p =>
                            (p.SetType == 1 || p.SetType == 2)
                            && allProductCodes.Contains(p.ProductCode)
                        )
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
                        failureDetails.AddRange(storeResult.FailureDetails);
                    }
                }

                _logger.LogInformation(
                    "并发同步完成。总创建: {CreatedCount}, 总更新: {UpdatedCount}, 失败: {FailedCount}",
                    result.CreatedCount,
                    result.UpdatedCount,
                    result.FailedCount
                );

                return BuildAggregateResponse(result, failureDetails);
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                _logger.LogWarning(ex, "同步商品到分店遇到套装成本业务锁冲突");
                return ApiResponse<SyncProductsToStoresResult>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
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
            SyncProductsToStoresResult result,
            IReadOnlyCollection<BatchOperationFailureDto>? failureDetails = null
        )
        {
            var failures = failureDetails?.ToList() ?? new List<BatchOperationFailureDto>();
            var containsBusyFailure = failures.Any(failure =>
                string.Equals(
                    failure.ErrorCode,
                    SetChildPurchasePriceMutationLock.BusyErrorCode,
                    StringComparison.Ordinal
                )
            );
            if (result.FailedCount > 0 && result.CreatedCount + result.UpdatedCount == 0)
            {
                return new ApiResponse<SyncProductsToStoresResult>
                {
                    Success = false,
                    Message = containsBusyFailure
                        ? "套装商品正在被其他操作修改，请稍后重试"
                        : "商品同步到分店失败，请稍后重试或联系管理员",
                    ErrorCode = containsBusyFailure
                        ? SetChildPurchasePriceMutationLock.BusyErrorCode
                        : "SYNC_PRODUCTS_TO_STORES_FAILED",
                    Data = result,
                    Details = failures.Count > 0 ? failures : result,
                    Timestamp = DateTime.UtcNow,
                };
            }

            var message = result.FailedCount > 0
                ? "商品同步到分店部分完成，部分分店失败"
                : "同步成功";
            var response = ApiResponse<SyncProductsToStoresResult>.OK(result, message);
            if (failures.Count > 0)
            {
                response.Details = failures;
            }
            return response;
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
                await independentDb.Ado.BeginTranAsync();

                var requestedProductCodes = products.Select(p => p.ProductCode).ToList();
                var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                    independentDb,
                    requestedProductCodes
                );
                // 等待业务锁期间来源主档或套装关系可能已变化，必须锁内重读，不能回写锁前缓存实体。
                products = await independentDb.Queryable<Product>()
                    .Where(p => p.ProductCode != null && requestedProductCodes.Contains(p.ProductCode))
                    .Where(p => !p.IsDeleted)
                    .ToListAsync();
                var lockedProductCodes = products
                    .Select(p => p.ProductCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .ToList();
                // 锁内复读保持 ProductSetCode 事实来源，避免回写锁前关系快照或受 ProductType 影响漏行。
                productSetCodes = lockedProductCodes.Count == 0
                    ? new List<ProductSetCode>()
                    : await independentDb.Queryable<ProductSetCode>()
                        .Where(p =>
                            (p.SetType == 1 || p.SetType == 2)
                            && lockedProductCodes.Contains(p.ProductCode)
                        )
                        .Where(p => p.IsActive && !p.IsDeleted)
                        .ToListAsync();
                lockScope.EnsureCovers(independentDb, lockedProductCodes);

                var storeMultiCodeProductDb = new SimpleClient<StoreMultiCodeProduct>(independentDb);
                var storeRetailPriceDb = new SimpleClient<StoreRetailPrice>(independentDb);

                if (productSetCodes.Count > 0)
                {
                    var productCodesNeedMultiCodeSync = productSetCodes
                        .Select(p => p.ProductCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var existingMultiCodeRecords = await storeMultiCodeProductDb
                        .AsQueryable()
                        .Where(p => p.StoreCode == storeCode)
                        .Where(p =>
                            p.ProductCode != null
                            && productCodesNeedMultiCodeSync.Contains(p.ProductCode)
                        )
                        .Where(p => !p.IsDeleted)
                        .ToListAsync();

                    var newMultiCodeRecords = new List<StoreMultiCodeProduct>();
                    var updateMultiCodeRecords = new List<StoreMultiCodeProduct>();

                    foreach (var productSetCode in productSetCodes)
                    {
                        var existingRecord = existingMultiCodeRecords.FirstOrDefault(p =>
                            p.StoreCode == storeCode
                            && p.ProductCode == productSetCode.ProductCode
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
                                // Type1/Type2 均先以空成本落库，统一服务再按门店主成本计算，避免复制总部旧值。
                                PurchasePrice = null,
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
                            // Type1/Type2 子项成本只能由统一服务写回，不能先覆盖为总部成本。
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
                        foreach (var record in updateMultiCodeRecords)
                        {
                            var update = independentDb.Updateable<StoreMultiCodeProduct>()
                                .Where(x => x.UUID == record.UUID && !x.IsDeleted);
                            if (request.SyncRetailPrice)
                                update = update.SetColumns(x =>
                                    x.MultiCodeRetailPrice == record.MultiCodeRetailPrice
                                );
                            await update
                                .SetColumns(x => x.MultiBarcode == record.MultiBarcode)
                                .SetColumns(x => x.UpdatedBy == record.UpdatedBy)
                                .SetColumns(x => x.UpdatedAt == record.UpdatedAt)
                                .ExecuteCommandAsync();
                        }
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
                        foreach (var record in updateRetailPriceRecords)
                        {
                            var update = independentDb.Updateable<StoreRetailPrice>()
                                .Where(x => x.UUID == record.UUID && !x.IsDeleted);
                            if (request.SyncPurchasePrice)
                                update = update.SetColumns(x => x.PurchasePrice == record.PurchasePrice);
                            if (request.SyncRetailPrice)
                                update = update.SetColumns(x =>
                                    x.StoreRetailPriceValue == record.StoreRetailPriceValue
                                );
                            if (request.SyncIsAutoPricing)
                                update = update.SetColumns(x => x.IsAutoPricing == record.IsAutoPricing);
                            if (request.SyncIsSpecialProduct)
                                update = update.SetColumns(x => x.IsSpecialProduct == record.IsSpecialProduct);
                            await update
                                .SetColumns(x => x.UpdatedBy == record.UpdatedBy)
                                .SetColumns(x => x.UpdatedAt == record.UpdatedAt)
                                .ExecuteCommandAsync();
                        }
                        result.StoreRetailPriceUpdatedCount = updateRetailPriceRecords.Count;
                    result.UpdatedCount += updateRetailPriceRecords.Count;
                }

                if (productSetCodes.Any(x => x.SetType == 1 || x.SetType == 2))
                {
                    var setParentCodes = productSetCodes
                        .Select(x => x.ProductCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var storeParentPrices = await independentDb.Queryable<StoreRetailPrice>()
                        .Where(price =>
                            price.StoreCode == storeCode
                            && price.ProductCode != null
                            && setParentCodes.Contains(price.ProductCode)
                            && price.IsActive
                            && !price.IsDeleted
                        )
                        .ToListAsync();
                    var invalidParentPrice = setParentCodes.FirstOrDefault(productCode =>
                    {
                        var matching = storeParentPrices
                            .Where(price => string.Equals(price.ProductCode, productCode, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        return matching.Count != 1 || matching[0].PurchasePrice.GetValueOrDefault() <= 0;
                    });
                    if (invalidParentPrice != null)
                    {
                        throw new InvalidOperationException($"门店 {storeCode} 主商品 {invalidParentPrice} 成本记录缺失、重复或为空");
                    }

                    // 门店关系已完整落库后统一计算：Type1 按零售价分摊，Type2 等于对应门店主成本。
                    var writeback = await new SetChildPurchasePriceService(independentDb)
                        .RecalculateStoresLockedAsync(
                            lockScope,
                            productCodes,
                            new[] { storeCode },
                            "System"
                        );
                    if (writeback.StoreMultiCodeProduct.SkippedGroupCount > 0)
                    {
                        throw new InvalidOperationException(
                            writeback.Errors.FirstOrDefault()?.Reason ?? "目标套装组无法重算"
                        );
                    }
                }

                await independentDb.Ado.CommitTranAsync();
                result.Success = true;
            }
            catch (Exception ex)
            {
                if (independentDb != null)
                {
                    try
                    {
                        await independentDb.Ado.RollbackTranAsync();
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogWarning(
                            rollbackEx,
                            "同步商品到分店 {StoreCode} 回滚失败",
                            storeCode
                        );
                    }
                }
                _logger.LogError(ex, "同步商品到分店 {StoreCode} 失败", storeCode);
                var isBusy = SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _);
                var message = isBusy
                    ? $"分店 {storeCode}: 套装商品正在被其他操作修改，请稍后重试"
                    : $"分店 {storeCode} 同步失败，请稍后重试或联系管理员";
                result.Success = false;
                result.FailedCount = 1;
                result.ErrorCode = isBusy
                    ? SetChildPurchasePriceMutationLock.BusyErrorCode
                    : "SYNC_PRODUCTS_TO_STORES_FAILED";
                result.Errors = new List<string> { message };
                result.FailureDetails.Add(new BatchOperationFailureDto
                {
                    ItemKey = storeCode,
                    Message = message,
                    ErrorCode = result.ErrorCode,
                });
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
            public string? ErrorCode { get; set; }
            public List<BatchOperationFailureDto> FailureDetails { get; set; } = new();
        }
    }
}
