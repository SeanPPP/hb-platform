using System.Data;
using System.Linq;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using SetChildPurchasePriceMutationLock = BlazorApp.Api.Services.ProductCosts.ProductCostMutationLock;
using SetChildPurchasePriceService = BlazorApp.Api.Services.ProductCosts.ProductCostRecalculationService;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    internal sealed class LocalSupplierInvoicesPricingHandler
    {
        private readonly LocalSupplierInvoicesDependencies _dependencies;
        private SqlSugarContext _context => _dependencies.Context;
        private HqSqlSugarContext _hqContext => _dependencies.HqContext;
        private IMapper _mapper => _dependencies.Mapper;
        private ILogger _logger => _dependencies.Logger;
        private IAutoPricingService _autoPricingService => _dependencies.AutoPricingService;
        private IWarehouseProductChangeHistoryService _changeHistoryService => _dependencies.ChangeHistoryService;
        private ILocalSupplierInvoiceHqProductSyncService? _hqProductSyncService => _dependencies.HqProductSyncService;

        public LocalSupplierInvoicesPricingHandler(LocalSupplierInvoicesDependencies dependencies)
        {
            _dependencies = dependencies;
        }

        private sealed class ProductDetectProjection
        {
            public string ItemNumber { get; set; } = string.Empty;
            public string? ProductCode { get; set; }
            public string? ProductName { get; set; }
            public string? ProductImage { get; set; }
        }

        private sealed class PriceDetectProjection
        {
            public string ProductCode { get; set; } = string.Empty;
            public decimal? PurchasePrice { get; set; }
            public decimal? Retail { get; set; }
            public string? StoreProductCode { get; set; }
        }

        private sealed class StorePriceUpdatePlan
        {
            public StoreRetailPrice Entity { get; init; } = new();
            public HashSet<string> Columns { get; } = new();
        }

        private static (HashSet<string> Columns, List<string> SkippedFields) ApplyUpdateToStorePriceFields(
            StoreRetailPrice storePrice,
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            var columns = new HashSet<string>();
            var skippedFields = new List<string>();

            if (updateFields.UpdatePurchasePrice)
            {
                // 请求未指定固定值时，使用当前进货单明细里的进货价。
                var purchasePriceToUpdate = updateFields.PurchasePrice ?? detail.PurchasePrice;
                if (LocalSupplierInvoicesRules.IsPositiveValue(purchasePriceToUpdate))
                {
                    storePrice.PurchasePrice = purchasePriceToUpdate.GetValueOrDefault();
                    columns.Add(nameof(StoreRetailPrice.PurchasePrice));
                }
                else
                {
                    skippedFields.Add("进货价为空或为0");
                }
            }

            if (updateFields.UpdateRetailPrice)
            {
                // 明细零售价为空时，回退使用商品检测计算出的新自动零售价。
                var retailPriceToUpdate = updateFields.RetailPrice ?? detail.RetailPrice ?? detail.NewAutoRetailPrice;
                if (LocalSupplierInvoicesRules.IsPositiveValue(retailPriceToUpdate))
                {
                    storePrice.StoreRetailPriceValue = retailPriceToUpdate.GetValueOrDefault();
                    columns.Add(nameof(StoreRetailPrice.StoreRetailPriceValue));
                }
                else
                {
                    skippedFields.Add("零售价为空或为0");
                }
            }

            if (updateFields.UpdateIsAutoPricing)
            {
                var isAutoPricingToUpdate = updateFields.IsAutoPricing ?? detail.AutoPricing ?? false;
                storePrice.IsAutoPricing = isAutoPricingToUpdate;
                columns.Add(nameof(StoreRetailPrice.IsAutoPricing));
            }

            if (updateFields.UpdateIsSpecialProduct)
            {
                var isSpecialProductToUpdate = updateFields.IsSpecialProduct ?? detail.IsSpecialProduct;
                if (isSpecialProductToUpdate != null)
                {
                    storePrice.IsSpecialProduct = isSpecialProductToUpdate.GetValueOrDefault();
                    columns.Add(nameof(StoreRetailPrice.IsSpecialProduct));
                }
                else
                {
                    skippedFields.Add("特殊商品为空");
                }
            }

            if (updateFields.UpdateDiscountRate)
            {
                var discountRateToUpdate = updateFields.DiscountRate ?? detail.DiscountRate;
                if (LocalSupplierInvoicesRules.IsPositiveValue(discountRateToUpdate))
                {
                    storePrice.DiscountRate = discountRateToUpdate.GetValueOrDefault();
                    columns.Add(nameof(StoreRetailPrice.DiscountRate));
                }
                else
                {
                    skippedFields.Add("折扣率为空或为0");
                }
            }

            return (columns, skippedFields);
        }

        public async Task<ApiResponse<UpdateLastPurchasePricesResultDto>> UpdateLastPurchasePricesAsync(
            string invoiceGuid,
            UpdateLastPurchasePricesRequest request,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<UpdateLastPurchasePricesResultDto>.Error(
                        "单据不存在",
                        "NOT_FOUND"
                    );

                var selectedDetailGuids = (request.DetailGuids ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct()
                    .ToList();

                var detailsQuery = db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false);
                if (selectedDetailGuids.Count > 0)
                {
                    detailsQuery = detailsQuery.Where(x => selectedDetailGuids.Contains(x.DetailGUID));
                }

                var details = await detailsQuery.ToListAsync();
                var result = new UpdateLastPurchasePricesResultDto
                {
                    Total = selectedDetailGuids.Count > 0 ? selectedDetailGuids.Count : details.Count,
                };

                if (selectedDetailGuids.Count > 0)
                {
                    var foundDetailGuids = details
                        .Select(x => x.DetailGUID)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var missingDetailGuid in selectedDetailGuids.Where(x => !foundDetailGuids.Contains(x)))
                    {
                        result.Skipped++;
                        result.Errors.Add($"明细 {missingDetailGuid} 跳过：明细不存在或不属于当前单据");
                    }
                }

                if (details.Count == 0)
                    return ApiResponse<UpdateLastPurchasePricesResultDto>.OK(result, "没有可更新的明细");

                var productCodes = details
                    .Select(x => x.ProductCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct()
                    .ToList();
                var storeCodes = details
                    .Select(x =>
                        LocalSupplierInvoicesRules.ResolveDetailStoreCode(
                            x.StoreCode,
                            header.StoreCode
                        )
                    )
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct()
                    .ToList();

                var storePricesByKey = new Dictionary<string, StoreRetailPrice>();
                if (productCodes.Count > 0 && storeCodes.Count > 0)
                {
                    storePricesByKey = (await db.Queryable<StoreRetailPrice>()
                            .Where(x =>
                                x.ProductCode != null
                                && x.StoreCode != null
                                && productCodes.Contains(x.ProductCode)
                                && storeCodes.Contains(x.StoreCode)
                                && x.IsDeleted == false
                            )
                            .ToListAsync())
                        .GroupBy(x => $"{x.ProductCode}\u001f{x.StoreCode}")
                        .ToDictionary(group => group.Key, group => group.First());
                }

                var productsByCode = new Dictionary<string, Product>();
                if (productCodes.Count > 0)
                {
                    productsByCode = (await db.Queryable<Product>()
                            .Where(x =>
                                x.ProductCode != null
                                && productCodes.Contains(x.ProductCode)
                                && x.IsDeleted == false
                            )
                            .ToListAsync())
                        .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                        .GroupBy(x => x.ProductCode!)
                        .ToDictionary(group => group.Key, group => group.First());
                }

                var now = DateTime.UtcNow;
                var updates = new List<StoreLocalSupplierInvoiceDetails>();
                foreach (var detail in details)
                {
                    if (string.IsNullOrWhiteSpace(detail.ProductCode))
                    {
                        result.Skipped++;
                        result.Errors.Add($"明细 {detail.DetailGUID} 跳过：未找到商品编码");
                        continue;
                    }

                    var storeCode = LocalSupplierInvoicesRules.ResolveDetailStoreCode(
                        detail.StoreCode,
                        header.StoreCode
                    );
                    var storePriceKey = $"{detail.ProductCode}\u001f{storeCode}";
                    storePricesByKey.TryGetValue(storePriceKey, out var storePrice);
                    productsByCode.TryGetValue(detail.ProductCode, out var product);

                    // 上次进货价快照按分店价优先；分店价缺失时回退商品主档进货价。
                    var lastPurchasePrice = LocalSupplierInvoicesRules.IsPositiveValue(
                        storePrice?.PurchasePrice
                    )
                        ? storePrice!.PurchasePrice
                        : product?.PurchasePrice;
                    if (!LocalSupplierInvoicesRules.IsPositiveValue(lastPurchasePrice))
                    {
                        result.Skipped++;
                        result.Errors.Add($"明细 {detail.DetailGUID} 跳过：未找到有效上次进货价");
                        continue;
                    }

                    updates.Add(new StoreLocalSupplierInvoiceDetails
                    {
                        DetailGUID = detail.DetailGUID,
                        LastPurchasePrice = lastPurchasePrice,
                        UpdatedAt = now,
                        UpdatedBy = updatedBy,
                    });
                }

                if (updates.Count > 0)
                {
                    await db.Updateable(updates)
                        .UpdateColumns(x => new { x.LastPurchasePrice, x.UpdatedAt, x.UpdatedBy })
                        .WhereColumns(x => x.DetailGUID)
                        .ExecuteCommandAsync();
                }

                result.Updated = updates.Count;
                return ApiResponse<UpdateLastPurchasePricesResultDto>.OK(result, "更新上次进货价完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新本地进货单上次进货价失败");
                return ApiResponse<UpdateLastPurchasePricesResultDto>.Error(
                    "更新上次进货价失败",
                    "UPDATE_LAST_PURCHASE_PRICE_ERROR"
                );
            }
        }

        public async Task<ApiResponse<UpdateToStorePricesResultDto>> UpdateDetailsToStorePricesAsync(
            UpdateToStorePricesRequest dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                // 获取订单明细
                var preLockDetails = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(d =>
                        d.InvoiceGUID == dto.InvoiceGuid
                        && dto.DetailGuids.Contains(d.DetailGUID)
                        && d.IsDeleted == false
                    )
                    .ToListAsync();

                if (preLockDetails == null || preLockDetails.Count == 0)
                {
                    return ApiResponse<UpdateToStorePricesResultDto>.Error("未找到要更新的明细记录", "NOT_FOUND");
                }

                var totalUpdated = 0;
                var targetStoreCodes = dto.TargetStoreCodes
                    .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
                    .Select(storeCode => storeCode.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var productCodes = preLockDetails
                    .Where(d => !string.IsNullOrWhiteSpace(d.ProductCode))
                    .Select(d => d.ProductCode!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var expectedDetailProducts = preLockDetails.ToDictionary(
                    detail => detail.DetailGUID,
                    detail => detail.ProductCode,
                    StringComparer.OrdinalIgnoreCase
                );

                const int updateBatchSize = 500;

                await db.Ado.BeginTranAsync();
                try
                {
                    // 成本主档、关系与门店投影必须在同一把父商品业务锁下写入，锁内部会按编码排序，避免批量操作互相等待形成死锁。
                    var lockScope = productCodes.Count == 0
                        ? null
                        : await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            productCodes
                        );
                    // 业务锁内重新读取明细和两层主成本源，禁止继续使用等待锁之前的实体快照。
                    var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                        .Where(d =>
                            d.InvoiceGUID == dto.InvoiceGuid
                            && dto.DetailGuids.Contains(d.DetailGUID)
                            && d.IsDeleted == false
                        )
                        .ToListAsync();
                    if (
                        details.Count != preLockDetails.Count
                        || details.Any(detail =>
                            !expectedDetailProducts.TryGetValue(detail.DetailGUID, out var expectedCode)
                            || !string.Equals(
                                expectedCode?.Trim(),
                                detail.ProductCode?.Trim(),
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    {
                        throw new InvalidOperationException("等待商品锁期间进货单明细归属已变化，请重新读取后重试");
                    }

                    productCodes = details
                        .Where(detail => !string.IsNullOrWhiteSpace(detail.ProductCode))
                        .Select(detail => detail.ProductCode!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    lockScope?.EnsureCovers(db, productCodes);

                    var productsByCode = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
                    if (dto.UpdateFields.UpdatePurchasePrice && productCodes.Count > 0)
                    {
                        productsByCode = (await db.Queryable<Product>()
                                .Where(product =>
                                    product.ProductCode != null
                                    && productCodes.Contains(product.ProductCode)
                                    && product.IsDeleted == false
                                )
                                .ToListAsync())
                            .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                            .GroupBy(product => product.ProductCode!, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                    }

                    var allPotentialPrices = await db.Queryable<StoreRetailPrice>()
                        .Where(sp =>
                            sp.IsDeleted == false
                            && sp.StoreCode != null
                            && targetStoreCodes.Contains(sp.StoreCode)
                            && sp.ProductCode != null
                            && productCodes.Contains(sp.ProductCode)
                        )
                        .ToListAsync();
                    var priceDict = allPotentialPrices
                        .GroupBy(
                            sp => $"{sp.StoreCode}_{sp.ProductCode}",
                            StringComparer.OrdinalIgnoreCase
                        )
                        .ToDictionary(
                            group => group.Key,
                            group => group.First(),
                            StringComparer.OrdinalIgnoreCase
                        );
                    // 同一分店商品只保留最后一次计算结果，避免重复明细把同一价格记录反复加入大批量更新。
                    var updateMap = new Dictionary<string, StorePriceUpdatePlan>(StringComparer.OrdinalIgnoreCase);
                    var insertMap = new Dictionary<string, StoreRetailPrice>(StringComparer.OrdinalIgnoreCase);
                    var productUpdateMap = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
                    var skipped = 0;
                    var skipMessages = new List<string>();
                    var now = DateTime.Now;

                    if (dto.UpdateFields.UpdatePurchasePrice)
                    {
                        foreach (var detail in details)
                        {
                            var productCode = detail.ProductCode?.Trim();
                            if (
                                string.IsNullOrWhiteSpace(productCode)
                                || !productsByCode.TryGetValue(productCode, out var product)
                            )
                            {
                                continue;
                            }

                            var purchasePriceToUpdate = dto.UpdateFields.PurchasePrice ?? detail.PurchasePrice;
                            if (!LocalSupplierInvoicesRules.IsPositiveValue(purchasePriceToUpdate))
                            {
                                continue;
                            }

                            // 更新到分店勾选进货价时，同步商品主档进货价；同一商品多条明细按最后一条有效明细生效。
                            product.PurchasePrice = purchasePriceToUpdate.GetValueOrDefault();
                            product.UpdatedAt = now;
                            product.UpdatedBy = updatedBy;
                            productUpdateMap[productCode] = product;
                        }
                    }

                    foreach (var storeCode in targetStoreCodes)
                    {
                        foreach (var detail in details)
                        {
                            var detailProductCode = detail.ProductCode?.Trim();
                            if (string.IsNullOrWhiteSpace(detailProductCode))
                            {
                                skipped++;
                                skipMessages.Add($"{detail.DetailGUID}：{storeCode}：商品编码为空，已跳过");
                                continue;
                            }

                            var key = $"{storeCode}_{detailProductCode}";

                            if (priceDict.TryGetValue(key, out var storePrice))
                            {
                                // 记录存在，准备更新
                                var (columns, skippedFields) = ApplyUpdateToStorePriceFields(
                                    storePrice,
                                    detail,
                                    dto.UpdateFields
                                );

                                if (columns.Count == 0)
                                {
                                    skipped++;
                                    skipMessages.Add($"{detail.DetailGUID}：{storeCode}：{string.Join("，", skippedFields)}，已跳过");
                                    continue;
                                }

                                storePrice.UpdatedAt = now;
                                storePrice.UpdatedBy = updatedBy;
                                columns.Add(nameof(StoreRetailPrice.UpdatedAt));
                                columns.Add(nameof(StoreRetailPrice.UpdatedBy));

                                if (!updateMap.TryGetValue(key, out var plan))
                                {
                                    plan = new StorePriceUpdatePlan { Entity = storePrice };
                                    updateMap[key] = plan;
                                }
                                foreach (var column in columns)
                                {
                                    plan.Columns.Add(column);
                                }
                                continue;
                            }

                            if (!insertMap.TryGetValue(key, out storePrice))
                            {
                                storePrice = new StoreRetailPrice
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = storeCode,
                                    ProductCode = detailProductCode,
                                    StoreProductCode = storeCode + detailProductCode,
                                    SupplierCode = detail.SupplierCode,
                                    IsActive = true,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                    CreatedBy = updatedBy,
                                    UpdatedBy = updatedBy,
                                    IsDeleted = false,
                                };
                            }

                            var (insertColumns, insertSkippedFields) = ApplyUpdateToStorePriceFields(
                                storePrice,
                                detail,
                                dto.UpdateFields
                            );

                            if (insertColumns.Count == 0)
                            {
                                skipped++;
                                skipMessages.Add($"{detail.DetailGUID}：{storeCode}：{string.Join("，", insertSkippedFields)}，已跳过");
                                continue;
                            }

                            storePrice.UpdatedAt = now;
                            storePrice.UpdatedBy = updatedBy;
                            insertMap[key] = storePrice;
                        }
                    }

                    // 批量插入缺失的分店价格记录，再批量更新已存在记录。
                    var inserts = insertMap.Values.ToList();
                    if (inserts.Count > 0)
                    {
                        for (var i = 0; i < inserts.Count; i += updateBatchSize)
                        {
                            var batch = inserts.Skip(i).Take(updateBatchSize).ToList();
                            await db.Insertable(batch).ExecuteCommandAsync();
                        }
                        _logger.LogInformation(
                            "批量新建分店价格表成功，共新建 {Count} 条记录",
                            inserts.Count
                        );
                    }

                    var updates = updateMap.Values.ToList();
                    if (updates.Count > 0)
                    {
                        foreach (var group in updates.GroupBy(x => string.Join("|", x.Columns.OrderBy(column => column))))
                        {
                            var updateColumnArray = group.First().Columns.ToArray();
                            var entities = group.Select(x => x.Entity).ToList();
                            for (var i = 0; i < entities.Count; i += updateBatchSize)
                            {
                                var batch = entities.Skip(i).Take(updateBatchSize).ToList();
                                await db.Updateable(batch)
                                    .UpdateColumns(updateColumnArray)
                                    .ExecuteCommandAsync();
                            }
                        }
                        totalUpdated = updates.Count;
                        _logger.LogInformation(
                            "批量更新分店价格表成功，共更新 {Count} 条记录",
                            updates.Count
                        );
                    }

                    var productUpdates = productUpdateMap.Values.ToList();
                    if (productUpdates.Count > 0)
                    {
                        for (var i = 0; i < productUpdates.Count; i += updateBatchSize)
                        {
                            var batch = productUpdates.Skip(i).Take(updateBatchSize).ToList();
                            await db.Updateable(batch)
                                .UpdateColumns(product => new
                                {
                                    product.PurchasePrice,
                                    product.UpdatedAt,
                                    product.UpdatedBy,
                                })
                                .ExecuteCommandAsync();
                        }
                        _logger.LogInformation(
                            "更新到分店价格同步商品主档进货价成功，共更新 {Count} 个商品",
                            productUpdates.Count
                        );
                    }

                    if (lockScope != null && (inserts.Count > 0 || updates.Count > 0 || productUpdates.Count > 0))
                    {
                        var costWriteback = new SetChildPurchasePriceService(db);
                        // 只按真正写入的门店价格实体构造业务组，禁止把被跳过的商品扩展进门店×商品笛卡尔积。
                        var actualStoreProductGroups = inserts
                            .Concat(updates.Select(update => update.Entity))
                            .Where(price =>
                                !string.IsNullOrWhiteSpace(price.StoreCode)
                                && !string.IsNullOrWhiteSpace(price.ProductCode)
                            )
                            .Select(price =>
                                (
                                    StoreCode: (string?)price.StoreCode!.Trim(),
                                    ProductCode: (string?)price.ProductCode!.Trim()
                                )
                            )
                            .GroupBy(group => group.StoreCode!, StringComparer.OrdinalIgnoreCase)
                            .SelectMany(storeGroup =>
                                storeGroup
                                    .GroupBy(group => group.ProductCode!, StringComparer.OrdinalIgnoreCase)
                                    .Select(productGroup => productGroup.First())
                            )
                            .ToList();
                        var affectedProductCodes = actualStoreProductGroups
                            .Select(group => group.ProductCode!)
                            .Concat(productUpdateMap.Keys)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var repairPurchasePrices = productUpdateMap
                            .Where(pair => LocalSupplierInvoicesRules.IsPositiveValue(pair.Value.PurchasePrice))
                            .ToDictionary(
                                pair => pair.Key,
                                pair => pair.Value.PurchasePrice!.Value,
                                StringComparer.OrdinalIgnoreCase
                            );
                        var repairGroups = actualStoreProductGroups
                            .Where(group => repairPurchasePrices.ContainsKey(group.ProductCode!))
                            .ToList();

                        if (repairGroups.Count > 0)
                        {
                            var repair = await costWriteback.RepairMissingStoreRelationsLockedAsync(
                                lockScope,
                                repairPurchasePrices,
                                updatedBy,
                                repairGroups
                            );
                            _logger.LogInformation(
                                "本地进货单 {InvoiceGuid} 更新到分店套装投影检查结果（事务尚未提交）：目标组 {TargetGroupCount}，拟补齐组 {RepairedGroupCount}，拟补齐关系 {RepairedRelationCount}，失败商品 {FailureCount}",
                                dto.InvoiceGuid,
                                repairGroups.Count,
                                repair.AutoRepairedStoreGroupCount,
                                repair.AutoRepairedRelationCount,
                                repair.Failures.Count
                            );
                            if (repair.Failures.Count > 0)
                            {
                                var targetStoresByProduct = repairGroups
                                    .GroupBy(
                                        group => group.ProductCode!,
                                        StringComparer.OrdinalIgnoreCase
                                    )
                                    .ToDictionary(
                                        group => group.Key,
                                        group => group
                                            .Select(item => item.StoreCode!)
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .OrderBy(
                                                storeCode => storeCode,
                                                StringComparer.OrdinalIgnoreCase
                                            )
                                            .ToList(),
                                        StringComparer.OrdinalIgnoreCase
                                    );
                                var reasons = string.Join(
                                    "；",
                                    repair.Failures.Values
                                        .OrderBy(failure => failure.ProductCode, StringComparer.OrdinalIgnoreCase)
                                        .Select(failure =>
                                        {
                                            var storeCodes = !string.IsNullOrWhiteSpace(failure.StoreCode)
                                                ? failure.StoreCode
                                                : targetStoresByProduct.TryGetValue(
                                                    failure.ProductCode,
                                                    out var targetStores
                                                )
                                                    ? string.Join(", ", targetStores)
                                                    : "未知";
                                            return $"{failure.ProductCode} / 分店 {storeCodes}: {failure.Message}";
                                        })
                                );
                                throw new InvalidOperationException(
                                    $"目标分店套装关系无法安全补齐。{reasons}"
                                );
                            }
                        }

                        // 先回算实际受影响商品的全局关系，再只回算本次实际写入的精确门店商品组。
                        if (affectedProductCodes.Count > 0)
                        {
                            await costWriteback.RecalculateGlobalLockedAsync(
                                lockScope,
                                affectedProductCodes,
                                updatedBy
                            );
                        }
                        if (actualStoreProductGroups.Count > 0)
                        {
                            await costWriteback.RecalculateStoreGroupsLockedAsync(
                                lockScope,
                                actualStoreProductGroups,
                                updatedBy
                            );
                        }
                    }

                    if (inserts.Count == 0 && updates.Count == 0)
                    {
                        _logger.LogWarning("没有找到需要更新或新建的分店价格记录");
                    }

                    if (skipped > 0)
                    {
                        _logger.LogInformation(
                            "更新到分店价格跳过 {Count} 条空值或0值记录",
                            skipped
                        );
                    }

                    await db.Ado.CommitTranAsync();

                    var result = new UpdateToStorePricesResultDto
                    {
                        Inserted = inserts.Count,
                        Updated = totalUpdated,
                        Skipped = skipped,
                        UpdatedPurchasePrices = productUpdates.Count,
                        Failed = 0,
                        Errors = skipMessages,
                    };

                    return ApiResponse<UpdateToStorePricesResultDto>.OK(result);
                }
                catch (Exception exTran)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(exTran, "更新到分店价格表事务失败");
                    var isBusy = SetChildPurchasePriceMutationLock.TryResolveConflict(
                        exTran,
                        out var conflict
                    );
                    var msg = isBusy
                        ? conflict!.Message
                        : exTran.InnerException?.Message ?? exTran.Message ?? "更新失败";
                    return ApiResponse<UpdateToStorePricesResultDto>.Error(
                        msg,
                        isBusy ? SetChildPurchasePriceMutationLock.BusyErrorCode : "UPDATE_ERROR"
                    );
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新到分店价格表失败");
                var isBusy = SetChildPurchasePriceMutationLock.TryResolveConflict(
                    ex,
                    out var conflict
                );
                var msg = isBusy
                    ? conflict!.Message
                    : ex.InnerException?.Message ?? ex.Message ?? "更新失败";
                return ApiResponse<UpdateToStorePricesResultDto>.Error(
                    msg,
                    isBusy ? SetChildPurchasePriceMutationLock.BusyErrorCode : "UPDATE_ERROR"
                );
            }
        }
    }
}
