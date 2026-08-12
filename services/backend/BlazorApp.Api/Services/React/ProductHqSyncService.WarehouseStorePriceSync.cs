using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public partial class ProductHqSyncService
{
    public async Task<ApiResponse<WarehouseStorePriceHqValidationResultDto>> ValidateWarehouseStorePriceTargetsAsync(
        IReadOnlyCollection<string> targetStoreCodes,
        CancellationToken cancellationToken = default
    )
    {
        var result = new WarehouseStorePriceHqValidationResultDto();
        var normalizedCodes = NormalizeWarehouseStorePriceCodes(targetStoreCodes);
        if (normalizedCodes.Count == 0)
        {
            result.Errors.Add(new WarehouseStorePriceSyncErrorDto
            {
                Stage = "HqValidation",
                Code = "HQ_TARGET_STORES_REQUIRED",
                Message = "HQ 目标分店不能为空",
            });
            return ApiResponse<WarehouseStorePriceHqValidationResultDto>.Error(
                "HQ 目标分店不能为空",
                "HQ_TARGET_STORES_REQUIRED",
                result
            );
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hqContext.CheckConnection();
            var branches = await _hqContext.Db.Queryable<HqBranch>().ToListAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var canonicalByCode = branches
                .Select(branch => NormalizeCode(branch.BranchCode))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(code => code, code => code, StringComparer.OrdinalIgnoreCase);

            foreach (var code in normalizedCodes)
            {
                if (canonicalByCode.TryGetValue(code, out var canonicalCode))
                {
                    result.CanonicalTargetStoreCodes.Add(canonicalCode);
                    continue;
                }

                result.Errors.Add(new WarehouseStorePriceSyncErrorDto
                {
                    Stage = "HqValidation",
                    StoreCode = code,
                    Code = "HQ_TARGET_STORE_NOT_FOUND",
                    Message = $"HQ 分店不存在: {code}",
                });
            }

            if (result.Errors.Count > 0)
            {
                return ApiResponse<WarehouseStorePriceHqValidationResultDto>.Error(
                    "HQ 缺少一个或多个目标分店",
                    "HQ_TARGET_STORE_NOT_FOUND",
                    result
                );
            }

            return ApiResponse<WarehouseStorePriceHqValidationResultDto>.OK(
                result,
                "HQ 目标分店校验通过"
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "仓库价格同步 HQ 分店预校验失败");
            result.Errors.Add(new WarehouseStorePriceSyncErrorDto
            {
                Stage = "HqValidation",
                Code = "HQ_VALIDATION_FAILED",
                Message = "HQ 分店预校验失败",
            });
            return ApiResponse<WarehouseStorePriceHqValidationResultDto>.Error(
                "HQ 分店预校验失败",
                "HQ_VALIDATION_FAILED",
                result
            );
        }
    }

    public async Task<ApiResponse<WarehouseStorePriceHqSyncResultDto>> SyncWarehouseStorePricesAsync(
        WarehouseStorePriceHqSyncRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        var result = new WarehouseStorePriceHqSyncResultDto();
        var products = NormalizeWarehouseStorePriceProducts(request?.Products);
        var targetStoreCodes = NormalizeWarehouseStorePriceCodes(request?.TargetStoreCodes);
        var updatedBy = NormalizeCode(request?.UpdatedBy) ?? "system";
        if (products.Count == 0 || targetStoreCodes.Count == 0)
        {
            result.Errors.Add(new WarehouseStorePriceSyncErrorDto
            {
                Stage = "HqWrite",
                Code = "HQ_SYNC_REQUEST_INVALID",
                Message = "HQ 同步商品和目标分店均不能为空",
            });
            return ApiResponse<WarehouseStorePriceHqSyncResultDto>.Error(
                "HQ 同步请求无效",
                "HQ_SYNC_REQUEST_INVALID",
                result
            );
        }

        if (!await SyncLock.WaitAsync(0, cancellationToken))
        {
            result.Errors.Add(new WarehouseStorePriceSyncErrorDto
            {
                Stage = "HqWrite",
                Code = "PRODUCT_HQ_SYNC_CONFLICT",
                Message = "已有商品 HQ 同步任务正在执行，请稍后再试",
            });
            return ApiResponse<WarehouseStorePriceHqSyncResultDto>.Error(
                "已有商品 HQ 同步任务正在执行，请稍后再试",
                "PRODUCT_HQ_SYNC_CONFLICT",
                result
            );
        }

        var hqDb = _hqContext.Db;
        var originalTimeout = hqDb.Ado.CommandTimeOut;
        hqDb.Ado.CommandTimeOut = 1800;
        try
        {
            var targetValidation = await ValidateWarehouseStorePriceTargetsAsync(
                targetStoreCodes,
                cancellationToken
            );
            var validationResult = targetValidation.Data
                ?? targetValidation.Details as WarehouseStorePriceHqValidationResultDto;
            if (!targetValidation.Success || validationResult == null)
            {
                if (validationResult != null)
                {
                    result.Errors.AddRange(validationResult.Errors);
                }
                return ApiResponse<WarehouseStorePriceHqSyncResultDto>.Error(
                    targetValidation.Message,
                    targetValidation.ErrorCode ?? "HQ_VALIDATION_FAILED",
                    result
                );
            }

            var canonicalTargets = validationResult.CanonicalTargetStoreCodes;
            var productCodes = products.Select(product => product.ProductCode).ToList();
            var priceByCode = products.ToDictionary(
                product => product.ProductCode,
                product => product,
                StringComparer.OrdinalIgnoreCase
            );
            var allHqStoreCodes = (await hqDb.Queryable<HqBranch>()
                    .Select(row => row.BranchCode)
                    .ToListAsync())
                .Select(NormalizeCode)
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var allHqStoreCodeSet = allHqStoreCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hqProducts = await QueryHqProductsByCodesAsync(
                hqDb,
                productCodes,
                cancellationToken
            );
            var hqProductByCode = hqProducts
                .Where(row => NormalizeCode(row.H商品编码) != null)
                .GroupBy(row => NormalizeCode(row.H商品编码)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(row => row.FGC_LastModifyDate).First(),
                    StringComparer.OrdinalIgnoreCase
                );
            var missingProductCodes = productCodes
                .Where(code => !hqProductByCode.ContainsKey(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pushResult = new PushProductsToHqResult();
            var resolvedSelection = new PushToHqSelection(
                new List<Product>(),
                new Dictionary<string, PushProductsToHqItem>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0
            );
            if (missingProductCodes.Count > 0)
            {
                var pushRequest = new PushProductsToHqRequest
                {
                    Items = products
                        .Where(product => missingProductCodes.Contains(product.ProductCode))
                        .Select(product => new PushProductsToHqItem
                        {
                            ProductCode = product.ProductCode,
                            ImportPrice = product.ImportPrice,
                            OemPrice = product.OemPrice,
                        })
                        .ToList(),
                };
                resolvedSelection = await ResolvePushSelectionAsync(
                    _localContext.Db,
                    pushRequest,
                    pushResult
                );
                cancellationToken.ThrowIfCancellationRequested();
                var resolvedProductCodes = resolvedSelection.Products
                    .Select(product => NormalizeCode(product.ProductCode))
                    .Where(code => code != null)
                    .Select(code => code!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unresolvedProductCodes = missingProductCodes
                    .Where(code => !resolvedProductCodes.Contains(code))
                    .ToList();
                if (
                    resolvedSelection.ItemFailureCount > 0
                    || resolvedSelection.Products.Count != missingProductCodes.Count
                )
                {
                    foreach (var productCode in unresolvedProductCodes)
                    {
                        result.Errors.Add(new WarehouseStorePriceSyncErrorDto
                        {
                            Stage = "HqProvision",
                            ProductCode = productCode,
                            Code = "HQ_PRODUCT_RESOLUTION_FAILED",
                            Message = pushResult.Errors.FirstOrDefault(error =>
                                    error.Contains(productCode, StringComparison.OrdinalIgnoreCase)
                                )
                                ?? "无法解析为本地完整商品资料",
                        });
                    }
                    if (unresolvedProductCodes.Count == 0)
                    {
                        foreach (var error in pushResult.Errors)
                        {
                            result.Errors.Add(new WarehouseStorePriceSyncErrorDto
                            {
                                Stage = "HqProvision",
                                Code = "HQ_PRODUCT_RESOLUTION_FAILED",
                                Message = error,
                            });
                        }
                    }
                    if (result.Errors.Count == 0)
                    {
                        result.Errors.Add(new WarehouseStorePriceSyncErrorDto
                        {
                            Stage = "HqProvision",
                            Code = "HQ_PRODUCT_RESOLUTION_FAILED",
                            Message = "一个或多个商品无法解析为本地完整商品资料",
                        });
                    }
                    return ApiResponse<WarehouseStorePriceHqSyncResultDto>.Error(
                        "HQ 建档所需商品资料不完整",
                        "HQ_PRODUCT_RESOLUTION_FAILED",
                        result
                    );
                }
            }

            var productSetCodes = new List<ProductSetCode>();
            foreach (var codeBatch in missingProductCodes.Chunk(HqCodeBatchSize))
            {
                var codes = codeBatch.ToList();
                productSetCodes.AddRange(
                    await _localContext.Db.Queryable<ProductSetCode>()
                        .Where(row => codes.Contains(row.ProductCode) && !row.IsDeleted)
                        .ToListAsync()
                );
            }
            productSetCodes = DeduplicateByBusinessKey(
                productSetCodes,
                row => BuildProductSetCodeBusinessKey(row.ProductCode, row.SetProductCode)
            );
            var storeMultiCodes = new List<StoreMultiCodeProduct>();
            foreach (var codeBatch in missingProductCodes.Chunk(HqCodeBatchSize))
            {
                var codes = codeBatch.ToList();
                storeMultiCodes.AddRange(
                    await _localContext.Db.Queryable<StoreMultiCodeProduct>()
                        .Where(row =>
                            row.ProductCode != null
                            && codes.Contains(row.ProductCode)
                            && !row.IsDeleted
                        )
                        .ToListAsync()
                );
            }
            storeMultiCodes = storeMultiCodes
                .Where(row => allHqStoreCodeSet.Contains(NormalizeCode(row.StoreCode) ?? string.Empty))
                .ToList();
            storeMultiCodes = DeduplicateByBusinessKey(
                storeMultiCodes,
                row => BuildStoreMultiCodeKey(row.StoreCode, row.ProductCode, row.MultiCodeProductCode)
            );
            cancellationToken.ThrowIfCancellationRequested();

            hqDb.Ado.BeginTran();
            try
            {
                var existingProductCodes = hqProductByCode.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var localProductByCode = resolvedSelection.Products
                    .Where(product => NormalizeCode(product.ProductCode) != null)
                    .ToDictionary(
                        product => NormalizeCode(product.ProductCode)!,
                        product => product,
                        StringComparer.OrdinalIgnoreCase
                    );
                var missingProducts = localProductByCode.Values.ToList();

                if (missingProducts.Count > 0)
                {
                    var updateFields = new PushToHqUpdateFieldSelection(null);
                    await UpsertHqProductsAsync(
                        hqDb,
                        missingProducts,
                        resolvedSelection.InventoryCandidates,
                        resolvedSelection.DomesticProductImages,
                        resolvedSelection.DomesticSupplierCodes,
                        updateFields,
                        pushResult,
                        existingProductCodes,
                        auditUser: updatedBy
                    );
                    await UpsertHqProductSetCodesAsync(
                        hqDb,
                        missingProducts,
                        productSetCodes,
                        pushResult,
                        auditUser: updatedBy
                    );
                    var allStoresByMissingProduct = missingProducts
                        .Select(product => NormalizeCode(product.ProductCode))
                        .Where(code => code != null)
                        .Select(code => code!)
                        .ToDictionary(
                            code => code,
                            _ => allHqStoreCodes,
                            StringComparer.OrdinalIgnoreCase
                        );
                    await UpsertHqStoreMultiCodesAsync(
                        hqDb,
                        missingProducts,
                        productSetCodes,
                        storeMultiCodes,
                        allStoresByMissingProduct,
                        allHqStoreCodes,
                        pushResult,
                        auditUser: updatedBy
                    );
                    await UpsertHqWarehouseInventoriesAsync(
                        hqDb,
                        missingProducts,
                        resolvedSelection.InventoryCandidates,
                        updateFields,
                        pushResult,
                        auditUser: updatedBy
                    );
                }

                var storesByProduct = productCodes.ToDictionary(
                    code => code,
                    code => missingProductCodes.Contains(code)
                        ? allHqStoreCodes
                        : canonicalTargets,
                    StringComparer.OrdinalIgnoreCase
                );
                var priceCounts = await UpsertWarehouseStorePricesAsync(
                    hqDb,
                    localProductByCode,
                    hqProductByCode,
                    priceByCode,
                    storesByProduct,
                    updatedBy,
                    cancellationToken
                );
                result.HqCreatedCount = priceCounts.Created;
                result.HqUpdatedCount = priceCounts.Updated;
                result.HqProvisionedProductCount = missingProducts.Count;
                hqDb.Ado.CommitTran();
            }
            catch
            {
                hqDb.Ado.RollbackTran();
                throw;
            }

            return ApiResponse<WarehouseStorePriceHqSyncResultDto>.OK(
                result,
                "仓库商品价格已同步到 HQ"
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "仓库商品价格 HQ 同步失败");
            result.Errors.Add(new WarehouseStorePriceSyncErrorDto
            {
                Stage = "HqWrite",
                Code = "WAREHOUSE_STORE_PRICE_HQ_SYNC_FAILED",
                Message = "仓库商品价格 HQ 同步失败",
            });
            return ApiResponse<WarehouseStorePriceHqSyncResultDto>.Error(
                "仓库商品价格 HQ 同步失败",
                "WAREHOUSE_STORE_PRICE_HQ_SYNC_FAILED",
                result
            );
        }
        finally
        {
            hqDb.Ado.CommandTimeOut = originalTimeout;
            SyncLock.Release();
        }
    }

    private static async Task<WarehouseStorePriceHqWriteCounts> UpsertWarehouseStorePricesAsync(
        ISqlSugarClient hqDb,
        IReadOnlyDictionary<string, Product> localProductByCode,
        IReadOnlyDictionary<string, DIC_商品信息字典表> hqProductByCode,
        IReadOnlyDictionary<string, WarehouseStorePriceHqProductDto> priceByCode,
        IReadOnlyDictionary<string, List<string>> storesByProduct,
        string updatedBy,
        CancellationToken cancellationToken
    )
    {
        var productCodes = priceByCode.Keys.ToList();
        var targetStoreCodes = storesByProduct.Values
            .SelectMany(codes => codes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingRows = new List<DIC_商品零售价表>();
        foreach (var productCodeBatch in productCodes.Chunk(HqCodeBatchSize))
        {
            var productCodeChunk = productCodeBatch.ToList();
            foreach (var storeCodeBatch in targetStoreCodes.Chunk(HqCodeBatchSize))
            {
                var storeCodeChunk = storeCodeBatch.ToList();
                existingRows.AddRange(
                    await hqDb.Queryable<DIC_商品零售价表>()
                        .Where(row =>
                            productCodeChunk.Contains(row.H商品编码)
                            && storeCodeChunk.Contains(row.H分店代码)
                        )
                        .ToListAsync()
                );
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var existingByKey = existingRows
            .Select(row => new
            {
                Key = BuildStoreProductKey(row.H分店代码, row.H商品编码),
                Row = row,
            })
            .Where(item => item.Key != null)
            .GroupBy(item => item.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Row).ToList(),
                StringComparer.OrdinalIgnoreCase
            );
        var inserts = new List<DIC_商品零售价表>();
        var updates = new List<DIC_商品零售价表>();
        var updatedCount = 0;
        var now = DateTime.Now;
        var effectiveAuditUser = NormalizeCode(updatedBy) ?? "system";

        foreach (var pair in priceByCode)
        {
            if (!storesByProduct.TryGetValue(pair.Key, out var storeCodes))
            {
                continue;
            }

            foreach (var storeCode in storeCodes)
            {
                var key = BuildStoreProductKey(storeCode, pair.Key);
                if (key == null)
                {
                    continue;
                }

                if (!existingByKey.TryGetValue(key, out var existingPriceRows))
                {
                    var created = hqProductByCode.TryGetValue(pair.Key, out var hqProduct)
                        ? MapHqProductToWarehouseStorePrice(hqProduct, storeCode)
                        : localProductByCode.TryGetValue(pair.Key, out var localProduct)
                            ? MapProductToHqRetailPrice(localProduct, storeCode)
                            : null;
                    if (created == null)
                    {
                        continue;
                    }

                    created.H进货价 = pair.Value.ImportPrice;
                    created.H分店零售价 = pair.Value.OemPrice;
                    created.H折扣率 = 0m;
                    created.H是否自动定价 = false;
                    created.FGC_Creator = effectiveAuditUser;
                    created.FGC_CreateDate = now;
                    created.FGC_LastModifier = effectiveAuditUser;
                    created.FGC_LastModifyDate = now;
                    inserts.Add(created);
                    continue;
                }

                // 已有 HQ 价格行严格只改四个业务字段和审计，保留库存、活动、状态等字段。
                foreach (var existingPriceRow in existingPriceRows)
                {
                    existingPriceRow.H进货价 = pair.Value.ImportPrice;
                    existingPriceRow.H分店零售价 = pair.Value.OemPrice;
                    existingPriceRow.H折扣率 = 0m;
                    existingPriceRow.H是否自动定价 = false;
                    existingPriceRow.FGC_LastModifier = effectiveAuditUser;
                    existingPriceRow.FGC_LastModifyDate = now;
                    updates.Add(existingPriceRow);
                }
                updatedCount++;
            }
        }

        foreach (var batch in updates.Chunk(HqWriteBatchSize))
        {
            await hqDb.Updateable(batch.ToList())
                .UpdateColumns(row => new
                {
                    row.H进货价,
                    row.H分店零售价,
                    row.H折扣率,
                    row.H是否自动定价,
                    row.FGC_LastModifier,
                    row.FGC_LastModifyDate,
                })
                .ExecuteCommandAsync();
        }

        foreach (var batch in inserts.Chunk(HqWriteBatchSize))
        {
            await hqDb.Insertable(batch.ToList())
                .IgnoreColumns(row => row.ID)
                .ExecuteCommandAsync();
        }

        return new WarehouseStorePriceHqWriteCounts(inserts.Count, updatedCount);
    }

    private static async Task<List<DIC_商品信息字典表>> QueryHqProductsByCodesAsync(
        ISqlSugarClient hqDb,
        IReadOnlyCollection<string> productCodes,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<DIC_商品信息字典表>();
        foreach (var codeBatch in productCodes.Chunk(HqCodeBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codes = codeBatch.ToList();
            rows.AddRange(
                await hqDb.Queryable<DIC_商品信息字典表>()
                    .Where(row => row.H商品编码 != null && codes.Contains(row.H商品编码))
                    .ToListAsync()
            );
        }

        return rows;
    }

    private static DIC_商品零售价表 MapHqProductToWarehouseStorePrice(
        DIC_商品信息字典表 hqProduct,
        string storeCode
    )
    {
        var now = DateTime.Now;
        var defaultDate = new DateTime(1900, 1, 1);
        var productCode = NormalizeCode(hqProduct.H商品编码) ?? string.Empty;
        var supplierCode = NormalizeCode(hqProduct.H供货商编码) ?? "200";
        return new DIC_商品零售价表
        {
            HGUID = UuidHelper.GenerateUuid7(),
            H分店代码 = storeCode,
            H商品编码 = productCode,
            H分店商品编码 = storeCode + productCode,
            H供应商编码 = supplierCode,
            H分店供应商编码 = storeCode + supplierCode,
            H进货价 = hqProduct.H进货价,
            H分店零售价 = hqProduct.H零售价,
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
            H使用状态 = hqProduct.H使用状态,
            H是否自动定价 = hqProduct.H是否自动定价,
            H自动新价格 = 0,
            H盘点入库记录数 = 0,
            H是否特殊商品 = hqProduct.H是否特殊商品,
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

    private static List<WarehouseStorePriceHqProductDto> NormalizeWarehouseStorePriceProducts(
        IEnumerable<WarehouseStorePriceHqProductDto>? products
    )
    {
        return (products ?? Array.Empty<WarehouseStorePriceHqProductDto>())
            .Where(product => NormalizeCode(product?.ProductCode) != null)
            .Select(product => new WarehouseStorePriceHqProductDto
            {
                ProductCode = NormalizeCode(product.ProductCode)!,
                ImportPrice = product.ImportPrice,
                OemPrice = product.OemPrice,
            })
            .GroupBy(product => product.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static List<string> NormalizeWarehouseStorePriceCodes(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(NormalizeCode)
            .Where(value => value != null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record WarehouseStorePriceHqWriteCounts(int Created, int Updated);
}
