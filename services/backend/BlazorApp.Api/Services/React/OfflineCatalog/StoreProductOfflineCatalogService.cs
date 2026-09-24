using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace BlazorApp.Api.Services.React.OfflineCatalog
{
    /// <summary>
    /// 移动端离线目录服务：从数据库构建分店索引（一售卖码一行），
    /// 由 IOfflineCatalogIndexCache 缓存版本并提供 sync-plan / full 分页 / delta 分页。
    /// 索引构建参考 Hbpos.Api CatalogService.BuildSellableIndexCoreAsync，行字段对齐商品维护 fast-detail。
    /// </summary>
    public class StoreProductOfflineCatalogService : IStoreProductOfflineCatalogService, IOfflineCatalogIndexBuilder
    {
        public const int DeltaMaxOperations = 5_000;
        private const int SourceBatchSize = 20_000;
        private const string PricingStrategiesCacheKey = "StoreProductMaintenance:PricingStrategies:Active";
        private static readonly TimeSpan PricingStrategiesCacheDuration = TimeSpan.FromSeconds(60);

        private readonly ISqlSugarClient _db;
        private readonly IOfflineCatalogIndexCache _cache;
        private readonly IAutoPricingService _autoPricingService;
        private readonly IMemoryCache _memoryCache;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StoreProductOfflineCatalogService> _logger;

        public StoreProductOfflineCatalogService(
            SqlSugarContext context,
            IOfflineCatalogIndexCache cache,
            IAutoPricingService autoPricingService,
            IMemoryCache memoryCache,
            IServiceScopeFactory scopeFactory,
            ILogger<StoreProductOfflineCatalogService> logger)
        {
            _db = context.Db;
            _cache = cache;
            _autoPricingService = autoPricingService;
            _memoryCache = memoryCache;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// 在独立 DI scope 中构建索引。
        ///
        /// 构建会在客户端断开后继续（结果进缓存），因此不能使用当前 HTTP 请求的 scoped DbContext，
        /// 否则请求结束、scope 释放后查询会静默失败。异常在此显式记录，避免后台任务的异常无人观察。
        /// </summary>
        private async Task<OfflineCatalogIndex?> BuildIndexInOwnScopeAsync(string storeCode, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var builder = scope.ServiceProvider.GetRequiredService<IOfflineCatalogIndexBuilder>();
            try
            {
                return await builder.BuildIndexAsync(storeCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OfflineCatalog build failed store={StoreCode}", storeCode);
                throw;
            }
        }

        public async Task<OfflineCatalogSyncPlanDto?> GetSyncPlanAsync(
            string storeCode,
            string? baseCatalogVersion,
            CancellationToken cancellationToken)
        {
            var normalizedStoreCode = storeCode.Trim();
            var normalizedBase = string.IsNullOrWhiteSpace(baseCatalogVersion) ? null : baseCatalogVersion.Trim();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var target = await _cache.GetOrBuildCurrentAsync(
                    normalizedStoreCode,
                    token => BuildIndexInOwnScopeAsync(normalizedStoreCode, token),
                    cancellationToken);
                if (target is null)
                {
                    return null;
                }

                if (normalizedBase is null)
                {
                    var lease = _cache.TryCreateFullLease(target);
                    if (lease is not null)
                    {
                        return BuildPlan(target, OfflineCatalogSyncModes.Full, null, lease.LeaseId, null);
                    }

                    continue;
                }

                if (string.Equals(normalizedBase, target.CatalogVersion, StringComparison.Ordinal))
                {
                    return BuildPlan(target, OfflineCatalogSyncModes.NoChange, normalizedBase, null, null);
                }

                var baseline = _cache.GetByVersion(normalizedStoreCode, normalizedBase);
                if (baseline is null)
                {
                    // 基线不在保留窗口内时不能猜测删除项，明确要求客户端回退全量。
                    var lease = _cache.TryCreateFullLease(target);
                    if (lease is not null)
                    {
                        return BuildPlan(target, OfflineCatalogSyncModes.Full, normalizedBase, lease.LeaseId, null);
                    }

                    continue;
                }

                var operations = target.GetDeltaOperations(baseline);
                if (operations.Count > DeltaMaxOperations)
                {
                    var lease = _cache.TryCreateFullLease(target);
                    if (lease is not null)
                    {
                        return BuildPlan(target, OfflineCatalogSyncModes.Full, normalizedBase, lease.LeaseId, operations.Count);
                    }

                    continue;
                }

                var deltaLease = _cache.TryCreateDeltaLease(baseline, target, operations);
                if (deltaLease is not null)
                {
                    return BuildPlan(target, OfflineCatalogSyncModes.Delta, normalizedBase, deltaLease.LeaseId, operations.Count);
                }
            }

            // 构建与计划计算期间版本反复被淘汰；让客户端稍后重试，不能下发失效租约。
            throw new OfflineCatalogCapacityBusyException();
        }

        public async Task<OfflineCatalogPageDto?> GetPageAsync(
            string storeCode,
            string? cursor,
            int pageSize,
            string? catalogVersion,
            string? downloadLeaseId,
            CancellationToken cancellationToken)
        {
            var normalizedStoreCode = storeCode.Trim();
            var pinnedVersion = string.IsNullOrWhiteSpace(catalogVersion) ? null : catalogVersion.Trim();
            OfflineCatalogIndex? index;
            if (!string.IsNullOrWhiteSpace(downloadLeaseId))
            {
                var lease = _cache.GetAndTouchLease(downloadLeaseId, normalizedStoreCode);
                if (lease is null || lease.Kind != "full")
                {
                    throw new OfflineCatalogSnapshotExpiredException(normalizedStoreCode, pinnedVersion ?? downloadLeaseId);
                }

                pinnedVersion ??= lease.TargetCatalogVersion;
                if (!string.Equals(pinnedVersion, lease.TargetCatalogVersion, StringComparison.Ordinal))
                {
                    throw new OfflineCatalogSnapshotExpiredException(normalizedStoreCode, pinnedVersion);
                }
            }

            if (pinnedVersion is not null)
            {
                index = _cache.GetByVersion(normalizedStoreCode, pinnedVersion)
                    ?? throw new OfflineCatalogSnapshotExpiredException(normalizedStoreCode, pinnedVersion);
            }
            else
            {
                index = await _cache.GetOrBuildCurrentAsync(
                    normalizedStoreCode,
                    token => BuildIndexInOwnScopeAsync(normalizedStoreCode, token),
                    cancellationToken);
                if (index is null)
                {
                    return null;
                }
            }

            var page = index.GetPage(cursor, pageSize);
            page.DownloadLeaseId = string.IsNullOrWhiteSpace(downloadLeaseId) ? null : downloadLeaseId;
            return page;
        }

        public Task<OfflineCatalogDeltaPageDto> GetDeltaPageAsync(
            string storeCode,
            string baseCatalogVersion,
            string targetCatalogVersion,
            string? cursor,
            int pageSize,
            string? downloadLeaseId,
            CancellationToken cancellationToken)
        {
            var normalizedStoreCode = storeCode.Trim();
            var baseline = _cache.GetByVersion(normalizedStoreCode, baseCatalogVersion.Trim())
                ?? throw new OfflineCatalogSnapshotExpiredException(normalizedStoreCode, baseCatalogVersion);
            var target = _cache.GetByVersion(normalizedStoreCode, targetCatalogVersion.Trim())
                ?? throw new OfflineCatalogSnapshotExpiredException(normalizedStoreCode, targetCatalogVersion);

            IReadOnlyList<OfflineCatalogDeltaOperation> operations;
            if (!string.IsNullOrWhiteSpace(downloadLeaseId))
            {
                var lease = _cache.GetAndTouchLease(downloadLeaseId, normalizedStoreCode);
                if (lease is null
                    || lease.Kind != "delta"
                    || lease.Operations is null
                    || !string.Equals(lease.BaseCatalogVersion, baseline.CatalogVersion, StringComparison.Ordinal)
                    || !string.Equals(lease.TargetCatalogVersion, target.CatalogVersion, StringComparison.Ordinal))
                {
                    throw new OfflineCatalogSnapshotExpiredException(normalizedStoreCode, targetCatalogVersion);
                }

                operations = lease.Operations;
            }
            else
            {
                operations = target.GetDeltaOperations(baseline);
            }

            var page = target.GetDeltaPage(baseline, operations, cursor, pageSize);
            page.DownloadLeaseId = string.IsNullOrWhiteSpace(downloadLeaseId) ? null : downloadLeaseId;
            return Task.FromResult(page);
        }

        private static OfflineCatalogSyncPlanDto BuildPlan(
            OfflineCatalogIndex target,
            string mode,
            string? baseVersion,
            string? leaseId,
            int? deltaOperationCount)
        {
            return new OfflineCatalogSyncPlanDto
            {
                StoreCode = target.StoreCode,
                GeneratedAt = OfflineCatalogChecksum.FormatTimestamp(target.GeneratedAt),
                Mode = mode,
                BaseCatalogVersion = baseVersion,
                TargetCatalogVersion = target.CatalogVersion,
                TargetTotal = target.Items.Count,
                DownloadLeaseId = leaseId,
                DeltaOperationCount = deltaOperationCount,
            };
        }

        // ---------------- 索引构建 ----------------

        public async Task<OfflineCatalogIndex?> BuildIndexAsync(string storeCode, CancellationToken cancellationToken)
        {
            var totalSw = Stopwatch.StartNew();
            var store = await _db.Queryable<Store>()
                .Where(s => s.StoreCode == storeCode && !s.IsDeleted)
                .Select(s => new { s.StoreCode, s.StoreName })
                .FirstAsync();
            if (store is null)
            {
                _logger.LogWarning("OfflineCatalog build skipped: store not found store={StoreCode}", storeCode);
                return null;
            }

            _logger.LogInformation("OfflineCatalog build start store={StoreCode}", storeCode);
            var stepSw = Stopwatch.StartNew();
            var baseRows = await LoadBaseRowsAsync(storeCode, cancellationToken);
            stepSw.Stop();
            var baseRowsMs = stepSw.ElapsedMilliseconds;
            _logger.LogInformation(
                "OfflineCatalog base rows loaded store={StoreCode} products={Products} elapsed_ms={ElapsedMs}",
                storeCode,
                baseRows.Count,
                baseRowsMs);

            stepSw.Restart();
            // 只取本店在售商品的套码/多码定义：ProductSetCode 是全局表，直接全表读取会把
            // 其他门店的数据也拉过来（远程库下传输量与耗时都不可接受），因此用分店价做内连接过滤。
            var setCodesByProduct = (await _db.Ado.SqlQueryAsync<ProductSetCode>(
                    """
                    SELECT
                        psc.SetCodeId, psc.ProductCode, psc.SetProductCode, psc.SetItemNumber, psc.SetBarcode,
                        psc.SetPurchasePrice, psc.SetRetailPrice, psc.SetQuantity, psc.SetType, psc.IsActive,
                        psc.UpdatedAt, psc.CreatedAt
                    FROM [ProductSetCode] psc
                    INNER JOIN [StoreRetailPrice] sp
                        ON sp.ProductCode = psc.ProductCode
                        AND sp.StoreCode = @StoreCode
                        AND ISNULL(sp.IsDeleted, 0) = 0
                    WHERE ISNULL(psc.IsDeleted, 0) = 0
                    """,
                    new SugarParameter("@StoreCode", storeCode)))
                .GroupBy(s => s.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var projections = (await _db.Queryable<StoreMultiCodeProduct>()
                    .Where(x => x.StoreCode == storeCode && !x.IsDeleted)
                    .Select(x => new StoreMultiCodeProduct
                    {
                        UUID = x.UUID,
                        StoreCode = x.StoreCode,
                        ProductCode = x.ProductCode,
                        MultiCodeProductCode = x.MultiCodeProductCode,
                        StoreMultiCodeProductCode = x.StoreMultiCodeProductCode,
                        MultiBarcode = x.MultiBarcode,
                        PurchasePrice = x.PurchasePrice,
                        MultiCodeRetailPrice = x.MultiCodeRetailPrice,
                        DiscountRate = x.DiscountRate,
                        IsAutoPricing = x.IsAutoPricing,
                        IsSpecialProduct = x.IsSpecialProduct,
                        IsActive = x.IsActive,
                        UpdatedAt = x.UpdatedAt,
                        CreatedAt = x.CreatedAt,
                    })
                    .ToListAsync())
                .GroupBy(x => ResolveSetProductCode(x.MultiCodeProductCode, x.UUID), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var clearanceByProduct = (await _db.Queryable<StoreClearancePrice>()
                    .Where(x => x.StoreCode == storeCode && !x.IsDeleted && x.ProductCode != null)
                    .Select(x => new StoreClearancePrice
                    {
                        UUID = x.UUID,
                        StoreCode = x.StoreCode,
                        ProductCode = x.ProductCode,
                        ClearanceBarcode = x.ClearanceBarcode,
                        ClearancePrice = x.ClearancePrice,
                        UpdatedAt = x.UpdatedAt,
                        CreatedAt = x.CreatedAt,
                    })
                    .ToListAsync())
                .GroupBy(x => x.ProductCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.UUID, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
            stepSw.Stop();
            var codesMs = stepSw.ElapsedMilliseconds;
            _logger.LogInformation(
                "OfflineCatalog codes loaded store={StoreCode} setCodeProducts={SetCodeProducts} projections={Projections} clearance={Clearance} elapsed_ms={ElapsedMs}",
                storeCode,
                setCodesByProduct.Count,
                projections.Count,
                clearanceByProduct.Count,
                codesMs);

            var strategies = await _memoryCache.GetOrCreateAsync(
                PricingStrategiesCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = PricingStrategiesCacheDuration;
                    return await _autoPricingService.GetAllActiveStrategiesAsync();
                }) ?? new List<PricingStrategy>();

            stepSw.Restart();
            var stats = new OfflineCatalogBuildStats();
            var items = new List<OfflineCatalogItemDto>(baseRows.Count * 3);
            foreach (var row in baseRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendProductRows(items, storeCode, store.StoreName, row, setCodesByProduct, projections, clearanceByProduct, strategies, stats);
            }

            var generatedAt = DateTimeOffset.UtcNow;
            var index = new OfflineCatalogIndex(storeCode, generatedAt, items);
            stepSw.Stop();
            totalSw.Stop();
            _logger.LogInformation(
                "OfflineCatalog build completed store={StoreCode} version={Version} products={Products} rows={Rows} pricing_failures={PricingFailures} base_rows_ms={BaseRowsMs} codes_ms={CodesMs} build_ms={BuildMs} total_ms={TotalMs}",
                storeCode,
                index.CatalogVersion,
                baseRows.Count,
                index.Items.Count,
                stats.PricingFailures,
                baseRowsMs,
                codesMs,
                stepSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds);
            return index;
        }

        /// <summary>按 (ProductCode, UUID) 键集分批读取本店分店价 + 商品基础字段，避免 OFFSET 越翻越慢。</summary>
        private async Task<List<OfflineCatalogBaseRow>> LoadBaseRowsAsync(string storeCode, CancellationToken cancellationToken)
        {
            var rows = new List<OfflineCatalogBaseRow>();
            var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? lastProductCode = null;
            string? lastUuid = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await _db.Ado.SqlQueryAsync<OfflineCatalogBaseRow>(
                    """
                    SELECT TOP (@BatchSize)
                        p.ProductCode AS ProductCode,
                        p.ProductName AS ProductName,
                        p.ItemNumber AS ItemNumber,
                        p.Barcode AS Barcode,
                        p.ProductImage AS ProductImage,
                        p.ProductType AS ProductType,
                        p.LocalSupplierCode AS LocalSupplierCode,
                        ls.Name AS LocalSupplierName,
                        pg.Grade AS Grade,
                        sp.UUID AS StorePriceUuid,
                        sp.SupplierCode AS SupplierCode,
                        sp.PurchasePrice AS PurchasePrice,
                        sp.StoreRetailPriceValue AS RetailPrice,
                        sp.DiscountRate AS DiscountRate,
                        sp.IsAutoPricing AS IsAutoPricing,
                        sp.IsSpecialProduct AS IsSpecialProduct,
                        sp.UpdatedAt AS StorePriceUpdatedAt,
                        sp.CreatedAt AS StorePriceCreatedAt,
                        p.UpdatedAt AS ProductUpdatedAt
                    FROM [StoreRetailPrice] sp
                    INNER JOIN [Product] p
                        ON p.ProductCode = sp.ProductCode AND ISNULL(p.IsDeleted, 0) = 0
                    LEFT JOIN [ProductGrade] pg
                        ON pg.ProductCode = p.ProductCode AND ISNULL(pg.IsDeleted, 0) = 0
                    LEFT JOIN [LocalSupplier] ls
                        ON ls.LocalSupplierCode = p.LocalSupplierCode AND ISNULL(ls.IsDeleted, 0) = 0
                    WHERE sp.StoreCode = @StoreCode
                        AND ISNULL(sp.IsDeleted, 0) = 0
                        AND sp.ProductCode IS NOT NULL
                        AND (@LastProductCode IS NULL
                             OR sp.ProductCode > @LastProductCode
                             OR (sp.ProductCode = @LastProductCode AND sp.UUID > @LastUuid))
                    ORDER BY sp.ProductCode, sp.UUID
                    """,
                    new SugarParameter("@BatchSize", SourceBatchSize),
                    new SugarParameter("@StoreCode", storeCode),
                    new SugarParameter("@LastProductCode", (object?)lastProductCode ?? DBNull.Value),
                    new SugarParameter("@LastUuid", (object?)lastUuid ?? DBNull.Value));
                foreach (var row in batch)
                {
                    if (string.IsNullOrWhiteSpace(row.ProductCode) || !seenProducts.Add(row.ProductCode))
                    {
                        // 同店同商品多条分店价只取键集顺序的第一条，与 fast-detail 的 TOP 1 语义一致。
                        continue;
                    }

                    rows.Add(row);
                }

                _logger.LogInformation(
                    "OfflineCatalog base rows batch store={StoreCode} batch={BatchCount} total={Total}",
                    storeCode,
                    batch.Count,
                    rows.Count);

                if (batch.Count < SourceBatchSize)
                {
                    break;
                }

                var last = batch[^1];
                lastProductCode = last.ProductCode;
                lastUuid = last.StorePriceUuid;
            }

            return rows;
        }

        private void AppendProductRows(
            List<OfflineCatalogItemDto> items,
            string storeCode,
            string storeName,
            OfflineCatalogBaseRow row,
            Dictionary<string, List<ProductSetCode>> setCodesByProduct,
            Dictionary<string, StoreMultiCodeProduct> projections,
            Dictionary<string, StoreClearancePrice> clearanceByProduct,
            List<PricingStrategy> strategies,
            OfflineCatalogBuildStats stats)
        {
            var productCode = row.ProductCode!.Trim();
            var supplierCode = row.SupplierCode ?? row.LocalSupplierCode;
            var pricing = ResolvePricing(strategies, row.PurchasePrice, supplierCode, storeCode, stats);
            clearanceByProduct.TryGetValue(productCode, out var clearance);
            var productUpdatedAt = Latest(
                ToOffset(row.StorePriceUpdatedAt ?? row.StorePriceCreatedAt),
                ToOffset(row.ProductUpdatedAt));

            OfflineCatalogItemDto CreateProductLevel(string lookupCode, string matchSource)
            {
                return OfflineCatalogItemFinalizer.Finalize(new OfflineCatalogItemDto
                {
                    StoreCode = storeCode,
                    LookupCode = lookupCode,
                    MatchSource = matchSource,
                    ProductCode = productCode,
                    ProductName = row.ProductName ?? string.Empty,
                    ItemNumber = NullIfBlank(row.ItemNumber),
                    Barcode = NullIfBlank(row.Barcode),
                    ProductImage = NullIfBlank(row.ProductImage),
                    ProductType = row.ProductType,
                    Grade = NullIfBlank(row.Grade),
                    LocalSupplierCode = NullIfBlank(row.LocalSupplierCode),
                    LocalSupplierName = NullIfBlank(row.LocalSupplierName),
                    StoreName = NullIfBlank(storeName),
                    StorePriceUuid = NullIfBlank(row.StorePriceUuid),
                    PurchasePrice = row.PurchasePrice,
                    RetailPrice = row.RetailPrice,
                    DiscountRate = row.DiscountRate,
                    IsAutoPricing = row.IsAutoPricing ?? false,
                    IsSpecialProduct = row.IsSpecialProduct ?? false,
                    Rate = pricing.Rate,
                    StrategySourceLabel = pricing.StrategySourceLabel,
                    StrategyRuleLabel = pricing.StrategyRuleLabel,
                    ClearanceUuid = clearance?.UUID,
                    ClearanceBarcode = NullIfBlank(clearance?.ClearanceBarcode),
                    ClearancePrice = clearance?.ClearancePrice,
                    UpdatedAt = OfflineCatalogChecksum.FormatTimestamp(productUpdatedAt),
                });
            }

            // 商品级三行：主条码 / 货号 / 商品编码，共享同一组商品级字段。
            if (!string.IsNullOrWhiteSpace(row.Barcode))
            {
                items.Add(CreateProductLevel(row.Barcode.Trim(), "ProductBarcode"));
            }

            if (!string.IsNullOrWhiteSpace(row.ItemNumber))
            {
                items.Add(CreateProductLevel(row.ItemNumber.Trim(), "ItemNumber"));
            }

            items.Add(CreateProductLevel(productCode, "ProductCode"));

            if (clearance is not null && !string.IsNullOrWhiteSpace(clearance.ClearanceBarcode))
            {
                var clearanceRow = CreateProductLevel(clearance.ClearanceBarcode.Trim(), "ClearanceBarcode");
                clearanceRow.UpdatedAt = OfflineCatalogChecksum.FormatTimestamp(
                    Latest(productUpdatedAt, ToOffset(clearance.UpdatedAt ?? clearance.CreatedAt)));
                items.Add(OfflineCatalogItemFinalizer.Finalize(clearanceRow));
            }

            if (!setCodesByProduct.TryGetValue(productCode, out var setCodes))
            {
                return;
            }

            foreach (var setCode in setCodes)
            {
                var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                projections.TryGetValue(setProductCode, out var projection);
                var isMulti = row.ProductType == 2;
                var barcode = projection?.MultiBarcode ?? setCode.SetBarcode;
                if (string.IsNullOrWhiteSpace(barcode))
                {
                    continue;
                }

                var codeRow = CreateProductLevel(barcode.Trim(), isMulti ? "MultiBarcode" : "SetBarcode");
                codeRow.CodeId = setCode.SetCodeId;
                codeRow.CodeUuid = projection?.UUID;
                codeRow.CodeProductCode = setProductCode;
                codeRow.CodeItemNumber = NullIfBlank(setCode.SetItemNumber);
                codeRow.CodeRetailPrice = projection?.MultiCodeRetailPrice ?? setCode.SetRetailPrice;
                codeRow.CodePurchasePrice = projection?.PurchasePrice ?? setCode.SetPurchasePrice;
                codeRow.CodeQuantity = setCode.SetQuantity;
                codeRow.CodeType = setCode.SetType;
                codeRow.CodeDiscountRate = projection?.DiscountRate;
                codeRow.CodeIsAutoPricing = projection?.IsAutoPricing ?? false;
                codeRow.CodeIsSpecialProduct = projection?.IsSpecialProduct ?? false;
                codeRow.CodeIsActive = projection?.IsActive ?? setCode.IsActive;
                codeRow.UpdatedAt = OfflineCatalogChecksum.FormatTimestamp(
                    Latest(
                        productUpdatedAt,
                        Latest(
                            ToOffset(setCode.UpdatedAt ?? setCode.CreatedAt),
                            ToOffset(projection?.UpdatedAt ?? projection?.CreatedAt))));
                items.Add(OfflineCatalogItemFinalizer.Finalize(codeRow));
            }
        }

        private (decimal? Rate, string? StrategySourceLabel, string? StrategyRuleLabel) ResolvePricing(
            List<PricingStrategy> strategies,
            decimal? purchasePrice,
            string? supplierCode,
            string storeCode,
            OfflineCatalogBuildStats stats)
        {
            if (!purchasePrice.HasValue || purchasePrice.Value <= 0)
            {
                return (null, null, null);
            }

            var empty = new List<PricingStrategy>();
            var supplierStrategies = string.IsNullOrWhiteSpace(supplierCode)
                ? empty
                : strategies.Where(s => s.Targets?.Any(t => t.TargetType == "Supplier" && t.TargetCode == supplierCode) ?? false).ToList();
            var storeStrategies = strategies
                .Where(s => s.Targets?.Any(t => t.TargetType == "Store" && t.TargetCode == storeCode) ?? false)
                .ToList();
            var globalStrategies = strategies
                .Where(s => s.Level == "Global" || s.Targets == null || s.Targets.Count == 0)
                .ToList();
            try
            {
                var strategy = _autoPricingService.FindBestStrategyForPrice(purchasePrice.Value, supplierStrategies, storeStrategies, globalStrategies);
                var rate = _autoPricingService.CalculateRate(purchasePrice.Value, strategy);
                var rule = strategy?.Details?.FirstOrDefault(d => purchasePrice.Value >= d.MinPrice && purchasePrice.Value <= d.MaxPrice);
                return (
                    rate,
                    strategy?.Name ?? "自动定价策略",
                    rule == null ? null : $"{rule.MinPrice:0.##} - {rule.MaxPrice:0.##}");
            }
            catch (Exception)
            {
                // 个别商品的进货价不满足定价策略约束（例如低于 0.10 时 CalculateRate 直接抛出），
                // 只放弃该行的建议 Rate 与策略标签；整店离线目录的查询与标签打印不依赖这两个展示字段，
                // 绝不能让单个脏数据导致十几万商品的快照构建整体失败。
                stats.PricingFailures += 1;
                return (null, null, null);
            }
        }

        /// <summary>单次构建的降级统计，便于在完成日志中量化脏数据规模。</summary>
        private sealed class OfflineCatalogBuildStats
        {
            public int PricingFailures;
        }

        private static string ResolveSetProductCode(string? setProductCode, string fallback)
        {
            return string.IsNullOrWhiteSpace(setProductCode) ? fallback : setProductCode;
        }

        private static string? NullIfBlank(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static DateTimeOffset? Latest(DateTimeOffset? left, DateTimeOffset? right)
        {
            if (!left.HasValue) return right;
            if (!right.HasValue) return left;
            return left.Value >= right.Value ? left : right;
        }

        private static DateTimeOffset? ToOffset(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            // 数据库时间不带时区信息；按 UTC 标注即可，两端只要求同一时间戳字符串一致。
            var utc = value.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : value.Value.ToUniversalTime();
            return new DateTimeOffset(utc);
        }

        /// <summary>SqlSugar 原生 SQL 映射行；字段名与 SELECT 别名一致。</summary>
        public sealed class OfflineCatalogBaseRow
        {
            public string? ProductCode { get; set; }
            public string? ProductName { get; set; }
            public string? ItemNumber { get; set; }
            public string? Barcode { get; set; }
            public string? ProductImage { get; set; }
            public int? ProductType { get; set; }
            public string? LocalSupplierCode { get; set; }
            public string? LocalSupplierName { get; set; }
            public string? Grade { get; set; }
            public string? StorePriceUuid { get; set; }
            public string? SupplierCode { get; set; }
            public decimal? PurchasePrice { get; set; }
            public decimal? RetailPrice { get; set; }
            public decimal? DiscountRate { get; set; }
            public bool? IsAutoPricing { get; set; }
            public bool? IsSpecialProduct { get; set; }
            public DateTime? StorePriceUpdatedAt { get; set; }
            public DateTime? StorePriceCreatedAt { get; set; }
            public DateTime? ProductUpdatedAt { get; set; }
        }
    }
}
