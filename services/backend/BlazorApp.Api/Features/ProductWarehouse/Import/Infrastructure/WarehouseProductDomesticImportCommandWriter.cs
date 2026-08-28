using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed class WarehouseProductDomesticImportCommandWriter : ProductWarehouseSliceBase
{
    private readonly WarehouseProductDomesticImportSourceQueryStore _sourceQueryStore;

    internal WarehouseProductDomesticImportCommandWriter(ProductWarehouseSliceContext context)
        : base(context)
    {
        _sourceQueryStore = new WarehouseProductDomesticImportSourceQueryStore(context.Context);
    }

    /// <summary>
    /// 国内导入命令写入器拥有唯一事务及产品锁，失败时维持整批回滚语义。
    /// </summary>
    internal async Task<ImportFromDomesticResponseDto> ExecuteDomesticImportAsync(
        ImportFromDomesticRequestDto request,
        WarehouseProductDomesticImportPlan executionPlan
    )
    {
        var response = WarehouseProductDomesticImportResultAssembler.CreatePending();
        var effectiveUpdatedBy = executionPlan.UpdatedBy;
        var importBatchGuid = executionPlan.BatchGuid;

        try
        {
            // 开启事务
            _context.Db.Ado.BeginTran();
            var now = DateTime.Now;
            var codes = executionPlan.ProductCodes;
            // 国内导入会同时写主成本、套装关系和门店价格，必须先取产品锁再读取最新状态。
            var setChildPurchasePriceLock =
                await SetChildPurchasePriceMutationLock.AcquireProductsAsync(_context.Db, codes);
            var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(codes);

            // ===== 批量预加载数据（避免 N+1 问题）=====
            var coreSources = await _sourceQueryStore.LoadCoreAsync(codes);
            var domesticProductsDict = coreSources.DomesticProducts;
            var nameResolutions = await ResolveImportProductNamesAsync(
                domesticProductsDict.Values
            );
            var warehouseProductsDict = coreSources.WarehouseProducts;
            var productsDict = coreSources.Products;
            var relatedSources = await _sourceQueryStore.LoadRelatedAsync(codes);
            var setProductsByCode = relatedSources.SetProducts;
            var existingProductSetCodes = relatedSources.ExistingProductSetCodes;
            var activeStores = relatedSources.ActiveStores;
            var existingMultiCodeKeys = relatedSources.ExistingMultiCodeKeys;
            var storeRetailPricesByCode = relatedSources.StoreRetailPrices;

            // 待操作的列表（批量插入/更新）
            var toUpdateWarehouseProducts = new List<WarehouseProduct>();
            var toInsertWarehouseProducts = new List<WarehouseProduct>();
            var toInsertProducts = new List<Product>();
            var toUpdateProducts = new List<Product>();
            var toUpdateDomesticProducts = new List<DomesticProduct>();
            var toInsertProductSetCodes = new List<ProductSetCode>();
            var toInsertStoreMultiCodeProducts = new List<StoreMultiCodeProduct>();
            var toInsertStoreRetailPrices = new List<StoreRetailPrice>();

            // ===== 遍历处理每个商品 =====
            foreach (var productCode in codes)
            {
                var result = WarehouseProductDomesticImportResultAssembler.CreateDetail(
                    productCode
                );

                // 检查国内商品是否存在
                if (!domesticProductsDict.TryGetValue(productCode, out var domesticProduct))
                {
                    WarehouseProductDomesticImportResultAssembler.AddFailure(
                        response,
                        result,
                        "商品不存在"
                    );
                    continue;
                }
                var nameResolution = nameResolutions.TryGetValue(
                    productCode,
                    out var resolvedName
                )
                    ? resolvedName
                    : new ImportProductNameResolution(
                        domesticProduct.ProductName ?? domesticProduct.HBProductNo ?? productCode,
                        null,
                        false
                    );

                // 补全图片 URL
                var finalImageUrl = ProductImageUrlHelper.EnsureImageUrl(
                    domesticProduct.ProductImage,
                    domesticProduct.HBProductNo ?? domesticProduct.ProductCode
                );

                // 获取已存在的仓库商品
                warehouseProductsDict.TryGetValue(productCode, out var existingWp);

                // 获取价格覆盖（如果有）
                ImportPriceOverrideDto? priceOverride = null;
                if (
                    request.PriceOverrides != null
                    && request.PriceOverrides.TryGetValue(productCode, out var priceValue)
                )
                {
                    priceOverride = priceValue;
                }

                // 确定最终价格
                var domesticPrice =
                    priceOverride?.DomesticPrice ?? domesticProduct.DomesticPrice;
                var oemPrice = priceOverride?.OEMPrice ?? domesticProduct.OEMPrice;
                var importPrice = priceOverride?.ImportPrice ?? domesticProduct.ImportPrice;

                // 验证价格
                if (
                    (domesticPrice ?? 0) <= 0
                    || (oemPrice ?? 0) <= 0
                    || (importPrice ?? 0) <= 0
                )
                {
                    WarehouseProductDomesticImportResultAssembler.AddFailure(
                        response,
                        result,
                        "国内价、零售价、进口价必须大于 0"
                    );
                    continue;
                }

                var unitVolume = priceOverride?.Volume ?? domesticProduct.UnitVolume;
                WarehouseProduct wp;

                // 更新或创建仓库商品
                if (existingWp != null)
                {
                    WarehouseProductDomesticImportEntityMapper.ApplyWarehouseProductUpdate(
                        existingWp,
                        domesticPrice,
                        oemPrice,
                        importPrice,
                        unitVolume,
                        effectiveUpdatedBy,
                        now
                    );
                    toUpdateWarehouseProducts.Add(existingWp);
                    wp = existingWp;
                }
                else
                {
                    wp = WarehouseProductDomesticImportEntityMapper.CreateWarehouseProduct(
                        productCode,
                        domesticPrice,
                        oemPrice,
                        importPrice,
                        unitVolume,
                        effectiveUpdatedBy,
                        now
                    );
                    toInsertWarehouseProducts.Add(wp);
                }

                // 同步更新国内商品表的价格与体积
                WarehouseProductDomesticImportEntityMapper.ApplyDomesticProductUpdate(
                    domesticProduct,
                    domesticPrice,
                    oemPrice,
                    importPrice,
                    unitVolume,
                    finalImageUrl,
                    nameResolution.WasTranslated,
                    nameResolution.EnglishName,
                    effectiveUpdatedBy,
                    now
                );
                toUpdateDomesticProducts.Add(domesticProduct);

                // 创建商品记录（如果不存在）
                if (!productsDict.TryGetValue(productCode, out var existingProduct))
                {
                    var product = WarehouseProductDomesticImportEntityMapper.CreateProduct(
                        domesticProduct,
                        wp,
                        nameResolution.DisplayName,
                        nameResolution.EnglishName,
                        effectiveUpdatedBy,
                        now
                    );
                    toInsertProducts.Add(product);
                }
                else if (
                    ShouldSmartFillExistingProductName(
                        existingProduct,
                        domesticProduct,
                        nameResolution
                    )
                )
                {
                    var translatedEnglishName = nameResolution.EnglishName;
                    if (!string.IsNullOrWhiteSpace(translatedEnglishName))
                    {
                        WarehouseProductDomesticImportEntityMapper.ApplySmartFilledProductName(
                            existingProduct,
                            translatedEnglishName,
                            effectiveUpdatedBy,
                            now
                        );
                        toUpdateProducts.Add(existingProduct);
                    }
                }

                // 处理套装商品（使用内存查找，避免 N+1）
                setProductsByCode.TryGetValue(productCode, out var setProducts);
                setProducts ??= new List<DomesticSetProduct>();

                var isSetProduct = domesticProduct.ProductType > 0;
                if (isSetProduct && setProducts.Count > 0)
                {
                    existingProductSetCodes.TryGetValue(productCode, out var existingSet);
                    existingSet ??= new HashSet<string>();
                    foreach (var sp in setProducts)
                    {
                        if (string.IsNullOrWhiteSpace(sp.SetProductCode))
                            continue;
                        var setProductCode = sp.SetProductCode;
                        if (existingSet.Contains(setProductCode))
                            continue;
                        existingSet.Add(setProductCode);
                        toInsertProductSetCodes.Add(
                            WarehouseProductDomesticImportEntityMapper.CreateProductSetCode(
                                productCode,
                                sp,
                                wp.OEMPrice,
                                now
                            )
                        );
                    }
                }

                // 同步多码商品到门店
                if (request.SyncMultiCodes)
                {
                    existingMultiCodeKeys.TryGetValue(productCode, out var existingKeys);
                    existingKeys ??= new HashSet<(string MultiBarcode, string StoreCode)>();
                    foreach (var sp in setProducts)
                    {
                        if (string.IsNullOrWhiteSpace(sp.SetBarcode))
                            continue;
                        var setBarcode = sp.SetBarcode;
                        foreach (var storeCode in activeStores)
                        {
                            if (existingKeys.Contains((setBarcode, storeCode)))
                                continue;
                            existingKeys.Add((setBarcode, storeCode));
                            toInsertStoreMultiCodeProducts.Add(
                                WarehouseProductDomesticImportEntityMapper.CreateStoreMultiCodeProduct(
                                    productCode,
                                    storeCode,
                                    sp,
                                    now
                                )
                            );
                        }
                    }
                }

                // 同步门店零售价
                if (request.SyncStorePrices)
                {
                    if (!storeRetailPricesByCode.ContainsKey(productCode))
                    {
                        foreach (var storeCode in activeStores)
                        {
                            toInsertStoreRetailPrices.Add(
                                WarehouseProductDomesticImportEntityMapper.CreateStoreRetailPrice(
                                    productCode,
                                    storeCode,
                                    wp,
                                    now
                                )
                            );
                        }
                    }
                }

                WarehouseProductDomesticImportResultAssembler.AddSuccess(response, result);
            }

            // ===== 批量执行数据库操作 =====
            if (toUpdateWarehouseProducts.Any())
            {
                await _context
                    .Db.Updateable(toUpdateWarehouseProducts)
                    .UpdateColumns(wp => new
                    {
                        wp.DomesticPrice,
                        wp.OEMPrice,
                        wp.ImportPrice,
                        wp.Volume,
                        wp.UpdatedAt,
                        wp.UpdatedBy,
                    })
                    .ExecuteCommandAsync();
            }
            if (toInsertWarehouseProducts.Any())
            {
                await _context.Db.Insertable(toInsertWarehouseProducts).ExecuteCommandAsync();
            }
            if (toUpdateDomesticProducts.Any())
            {
                await _context
                    .Db.Updateable(toUpdateDomesticProducts)
                    .UpdateColumns(dp => new
                    {
                        dp.DomesticPrice,
                        dp.OEMPrice,
                        dp.ImportPrice,
                        dp.UnitVolume,
                        dp.ProductImage,
                        dp.EnglishProductName,
                        dp.UpdatedAt,
                        dp.UpdatedBy,
                    })
                    .ExecuteCommandAsync();
            }
            if (toInsertProducts.Any())
            {
                await _context.Db.Insertable(toInsertProducts).ExecuteCommandAsync();
            }
            if (toUpdateProducts.Any())
            {
                await _context
                    .Db.Updateable(toUpdateProducts)
                    .UpdateColumns(p => new
                    {
                        p.ProductName,
                        p.EnglishName,
                        p.UpdatedAt,
                        p.UpdatedBy,
                    })
                    .WhereColumns(p => new { p.ProductCode })
                    .ExecuteCommandAsync();
            }
            if (toInsertProductSetCodes.Any())
            {
                await _context.Db.Insertable(toInsertProductSetCodes).ExecuteCommandAsync();
            }
            if (toInsertStoreMultiCodeProducts.Any())
            {
                await _context
                    .Db.Insertable(toInsertStoreMultiCodeProducts)
                    .ExecuteCommandAsync();
            }
            if (toInsertStoreRetailPrices.Any())
            {
                await _context.Db.Insertable(toInsertStoreRetailPrices).ExecuteCommandAsync();
            }

            var successfulProductCodes = response.Results
                .Where(result => result.Success && !string.IsNullOrWhiteSpace(result.ProductCode))
                .Select(result => result.ProductCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // 不只处理本次新插入的关系：成功导入后已存在有效 Type1/Type2 关系的主商品都要重算。
            // 失败项未写入本批列表，不能进入重算，避免把应回滚的组意外持久化。
            var productCodesWithCostDerivedSet = successfulProductCodes.Count == 0
                ? new List<string>()
                : (
                    await _context.Db.Queryable<ProductSetCode>()
                        .Where(setCode =>
                            successfulProductCodes.Contains(setCode.ProductCode)
                            && (setCode.SetType == 1 || setCode.SetType == 2)
                            && setCode.IsActive
                            && !setCode.IsDeleted
                        )
                        .Select(setCode => setCode.ProductCode)
                        .ToListAsync()
                )
                    .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                    .Select(productCode => productCode!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            if (productCodesWithCostDerivedSet.Count > 0)
            {
                var purchasePriceService = new SetChildPurchasePriceService(_context.Db);
                var globalRecalculation = await purchasePriceService
                    .RecalculateGlobalLockedAsync(
                        setChildPurchasePriceLock,
                        productCodesWithCostDerivedSet,
                        updatedBy: effectiveUpdatedBy
                    );
                if (globalRecalculation.ProductSetCode.SkippedGroupCount > 0)
                {
                    var reason = globalRecalculation.Errors.FirstOrDefault()?.Reason
                        ?? "导入后的套装子项成本重算不完整";
                    throw new InvalidOperationException(reason);
                }

                // 只重算实际存在门店投影的精确 (StoreCode, ProductCode) 组。
                // SyncMultiCodes=false 时缺失投影不属于本次导入目标，不能扩展成全部活跃门店的笛卡尔积后误判失败。
                var storeGroups = activeStores.Count == 0
                    ? new List<WarehouseProductDomesticImportStoreGroupRow>()
                    : await _context.Db.Queryable<StoreMultiCodeProduct>()
                        .Where(row =>
                            row.ProductCode != null
                            && productCodesWithCostDerivedSet.Contains(row.ProductCode)
                            && row.StoreCode != null
                            && activeStores.Contains(row.StoreCode)
                            && row.IsActive
                            && !row.IsDeleted
                        )
                        .Select(row => new WarehouseProductDomesticImportStoreGroupRow
                        {
                            StoreCode = row.StoreCode,
                            ProductCode = row.ProductCode,
                        })
                        .ToListAsync();
                if (storeGroups.Count > 0)
                {
                    var storeRecalculation = await purchasePriceService
                        .RecalculateStoreGroupsLockedAsync(
                            setChildPurchasePriceLock,
                            storeGroups.Select(row => (row.StoreCode, row.ProductCode)),
                            effectiveUpdatedBy
                        );
                    if (storeRecalculation.StoreMultiCodeProduct.SkippedGroupCount > 0)
                    {
                        var reason = storeRecalculation.Errors.FirstOrDefault()?.Reason
                            ?? "导入后的门店套装子项成本重算不完整";
                        throw new InvalidOperationException(reason);
                    }
                }
            }

            var changedProductCodes = response.Results
                .Where(item => item.Success && !string.IsNullOrWhiteSpace(item.ProductCode))
                .Select(item => item.ProductCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await RecordProductChangeHistoryAsync(
                beforeSnapshots,
                changedProductCodes,
                action: "Import",
                source: "DomesticImport",
                actorName: effectiveUpdatedBy,
                batchGuid: importBatchGuid
            );

            _context.Db.Ado.CommitTran();

            WarehouseProductDomesticImportResultAssembler.Complete(response);
        }
        catch (Exception ex)
        {
            _context.Db.Ado.RollbackTran();
            if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
            {
                throw;
            }
            _logger.LogError(ex, "从国内商品导入失败");
            WarehouseProductDomesticImportResultAssembler.RejectExecution(
                response,
                ex.Message
            );
        }

        return response;
    }

    /// <summary>
    /// 从非 Hotbargain 商品导入到仓库商品
    /// 将已有商品（Product 表）导入到仓库商品表（WarehouseProduct）
    /// </summary>
    /// <param name="request">导入请求，包含商品编码列表</param>
    /// <returns>导入结果</returns>
    public Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
        ImportNonHotbargainRequestDto request
    )
    {
        return ImportNonHotbargainProductsAsync(request, SystemUpdatedBy);
    }

    public async Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
        ImportNonHotbargainRequestDto request,
        string? updatedBy
    )
    {
        var response = WarehouseProductDomesticImportResultAssembler.CreatePending();

        if (request.ProductCodes == null || !request.ProductCodes.Any())
        {
            response.Message = "请选择要导入的商品";
            response.Success = false;
            return response;
        }

        var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
        var importBatchGuid = Guid.NewGuid();

        try
        {
            // 开启事务
            _context.Db.Ado.BeginTran();
            var now = DateTime.Now;
            var codes = request.ProductCodes.Distinct().ToList();
            var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(codes);

            // 批量查询商品表（避免 N+1 问题）
            var productsDict = (
                await _context
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductCode != null && codes.Contains(p.ProductCode))
                    .ToListAsync()
            )
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                .GroupBy(p => p.ProductCode!)
                .ToDictionary(g => g.Key, g => g.First());

            // 批量查询已存在的仓库商品编码（避免 N+1 问题）
            var existingWpCodes = (
                await _context
                    .Db.Queryable<WarehouseProduct>()
                    // 软删除的仓库记录不应阻止同商品重新导入。
                    .Where(wp => codes.Contains(wp.ProductCode) && !wp.IsDeleted)
                    .Select(wp => wp.ProductCode)
                    .ToListAsync()
            ).ToHashSet();
            var softDeletedWarehouseProducts = (
                await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(wp => codes.Contains(wp.ProductCode) && wp.IsDeleted)
                    .ToListAsync()
            ).ToDictionary(wp => wp.ProductCode);

            // 待插入的仓库商品列表
            var toInsertWarehouseProducts = new List<WarehouseProduct>();
            var toRestoreWarehouseProducts = new List<WarehouseProduct>();

            // 遍历处理每个商品
            foreach (var productCode in codes)
            {
                var result = WarehouseProductDomesticImportResultAssembler.CreateDetail(
                    productCode
                );

                // 检查商品是否存在
                if (!productsDict.TryGetValue(productCode, out var product))
                {
                    WarehouseProductDomesticImportResultAssembler.AddFailure(
                        response,
                        result,
                        "商品不存在"
                    );
                    continue;
                }

                // 检查是否已存在于仓库
                if (existingWpCodes.Contains(productCode))
                {
                    WarehouseProductDomesticImportResultAssembler.AddFailure(
                        response,
                        result,
                        "商品已存在于仓库中"
                    );
                    continue;
                }

                // 软删除记录改为恢复，避免主键冲突并符合重新导入语义。
                if (softDeletedWarehouseProducts.TryGetValue(productCode, out var deletedWp))
                {
                    WarehouseProductDomesticImportEntityMapper.RestoreNonHotbargainWarehouseProduct(
                        deletedWp,
                        product,
                        effectiveUpdatedBy,
                        now
                    );
                    toRestoreWarehouseProducts.Add(deletedWp);

                    WarehouseProductDomesticImportResultAssembler.AddSuccess(
                        response,
                        result
                    );
                    continue;
                }

                // 创建仓库商品记录
                var wp = WarehouseProductDomesticImportEntityMapper.CreateNonHotbargainWarehouseProduct(
                    productCode,
                    product,
                    effectiveUpdatedBy,
                    now
                );
                toInsertWarehouseProducts.Add(wp);

                WarehouseProductDomesticImportResultAssembler.AddSuccess(response, result);
            }

            // 批量插入仓库商品
            if (toInsertWarehouseProducts.Any())
            {
                await _context.Db.Insertable(toInsertWarehouseProducts).ExecuteCommandAsync();
            }
            if (toRestoreWarehouseProducts.Any())
            {
                var restoredProductCodes = toRestoreWarehouseProducts
                    .Select(wp => wp.ProductCode)
                    .ToList();
                await _context
                    .Db.Deleteable<ProductLocation>()
                    .Where(pl =>
                        pl.ProductCode != null && restoredProductCodes.Contains(pl.ProductCode)
                    )
                    .ExecuteCommandAsync();
                await _context.Db.Updateable(toRestoreWarehouseProducts).ExecuteCommandAsync();
            }

            var changedProductCodes = response.Results
                .Where(item => item.Success && !string.IsNullOrWhiteSpace(item.ProductCode))
                .Select(item => item.ProductCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await RecordProductChangeHistoryAsync(
                beforeSnapshots,
                changedProductCodes,
                action: "Import",
                source: "NonDomesticImport",
                actorName: effectiveUpdatedBy,
                batchGuid: importBatchGuid
            );

            // 提交事务
            _context.Db.Ado.CommitTran();

            // 全部失败时整体视为失败
            WarehouseProductDomesticImportResultAssembler.CompleteNonHotbargain(response);
        }
        catch (Exception ex)
        {
            _context.Db.Ado.RollbackTran();
            _logger.LogError(ex, "导入非 Hotbargain 商品失败");
            WarehouseProductDomesticImportResultAssembler.RejectExecution(
                response,
                ex.Message
            );
        }

        return response;
    }
}
