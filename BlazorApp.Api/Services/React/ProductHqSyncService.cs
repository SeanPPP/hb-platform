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

        public async Task<ApiResponse<PushProductsToHqResult>> PushToHqAsync(List<string> productCodes)
        {
            if (productCodes == null || productCodes.Count == 0)
            {
                return ApiResponse<PushProductsToHqResult>.Error(
                    "商品编码列表不能为空",
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

                var normalizedCodes = productCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                result.TotalCount = normalizedCodes.Count;
                if (normalizedCodes.Count == 0)
                {
                    return ApiResponse<PushProductsToHqResult>.Error(
                        "商品编码列表不能为空",
                        "PRODUCT_HQ_PUSH_EMPTY_CODES"
                    );
                }

                var products = await localDb.Queryable<Product>()
                    .Where(row =>
                        row.ProductCode != null
                        && normalizedCodes.Contains(row.ProductCode)
                        && !row.IsDeleted
                    )
                    .ToListAsync();
                products = DeduplicateByBusinessKey(products, row => row.ProductCode);
                result.TotalLocalProducts = products.Count;
                var foundCodes = products
                    .Select(row => NormalizeCode(row.ProductCode))
                    .Where(code => code != null)
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingCodes = normalizedCodes
                    .Where(code => !foundCodes.Contains(code))
                    .ToList();
                foreach (var missingCode in missingCodes)
                {
                    result.Errors.Add($"商品不存在或已删除: {missingCode}");
                }

                if (products.Count == 0)
                {
                    result.FailedCount = normalizedCodes.Count;
                    result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    return new ApiResponse<PushProductsToHqResult>
                    {
                        Success = false,
                        Message = "未找到有效的本地商品",
                        ErrorCode = "PRODUCT_HQ_PUSH_NO_PRODUCTS",
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
                        && row.IsActive
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
                            && row.IsActive
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
                    await UpsertHqProductsAsync(hqDb, products, result);
                    await UpsertHqRetailPricesAsync(hqDb, products, activeStoreCodes, result);
                    await UpsertHqProductSetCodesAsync(hqDb, products, productSetCodes, result);
                    await UpsertHqStoreMultiCodesAsync(
                        hqDb,
                        products,
                        productSetCodes,
                        storeMultiCodes,
                        activeStoreCodes,
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
                _logger.LogError(ex, "商品推送HQ失败");
                result.DurationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                result.Errors.Add(ex.Message);
                result.FailedCount = result.TotalCount > 0 ? result.TotalCount : productCodes.Count;
                result.TotalCount = result.TotalCount > 0 ? result.TotalCount : productCodes.Count;
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

        private static async Task UpsertHqProductsAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
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

                var hqProduct = MapProductToHqProduct(product);
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
                        H使用状态 = hqProduct.H使用状态,
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

        private static async Task UpsertHqRetailPricesAsync(
            ISqlSugarClient hqDb,
            List<Product> products,
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

                    var hqPrice = MapProductToHqRetailPrice(product, storeCode);
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
                            H使用状态 = hqPrice.H使用状态,
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
                    H商品编码 = row.H商品编码,
                    H多码商品编号 = row.H多码商品编号,
                })
                .ToListAsync();
            var existingKeys = existingRows
                .Select(row => BuildProductSetCodeBusinessKey(row.H商品编码, row.H多码商品编号))
                .Where(key => key != null)
                .Select(key => key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

                var hqSetCode = MapProductSetCodeToHq(setCode, product);
                if (!existingKeys.Contains(key))
                {
                    inserts.Add(hqSetCode);
                    existingKeys.Add(key);
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
                    .Where(row =>
                        row.H商品编码 == productCode
                        && row.H多码商品编号 == setProductCode
                    )
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
                    H分店代码 = row.H分店代码,
                    H商品编码 = row.H商品编码,
                    H多码商品编码 = row.H多码商品编码,
                })
                .ToListAsync();
            var existingKeys = existingRows
                .Select(row => BuildStoreMultiCodeKey(row.H分店代码, row.H商品编码, row.H多码商品编码))
                .Where(key => key != null)
                .Select(key => key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                    var hqStoreMultiCode = MapStoreMultiCodeToHq(
                        storeCode,
                        product,
                        setCode,
                        storeMultiCode
                    );
                    if (!existingKeys.Contains(key))
                    {
                        inserts.Add(hqStoreMultiCode);
                        existingKeys.Add(key);
                        continue;
                    }

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
                        .Where(row =>
                            row.H分店代码 == storeCode
                            && row.H商品编码 == productCode
                            && row.H多码商品编码 == multiCode
                        )
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

        private static DIC_商品信息字典表 MapProductToHqProduct(Product product)
        {
            var now = DateTime.Now;
            var productCode = NormalizeCode(product.ProductCode) ?? string.Empty;
            var displayName = NormalizeCode(product.EnglishName) ?? product.ProductName;
            return new DIC_商品信息字典表
            {
                HGUID = NormalizeCode(product.UUID) ?? UuidHelper.GenerateUuid7(),
                H商品标签GUID = string.Empty,
                H商品分类码GUID = string.Empty,
                H商品编码 = productCode,
                H货号 = NormalizeCode(product.ItemNumber) ?? string.Empty,
                H主条形码 = NormalizeCode(product.Barcode) ?? string.Empty,
                H商品名称 = displayName ?? string.Empty,
                H大写名称 = product.ProductName ?? string.Empty,
                H商品类型 = product.ProductType ?? 0,
                H规格 = string.Empty,
                H单位 = "个",
                H进货价 = product.PurchasePrice ?? 0,
                H零售价 = product.RetailPrice ?? 0,
                H是否自动定价 = product.IsAutoPricing,
                H商品图片 = NormalizeCode(product.ProductImage) ?? string.Empty,
                H腾讯云图地址 = string.Empty,
                中包数量 = product.MiddlePackageQuantity ?? 0,
                H使用状态 = product.IsActive,
                H是否特殊商品 = product.IsSpecialProduct,
                H进货单主表GUID = string.Empty,
                H进货单详情GUID = string.Empty,
                H供货商编码 = "200",
                CBP商品中文名称 = product.ProductName ?? string.Empty,
                CBP供应商编码 = NormalizeCode(product.LocalSupplierCode) ?? string.Empty,
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
            string storeCode
        )
        {
            var now = DateTime.Now;
            var defaultDate = new DateTime(1900, 1, 1);
            var productCode = NormalizeCode(product.ProductCode) ?? string.Empty;
            var supplierCode = NormalizeCode(product.LocalSupplierCode) ?? "200";
            return new DIC_商品零售价表
            {
                HGUID = UuidHelper.GenerateUuid7(),
                H分店代码 = storeCode,
                H商品编码 = productCode,
                H分店商品编码 = storeCode + productCode,
                H供应商编码 = "200",
                H分店供应商编码 = storeCode + supplierCode,
                H进货价 = product.PurchasePrice ?? 0,
                H分店零售价 = product.RetailPrice ?? 0,
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

        private static DIC_一品多码表 MapProductSetCodeToHq(
            ProductSetCode setCode,
            Product product
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
                H进货价 = setCode.SetPurchasePrice ?? product.PurchasePrice ?? 0,
                H一品多码零售价 = setCode.SetRetailPrice ?? product.RetailPrice ?? 0,
                H使用状态 = setCode.IsActive,
                H是否自动定价 = product.IsAutoPricing,
                FGC_Creator = "HBweb",
                FGC_CreateDate = now,
                FGC_LastModifier = "HBweb",
                FGC_LastModifyDate = now,
            };
        }

        private static DIC_分店一品多码表 MapStoreMultiCodeToHq(
            string storeCode,
            Product product,
            ProductSetCode setCode,
            StoreMultiCodeProduct? storeMultiCode
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
                H进货价 = storeMultiCode?.PurchasePrice ?? setCode.SetPurchasePrice ?? product.PurchasePrice ?? 0,
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

        private sealed class ProductShadowRunRow
        {
            public long SyncRunId { get; set; }
            public long SourceRowCount { get; set; }
            public long ShadowRowCount { get; set; }
            public string? BackupTableName { get; set; }
        }
    }
}
