using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 商品 HQ 同步统一实现。
    /// 旧入口只委托到这里，避免继续使用 Product/价格/分店多码混合同步链路。
    /// </summary>
    public class ProductHqSyncService : IProductHqSyncService
    {
        private const string ShadowTableName = "Product_Shadow";
        private const int HqReadBatchSize = 5000;
        private const int WriteBatchSize = 1000;
        private static readonly SemaphoreSlim SyncLock = new(1, 1);

        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IMapper _mapper;
        private readonly ILogger<ProductHqSyncService> _logger;

        public ProductHqSyncService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger<ProductHqSyncService> logger
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ApiResponse<HqProductSyncResult>> SyncFullAsync()
        {
            if (!await SyncLock.WaitAsync(0))
            {
                return ApiResponse<HqProductSyncResult>.Error(
                    "已有商品HQ同步任务正在执行，请稍后再试",
                    "PRODUCT_HQ_SYNC_CONFLICT"
                );
            }

            var startedAt = DateTime.UtcNow;
            var result = new HqProductSyncResult();
            var db = _localContext.Db;
            var originalTimeout = db.Ado.CommandTimeOut;
            db.Ado.CommandTimeOut = 1800;

            try
            {
                _hqContext.CheckConnection();
                if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                {
                    await SyncFullWithShadowAsync(db, result);
                }
                else
                {
                    // 非 SQL Server 测试环境没有存储过程，仍保持“只处理 Product”的行为语义。
                    await SyncFullDirectAsync(db, result);
                }

                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                return ApiResponse<HqProductSyncResult>.OK(result, "商品HQ全量同步完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品HQ全量同步失败");
                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                result.Errors.Add(ex.Message);
                return ApiResponse<HqProductSyncResult>.Error(
                    $"商品HQ全量同步失败: {ex.Message}",
                    "PRODUCT_HQ_FULL_SYNC_ERROR",
                    result
                );
            }
            finally
            {
                db.Ado.CommandTimeOut = originalTimeout;
                SyncLock.Release();
            }
        }

        public async Task<ApiResponse<HqProductSyncResult>> SyncIncrementalAsync(
            DateTime? startDate = null
        )
        {
            if (!await SyncLock.WaitAsync(0))
            {
                return ApiResponse<HqProductSyncResult>.Error(
                    "已有商品HQ同步任务正在执行，请稍后再试",
                    "PRODUCT_HQ_SYNC_CONFLICT"
                );
            }

            var startedAt = DateTime.UtcNow;
            var result = new HqProductSyncResult();
            var db = _localContext.Db;
            var originalTimeout = db.Ado.CommandTimeOut;
            db.Ado.CommandTimeOut = 1800;

            try
            {
                _hqContext.CheckConnection();
                var effectiveStart = startDate ?? DateTime.UtcNow.AddDays(-30);

                db.Ado.BeginTran();
                try
                {
                    var productSnapshot = await SyncProductsIncrementalCoreAsync(
                        db,
                        effectiveStart,
                        result
                    );
                    await SyncProductSetCodesIncrementalCoreAsync(
                        db,
                        effectiveStart,
                        productSnapshot.ActiveProductCodes,
                        productSnapshot.SoftDeletedProductCodes,
                        result
                    );
                    db.Ado.CommitTran();
                }
                catch
                {
                    db.Ado.RollbackTran();
                    throw;
                }

                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                return ApiResponse<HqProductSyncResult>.OK(result, "商品HQ增量同步完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品HQ增量同步失败");
                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                result.Errors.Add(ex.Message);
                return ApiResponse<HqProductSyncResult>.Error(
                    $"商品HQ增量同步失败: {ex.Message}",
                    "PRODUCT_HQ_INCREMENTAL_SYNC_ERROR",
                    result
                );
            }
            finally
            {
                db.Ado.CommandTimeOut = originalTimeout;
                SyncLock.Release();
            }
        }

        public async Task<ApiResponse<HqProductSyncResult>> SyncSelectedFromHqAsync(
            List<string> productCodes
        )
        {
            if (productCodes == null || productCodes.Count == 0)
            {
                return ApiResponse<HqProductSyncResult>.Error(
                    "商品编码列表不能为空",
                    "PRODUCT_HQ_SELECTED_SYNC_EMPTY_CODES"
                );
            }

            if (!await SyncLock.WaitAsync(0))
            {
                return ApiResponse<HqProductSyncResult>.Error(
                    "已有商品HQ同步任务正在执行，请稍后再试",
                    "PRODUCT_HQ_SYNC_CONFLICT"
                );
            }

            var startedAt = DateTime.UtcNow;
            var result = new HqProductSyncResult();
            var db = _localContext.Db;
            var originalTimeout = db.Ado.CommandTimeOut;
            db.Ado.CommandTimeOut = 1800;

            try
            {
                _hqContext.CheckConnection();
                var requestedCodes = productCodes
                    .Select(NormalizeCode)
                    .Where(code => code != null)
                    .Select(code => code!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (requestedCodes.Count == 0)
                {
                    return ApiResponse<HqProductSyncResult>.Error(
                        "商品编码列表不能为空",
                        "PRODUCT_HQ_SELECTED_SYNC_EMPTY_CODES"
                    );
                }

                var selectedLocalProducts = await db.Queryable<Product>()
                    .Where(row =>
                        row.ProductCode != null
                        && requestedCodes.Contains(row.ProductCode)
                        && !row.IsDeleted
                    )
                    .ToListAsync();
                selectedLocalProducts = DeduplicateByBusinessKey(
                    selectedLocalProducts,
                    row => row.ProductCode
                );
                result.TotalLocalProducts = selectedLocalProducts.Count;

                var selectedLocalByCode = selectedLocalProducts
                    .Where(row => NormalizeCode(row.ProductCode) != null)
                    .ToDictionary(row => NormalizeCode(row.ProductCode)!, StringComparer.OrdinalIgnoreCase);
                foreach (var requestedCode in requestedCodes)
                {
                    if (!selectedLocalByCode.ContainsKey(requestedCode))
                    {
                        result.Errors.Add($"本地商品不存在或已删除: {requestedCode}");
                    }
                }

                if (selectedLocalProducts.Count == 0)
                {
                    result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    return ApiResponse<HqProductSyncResult>.Error(
                        "未找到有效的本地商品",
                        "PRODUCT_HQ_SELECTED_SYNC_NO_PRODUCTS",
                        result
                    );
                }

                var selectedLocalCodes = selectedLocalByCode.Keys.ToList();
                var directHqRows = await _hqContext.Db.Queryable<DIC_商品信息字典表>()
                    .Where(row =>
                        row.H商品编码 != null
                        && selectedLocalCodes.Contains(row.H商品编码)
                        && row.H使用状态 == true
                    )
                    .ToListAsync();
                var directHqCodes = directHqRows
                    .Select(row => NormalizeCode(row.H商品编码))
                    .Where(code => code != null)
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var resolvedHqCodes = new HashSet<string>(directHqCodes, StringComparer.OrdinalIgnoreCase);
                var needFallback = selectedLocalProducts
                    .Where(row =>
                        NormalizeCode(row.ProductCode) is { } code
                        && !directHqCodes.Contains(code)
                    )
                    .ToList();

                if (needFallback.Count > 0)
                {
                    // 兜底只用于找 HQ 商品编码：分店零售价表提供供应商，商品字典表提供货号。
                    var fallbackMatches = await ResolveHqCodesBySupplierItemAsync(needFallback);
                    foreach (var localProduct in needFallback)
                    {
                        var localCode = NormalizeCode(localProduct.ProductCode);
                        var key = BuildSupplierItemKey(localProduct.LocalSupplierCode, localProduct.ItemNumber);
                        if (localCode == null || key == null)
                        {
                            result.Errors.Add($"商品缺少供应商或货号，无法兜底匹配: {localCode ?? "(空编码)"}");
                            continue;
                        }

                        if (!fallbackMatches.TryGetValue(key, out var matchedCodes) || matchedCodes.Count == 0)
                        {
                            result.Errors.Add($"HQ未找到供应商+货号匹配: {localProduct.LocalSupplierCode}/{localProduct.ItemNumber}");
                            continue;
                        }

                        if (matchedCodes.Count > 1)
                        {
                            result.Errors.Add($"HQ供应商+货号匹配到多个商品，已跳过: {localProduct.LocalSupplierCode}/{localProduct.ItemNumber}");
                            continue;
                        }

                        resolvedHqCodes.Add(matchedCodes[0]);
                    }
                }

                if (resolvedHqCodes.Count == 0)
                {
                    result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    return ApiResponse<HqProductSyncResult>.Error(
                        "未匹配到可同步的HQ商品",
                        "PRODUCT_HQ_SELECTED_SYNC_NO_HQ_MATCH",
                        result
                    );
                }

                var resolvedHqCodeList = resolvedHqCodes.ToList();
                var hqProducts = await _hqContext.Db.Queryable<DIC_商品信息字典表>()
                    .Where(row =>
                        row.H商品编码 != null
                        && resolvedHqCodeList.Contains(row.H商品编码)
                        && row.H使用状态 == true
                    )
                    .ToListAsync();
                hqProducts = hqProducts
                    .GroupBy(row => row.H商品编码!)
                    .Select(group => group.OrderByDescending(row => row.FGC_LastModifyDate).First())
                    .ToList();
                result.TotalHqProducts = hqProducts.Count;

                db.Ado.BeginTran();
                try
                {
                    var activeProductCodes = await UpsertSelectedProductsFromHqAsync(db, hqProducts, result);
                    await SyncSelectedProductSetCodesFromHqAsync(db, activeProductCodes, result);
                    await SyncSelectedStoreRetailPricesFromHqAsync(db, activeProductCodes, result);
                    await SyncSelectedStoreMultiCodesFromHqAsync(db, activeProductCodes, result);
                    db.Ado.CommitTran();
                }
                catch
                {
                    db.Ado.RollbackTran();
                    throw;
                }

                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                return ApiResponse<HqProductSyncResult>.OK(result, "选中商品HQ同步完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "选中商品HQ同步失败");
                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                result.Errors.Add(ex.Message);
                return ApiResponse<HqProductSyncResult>.Error(
                    $"选中商品HQ同步失败: {ex.Message}",
                    "PRODUCT_HQ_SELECTED_SYNC_ERROR",
                    result
                );
            }
            finally
            {
                db.Ado.CommandTimeOut = originalTimeout;
                SyncLock.Release();
            }
        }

        private async Task<Dictionary<string, List<string>>> ResolveHqCodesBySupplierItemAsync(
            List<Product> localProducts
        )
        {
            var supplierCodes = localProducts
                .Select(row => NormalizeCode(row.LocalSupplierCode))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var itemNumbers = localProducts
                .Select(row => NormalizeCode(row.ItemNumber))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (supplierCodes.Count == 0 || itemNumbers.Count == 0)
            {
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            var candidates = await _hqContext.Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                    (retail, product) => new JoinQueryInfos(
                        JoinType.Inner,
                        retail.H商品编码 == product.H商品编码
                    )
                )
                .Where((retail, product) =>
                    retail.H使用状态 == true
                    && product.H使用状态 == true
                    && supplierCodes.Contains(retail.H供应商编码)
                    && itemNumbers.Contains(product.H货号)
                    && !string.IsNullOrEmpty(product.H商品编码)
                )
                .Select((retail, product) => new SupplierItemHqProductMatch
                {
                    SupplierCode = retail.H供应商编码,
                    ItemNumber = product.H货号,
                    ProductCode = product.H商品编码,
                })
                .ToListAsync();

            return candidates
                .Select(row => new
                {
                    Key = BuildSupplierItemKey(row.SupplierCode, row.ItemNumber),
                    ProductCode = NormalizeCode(row.ProductCode),
                })
                .Where(row => row.Key != null && row.ProductCode != null)
                .GroupBy(row => row.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row => row.ProductCode!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private async Task<List<string>> UpsertSelectedProductsFromHqAsync(
            ISqlSugarClient db,
            List<DIC_商品信息字典表> hqProducts,
            HqProductSyncResult result
        )
        {
            var productCodes = hqProducts
                .Select(row => NormalizeCode(row.H商品编码))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var localRows = await db.Queryable<Product>()
                .Where(row => row.ProductCode != null && productCodes.Contains(row.ProductCode))
                .ToListAsync();
            var localByCode = localRows
                .Where(row => NormalizeCode(row.ProductCode) != null)
                .GroupBy(row => NormalizeCode(row.ProductCode)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var hqRow in hqProducts)
            {
                var code = NormalizeCode(hqRow.H商品编码);
                if (code == null)
                {
                    continue;
                }

                if (localByCode.TryGetValue(code, out var local))
                {
                    ApplyProductUpdate(hqRow, local);
                    await db.Updateable(local).ExecuteCommandAsync();
                    result.ProductsUpdated++;
                    continue;
                }

                var product = MapNewProduct(hqRow);
                await db.Insertable(product).ExecuteCommandAsync();
                localByCode[code] = product;
                result.ProductsAdded++;
            }

            return productCodes;
        }

        private async Task SyncSelectedProductSetCodesFromHqAsync(
            ISqlSugarClient db,
            List<string> productCodes,
            HqProductSyncResult result
        )
        {
            if (productCodes.Count == 0)
            {
                return;
            }

            var hqRows = await _hqContext.Db.Queryable<DIC_一品多码表>()
                .Where(row =>
                    row.H使用状态 == true
                    && row.H商品编码 != null
                    && productCodes.Contains(row.H商品编码)
                    && !string.IsNullOrEmpty(row.H多码商品编号)
                )
                .ToListAsync();
            if (hqRows.Count == 0)
            {
                return;
            }

            var localRows = await db.Queryable<ProductSetCode>()
                .Where(row => productCodes.Contains(row.ProductCode))
                .ToListAsync();
            var byGuid = localRows
                .Where(row => !string.IsNullOrWhiteSpace(row.SetCodeId))
                .GroupBy(row => row.SetCodeId)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var byBusinessKey = localRows
                .Select(row => new
                {
                    Key = BuildProductSetCodeBusinessKey(row.ProductCode, row.SetProductCode),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

            foreach (var hqRow in hqRows)
            {
                var local = FindProductSetCode(hqRow, byGuid, byBusinessKey);
                if (local == null)
                {
                    local = MapNewProductSetCode(hqRow);
                    await db.Insertable(local).ExecuteCommandAsync();
                    result.ProductSetCodesAdded++;
                    byGuid[local.SetCodeId] = local;
                    var businessKey = BuildProductSetCodeBusinessKey(local.ProductCode, local.SetProductCode);
                    if (businessKey != null)
                    {
                        byBusinessKey[businessKey] = local;
                    }
                    continue;
                }

                await NormalizeProductSetCodeIdAsync(db, local, hqRow.HGUID, byGuid);
                ApplyProductSetCodeUpdate(hqRow, local);
                await db.Updateable(local).ExecuteCommandAsync();
                result.ProductSetCodesUpdated++;
            }
        }

        private async Task SyncSelectedStoreRetailPricesFromHqAsync(
            ISqlSugarClient db,
            List<string> productCodes,
            HqProductSyncResult result
        )
        {
            var activeStoreCodes = await GetActiveLocalStoreCodesAsync(db);
            if (productCodes.Count == 0 || activeStoreCodes.Count == 0)
            {
                return;
            }

            var hqRows = await _hqContext.Db.Queryable<DIC_商品零售价表>()
                .Where(row =>
                    row.H使用状态 == true
                    && productCodes.Contains(row.H商品编码)
                    && activeStoreCodes.Contains(row.H分店代码)
                )
                .ToListAsync();
            if (hqRows.Count == 0)
            {
                return;
            }

            var localRows = await db.Queryable<StoreRetailPrice>()
                .Where(row => row.ProductCode != null && productCodes.Contains(row.ProductCode))
                .ToListAsync();
            var localByKey = localRows
                .Select(row => new
                {
                    Key = BuildStoreProductKey(row.StoreCode, row.ProductCode),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

            foreach (var hqRow in hqRows)
            {
                var key = BuildStoreProductKey(hqRow.H分店代码, hqRow.H商品编码);
                if (key == null)
                {
                    continue;
                }

                if (localByKey.TryGetValue(key, out var local))
                {
                    ApplyStoreRetailPriceUpdate(hqRow, local);
                    await db.Updateable(local).ExecuteCommandAsync();
                    result.StoreRetailPricesUpdated++;
                    continue;
                }

                local = MapNewStoreRetailPrice(hqRow);
                await db.Insertable(local).ExecuteCommandAsync();
                localByKey[key] = local;
                result.StoreRetailPricesCreated++;
            }
        }

        private async Task SyncSelectedStoreMultiCodesFromHqAsync(
            ISqlSugarClient db,
            List<string> productCodes,
            HqProductSyncResult result
        )
        {
            var activeStoreCodes = await GetActiveLocalStoreCodesAsync(db);
            if (productCodes.Count == 0 || activeStoreCodes.Count == 0)
            {
                return;
            }

            var hqRows = await _hqContext.Db.Queryable<DIC_分店一品多码表>()
                .Where(row =>
                    row.H使用状态 == true
                    && row.H商品编码 != null
                    && productCodes.Contains(row.H商品编码)
                    && row.H分店代码 != null
                    && activeStoreCodes.Contains(row.H分店代码)
                    && !string.IsNullOrEmpty(row.H多码商品编码)
                )
                .ToListAsync();
            if (hqRows.Count == 0)
            {
                return;
            }

            var localRows = await db.Queryable<StoreMultiCodeProduct>()
                .Where(row => row.ProductCode != null && productCodes.Contains(row.ProductCode))
                .ToListAsync();
            var localByKey = localRows
                .Select(row => new
                {
                    Key = BuildStoreMultiCodeKey(
                        row.StoreCode,
                        row.ProductCode,
                        row.MultiCodeProductCode
                    ),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

            foreach (var hqRow in hqRows)
            {
                var key = BuildStoreMultiCodeKey(
                    hqRow.H分店代码,
                    hqRow.H商品编码,
                    hqRow.H多码商品编码
                );
                if (key == null)
                {
                    continue;
                }

                if (localByKey.TryGetValue(key, out var local))
                {
                    ApplyStoreMultiCodeUpdate(hqRow, local);
                    await db.Updateable(local).ExecuteCommandAsync();
                    result.StoreMultiCodesUpdated++;
                    continue;
                }

                local = MapNewStoreMultiCode(hqRow);
                await db.Insertable(local).ExecuteCommandAsync();
                localByKey[key] = local;
                result.StoreMultiCodesCreated++;
            }
        }

        public async Task<ApiResponse<PushProductsToHqResult>> PushToHqAsync(
            PushProductsToHqRequest request
        )
        {
            if (
                request == null
                || (
                    (request.ProductCodes == null || request.ProductCodes.Count == 0)
                    && (request.Items == null || request.Items.Count == 0)
                )
            )
            {
                return ApiResponse<PushProductsToHqResult>.Error(
                    "推送商品列表不能为空",
                    "PRODUCT_HQ_PUSH_EMPTY_CODES"
                );
            }

            if (!await SyncLock.WaitAsync(0))
            {
                return ApiResponse<PushProductsToHqResult>.Error(
                    "已有商品HQ同步任务正在执行，请稍后再试",
                    "PRODUCT_HQ_SYNC_CONFLICT"
                );
            }

            var startedAt = DateTime.UtcNow;
            var result = new PushProductsToHqResult();
            var localDb = _localContext.Db;
            var hqDb = _hqContext.Db;
            var originalTimeout = hqDb.Ado.CommandTimeOut;
            hqDb.Ado.CommandTimeOut = 1800;

            try
            {
                _hqContext.CheckConnection();
                var resolvedSelection = await ResolvePushSelectionAsync(localDb, request, result);
                var products = resolvedSelection.Products;
                var inventoryCandidates = resolvedSelection.InventoryCandidates;
                var domesticProductImages = resolvedSelection.DomesticProductImages;
                if (result.TotalCount == 0)
                {
                    // 统一在服务层记录业务失败关键信息，方便中心日志按错误码和耗时检索。
                    LogPushToHqBusinessFailure(
                        "PRODUCT_HQ_PUSH_EMPTY_CODES",
                        result,
                        "推送商品列表不能为空"
                    );
                    return ApiResponse<PushProductsToHqResult>.Error(
                        "推送商品列表不能为空",
                        "PRODUCT_HQ_PUSH_EMPTY_CODES"
                    );
                }
                result.TotalLocalProducts = products.Count;

                if (products.Count == 0)
                {
                    result.FailedCount = result.TotalCount;
                    result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    LogPushToHqBusinessFailure(
                        "PRODUCT_HQ_PUSH_NO_PRODUCTS",
                        result,
                        "未找到有效的本地商品"
                    );
                    return new ApiResponse<PushProductsToHqResult>
                    {
                        Success = false,
                        Message = "未找到有效的本地商品",
                        ErrorCode = "PRODUCT_HQ_PUSH_NO_PRODUCTS",
                        Data = result,
                        Details = result,
                    };
                }

                if (resolvedSelection.ItemFailureCount > 0)
                {
                    result.SuccessCount = 0;
                    result.FailedCount = result.TotalCount;
                    result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    LogPushToHqBusinessFailure(
                        "PRODUCT_HQ_PUSH_ITEM_ERRORS",
                        result,
                        "推送候选包含错误，未写入HQ"
                    );
                    return new ApiResponse<PushProductsToHqResult>
                    {
                        Success = false,
                        Message = "推送候选包含错误，未写入HQ",
                        ErrorCode = "PRODUCT_HQ_PUSH_ITEM_ERRORS",
                        Data = result,
                        Details = result,
                    };
                }

                var activeProductCodes = products
                    .Select(row => NormalizeCode(row.ProductCode))
                    .Where(code => code != null)
                    .Select(code => code!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var productSetCodes = await localDb.Queryable<ProductSetCode>()
                    .Where(row =>
                        activeProductCodes.Contains(row.ProductCode)
                        && !row.IsDeleted
                    )
                    .ToListAsync();
                productSetCodes = DeduplicateByBusinessKey(
                    productSetCodes,
                    row => BuildProductSetCodeBusinessKey(row.ProductCode, row.SetProductCode)
                );
                var activeStoreCodes = (await hqDb.Queryable<HqBranch>()
                    .Select(row => row.BranchCode)
                    .ToListAsync())
                    // 推送到 HQ 时以 HQ 分店表为准，避免本地门店资料缺失导致 HQ 分店价格不完整。
                    .Select(NormalizeCode)
                    .Where(code => code != null)
                    .Select(code => code!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var storeMultiCodes = activeStoreCodes.Count == 0
                    ? new List<StoreMultiCodeProduct>()
                    : await localDb.Queryable<StoreMultiCodeProduct>()
                        .Where(row =>
                            row.ProductCode != null
                            && activeProductCodes.Contains(row.ProductCode)
                            && row.StoreCode != null
                            && activeStoreCodes.Contains(row.StoreCode)
                            && !row.IsDeleted
                        )
                        .ToListAsync();
                storeMultiCodes = DeduplicateByBusinessKey(
                    storeMultiCodes,
                    row => BuildStoreMultiCodeKey(row.StoreCode, row.ProductCode, row.MultiCodeProductCode)
                );

                hqDb.Ado.BeginTran();
                try
                {
                    await UpsertHqProductsAsync(
                        hqDb,
                        products,
                        inventoryCandidates,
                        domesticProductImages,
                        result
                    );
                    await UpsertHqRetailPricesAsync(
                        hqDb,
                        products,
                        inventoryCandidates,
                        activeStoreCodes,
                        result
                    );
                    await UpsertHqProductSetCodesAsync(hqDb, products, productSetCodes, result);
                    await UpsertHqStoreMultiCodesAsync(
                        hqDb,
                        products,
                        productSetCodes,
                        storeMultiCodes,
                        activeStoreCodes,
                        result
                    );
                    await UpsertHqWarehouseInventoriesAsync(
                        hqDb,
                        products,
                        inventoryCandidates,
                        result
                    );
                    hqDb.Ado.CommitTran();
                }
                catch
                {
                    hqDb.Ado.RollbackTran();
                    throw;
                }

                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                result.SuccessCount = products.Count;
                result.FailedCount = result.TotalCount - result.SuccessCount;
                return ApiResponse<PushProductsToHqResult>.OK(result, "商品推送HQ完成");
            }
            catch (Exception ex)
            {
                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                result.Errors.Add(ex.Message);
                result.FailedCount = result.TotalCount;
                _logger.LogError(
                    ex,
                    "商品推送HQ异常失败: ErrorCode={ErrorCode}, FailedCount={FailedCount}, FirstFailureReason={FirstFailureReason}, DurationMs={DurationMs}",
                    "PRODUCT_HQ_PUSH_ERROR",
                    result.FailedCount,
                    GetFirstPushFailureReason(result, ex.Message),
                    result.DurationMs
                );
                return new ApiResponse<PushProductsToHqResult>
                {
                    Success = false,
                    Message = $"商品推送HQ失败: {ex.Message}",
                    ErrorCode = "PRODUCT_HQ_PUSH_ERROR",
                    Data = result,
                    Details = result,
                };
            }
            finally
            {
                hqDb.Ado.CommandTimeOut = originalTimeout;
                SyncLock.Release();
            }
        }

        private void LogPushToHqBusinessFailure(
            string errorCode,
            PushProductsToHqResult result,
            string fallbackReason
        )
        {
            // 业务失败不带请求明细，只保留可检索字段，避免日志噪音和额外敏感暴露。
            _logger.LogWarning(
                "商品推送HQ业务失败: ErrorCode={ErrorCode}, FailedCount={FailedCount}, FirstFailureReason={FirstFailureReason}, DurationMs={DurationMs}",
                errorCode,
                result.FailedCount,
                GetFirstPushFailureReason(result, fallbackReason),
                result.DurationMs
            );
        }

        private static string GetFirstPushFailureReason(
            PushProductsToHqResult result,
            string fallbackReason
        )
        {
            var rawReason = result.Errors.FirstOrDefault(error => !string.IsNullOrWhiteSpace(error))
                ?? fallbackReason;
            return NormalizePushFailureReason(rawReason);
        }

        private static string NormalizePushFailureReason(string rawReason)
        {
            if (rawReason.Contains("商品不存在或已删除", StringComparison.Ordinal))
            {
                return "商品不存在";
            }

            if (rawReason.Contains("未找到匹配商品", StringComparison.Ordinal))
            {
                return "商品不存在";
            }

            if (rawReason.Contains("多条本地商品", StringComparison.Ordinal))
            {
                return "商品匹配冲突";
            }

            if (rawReason.Contains("商品编码为空", StringComparison.Ordinal))
            {
                return "商品编码为空";
            }

            var separatorIndex = rawReason.IndexOf(':');
            return separatorIndex > 0 ? rawReason[..separatorIndex].Trim() : rawReason;
        }

        private async Task<PushToHqSelection> ResolvePushSelectionAsync(
            ISqlSugarClient localDb,
            PushProductsToHqRequest request,
            PushProductsToHqResult result
        )
        {
            var rawRequestItems = (request.Items ?? new List<PushProductsToHqItem>())
                .Where(item =>
                    item != null
                    && (
                        !string.IsNullOrWhiteSpace(item.ProductCode)
                        || !string.IsNullOrWhiteSpace(item.LocalSupplierCode)
                        || !string.IsNullOrWhiteSpace(item.ItemNumber)
                    )
                )
                .ToList();
            var itemProductCodeSet = rawRequestItems
                .Select(item => NormalizeCode(item.ProductCode))
                .Where(code => code != null)
                .Select(code => code!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var normalizedCodes = (request.ProductCodes ?? new List<string>())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                // items 是新契约主来源；productCodes 只补充旧入口或额外编码，避免同一商品被重复统计为失败。
                .Where(code => !itemProductCodeSet.Contains(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var requestItems = rawRequestItems;

            var requestedProductCodes = normalizedCodes
                .Concat(
                    requestItems
                        .Select(item => NormalizeCode(item.ProductCode))
                        .Where(code => code != null)
                        .Select(code => code!)
                )
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var requestedSupplierCodes = requestItems
                .Where(item => string.IsNullOrWhiteSpace(item.ProductCode))
                .Select(item => NormalizeCode(item.LocalSupplierCode))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var requestedItemNumbers = requestItems
                .Where(item => string.IsNullOrWhiteSpace(item.ProductCode))
                .Select(item => NormalizeCode(item.ItemNumber))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var queriedProducts =
                requestedProductCodes.Count == 0
                && requestedSupplierCodes.Count == 0
                && requestedItemNumbers.Count == 0
                    ? new List<Product>()
                    : await localDb.Queryable<Product>()
                        .Where(row =>
                            !row.IsDeleted
                            && (
                                (row.ProductCode != null && requestedProductCodes.Contains(row.ProductCode))
                                || (
                                    row.LocalSupplierCode != null
                                    && requestedSupplierCodes.Contains(row.LocalSupplierCode)
                                    && row.ItemNumber != null
                                    && requestedItemNumbers.Contains(row.ItemNumber)
                                )
                            )
                        )
                        .ToListAsync();
            var deduplicatedProducts = queriedProducts
                .Select(row => new
                {
                    Key = NormalizeCode(row.ProductCode) ?? NormalizeCode(row.UUID),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.Row.UpdatedAt ?? item.Row.CreatedAt)
                    .ThenByDescending(item => item.Row.CreatedAt)
                    .First()
                    .Row)
                .ToList();
            var productsByCode = deduplicatedProducts
                .Where(row => NormalizeCode(row.ProductCode) != null)
                .ToDictionary(row => NormalizeCode(row.ProductCode)!, StringComparer.OrdinalIgnoreCase);
            var productsBySupplierItem = deduplicatedProducts
                .Select(row => new
                {
                    Key = BuildSupplierItemKey(row.LocalSupplierCode, row.ItemNumber),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.Row).ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

            var resolvedProductCodes = new List<string>();
            var inventoryCandidates = new Dictionary<string, PushProductsToHqItem>(
                StringComparer.OrdinalIgnoreCase
            );
            var failedCandidateCount = 0;
            var itemFailureCount = 0;

            foreach (var productCode in normalizedCodes)
            {
                if (productsByCode.TryGetValue(productCode, out var product))
                {
                    AppendResolvedProductCode(resolvedProductCodes, product.ProductCode);
                    continue;
                }

                result.Errors.Add($"商品不存在或已删除: {productCode}");
                failedCandidateCount++;
            }

            foreach (var item in requestItems)
            {
                var errorCountBeforeResolve = result.Errors.Count;
                // 前端 IsNewProduct 可能来自未刷新的页面状态；后端只信任本地 Product 实时匹配结果。
                var matchedProduct = ResolveMatchedProduct(productsByCode, productsBySupplierItem, item, result);
                var finalProductCode = NormalizeCode(matchedProduct?.ProductCode);
                if (matchedProduct == null)
                {
                    if (result.Errors.Count > errorCountBeforeResolve)
                    {
                        failedCandidateCount++;
                        itemFailureCount++;
                    }
                    continue;
                }

                if (finalProductCode == null)
                {
                    result.Errors.Add($"匹配成功但最终商品编码为空: {DescribePushItem(item)}");
                    failedCandidateCount++;
                    itemFailureCount++;
                    continue;
                }

                AppendResolvedProductCode(resolvedProductCodes, finalProductCode);
                if (!inventoryCandidates.ContainsKey(finalProductCode))
                {
                    inventoryCandidates[finalProductCode] = new PushProductsToHqItem
                    {
                        ProductCode = finalProductCode,
                        LocalSupplierCode = NormalizeCode(item.LocalSupplierCode),
                        ItemNumber = NormalizeCode(item.ItemNumber),
                        ProductName = NormalizeCode(item.ProductName),
                        EnglishName = NormalizeCode(item.EnglishName),
                        Barcode = NormalizeCode(item.Barcode),
                        ImageUrl = NormalizeCode(item.ImageUrl),
                        DomesticPrice = item.DomesticPrice,
                        ImportPrice = item.ImportPrice,
                        OemPrice = item.OemPrice,
                        IsNewProduct = false,
                    };
                }
            }

            // 旧 ProductCodes 入口没有候选价格时，补一个仅带商品资料的库存候选，
            // 这样仍能创建/更新价格记录，但不会伪造仓库状态去改 HQ/POS 启用状态。
            foreach (var resolvedProductCode in resolvedProductCodes)
            {
                if (
                    inventoryCandidates.ContainsKey(resolvedProductCode)
                    || !productsByCode.TryGetValue(resolvedProductCode, out var resolvedProduct)
                )
                {
                    continue;
                }

                inventoryCandidates[resolvedProductCode] = new PushProductsToHqItem
                {
                    ProductCode = resolvedProductCode,
                    ProductName = NormalizeCode(resolvedProduct.ProductName),
                    EnglishName = NormalizeCode(resolvedProduct.EnglishName),
                    Barcode = NormalizeCode(resolvedProduct.Barcode),
                    ImageUrl = NormalizeCode(resolvedProduct.ProductImage),
                    IsNewProduct = false,
                };
            }

            result.TotalCount = resolvedProductCodes.Count + failedCandidateCount;
            var products = resolvedProductCodes
                .Where(productsByCode.ContainsKey)
                .Select(code => productsByCode[code])
                .ToList();
            var productCodesForDomesticImage = products
                .Select(row => NormalizeCode(row.ProductCode))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var domesticProductImages = productCodesForDomesticImage.Count == 0
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : (await localDb.Queryable<DomesticProduct>()
                    .Where(row =>
                        productCodesForDomesticImage.Contains(row.ProductCode)
                        && !row.IsDeleted
                        && row.ProductImage != null
                        && row.ProductImage != ""
                    )
                    .Select(row => new { row.ProductCode, row.ProductImage })
                    .ToListAsync())
                    .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => NormalizeCode(group.First().ProductImage) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase
                    );
            return new PushToHqSelection(
                products,
                inventoryCandidates,
                domesticProductImages,
                itemFailureCount
            );
        }

        private static Product? ResolveMatchedProduct(
            IReadOnlyDictionary<string, Product> productsByCode,
            IReadOnlyDictionary<string, List<Product>> productsBySupplierItem,
            PushProductsToHqItem item,
            PushProductsToHqResult result
        )
        {
            var productCode = NormalizeCode(item.ProductCode);
            if (productCode != null)
            {
                if (productsByCode.TryGetValue(productCode, out var matchedByCode))
                {
                    return matchedByCode;
                }

                result.Errors.Add($"商品不存在或已删除: {productCode}");
                return null;
            }

            var supplierItemKey = BuildSupplierItemKey(item.LocalSupplierCode, item.ItemNumber);
            if (supplierItemKey == null)
            {
                result.Errors.Add($"商品候选缺少有效匹配键: {DescribePushItem(item)}");
                return null;
            }

            if (!productsBySupplierItem.TryGetValue(supplierItemKey, out var matchedProducts))
            {
                result.Errors.Add($"未找到匹配商品: {DescribePushItem(item)}");
                return null;
            }

            if (matchedProducts.Count != 1)
            {
                result.Errors.Add($"匹配到多条本地商品: {DescribePushItem(item)}");
                return null;
            }

            return matchedProducts[0];
        }

        private static void AppendResolvedProductCode(List<string> productCodes, string? productCode)
        {
            var normalizedProductCode = NormalizeCode(productCode);
            if (
                normalizedProductCode == null
                || productCodes.Contains(normalizedProductCode, StringComparer.OrdinalIgnoreCase)
            )
            {
                return;
            }

            productCodes.Add(normalizedProductCode);
        }

        private static string DescribePushItem(PushProductsToHqItem item)
        {
            var productCode = NormalizeCode(item.ProductCode);
            if (productCode != null)
            {
                return $"商品编码={productCode}";
            }

            return $"供应商={NormalizeCode(item.LocalSupplierCode) ?? "NULL"}, 货号={NormalizeCode(item.ItemNumber) ?? "NULL"}";
        }

        private static async Task UpsertHqProductsAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
            IReadOnlyDictionary<string, PushProductsToHqItem> pushCandidates,
            IReadOnlyDictionary<string, string> domesticProductImages,
            HqProductSyncResult result
        )
        {
            var productCodes = products
                .Select(row => NormalizeCode(row.ProductCode))
                .Where(code => code != null)
                .Select(code => code!)
                .ToList();
            var existingCodes = (await hqDb.Queryable<DIC_商品信息字典表>()
                    .Where(row => row.H商品编码 != null && productCodes.Contains(row.H商品编码))
                    .Select(row => row.H商品编码)
                    .ToListAsync())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var inserts = new List<DIC_商品信息字典表>();
            foreach (var product in products)
            {
                var code = NormalizeCode(product.ProductCode);
                if (code == null)
                {
                    continue;
                }

                var hqProduct = MapProductToHqProduct(
                    product,
                    ResolvePushSupplierCode(product, pushCandidates),
                    ResolvePushCandidate(product, pushCandidates),
                    ResolveDomesticProductImage(product, domesticProductImages)
                );
                if (!existingCodes.Contains(code))
                {
                    inserts.Add(hqProduct);
                    existingCodes.Add(code);
                    continue;
                }

                // 商品字典只更新本地 POS 商品负责维护的字段，避免覆盖 HQ 其他业务字段。
                await hqDb.Updateable<DIC_商品信息字典表>()
                    .SetColumns(row => new DIC_商品信息字典表
                    {
                        H货号 = hqProduct.H货号,
                        H主条形码 = hqProduct.H主条形码,
                        H商品名称 = hqProduct.H商品名称,
                        H大写名称 = hqProduct.H大写名称,
                        H商品类型 = hqProduct.H商品类型,
                        H规格 = hqProduct.H规格,
                        H单位 = hqProduct.H单位,
                        H进货价 = hqProduct.H进货价,
                        H零售价 = hqProduct.H零售价,
                        H是否自动定价 = hqProduct.H是否自动定价,
                        H商品图片 = hqProduct.H商品图片,
                        中包数量 = hqProduct.中包数量,
                        H是否特殊商品 = hqProduct.H是否特殊商品,
                        H供货商编码 = hqProduct.H供货商编码,
                        CBP供应商编码 = hqProduct.CBP供应商编码,
                        FGC_LastModifier = hqProduct.FGC_LastModifier,
                        FGC_LastModifyDate = hqProduct.FGC_LastModifyDate,
                    })
                    .Where(row => row.H商品编码 == code)
                    .ExecuteCommandAsync();
                result.ProductsUpdated++;
            }

            if (inserts.Count > 0)
            {
                await hqDb.Insertable(inserts)
                    .IgnoreColumns(row => row.ID)
                    .ExecuteCommandAsync();
                result.ProductsAdded += inserts.Count;
            }
        }

        private static async Task UpsertHqWarehouseInventoriesAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
            IReadOnlyDictionary<string, PushProductsToHqItem> inventoryCandidates,
            PushProductsToHqResult result
        )
        {
            var inventoryProductCodes = products
                .Select(row => NormalizeCode(row.ProductCode))
                .Where(code => code != null && inventoryCandidates.ContainsKey(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (inventoryProductCodes.Count == 0)
            {
                return;
            }

            var productByCode = products
                .Where(row => NormalizeCode(row.ProductCode) != null)
                .ToDictionary(row => NormalizeCode(row.ProductCode)!, StringComparer.OrdinalIgnoreCase);
            var existingInventories = await hqDb.Queryable<CBP_DIC_商品库存表>()
                .Where(row => row.H商品编码 != null && inventoryProductCodes.Contains(row.H商品编码))
                .ToListAsync();
            var existingInventoryByCode = existingInventories
                .Where(row => !string.IsNullOrWhiteSpace(row.H商品编码))
                .ToDictionary(row => row.H商品编码!, StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;
            var inserts = new List<CBP_DIC_商品库存表>();

            foreach (var productCode in inventoryProductCodes)
            {
                var candidate = inventoryCandidates[productCode];
                if (!productByCode.TryGetValue(productCode, out var product))
                {
                    continue;
                }

                if (existingInventoryByCode.TryGetValue(productCode, out var existingInventory))
                {
                    await hqDb.Updateable<CBP_DIC_商品库存表>()
                        .SetColumns(row => new CBP_DIC_商品库存表
                        {
                            H国内价格 = candidate.DomesticPrice ?? existingInventory.H国内价格,
                            H进口价格 = candidate.ImportPrice ?? existingInventory.H进口价格,
                            H贴牌价格 = candidate.OemPrice ?? existingInventory.H贴牌价格,
                            FGC_LastModifier = "HBweb",
                            FGC_LastModifyDate = now,
                        })
                        .Where(row => row.H商品编码 == productCode)
                        .ExecuteCommandAsync();
                    result.WarehouseInventoriesUpdated++;
                    continue;
                }

                inserts.Add(new CBP_DIC_商品库存表
                {
                    HGUID = Guid.NewGuid().ToString(),
                    H商品编码 = productCode,
                    H国内价格 = candidate.DomesticPrice,
                    H进口价格 = candidate.ImportPrice,
                    H贴牌价格 = candidate.OemPrice,
                    H库存 = 0,
                    H最小订货量 = 0,
                    H库存金额 = 0,
                    H库存预警数 = 0,
                    // 新增库存记录仍按本地商品启用状态初始化，后续货柜发送不再改动该状态。
                    H使用状态 = product.IsActive ? 1 : 0,
                    FGC_Creator = "HBweb",
                    FGC_CreateDate = now,
                    FGC_LastModifier = "HBweb",
                    FGC_LastModifyDate = now,
                });
            }

            if (inserts.Count > 0)
            {
                await hqDb.Insertable(inserts)
                    .IgnoreColumns(row => row.ID)
                    .ExecuteCommandAsync();
                result.WarehouseInventoriesCreated += inserts.Count;
            }
        }

        private static async Task UpsertHqRetailPricesAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
            IReadOnlyDictionary<string, PushProductsToHqItem> pushCandidates,
            List<string> activeStoreCodes,
            HqProductSyncResult result
        )
        {
            if (activeStoreCodes.Count == 0)
            {
                return;
            }

            var productCodes = products
                .Select(row => NormalizeCode(row.ProductCode))
                .Where(code => code != null)
                .Select(code => code!)
                .ToList();
            var existingRows = await hqDb.Queryable<DIC_商品零售价表>()
                .Where(row =>
                    activeStoreCodes.Contains(row.H分店代码)
                    && productCodes.Contains(row.H商品编码)
                )
                .Select(row => new DIC_商品零售价表
                {
                    H分店代码 = row.H分店代码,
                    H商品编码 = row.H商品编码,
                })
                .ToListAsync();
            var existingKeys = existingRows
                .Select(row => BuildStoreProductKey(row.H分店代码, row.H商品编码))
                .Where(key => key != null)
                .Select(key => key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var inserts = new List<DIC_商品零售价表>();
            foreach (var storeCode in activeStoreCodes)
            {
                foreach (var product in products)
                {
                    var productCode = NormalizeCode(product.ProductCode);
                    var key = BuildStoreProductKey(storeCode, productCode);
                    if (productCode == null || key == null)
                    {
                        continue;
                    }

                    var hqPrice = MapProductToHqRetailPrice(
                        product,
                        storeCode,
                        ResolvePushCandidate(product, pushCandidates)
                    );
                    if (!existingKeys.Contains(key))
                    {
                        inserts.Add(hqPrice);
                        existingKeys.Add(key);
                        continue;
                    }

                    // 零售价更新不触碰库存、活动和动态销售字段。
                    await hqDb.Updateable<DIC_商品零售价表>()
                        .SetColumns(row => new DIC_商品零售价表
                        {
                            H分店商品编码 = hqPrice.H分店商品编码,
                            H供应商编码 = hqPrice.H供应商编码,
                            H分店供应商编码 = hqPrice.H分店供应商编码,
                            H进货价 = hqPrice.H进货价,
                            H分店零售价 = hqPrice.H分店零售价,
                            H是否自动定价 = hqPrice.H是否自动定价,
                            H是否特殊商品 = hqPrice.H是否特殊商品,
                            FGC_LastModifier = hqPrice.FGC_LastModifier,
                            FGC_LastModifyDate = hqPrice.FGC_LastModifyDate,
                        })
                        .Where(row => row.H分店代码 == storeCode && row.H商品编码 == productCode)
                        .ExecuteCommandAsync();
                    result.StoreRetailPricesUpdated++;
                }
            }

            if (inserts.Count > 0)
            {
                await hqDb.Insertable(inserts)
                    .IgnoreColumns(row => row.ID)
                    .ExecuteCommandAsync();
                result.StoreRetailPricesCreated += inserts.Count;
            }
        }

        private static async Task UpsertHqProductSetCodesAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
            List<ProductSetCode> productSetCodes,
            HqProductSyncResult result
        )
        {
            if (productSetCodes.Count == 0)
            {
                return;
            }

            var productByCode = products
                .Where(row => NormalizeCode(row.ProductCode) != null)
                .ToDictionary(row => NormalizeCode(row.ProductCode)!, StringComparer.OrdinalIgnoreCase);
            var productCodes = productByCode.Keys.ToList();
            var existingRows = await hqDb.Queryable<DIC_一品多码表>()
                .Where(row => row.H商品编码 != null && productCodes.Contains(row.H商品编码))
                .Select(row => new DIC_一品多码表
                {
                    ID = row.ID,
                    HGUID = row.HGUID,
                    H商品编码 = row.H商品编码,
                    H多码商品编号 = row.H多码商品编号,
                    H多条形码 = row.H多条形码,
                })
                .ToListAsync();
            var existingByBusinessKey = existingRows
                .Select(row => new
                {
                    Key = BuildProductSetCodeBusinessKey(row.H商品编码, row.H多码商品编号),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var existingByGuidKey = existingRows
                .Select(row => new
                {
                    Key = BuildProductSetCodeBusinessKey(row.H商品编码, row.HGUID),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var existingByBarcodeKey = existingRows
                .Select(row => new
                {
                    Key = BuildProductSetCodeBusinessKey(row.H商品编码, row.H多条形码),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var allocatedProductSetPurchasePrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (
                var group in productSetCodes
                    .Select(row => new
                    {
                        ProductCode = NormalizeCode(row.ProductCode),
                        SetCode = row,
                    })
                    .Where(item => item.ProductCode != null && productByCode.ContainsKey(item.ProductCode))
                    .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
            )
            {
                var product = productByCode[group.Key];
                // HQ 一品多码进货价以本地主商品进货价为总额，按全局套装子码零售价比例分摊。
                var allocations = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                    group.Select(item => item.SetCode),
                    product.PurchasePrice,
                    setCode => setCode.SetProductCode,
                    setCode => setCode.SetRetailPrice
                );
                foreach (var allocation in allocations)
                {
                    var key = BuildProductSetCodeBusinessKey(group.Key, allocation.Key);
                    if (key != null)
                    {
                        allocatedProductSetPurchasePrices[key] = allocation.Value;
                    }
                }
            }

            var inserts = new List<DIC_一品多码表>();
            foreach (var setCode in productSetCodes)
            {
                var productCode = NormalizeCode(setCode.ProductCode);
                var setProductCode = NormalizeCode(setCode.SetProductCode);
                var key = BuildProductSetCodeBusinessKey(setCode.ProductCode, setCode.SetProductCode);
                if (
                    productCode == null
                    || setProductCode == null
                    || key == null
                    || !productByCode.TryGetValue(productCode, out var product)
                )
                {
                    continue;
                }

                allocatedProductSetPurchasePrices.TryGetValue(key, out var allocatedPurchasePrice);
                var hqSetCode = MapProductSetCodeToHq(
                    setCode,
                    product,
                    allocatedProductSetPurchasePrices.ContainsKey(key) ? allocatedPurchasePrice : null
                );
                var guidKey = BuildProductSetCodeBusinessKey(productCode, hqSetCode.HGUID);
                var barcodeKey = BuildProductSetCodeBusinessKey(productCode, hqSetCode.H多条形码);
                var existing =
                    existingByBusinessKey.GetValueOrDefault(key)
                    ?? (guidKey == null ? null : existingByGuidKey.GetValueOrDefault(guidKey))
                    ?? (barcodeKey == null ? null : existingByBarcodeKey.GetValueOrDefault(barcodeKey));
                if (existing == null)
                {
                    inserts.Add(hqSetCode);
                    existingByBusinessKey[key] = hqSetCode;
                    if (guidKey != null)
                    {
                        existingByGuidKey[guidKey] = hqSetCode;
                    }
                    if (barcodeKey != null)
                    {
                        existingByBarcodeKey[barcodeKey] = hqSetCode;
                    }
                    continue;
                }

                await hqDb.Updateable<DIC_一品多码表>()
                    .SetColumns(row => new DIC_一品多码表
                    {
                        H供应商编码 = hqSetCode.H供应商编码,
                        H主条形码 = hqSetCode.H主条形码,
                        H多条形码 = hqSetCode.H多条形码,
                        H进货价 = hqSetCode.H进货价,
                        H一品多码零售价 = hqSetCode.H一品多码零售价,
                        H使用状态 = hqSetCode.H使用状态,
                        H是否自动定价 = hqSetCode.H是否自动定价,
                        FGC_LastModifier = hqSetCode.FGC_LastModifier,
                        FGC_LastModifyDate = hqSetCode.FGC_LastModifyDate,
                    })
                    .Where(row => row.ID == existing.ID)
                    .ExecuteCommandAsync();
                result.ProductSetCodesUpdated++;
            }

            if (inserts.Count > 0)
            {
                await hqDb.Insertable(inserts)
                    .IgnoreColumns(row => row.ID)
                    .ExecuteCommandAsync();
                result.ProductSetCodesCreated += inserts.Count;
            }
        }

        private static async Task UpsertHqStoreMultiCodesAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
            List<ProductSetCode> productSetCodes,
            List<StoreMultiCodeProduct> storeMultiCodes,
            List<string> activeStoreCodes,
            HqProductSyncResult result
        )
        {
            if (productSetCodes.Count == 0 || activeStoreCodes.Count == 0)
            {
                return;
            }

            var productByCode = products
                .Where(row => NormalizeCode(row.ProductCode) != null)
                .ToDictionary(row => NormalizeCode(row.ProductCode)!, StringComparer.OrdinalIgnoreCase);
            var productCodes = productByCode.Keys.ToList();
            var existingRows = await hqDb.Queryable<DIC_分店一品多码表>()
                .Where(row =>
                    row.H分店代码 != null
                    && activeStoreCodes.Contains(row.H分店代码)
                    && row.H商品编码 != null
                    && productCodes.Contains(row.H商品编码)
                )
                .Select(row => new DIC_分店一品多码表
                {
                    ID = row.ID,
                    HGUID = row.HGUID,
                    H分店代码 = row.H分店代码,
                    H商品编码 = row.H商品编码,
                    H多码商品编码 = row.H多码商品编码,
                    H分店多码商品编码 = row.H分店多码商品编码,
                    H多条形码 = row.H多条形码,
                })
                .ToListAsync();
            var existingByBusinessKey = existingRows
                .Select(row => new
                {
                    Key = BuildStoreMultiCodeKey(row.H分店代码, row.H商品编码, row.H多码商品编码),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var existingByGuidKey = existingRows
                .Select(row => new
                {
                    Key = BuildStoreMultiCodeKey(row.H分店代码, row.H商品编码, row.HGUID),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var existingByBarcodeKey = existingRows
                .Select(row => new
                {
                    Key = BuildStoreMultiCodeKey(row.H分店代码, row.H商品编码, row.H多条形码),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var existingByStoreMultiProductKey = existingRows
                .Select(row => new
                {
                    Key = BuildStoreMultiCodeKey(row.H分店代码, row.H商品编码, row.H分店多码商品编码),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var storeMultiCodeByKey = storeMultiCodes
                .Select(row => new
                {
                    Key = BuildStoreMultiCodeKey(
                        row.StoreCode,
                        row.ProductCode,
                        row.MultiCodeProductCode
                    ),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);
            var allocatedStoreMultiPurchasePrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var storeCode in activeStoreCodes)
            {
                foreach (
                    var group in productSetCodes
                        .Select(row => new
                        {
                            ProductCode = NormalizeCode(row.ProductCode),
                            SetCode = row,
                        })
                        .Where(item => item.ProductCode != null && productByCode.ContainsKey(item.ProductCode))
                        .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
                )
                {
                    var product = productByCode[group.Key];
                    // 分店一品多码优先使用分店子码零售价参与分摊；没有分店价时回退全局子码零售价。
                    var allocations = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                        group.Select(item => item.SetCode),
                        product.PurchasePrice,
                        setCode => setCode.SetProductCode,
                        setCode =>
                        {
                            var setProductCode = NormalizeCode(setCode.SetProductCode);
                            var storeMultiKey = BuildStoreMultiCodeKey(storeCode, group.Key, setProductCode);
                            return storeMultiKey != null
                                && storeMultiCodeByKey.TryGetValue(storeMultiKey, out var storeMultiCode)
                                ? storeMultiCode.MultiCodeRetailPrice ?? setCode.SetRetailPrice
                                : setCode.SetRetailPrice;
                        }
                    );
                    foreach (var allocation in allocations)
                    {
                        var key = BuildStoreMultiCodeKey(storeCode, group.Key, allocation.Key);
                        if (key != null)
                        {
                            allocatedStoreMultiPurchasePrices[key] = allocation.Value;
                        }
                    }
                }
            }

            var inserts = new List<DIC_分店一品多码表>();
            foreach (var storeCode in activeStoreCodes)
            {
                foreach (var setCode in productSetCodes)
                {
                    var productCode = NormalizeCode(setCode.ProductCode);
                    var multiCode = NormalizeCode(setCode.SetProductCode);
                    var key = BuildStoreMultiCodeKey(storeCode, productCode, multiCode);
                    if (
                        productCode == null
                        || multiCode == null
                        || key == null
                        || !productByCode.TryGetValue(productCode, out var product)
                    )
                    {
                        continue;
                    }

                    storeMultiCodeByKey.TryGetValue(key, out var storeMultiCode);
                    allocatedStoreMultiPurchasePrices.TryGetValue(key, out var allocatedPurchasePrice);
                    var hqStoreMultiCode = MapStoreMultiCodeToHq(
                        storeCode,
                        product,
                        setCode,
                        storeMultiCode,
                        allocatedStoreMultiPurchasePrices.ContainsKey(key) ? allocatedPurchasePrice : null
                    );
                    var guidKey = BuildStoreMultiCodeKey(storeCode, productCode, hqStoreMultiCode.HGUID);
                    var barcodeKey = BuildStoreMultiCodeKey(storeCode, productCode, hqStoreMultiCode.H多条形码);
                    var storeMultiProductKey = BuildStoreMultiCodeKey(storeCode, productCode, hqStoreMultiCode.H分店多码商品编码);
                    var existing =
                        existingByBusinessKey.GetValueOrDefault(key)
                        ?? (guidKey == null ? null : existingByGuidKey.GetValueOrDefault(guidKey))
                        ?? (barcodeKey == null ? null : existingByBarcodeKey.GetValueOrDefault(barcodeKey))
                        ?? (storeMultiProductKey == null ? null : existingByStoreMultiProductKey.GetValueOrDefault(storeMultiProductKey));
                    if (existing == null)
                    {
                        inserts.Add(hqStoreMultiCode);
                        existingByBusinessKey[key] = hqStoreMultiCode;
                        if (guidKey != null)
                        {
                            existingByGuidKey[guidKey] = hqStoreMultiCode;
                        }
                        if (barcodeKey != null)
                        {
                            existingByBarcodeKey[barcodeKey] = hqStoreMultiCode;
                        }
                        if (storeMultiProductKey != null)
                        {
                            existingByStoreMultiProductKey[storeMultiProductKey] = hqStoreMultiCode;
                        }
                        continue;
                    }

                    // 兼容历史错码命中时，保留 HQ 既有业务编码，避免把 HGUID 回写成多码商品编码。
                    hqStoreMultiCode.H分店多码商品编码 = existing.H分店多码商品编码;

                    // 分店一品多码更新不触碰库存、活动和动态销售字段。
                    await hqDb.Updateable<DIC_分店一品多码表>()
                        .SetColumns(row => new DIC_分店一品多码表
                        {
                            H分店商品编码 = hqStoreMultiCode.H分店商品编码,
                            H分店多码商品编码 = hqStoreMultiCode.H分店多码商品编码,
                            H供应商编码 = hqStoreMultiCode.H供应商编码,
                            H主条形码 = hqStoreMultiCode.H主条形码,
                            H多条形码 = hqStoreMultiCode.H多条形码,
                            H进货价 = hqStoreMultiCode.H进货价,
                            H折扣率 = hqStoreMultiCode.H折扣率,
                            H一品多码零售价 = hqStoreMultiCode.H一品多码零售价,
                            H是否自动定价 = hqStoreMultiCode.H是否自动定价,
                            H是否特殊商品 = hqStoreMultiCode.H是否特殊商品,
                            H使用状态 = hqStoreMultiCode.H使用状态,
                            FGC_LastModifier = hqStoreMultiCode.FGC_LastModifier,
                            FGC_LastModifyDate = hqStoreMultiCode.FGC_LastModifyDate,
                        })
                        .Where(row => row.ID == existing.ID)
                        .ExecuteCommandAsync();
                    result.StoreMultiCodesUpdated++;
                }
            }

            if (inserts.Count > 0)
            {
                await hqDb.Insertable(inserts)
                    .IgnoreColumns(row => row.ID)
                    .ExecuteCommandAsync();
                result.StoreMultiCodesCreated += inserts.Count;
            }
        }

        private async Task SyncFullWithShadowAsync(ISqlSugarClient db, HqProductSyncResult result)
        {
            var syncRunId = await db.Ado.SqlQuerySingleAsync<long>(
                """
                DECLARE @SyncRunId BIGINT;
                EXEC dbo.usp_ProductShadow_Prepare
                    @SyncRunId = @SyncRunId OUTPUT,
                    @TriggeredBy = N'ProductHqSyncService',
                    @DropExistingShadow = 1;
                SELECT @SyncRunId;
                """
            );

            var hqRows = await QueryActiveHqProductsAsync();
            var products = hqRows.Select(MapNewProduct).ToList();
            foreach (var batch in products.Chunk(WriteBatchSize))
            {
                await db.Fastest<Product>()
                    .AS(ShadowTableName)
                    .PageSize(WriteBatchSize)
                    .BulkCopyAsync(batch.ToList());
            }

            await db.Ado.ExecuteCommandAsync(
                "EXEC dbo.usp_ProductShadow_Validate @SyncRunId, @SourceRowCount",
                new SugarParameter("@SyncRunId", syncRunId),
                new SugarParameter("@SourceRowCount", hqRows.Count)
            );
            await db.Ado.ExecuteCommandAsync(
                "EXEC dbo.usp_ProductShadow_Swap @SyncRunId",
                new SugarParameter("@SyncRunId", syncRunId)
            );

            var run = await db.Ado.SqlQuerySingleAsync<ProductShadowRunRow>(
                "SELECT SyncRunId, SourceRowCount, ShadowRowCount, BackupTableName FROM dbo.ProductHqSyncRun WHERE SyncRunId = @SyncRunId",
                new SugarParameter("@SyncRunId", syncRunId)
            );

            result.SyncRunId = syncRunId;
            result.SourceRowCount = run?.SourceRowCount ?? hqRows.Count;
            result.ShadowRowCount = run?.ShadowRowCount ?? products.Count;
            result.ProductsSwapped = true;
            result.BackupTableName = run?.BackupTableName;
            result.ProductsAdded = products.Count;
            result.TotalHqProducts = hqRows.Count;
        }

        private async Task SyncFullDirectAsync(ISqlSugarClient db, HqProductSyncResult result)
        {
            var hqRows = await QueryActiveHqProductsAsync();
            result.SourceRowCount = hqRows.Count;
            result.TotalHqProducts = hqRows.Count;

            var activeHqCodes = hqRows
                .Select(row => NormalizeCode(row.H商品编码))
                .Where(code => code != null)
                .Select(code => code!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var localRows = await db.Queryable<Product>()
                .Where(row => row.ProductCode != null)
                .ToListAsync();
            var localByCode = localRows
                .Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
                .GroupBy(row => row.ProductCode!)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var hqRow in hqRows)
            {
                var code = NormalizeCode(hqRow.H商品编码);
                if (code == null)
                {
                    continue;
                }

                if (localByCode.TryGetValue(code, out var local))
                {
                    ApplyProductUpdate(hqRow, local);
                    await db.Updateable(local).ExecuteCommandAsync();
                    result.ProductsUpdated++;
                }
                else
                {
                    await db.Insertable(MapNewProduct(hqRow)).ExecuteCommandAsync();
                    result.ProductsAdded++;
                }
            }

            var now = DateTime.UtcNow;
            var softDeleteRows = localRows
                .Where(row =>
                    !row.IsDeleted
                    && !string.IsNullOrWhiteSpace(row.ProductCode)
                    && !activeHqCodes.Contains(row.ProductCode!)
                )
                .ToList();
            foreach (var row in softDeleteRows)
            {
                row.IsDeleted = true;
                row.IsActive = false;
                row.UpdatedAt = now;
                await db.Updateable(row).ExecuteCommandAsync();
            }

            result.ProductsSoftDeleted = softDeleteRows.Count;
            result.ShadowRowCount = hqRows.Count;
            result.ProductsSwapped = true;
        }

        private async Task<ProductIncrementalSnapshot> SyncProductsIncrementalCoreAsync(
            ISqlSugarClient db,
            DateTime effectiveStart,
            HqProductSyncResult result
        )
        {
            var hqIndexRows = await _hqContext.Db.Queryable<DIC_商品信息字典表>()
                .Where(row => row.H使用状态 == true && !string.IsNullOrEmpty(row.H商品编码))
                .Select(row => new DIC_商品信息字典表
                {
                    H商品编码 = row.H商品编码,
                    FGC_LastModifyDate = row.FGC_LastModifyDate,
                })
                .ToListAsync();
            var activeHqCodes = hqIndexRows
                .Select(row => NormalizeCode(row.H商品编码))
                .Where(code => code != null)
                .Select(code => code!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.TotalHqProducts = activeHqCodes.Count;

            var changedRows = await _hqContext.Db.Queryable<DIC_商品信息字典表>()
                .Where(row => row.FGC_LastModifyDate >= effectiveStart)
                .ToListAsync();
            var activeChangedRows = changedRows
                .Where(row => row.H使用状态 && !string.IsNullOrWhiteSpace(row.H商品编码))
                .GroupBy(row => row.H商品编码!)
                .Select(group => group.OrderByDescending(row => row.FGC_LastModifyDate).First())
                .ToList();

            var localRows = await db.Queryable<Product>()
                .Where(row => row.ProductCode != null)
                .ToListAsync();
            result.TotalLocalProducts = localRows.Count;
            var localByCode = localRows
                .Where(row => !string.IsNullOrWhiteSpace(row.ProductCode))
                .GroupBy(row => row.ProductCode!)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var hqRow in activeChangedRows)
            {
                var code = NormalizeCode(hqRow.H商品编码);
                if (code == null)
                {
                    continue;
                }

                if (localByCode.TryGetValue(code, out var local))
                {
                    ApplyProductUpdate(hqRow, local);
                    await db.Updateable(local).ExecuteCommandAsync();
                    result.ProductsUpdated++;
                }
                else
                {
                    var product = MapNewProduct(hqRow);
                    await db.Insertable(product).ExecuteCommandAsync();
                    localByCode[code] = product;
                    result.ProductsAdded++;
                }
            }

            var now = DateTime.UtcNow;
            var softDeletedCodes = localRows
                .Where(row =>
                    !row.IsDeleted
                    && !string.IsNullOrWhiteSpace(row.ProductCode)
                    && !activeHqCodes.Contains(row.ProductCode!)
                )
                .Select(row => row.ProductCode!)
                .ToList();

            if (softDeletedCodes.Count > 0)
            {
                var affected = await db.Updateable<Product>()
                    .SetColumns(row => new Product
                    {
                        IsDeleted = true,
                        IsActive = false,
                        UpdatedAt = now,
                    })
                    .Where(row => softDeletedCodes.Contains(row.ProductCode!))
                    .ExecuteCommandAsync();
                result.ProductsSoftDeleted = affected;

                var associationResult = await SoftDeleteProductAssociationsAsync(
                    db,
                    softDeletedCodes,
                    now
                );
                result.StoreRetailPricesDeleted += associationResult.StoreRetailPricesDeleted;
                result.StoreMultiCodesDeleted += associationResult.StoreMultiCodesDeleted;
            }

            return new ProductIncrementalSnapshot(activeHqCodes, softDeletedCodes);
        }

        private static async Task<ProductAssociationDeleteResult> SoftDeleteProductAssociationsAsync(
            ISqlSugarClient db,
            List<string> productCodes,
            DateTime now
        )
        {
            var retailPricesDeleted = 0;
            var storeMultiCodesDeleted = 0;
            var normalizedCodes = productCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var chunk in normalizedCodes.Chunk(1000))
            {
                var codes = chunk.ToList();

                // 商品被 HQ 删除时，直接按 ProductCode 清理分店价格，避免前台继续查到孤儿价格。
                retailPricesDeleted += await db.Updateable<StoreRetailPrice>()
                    .SetColumns(row => new StoreRetailPrice
                    {
                        IsDeleted = true,
                        IsActive = false,
                        UpdatedAt = now,
                    })
                    .Where(row =>
                        !row.IsDeleted
                        && row.ProductCode != null
                        && codes.Contains(row.ProductCode)
                    )
                    .ExecuteCommandAsync();

                // 分店一品多码同样随商品删除软删；ProductSetCode 仍由专用同步链路处理。
                storeMultiCodesDeleted += await db.Updateable<StoreMultiCodeProduct>()
                    .SetColumns(row => new StoreMultiCodeProduct
                    {
                        IsDeleted = true,
                        IsActive = false,
                        UpdatedAt = now,
                    })
                    .Where(row =>
                        !row.IsDeleted
                        && row.ProductCode != null
                        && codes.Contains(row.ProductCode)
                    )
                    .ExecuteCommandAsync();
            }

            return new ProductAssociationDeleteResult(retailPricesDeleted, storeMultiCodesDeleted);
        }

        private async Task SyncProductSetCodesIncrementalCoreAsync(
            ISqlSugarClient db,
            DateTime effectiveStart,
            HashSet<string> activeProductCodes,
            List<string> softDeletedProductCodes,
            HqProductSyncResult result
        )
        {
            var hqCurrentRows = await _hqContext.Db.Queryable<DIC_一品多码表>()
                .Where(row =>
                    row.H使用状态 == true
                    && !string.IsNullOrEmpty(row.H商品编码)
                    && !string.IsNullOrEmpty(row.H多码商品编号)
                )
                .ToListAsync();
            hqCurrentRows = hqCurrentRows
                .Where(row => activeProductCodes.Contains(row.H商品编码!))
                .ToList();

            var hqCurrentGuidKeys = hqCurrentRows
                .Select(row => NormalizeCode(row.HGUID))
                .Where(key => key != null)
                .Select(key => key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hqCurrentBusinessKeys = hqCurrentRows
                .Select(row => BuildProductSetCodeBusinessKey(row.H商品编码, row.H多码商品编号))
                .Where(key => key != null)
                .Select(key => key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var localRows = await db.Queryable<ProductSetCode>().ToListAsync();
            var byGuid = localRows
                .Where(row => !string.IsNullOrWhiteSpace(row.SetCodeId))
                .GroupBy(row => row.SetCodeId)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var byBusinessKey = localRows
                .Select(row => new
                {
                    Key = BuildProductSetCodeBusinessKey(row.ProductCode, row.SetProductCode),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!)
                .ToDictionary(group => group.Key, group => group.First().Row, StringComparer.OrdinalIgnoreCase);

            var changedRows = await _hqContext.Db.Queryable<DIC_一品多码表>()
                .Where(row => row.FGC_LastModifyDate >= effectiveStart)
                .ToListAsync();

            var softDeletedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hqRow in changedRows)
            {
                var local = FindProductSetCode(hqRow, byGuid, byBusinessKey);
                if (hqRow.H使用状态 != true)
                {
                    if (local != null)
                    {
                        await SoftDeleteProductSetCodeAsync(db, local, softDeletedIds);
                    }
                    continue;
                }

                if (
                    string.IsNullOrWhiteSpace(hqRow.H商品编码)
                    || string.IsNullOrWhiteSpace(hqRow.H多码商品编号)
                    || !activeProductCodes.Contains(hqRow.H商品编码!)
                )
                {
                    continue;
                }

                if (local == null)
                {
                    local = MapNewProductSetCode(hqRow);
                    await db.Insertable(local).ExecuteCommandAsync();
                    result.ProductSetCodesAdded++;
                    byGuid[local.SetCodeId] = local;
                    var businessKey = BuildProductSetCodeBusinessKey(local.ProductCode, local.SetProductCode);
                    if (businessKey != null)
                    {
                        byBusinessKey[businessKey] = local;
                    }
                    continue;
                }

                await NormalizeProductSetCodeIdAsync(db, local, hqRow.HGUID, byGuid);
                ApplyProductSetCodeUpdate(hqRow, local);
                await db.Updateable(local).ExecuteCommandAsync();
                result.ProductSetCodesUpdated++;
            }

            var now = DateTime.UtcNow;
            var softDeletedProductCodeSet = softDeletedProductCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var local in localRows.Where(row => !row.IsDeleted))
            {
                var hasGuidMatch =
                    !string.IsNullOrWhiteSpace(local.SetCodeId)
                    && hqCurrentGuidKeys.Contains(local.SetCodeId);
                var businessKey = BuildProductSetCodeBusinessKey(local.ProductCode, local.SetProductCode);
                var hasBusinessMatch =
                    businessKey != null && hqCurrentBusinessKeys.Contains(businessKey);
                var productWasDeleted = softDeletedProductCodeSet.Contains(local.ProductCode);

                if (productWasDeleted || (!hasGuidMatch && !hasBusinessMatch))
                {
                    local.IsDeleted = true;
                    local.IsActive = false;
                    local.UpdatedAt = now;
                    await db.Updateable(local).ExecuteCommandAsync();
                    softDeletedIds.Add(local.SetCodeId);
                }
            }

            result.ProductSetCodesSoftDeleted = softDeletedIds.Count;
        }

        private async Task<List<DIC_商品信息字典表>> QueryActiveHqProductsAsync()
        {
            var rows = new List<DIC_商品信息字典表>();
            var lastId = 0;
            while (true)
            {
                var batch = await _hqContext.Db.Queryable<DIC_商品信息字典表>()
                    .Where(row =>
                        row.ID > lastId
                        && row.H使用状态 == true
                        && !string.IsNullOrEmpty(row.H商品编码)
                    )
                    .OrderBy(row => row.ID)
                    .Take(HqReadBatchSize)
                    .ToListAsync();

                if (batch.Count == 0)
                {
                    break;
                }

                rows.AddRange(batch);
                lastId = batch[^1].ID;
            }

            return rows
                .GroupBy(row => row.H商品编码!)
                .Select(group => group.OrderByDescending(row => row.FGC_LastModifyDate).First())
                .ToList();
        }

        private Product MapNewProduct(DIC_商品信息字典表 hqRow)
        {
            var product = _mapper.Map<Product>(hqRow);
            product.UUID = NormalizeCode(hqRow.HGUID) ?? UuidHelper.GenerateUuid7();
            product.ProductCode = NormalizeCode(hqRow.H商品编码);
            product.EnglishName = Truncate(hqRow.H大写名称, 200);
            product.IsDeleted = false;
            product.CreatedAt = hqRow.FGC_CreateDate == default ? DateTime.UtcNow : hqRow.FGC_CreateDate;
            product.UpdatedAt = DateTime.UtcNow;
            return product;
        }

        private void ApplyProductUpdate(DIC_商品信息字典表 hqRow, Product local)
        {
            var uuid = local.UUID;
            var createdAt = local.CreatedAt;
            var createdBy = local.CreatedBy;
            _mapper.Map(hqRow, local);
            local.UUID = uuid;
            local.ProductCode = NormalizeCode(hqRow.H商品编码);
            local.CreatedAt = createdAt;
            local.CreatedBy = createdBy;
            local.EnglishName = Truncate(hqRow.H大写名称, 200);
            local.IsDeleted = false;
            local.UpdatedAt = DateTime.UtcNow;
        }

        private ProductSetCode MapNewProductSetCode(DIC_一品多码表 hqRow)
        {
            var row = _mapper.Map<ProductSetCode>(hqRow);
            row.SetCodeId = NormalizeCode(hqRow.HGUID) ?? UuidHelper.GenerateUuid7();
            row.ProductCode = NormalizeCode(hqRow.H商品编码) ?? string.Empty;
            row.SetProductCode = NormalizeCode(hqRow.H多码商品编号) ?? string.Empty;
            row.SetItemNumber = row.SetProductCode;
            row.SetBarcode = NormalizeCode(hqRow.H多条形码) ?? NormalizeCode(hqRow.H主条形码);
            row.SetQuantity = 1;
            row.SetType = 2;
            row.IsActive = hqRow.H使用状态 ?? true;
            row.IsDeleted = false;
            row.CreatedAt = hqRow.FGC_CreateDate ?? DateTime.UtcNow;
            row.CreatedBy = hqRow.FGC_Creator;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = hqRow.FGC_LastModifier;
            return row;
        }

        private static void ApplyProductSetCodeUpdate(DIC_一品多码表 hqRow, ProductSetCode local)
        {
            local.ProductCode = NormalizeCode(hqRow.H商品编码) ?? string.Empty;
            local.SetProductCode = NormalizeCode(hqRow.H多码商品编号) ?? string.Empty;
            local.SetItemNumber = local.SetProductCode;
            local.SetBarcode = NormalizeCode(hqRow.H多条形码) ?? NormalizeCode(hqRow.H主条形码);
            local.SetPurchasePrice = hqRow.H进货价;
            local.SetRetailPrice = hqRow.H一品多码零售价;
            local.SetQuantity = local.SetQuantity <= 0 ? 1 : local.SetQuantity;
            local.SetType = 2;
            local.IsActive = hqRow.H使用状态 ?? true;
            local.IsDeleted = false;
            local.UpdatedAt = DateTime.UtcNow;
            local.UpdatedBy = hqRow.FGC_LastModifier;
        }

        private static ProductSetCode? FindProductSetCode(
            DIC_一品多码表 hqRow,
            Dictionary<string, ProductSetCode> byGuid,
            Dictionary<string, ProductSetCode> byBusinessKey
        )
        {
            var hguid = NormalizeCode(hqRow.HGUID);
            if (hguid != null && byGuid.TryGetValue(hguid, out var guidMatch))
            {
                return guidMatch;
            }

            var businessKey = BuildProductSetCodeBusinessKey(hqRow.H商品编码, hqRow.H多码商品编号);
            return businessKey != null && byBusinessKey.TryGetValue(businessKey, out var businessMatch)
                ? businessMatch
                : null;
        }

        private static async Task SoftDeleteProductSetCodeAsync(
            ISqlSugarClient db,
            ProductSetCode local,
            HashSet<string> softDeletedIds
        )
        {
            if (local.IsDeleted)
            {
                return;
            }

            local.IsDeleted = true;
            local.IsActive = false;
            local.UpdatedAt = DateTime.UtcNow;
            await db.Updateable(local).ExecuteCommandAsync();
            softDeletedIds.Add(local.SetCodeId);
        }

        private static async Task NormalizeProductSetCodeIdAsync(
            ISqlSugarClient db,
            ProductSetCode local,
            string? hguid,
            Dictionary<string, ProductSetCode> byGuid
        )
        {
            var normalizedGuid = NormalizeCode(hguid);
            if (
                normalizedGuid == null
                || local.SetCodeId == normalizedGuid
                || byGuid.ContainsKey(normalizedGuid)
            )
            {
                return;
            }

            var oldId = local.SetCodeId;
            await db.Ado.ExecuteCommandAsync(
                "UPDATE ProductSetCode SET SetCodeId = @NewId WHERE SetCodeId = @OldId",
                new SugarParameter("@NewId", normalizedGuid),
                new SugarParameter("@OldId", oldId)
            );
            byGuid.Remove(oldId);
            local.SetCodeId = normalizedGuid;
            byGuid[normalizedGuid] = local;
        }

        private static List<T> DeduplicateByBusinessKey<T>(
            IEnumerable<T> rows,
            Func<T, string?> keySelector
        )
            where T : BaseEntity
        {
            // 本地异常重复数据不应在一次 HQ upsert 中制造重复记录；同键取最近更新的一条。
            return rows
                .Select(row => new
                {
                    Key = NormalizeCode(keySelector(row)),
                    Row = row,
                })
                .Where(item => item.Key != null)
                .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.Row.UpdatedAt ?? item.Row.CreatedAt)
                    .ThenByDescending(item => item.Row.CreatedAt)
                    .First()
                    .Row)
                .ToList();
        }

        private static string ResolvePushSupplierCode(
            Product product,
            IReadOnlyDictionary<string, PushProductsToHqItem> pushCandidates
        )
        {
            var candidate = ResolvePushCandidate(product, pushCandidates);
            if (candidate != null)
            {
                // 关键位置：货柜发送 HQ 时，以本次明细候选的国内供应商代码优先。
                return NormalizeCode(candidate.LocalSupplierCode)
                    ?? NormalizeCode(product.LocalSupplierCode)
                    ?? string.Empty;
            }

            return NormalizeCode(product.LocalSupplierCode) ?? string.Empty;
        }

        private static PushProductsToHqItem? ResolvePushCandidate(
            Product product,
            IReadOnlyDictionary<string, PushProductsToHqItem> pushCandidates
        )
        {
            var productCode = NormalizeCode(product.ProductCode);
            return productCode != null && pushCandidates.TryGetValue(productCode, out var candidate)
                ? candidate
                : null;
        }

        private static string? ResolveDomesticProductImage(
            Product product,
            IReadOnlyDictionary<string, string> domesticProductImages
        )
        {
            var productCode = NormalizeCode(product.ProductCode);
            return productCode != null && domesticProductImages.TryGetValue(productCode, out var imageUrl)
                ? NormalizeCode(imageUrl)
                : null;
        }

        private static DIC_商品信息字典表 MapProductToHqProduct(
            Product product,
            string supplierCode,
            PushProductsToHqItem? candidate = null,
            string? domesticProductImage = null
        )
        {
            var now = DateTime.Now;
            var productCode = NormalizeCode(product.ProductCode) ?? string.Empty;
            var productName = NormalizeCode(candidate?.ProductName)
                ?? NormalizeCode(product.ProductName)
                ?? string.Empty;
            var displayName = NormalizeCode(candidate?.EnglishName)
                ?? NormalizeCode(product.EnglishName)
                ?? productName;
            var purchasePrice = candidate?.ImportPrice ?? product.PurchasePrice ?? 0;
            var retailPrice = candidate?.OemPrice ?? product.RetailPrice ?? 0;
            return new DIC_商品信息字典表
            {
                HGUID = NormalizeCode(product.UUID) ?? UuidHelper.GenerateUuid7(),
                H商品标签GUID = string.Empty,
                H商品分类码GUID = string.Empty,
                H商品编码 = productCode,
                H货号 = NormalizeCode(candidate?.ItemNumber) ?? NormalizeCode(product.ItemNumber) ?? string.Empty,
                H主条形码 = NormalizeCode(candidate?.Barcode) ?? NormalizeCode(product.Barcode) ?? string.Empty,
                H商品名称 = displayName,
                H大写名称 = productName,
                H商品类型 = product.ProductType ?? 0,
                H规格 = string.Empty,
                H单位 = "个",
                H进货价 = purchasePrice,
                H零售价 = retailPrice,
                H是否自动定价 = product.IsAutoPricing,
                // 货柜页图片来自 DomesticProduct；Product 主档未补图时不能把 HQ 图片覆盖为空。
                H商品图片 = NormalizeCode(candidate?.ImageUrl)
                    ?? NormalizeCode(product.ProductImage)
                    ?? NormalizeCode(domesticProductImage)
                    ?? string.Empty,
                H腾讯云图地址 = string.Empty,
                中包数量 = product.MiddlePackageQuantity ?? 0,
                // 货柜发送 HQ 不再使用仓库上下架状态覆盖 HQ/POS 商品启用状态。
                H使用状态 = product.IsActive,
                H是否特殊商品 = product.IsSpecialProduct,
                H进货单主表GUID = string.Empty,
                H进货单详情GUID = string.Empty,
                H供货商编码 = "200",
                CBP商品中文名称 = productName,
                CBP供应商编码 = supplierCode,
                CBP商品分类码GUID = string.Empty,
                FGC_Creator = "HBweb",
                FGC_CreateDate = now,
                FGC_LastModifier = "HBweb",
                FGC_LastModifyDate = now,
                FGC_UpdateHelp = string.Empty,
            };
        }

        private static DIC_商品零售价表 MapProductToHqRetailPrice(
            Product product,
            string storeCode,
            PushProductsToHqItem? candidate = null
        )
        {
            var now = DateTime.Now;
            var defaultDate = new DateTime(1900, 1, 1);
            var productCode = NormalizeCode(product.ProductCode) ?? string.Empty;
            var supplierCode = NormalizeCode(candidate?.LocalSupplierCode)
                ?? NormalizeCode(product.LocalSupplierCode)
                ?? "200";
            return new DIC_商品零售价表
            {
                HGUID = UuidHelper.GenerateUuid7(),
                H分店代码 = storeCode,
                H商品编码 = productCode,
                H分店商品编码 = storeCode + productCode,
                H供应商编码 = "200",
                H分店供应商编码 = storeCode + supplierCode,
                H进货价 = candidate?.ImportPrice ?? product.PurchasePrice ?? 0,
                H分店零售价 = candidate?.OemPrice ?? product.RetailPrice ?? 0,
                H库存 = 0,
                H库存金额 = 0,
                H库存预警数 = 0,
                H商品缺货日期 = defaultDate,
                H是否缺货状态 = false,
                H最小订货量 = 0,
                H最小订货量合计金额 = 0,
                H活动类型 = string.Empty,
                H满减活动代码 = string.Empty,
                H活动开始日期 = defaultDate,
                H活动结束日期 = defaultDate,
                H折扣率 = 0,
                H满减数量 = 0,
                H满减金额 = 0,
                H多码数量 = 0,
                // 分店价格新增记录按本地商品状态初始化，后续货柜发送不再更新该字段。
                H使用状态 = product.IsActive,
                H是否自动定价 = product.IsAutoPricing,
                H自动新价格 = 0,
                H盘点入库记录数 = 0,
                H是否特殊商品 = product.IsSpecialProduct,
                H动态销售数量 = 0,
                H动态销售额 = 0,
                H动态成本 = 0,
                H动态毛利 = 0,
                H动态毛利率 = 0,
                H动态销售占比 = 0,
                FGC_Creator = "HBweb",
                FGC_CreateDate = now,
                FGC_LastModifier = "HBweb",
                FGC_LastModifyDate = now,
            };
        }

        private static StoreRetailPrice MapNewStoreRetailPrice(DIC_商品零售价表 hqRow)
        {
            var row = new StoreRetailPrice
            {
                UUID = NormalizeCode(hqRow.HGUID) ?? UuidHelper.GenerateUuid7(),
                CreatedAt = hqRow.FGC_CreateDate == default ? DateTime.UtcNow : hqRow.FGC_CreateDate,
                CreatedBy = hqRow.FGC_Creator,
            };
            ApplyStoreRetailPriceUpdate(hqRow, row);
            return row;
        }

        private static void ApplyStoreRetailPriceUpdate(
            DIC_商品零售价表 hqRow,
            StoreRetailPrice local
        )
        {
            local.StoreCode = NormalizeCode(hqRow.H分店代码);
            local.ProductCode = NormalizeCode(hqRow.H商品编码);
            local.StoreProductCode = NormalizeCode(hqRow.H分店商品编码);
            local.SupplierCode = NormalizeCode(hqRow.H供应商编码);
            local.PurchasePrice = hqRow.H进货价;
            local.StoreRetailPriceValue = hqRow.H分店零售价;
            local.DiscountRate = hqRow.H折扣率;
            local.IsActive = hqRow.H使用状态;
            local.IsAutoPricing = hqRow.H是否自动定价;
            local.IsSpecialProduct = hqRow.H是否特殊商品;
            local.IsDeleted = false;
            local.UpdatedAt = DateTime.UtcNow;
            local.UpdatedBy = hqRow.FGC_LastModifier;
        }

        private static StoreMultiCodeProduct MapNewStoreMultiCode(DIC_分店一品多码表 hqRow)
        {
            var row = new StoreMultiCodeProduct
            {
                UUID = NormalizeCode(hqRow.HGUID) ?? UuidHelper.GenerateUuid7(),
                CreatedAt = hqRow.FGC_CreateDate ?? DateTime.UtcNow,
                CreatedBy = hqRow.FGC_Creator,
            };
            ApplyStoreMultiCodeUpdate(hqRow, row);
            return row;
        }

        private static void ApplyStoreMultiCodeUpdate(
            DIC_分店一品多码表 hqRow,
            StoreMultiCodeProduct local
        )
        {
            local.StoreCode = NormalizeCode(hqRow.H分店代码);
            local.ProductCode = NormalizeCode(hqRow.H商品编码);
            local.MultiCodeProductCode = NormalizeCode(hqRow.H多码商品编码);
            local.StoreMultiCodeProductCode = NormalizeCode(hqRow.H分店多码商品编码);
            local.MultiBarcode = NormalizeCode(hqRow.H多条形码);
            local.PurchasePrice = hqRow.H进货价;
            local.MultiCodeRetailPrice = hqRow.H一品多码零售价;
            local.DiscountRate = hqRow.H折扣率;
            local.IsAutoPricing = hqRow.H是否自动定价 ?? false;
            local.IsSpecialProduct = hqRow.H是否特殊商品 ?? false;
            local.IsActive = hqRow.H使用状态 ?? true;
            local.IsDeleted = false;
            local.UpdatedAt = DateTime.UtcNow;
            local.UpdatedBy = hqRow.FGC_LastModifier;
        }

        private static DIC_一品多码表 MapProductSetCodeToHq(
            ProductSetCode setCode,
            Product product,
            decimal? allocatedPurchasePrice = null
        )
        {
            var now = DateTime.Now;
            return new DIC_一品多码表
            {
                HGUID = NormalizeCode(setCode.SetCodeId) ?? UuidHelper.GenerateUuid7(),
                H商品编码 = NormalizeCode(setCode.ProductCode),
                H多码商品编号 = NormalizeCode(setCode.SetProductCode),
                H供应商编码 = "200",
                H主条形码 = NormalizeCode(product.Barcode),
                H多条形码 = NormalizeCode(setCode.SetBarcode),
                H进货价 = allocatedPurchasePrice ?? setCode.SetPurchasePrice ?? product.PurchasePrice ?? 0,
                H一品多码零售价 = setCode.SetRetailPrice ?? product.RetailPrice ?? 0,
                H使用状态 = setCode.IsActive,
                H是否自动定价 = product.IsAutoPricing,
                FGC_Creator = "HBweb",
                FGC_CreateDate = now,
                FGC_LastModifier = "HBweb",
                FGC_LastModifyDate = now,
            };
        }

        private static async Task<List<string>> GetActiveLocalStoreCodesAsync(ISqlSugarClient db)
        {
            var activeStoreCodes = await db.Queryable<Store>()
                .Where(row => row.IsActive && !row.IsDeleted && row.StoreCode != null)
                .Select(row => row.StoreCode)
                .ToListAsync();
            return activeStoreCodes
                .Select(NormalizeCode)
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DIC_分店一品多码表 MapStoreMultiCodeToHq(
            string storeCode,
            Product product,
            ProductSetCode setCode,
            StoreMultiCodeProduct? storeMultiCode,
            decimal? allocatedPurchasePrice = null
        )
        {
            var now = DateTime.Now;
            var productCode = NormalizeCode(product.ProductCode) ?? string.Empty;
            var multiCode = NormalizeCode(setCode.SetProductCode) ?? string.Empty;
            var storeMultiProductCode =
                NormalizeCode(storeMultiCode?.StoreMultiCodeProductCode) ?? storeCode + multiCode;

            return new DIC_分店一品多码表
            {
                HGUID = NormalizeCode(storeMultiCode?.UUID) ?? UuidHelper.GenerateUuid7(),
                H分店代码 = storeCode,
                H商品编码 = productCode,
                H分店商品编码 = storeCode + productCode,
                H多码商品编码 = multiCode,
                H分店多码商品编码 = storeMultiProductCode,
                H供应商编码 = "200",
                H主条形码 = NormalizeCode(product.Barcode),
                H多条形码 = NormalizeCode(storeMultiCode?.MultiBarcode) ?? NormalizeCode(setCode.SetBarcode),
                H进货价 =
                    allocatedPurchasePrice ?? storeMultiCode?.PurchasePrice ?? setCode.SetPurchasePrice ?? product.PurchasePrice ?? 0,
                H折扣率 = storeMultiCode?.DiscountRate ?? 0,
                H一品多码零售价 =
                    storeMultiCode?.MultiCodeRetailPrice ?? setCode.SetRetailPrice ?? product.RetailPrice ?? 0,
                H库存 = 0,
                H库存金额 = 0,
                H自动新价格 = 0,
                H库存预警数 = 0,
                H商品缺货日期 = null,
                H是否缺货状态 = false,
                H最小订货量 = 0,
                H最小订货量合计金额 = 0,
                H活动类型 = string.Empty,
                H满减活动代码 = string.Empty,
                H活动开始日期 = null,
                H活动结束日期 = null,
                H满减数量 = 0,
                H满减金额 = 0,
                H是否自动定价 = storeMultiCode?.IsAutoPricing ?? product.IsAutoPricing,
                H是否特殊商品 = storeMultiCode?.IsSpecialProduct ?? product.IsSpecialProduct,
                H商品柜组号 = string.Empty,
                H使用状态 = storeMultiCode?.IsActive ?? setCode.IsActive,
                H动态销售数量 = 0,
                H动态销售额 = 0,
                H动态成本 = 0,
                H动态毛利 = 0,
                H动态毛利率 = 0,
                H动态销售占比 = 0,
                FGC_Creator = "HBweb",
                FGC_CreateDate = now,
                FGC_LastModifier = "HBweb",
                FGC_LastModifyDate = now,
            };
        }

        private static string? BuildProductSetCodeBusinessKey(string? productCode, string? setProductCode)
        {
            var normalizedProductCode = NormalizeCode(productCode);
            var normalizedSetProductCode = NormalizeCode(setProductCode);
            return normalizedProductCode == null || normalizedSetProductCode == null
                ? null
                : $"{normalizedProductCode}\u001F{normalizedSetProductCode}";
        }

        private static string? BuildStoreProductKey(string? storeCode, string? productCode)
        {
            var normalizedStoreCode = NormalizeCode(storeCode);
            var normalizedProductCode = NormalizeCode(productCode);
            return normalizedStoreCode == null || normalizedProductCode == null
                ? null
                : $"{normalizedStoreCode}\u001F{normalizedProductCode}";
        }

        private static string? BuildStoreMultiCodeKey(
            string? storeCode,
            string? productCode,
            string? multiCode
        )
        {
            var normalizedStoreCode = NormalizeCode(storeCode);
            var normalizedProductCode = NormalizeCode(productCode);
            var normalizedMultiCode = NormalizeCode(multiCode);
            return normalizedStoreCode == null || normalizedProductCode == null || normalizedMultiCode == null
                ? null
                : $"{normalizedStoreCode}\u001F{normalizedProductCode}\u001F{normalizedMultiCode}";
        }

        private static string? BuildSupplierItemKey(string? supplierCode, string? itemNumber)
        {
            var normalizedSupplierCode = NormalizeCode(supplierCode);
            var normalizedItemNumber = NormalizeCode(itemNumber);
            return normalizedSupplierCode == null || normalizedItemNumber == null
                ? null
                : $"{normalizedSupplierCode}\u001F{normalizedItemNumber}";
        }

        private static string? NormalizeCode(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? Truncate(string? value, int maxLength)
        {
            var normalized = NormalizeCode(value);
            return normalized == null || normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength];
        }

        private sealed record ProductIncrementalSnapshot(
            HashSet<string> ActiveProductCodes,
            List<string> SoftDeletedProductCodes
        );

        private sealed record ProductAssociationDeleteResult(
            int StoreRetailPricesDeleted,
            int StoreMultiCodesDeleted
        );

        private sealed record PushToHqSelection(
            List<Product> Products,
            Dictionary<string, PushProductsToHqItem> InventoryCandidates,
            Dictionary<string, string> DomesticProductImages,
            int ItemFailureCount
        );

        private sealed class SupplierItemHqProductMatch
        {
            public string? SupplierCode { get; set; }
            public string? ItemNumber { get; set; }
            public string? ProductCode { get; set; }
        }

        private sealed class ProductShadowRunRow
        {
            public long SyncRunId { get; set; }
            public long SourceRowCount { get; set; }
            public long ShadowRowCount { get; set; }
            public string? BackupTableName { get; set; }
        }
    }
}
