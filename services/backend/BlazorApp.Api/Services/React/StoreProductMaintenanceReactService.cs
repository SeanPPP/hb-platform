using System.Diagnostics;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreProductMaintenanceReactService : IStoreProductMaintenanceReactService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<StoreProductMaintenanceReactService> _logger;
        private readonly IAutoPricingService _autoPricingService;
        private readonly IMemoryCache _cache;
        private const string PricingStrategiesCacheKey = "StoreProductMaintenance:PricingStrategies:Active";
        private static readonly TimeSpan PricingStrategiesCacheDuration = TimeSpan.FromSeconds(60);

        public StoreProductMaintenanceReactService(
            SqlSugarContext context,
            ILogger<StoreProductMaintenanceReactService> logger,
            IAutoPricingService autoPricingService,
            IMemoryCache cache
        )
        {
            _db = context.Db;
            _logger = logger;
            _autoPricingService = autoPricingService;
            _cache = cache;
        }

        public async Task<ApiResponse<List<StoreProductLookupItemDto>>> LookupAsync(
            StoreProductLookupRequestDto request,
            List<string>? accessibleStoreCodes
        )
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                var keyword = request.Keyword?.Trim();
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    _logger.LogDebug("StoreProductMaintenance lookup skipped because keyword is empty");
                    return ApiResponse<List<StoreProductLookupItemDto>>.Error("查询内容不能为空");
                }

                var selectedStoreCodes = ResolveScopedStoreCodes(request.StoreCode, accessibleStoreCodes);
                _logger.LogDebug(
                    "StoreProductMaintenance lookup started keyword={Keyword} requestedStore={RequestedStore} scope={Scope}",
                    keyword,
                    request.StoreCode,
                    FormatStoreScope(selectedStoreCodes)
                );
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    _logger.LogDebug(
                        "StoreProductMaintenance lookup returned empty because store scope is NONE keyword={Keyword}",
                        keyword
                    );
                    return ApiResponse<List<StoreProductLookupItemDto>>.OK(new List<StoreProductLookupItemDto>());
                }

                var matchSw = Stopwatch.StartNew();
                var hits = (await QueryLookupHitsAsync(keyword, selectedStoreCodes))
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                    .ToList();
                matchSw.Stop();

                var productMatches = hits.Count(item =>
                    item.MatchSource is "ProductBarcode" or "ItemNumber"
                );
                var setMatches = hits.Count(item => item.MatchSource == "SetBarcode");
                var clearanceMatches = hits.Count(item => item.MatchSource == "ClearanceBarcode");

                var enrichSw = Stopwatch.StartNew();
                var productCodeList = hits
                    .Select(item => item.ProductCode)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var productSnapshotMap = await QueryLookupProductSnapshotsAsync(productCodeList);
                enrichSw.Stop();

                var normalized = hits
                    .Where(item => productSnapshotMap.ContainsKey(item.ProductCode))
                    .Select(item =>
                    {
                        var snapshot = productSnapshotMap[item.ProductCode];
                        return new StoreProductLookupItemDto
                        {
                            ProductCode = snapshot.ProductCode,
                            ProductName = snapshot.ProductName,
                            ItemNumber = item.ItemNumber ?? snapshot.ItemNumber,
                            Barcode = item.Barcode ?? snapshot.Barcode,
                            ProductImage = snapshot.ProductImage,
                            Grade = snapshot.Grade,
                            ProductTypeLabel = NormalizeProductTypeLabel(snapshot.ProductType?.ToString()),
                            MatchSource = item.MatchSource,
                            MatchValue = item.MatchValue,
                        };
                    })
                    .GroupBy(item => $"{item.ProductCode}|{item.MatchSource}|{item.Barcode}|{item.ItemNumber}")
                    .Select(group =>
                    {
                        return group.First();
                    })
                    .OrderBy(item => item.ProductName)
                    .ToList();

                _logger.LogInformation(
                    "StoreProductMaintenance lookup timing keyword={Keyword} requestedStore={RequestedStore} scope={Scope} lookup_match_ms={LookupMatchMs} lookup_enrich_ms={LookupEnrichMs} product_hits={ProductHits} set_hits={SetHits} clearance_hits={ClearanceHits} raw_hit_count={RawHitCount} deduped_result_count={DedupedResultCount} total_ms={TotalMs}",
                    keyword,
                    request.StoreCode,
                    FormatStoreScope(selectedStoreCodes),
                    matchSw.ElapsedMilliseconds,
                    enrichSw.ElapsedMilliseconds,
                    productMatches,
                    setMatches,
                    clearanceMatches,
                    hits.Count,
                    normalized.Count,
                    totalSw.ElapsedMilliseconds
                );

                return ApiResponse<List<StoreProductLookupItemDto>>.OK(normalized);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品查询失败: {Keyword}", request.Keyword);
                return ApiResponse<List<StoreProductLookupItemDto>>.Error($"商品查询失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductDetailDto>> GetDetailAsync(
            string productCode,
            string? storeCode,
            List<string>? accessibleStoreCodes,
            bool includeCodes = true
        )
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return ApiResponse<StoreProductDetailDto>.Error("商品编码不能为空");
                }

                var selectedStoreCodes = ResolveScopedStoreCodes(storeCode, accessibleStoreCodes);
                _logger.LogDebug(
                    "StoreProductMaintenance detail started productCode={ProductCode} requestedStore={RequestedStore} scope={Scope}",
                    productCode,
                    storeCode,
                    FormatStoreScope(selectedStoreCodes)
                );
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    return ApiResponse<StoreProductDetailDto>.Error("当前账号或设备无权访问该分店");
                }

                var productSw = Stopwatch.StartNew();
                var product = await _db.Queryable<Product>()
                    .LeftJoin<ProductGrade>((p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .LeftJoin<HBLocalSupplier>((p, pg, ls) =>
                        p.LocalSupplierCode == ls.LocalSupplierCode && !ls.IsDeleted
                    )
                    .Where((p, pg, ls) => p.ProductCode == productCode && !p.IsDeleted)
                    .Select((p, pg, ls) => new
                    {
                        p.ProductCode,
                        p.ProductName,
                        p.ItemNumber,
                        p.Barcode,
                        p.ProductImage,
                        p.ProductType,
                        p.LocalSupplierCode,
                        LocalSupplierName = ls.Name,
                        Grade = pg.Grade,
                    })
                    .FirstAsync();
                productSw.Stop();

                if (product == null)
                {
                    return ApiResponse<StoreProductDetailDto>.Error("商品不存在");
                }

                StoreProductStorePriceDto? storePrice = null;
                StoreRetailPrice? storePriceEntity = null;
                var storePriceSw = Stopwatch.StartNew();
                if (selectedStoreCodes == null || selectedStoreCodes.Count > 0)
                {
                    storePriceEntity = await QueryStorePriceAsync(productCode, selectedStoreCodes);
                }
                storePriceSw.Stop();

                StoreProductClearancePriceDto? clearancePrice = null;
                StoreClearancePrice? clearancePriceEntity = null;
                var clearanceSw = Stopwatch.StartNew();
                if (selectedStoreCodes == null || selectedStoreCodes.Count > 0)
                {
                    clearancePriceEntity = await QueryClearancePriceAsync(productCode, selectedStoreCodes);
                }
                clearanceSw.Stop();

                var setCodesSw = Stopwatch.StartNew();
                var productSetCodes = includeCodes
                    ? await QueryProductSetCodesAsync(productCode, null, null)
                    : new List<ProductSetCode>();
                var codeCounts = includeCodes
                    ? new ProductSetCodeCounts
                    {
                        TotalSetCodeCount = productSetCodes.Count,
                    }
                    : await QueryProductSetCodeCountsAsync(productCode);
                setCodesSw.Stop();

                var currentStoreCode = ResolveCurrentStoreCode(storeCode, selectedStoreCodes, storePriceEntity);
                var pricingStrategyLoadSw = Stopwatch.StartNew();
                var pricingContext = await CreatePricingContextAsync();
                pricingStrategyLoadSw.Stop();

                var storeNameLoadSw = Stopwatch.StartNew();
                var storeNameMap = await QueryStoreNamesAsync(
                    storePriceEntity?.StoreCode,
                    clearancePriceEntity?.StoreCode,
                    currentStoreCode
                );
                storeNameLoadSw.Stop();

                if (storePriceEntity != null)
                {
                    storeNameMap.TryGetValue(storePriceEntity.StoreCode ?? string.Empty, out var storeName);
                    storePrice = BuildStorePriceDto(
                        storePriceEntity,
                        product.LocalSupplierCode,
                        storeName,
                        pricingContext
                    );
                }

                if (clearancePriceEntity != null)
                {
                    storeNameMap.TryGetValue(clearancePriceEntity.StoreCode ?? string.Empty, out var storeName);
                    clearancePrice = BuildClearancePriceDto(clearancePriceEntity, storeName);
                }

                var projectionSw = Stopwatch.StartNew();
                var projections = includeCodes
                    ? await QueryProjectedStoreMultiCodesAsync(productSetCodes, currentStoreCode)
                    : new List<StoreMultiCodeProduct>();
                projectionSw.Stop();
                var projectionMap = projections.ToDictionary(
                    p => ResolveSetProductCode(p.MultiCodeProductCode, p.UUID),
                    p => p
                );

                var setCodes = new List<StoreProductSetCodeDto>();
                var multiCodes = new List<StoreProductMultiCodeDto>();
                var buildResponseSw = Stopwatch.StartNew();
                foreach (var setCode in productSetCodes)
                {
                    var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                    projectionMap.TryGetValue(setProductCode, out var projection);
                    if (product.ProductType == 2)
                    {
                        multiCodes.Add(
                            BuildMultiCodeDto(
                                setCode,
                                projection,
                                product.LocalSupplierCode,
                                pricingContext
                            )
                        );
                        continue;
                    }

                    setCodes.Add(BuildSetCodeDto(setCode, projection));
                }
                buildResponseSw.Stop();

                var detail = new StoreProductDetailDto
                {
                    ProductCode = product.ProductCode ?? string.Empty,
                    ProductName = product.ProductName,
                    ItemNumber = product.ItemNumber,
                    Barcode = product.Barcode,
                    ProductImage = product.ProductImage,
                    ProductType = product.ProductType,
                    ProductTypeLabel = NormalizeProductTypeLabel(product.ProductType?.ToString()),
                    Grade = product.Grade,
                    LocalSupplierCode = product.LocalSupplierCode,
                    LocalSupplierName = product.LocalSupplierName,
                    StorePrice = storePrice,
                    ClearancePrice = clearancePrice,
                    MultiCodes = multiCodes,
                    SetCodes = setCodes,
                    SetCodeCount = product.ProductType == 2 ? 0 : codeCounts.TotalSetCodeCount,
                    MultiCodeCount = product.ProductType == 2 ? codeCounts.TotalSetCodeCount : 0,
                    CodesIncluded = includeCodes,
                };

                _logger.LogInformation(
                    "StoreProductMaintenance detail timing productCode={ProductCode} requestedStore={RequestedStore} scope={Scope} includeCodes={IncludeCodes} product_ms={ProductMs} storePrice_ms={StorePriceMs} clearance_ms={ClearanceMs} productSetCodes_ms={ProductSetCodesMs} pricing_strategy_load_ms={PricingStrategyLoadMs} store_name_load_ms={StoreNameLoadMs} projection_query_ms={ProjectionQueryMs} response_build_ms={ResponseBuildMs} set_count={SetCount} multi_count={MultiCount} total_ms={TotalMs}",
                    productCode,
                    storeCode,
                    FormatStoreScope(selectedStoreCodes),
                    includeCodes,
                    productSw.ElapsedMilliseconds,
                    storePriceSw.ElapsedMilliseconds,
                    clearanceSw.ElapsedMilliseconds,
                    setCodesSw.ElapsedMilliseconds,
                    pricingStrategyLoadSw.ElapsedMilliseconds,
                    storeNameLoadSw.ElapsedMilliseconds,
                    projectionSw.ElapsedMilliseconds,
                    buildResponseSw.ElapsedMilliseconds,
                    setCodes.Count,
                    multiCodes.Count,
                    totalSw.ElapsedMilliseconds
                );

                return ApiResponse<StoreProductDetailDto>.OK(detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
                return ApiResponse<StoreProductDetailDto>.Error($"获取商品详情失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductDetailDto>> GetFastDetailAsync(
            string productCode,
            string? storeCode,
            List<string>? accessibleStoreCodes
        )
        {
            var totalSw = Stopwatch.StartNew();
            try
            {
                var normalizedProductCode = productCode?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedProductCode))
                {
                    return ApiResponse<StoreProductDetailDto>.Error("商品编码不能为空");
                }

                var selectedStoreCodes = ResolveScopedStoreCodes(storeCode, accessibleStoreCodes);
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    return ApiResponse<StoreProductDetailDto>.Error("当前账号或设备无权访问该分店");
                }

                var targetStoreCode = ResolveFastDetailTargetStoreCode(storeCode, selectedStoreCodes);
                var baseSw = Stopwatch.StartNew();
                var fastRow = await QueryFastDetailBaseAsync(normalizedProductCode, targetStoreCode);
                baseSw.Stop();
                if (fastRow == null)
                {
                    return ApiResponse<StoreProductDetailDto>.Error("商品不存在");
                }

                var pricingContextRequired =
                    !string.IsNullOrWhiteSpace(fastRow.StorePriceUuid)
                    && fastRow.PurchasePrice.HasValue
                    && fastRow.PurchasePrice.Value > 0;
                PricingContext? pricingContext = null;
                var pricingStrategyLoadSw = Stopwatch.StartNew();
                if (pricingContextRequired)
                {
                    pricingContext = await CreatePricingContextAsync();
                }
                pricingStrategyLoadSw.Stop();

                var responseBuildSw = Stopwatch.StartNew();
                var storePriceEntity = BuildStorePriceEntity(fastRow);
                var clearancePriceEntity = BuildClearancePriceEntity(fastRow);
                var totalSetCodes = fastRow.TotalSetCodeCount;
                int setCodeCount = 0;
                int multiCodeCount = 0;
                if (totalSetCodes > 0)
                {
                    if (fastRow.ProductType == 2)
                        multiCodeCount = totalSetCodes;
                    else
                        setCodeCount = totalSetCodes;
                }
                var detail = new StoreProductDetailDto
                {
                    ProductCode = fastRow.ProductCode ?? string.Empty,
                    ProductName = fastRow.ProductName ?? string.Empty,
                    ItemNumber = fastRow.ItemNumber,
                    Barcode = fastRow.Barcode,
                    ProductImage = fastRow.ProductImage,
                    ProductType = fastRow.ProductType,
                    ProductTypeLabel = NormalizeProductTypeLabel(fastRow.ProductType?.ToString()),
                    Grade = fastRow.Grade,
                    LocalSupplierCode = fastRow.LocalSupplierCode,
                    LocalSupplierName = fastRow.LocalSupplierName,
                    StorePrice = storePriceEntity == null
                        ? null
                        : BuildStorePriceDto(
                            storePriceEntity,
                            fastRow.LocalSupplierCode,
                            fastRow.StoreName,
                            pricingContext
                        ),
                    ClearancePrice = clearancePriceEntity == null
                        ? null
                        : BuildClearancePriceDto(clearancePriceEntity, fastRow.ClearanceStoreName),
                    SetCodes = new List<StoreProductSetCodeDto>(),
                    MultiCodes = new List<StoreProductMultiCodeDto>(),
                    SetCodeCount = setCodeCount,
                    MultiCodeCount = multiCodeCount,
                    CodesIncluded = false,
                };
                responseBuildSw.Stop();

                _logger.LogInformation(
                    "StoreProductMaintenance fast-detail timing productCode={ProductCode} requestedStore={RequestedStore} scope={Scope} targetStore={TargetStore} pricing_context_required={PricingContextRequired} base_ms={BaseMs} pricing_strategy_load_ms={PricingStrategyLoadMs} response_build_ms={ResponseBuildMs} set_count={SetCount} multi_count={MultiCount} total_ms={TotalMs}",
                    normalizedProductCode,
                    storeCode,
                    FormatStoreScope(selectedStoreCodes),
                    targetStoreCode,
                    pricingContextRequired,
                    baseSw.ElapsedMilliseconds,
                    pricingStrategyLoadSw.ElapsedMilliseconds,
                    responseBuildSw.ElapsedMilliseconds,
                    detail.SetCodeCount,
                    detail.MultiCodeCount,
                    totalSw.ElapsedMilliseconds
                );

                return ApiResponse<StoreProductDetailDto>.OK(detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品快详情失败: {ProductCode}", productCode);
                return ApiResponse<StoreProductDetailDto>.Error($"获取商品详情失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            UpdateStoreProductPriceDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var entity = await _db.Queryable<StoreRetailPrice>()
                    .Where(x => x.UUID == uuid && !x.IsDeleted)
                    .FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<StoreProductStorePriceDto>.Error("分店商品记录不存在");
                }

                if (!CanAccessStore(entity.StoreCode, accessibleStoreCodes))
                {
                    return ApiResponse<StoreProductStorePriceDto>.Error("当前账号或设备无权修改该分店商品");
                }

                if (request.PurchasePrice.HasValue && request.PurchasePrice.Value < 0)
                {
                    return ApiResponse<StoreProductStorePriceDto>.Error("进货价不能为负数");
                }

                if (request.RetailPrice.HasValue && request.RetailPrice.Value < 0)
                {
                    return ApiResponse<StoreProductStorePriceDto>.Error("零售价不能为负数");
                }

                if (
                    request.DiscountRate.HasValue
                    && (request.DiscountRate.Value < 0 || request.DiscountRate.Value > 1)
                )
                {
                    return ApiResponse<StoreProductStorePriceDto>.Error("折扣率必须在 0 到 1 之间");
                }

                entity.PurchasePrice = request.PurchasePrice;
                entity.StoreRetailPriceValue = request.RetailPrice;
                entity.DiscountRate = request.DiscountRate;
                if (request.IsAutoPricing.HasValue)
                {
                    entity.IsAutoPricing = request.IsAutoPricing.Value;
                }

                if (request.IsSpecialProduct.HasValue)
                {
                    entity.IsSpecialProduct = request.IsSpecialProduct.Value;
                }

                if (request.IsActive.HasValue)
                {
                    entity.IsActive = request.IsActive.Value;
                }

                var supplierCode = entity.SupplierCode;
                if (string.IsNullOrWhiteSpace(supplierCode))
                {
                    supplierCode = await _db.Queryable<Product>()
                        .Where(p => p.ProductCode == entity.ProductCode)
                        .Select(p => p.LocalSupplierCode)
                        .FirstAsync();
                }

                if (entity.IsAutoPricing && entity.PurchasePrice.HasValue)
                {
                    var strategy = await _autoPricingService.FindStrategyForPriceAsync(
                        entity.PurchasePrice.Value,
                        supplierCode,
                        entity.StoreCode
                    );
                    entity.StoreRetailPriceValue = _autoPricingService.CalculateRetailPrice(
                        entity.PurchasePrice.Value,
                        strategy
                    );
                }

                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = updatedBy;
                await _db.Updateable(entity).ExecuteCommandAsync();
                await SyncCurrentStoreProjectedRecordsAsync(entity, updatedBy);

                var dto = await BuildStorePriceDtoAsync(entity, supplierCode);
                return ApiResponse<StoreProductStorePriceDto>.OK(dto, "保存成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店商品失败: {Uuid}", uuid);
                return ApiResponse<StoreProductStorePriceDto>.Error($"更新分店商品失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<SyncStoreProductWarehousePriceResultDto>> SyncWarehousePriceAsync(
            string uuid,
            SyncStoreProductWarehousePriceRequestDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            await _db.Ado.BeginTranAsync();
            try
            {
                var result = await SyncWarehousePriceWithinTransactionAsync(
                    uuid,
                    request,
                    updatedBy,
                    accessibleStoreCodes
                );
                if (result.Success)
                {
                    await _db.Ado.CommitTranAsync();
                }
                else
                {
                    await _db.Ado.RollbackTranAsync();
                }

                return result;
            }
            catch (Exception ex)
            {
                await _db.Ado.RollbackTranAsync();
                _logger.LogError(ex, "分店商品仓库价格对账失败: {Uuid}", uuid);
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error(
                    "仓库价格对账失败，请稍后重试"
                );
            }
        }

        private async Task<ApiResponse<SyncStoreProductWarehousePriceResultDto>> SyncWarehousePriceWithinTransactionAsync(
            string uuid,
            SyncStoreProductWarehousePriceRequestDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            // 定位读取只取锁序所需字段，保持普通 READ COMMITTED，不提前占用分店价更新锁。
            var locator = await _db.Ado.SqlQuerySingleAsync<StoreRetailPrice>(
                "SELECT UUID, StoreCode, ProductCode FROM StoreRetailPrice WHERE UUID = @uuid AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                new { uuid }
            );
            if (locator == null)
            {
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error(
                    "分店商品记录不存在"
                );
            }

            if (!CanAccessStore(locator.StoreCode, accessibleStoreCodes))
            {
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error(
                    "当前账号或设备无权修改该分店商品"
                );
            }

            // 与仓库商品写链路保持 Product → WarehouseProduct → StoreRetailPrice 的固定锁序。
            var product = await WithWarehouseSyncUpdateLock(
                    _db.Queryable<Product>()
                        .Where(x => x.ProductCode == locator.ProductCode && !x.IsDeleted)
                )
                .FirstAsync();
            var warehouseProduct = await WithWarehouseSyncUpdateLock(
                    _db.Queryable<WarehouseProduct>()
                        .Where(x => x.ProductCode == locator.ProductCode && !x.IsDeleted)
                )
                .FirstAsync();
            var entity = await WithWarehouseSyncUpdateLock(
                    _db.Queryable<StoreRetailPrice>().Where(x => x.UUID == uuid && !x.IsDeleted)
                )
                .FirstAsync();
            if (entity == null)
            {
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error(
                    "分店商品记录已变化",
                    "PRICE_VERSION_CONFLICT"
                );
            }

            // 最终锁定后必须先复核权限，避免并发换店时把无权分店价格放进冲突响应。
            if (!CanAccessStore(entity.StoreCode, accessibleStoreCodes))
            {
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error(
                    "当前账号或设备无权修改该分店商品"
                );
            }

            if (
                !string.Equals(entity.ProductCode, locator.ProductCode, StringComparison.Ordinal)
                || !string.Equals(entity.StoreCode, locator.StoreCode, StringComparison.Ordinal)
            )
            {
                return await BuildWarehousePriceConflictAsync(
                    entity,
                    entity.SupplierCode,
                    null,
                    null
                );
            }

            if (product == null)
            {
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error("商品记录不存在");
            }

            var supplierCode = product.LocalSupplierCode?.Trim();
            var previousPurchasePrice = entity.PurchasePrice;
            var previousRetailPrice = entity.StoreRetailPriceValue;
            var discountRate = entity.DiscountRate;
            if (!string.Equals(supplierCode, "200", StringComparison.Ordinal))
            {
                var notApplicable = await BuildWarehousePriceSyncResultAsync(
                    entity,
                    supplierCode,
                    "not_applicable",
                    false,
                    false,
                    false,
                    null,
                    null,
                    previousPurchasePrice,
                    previousRetailPrice,
                    discountRate
                );
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.OK(notApplicable);
            }

            var warehousePurchasePrice = NormalizeSourcePrice(warehouseProduct?.ImportPrice);
            var warehouseRetailPrice = NormalizeSourcePrice(warehouseProduct?.OEMPrice);
            if (
                warehouseProduct == null
                || (!warehousePurchasePrice.HasValue && !warehouseRetailPrice.HasValue)
            )
            {
                var missingSource = await BuildWarehousePriceSyncResultAsync(
                    entity,
                    supplierCode,
                    "missing_source",
                    false,
                    false,
                    false,
                    warehousePurchasePrice,
                    warehouseRetailPrice,
                    previousPurchasePrice,
                    previousRetailPrice,
                    discountRate
                );
                return ApiResponse<SyncStoreProductWarehousePriceResultDto>.OK(missingSource);
            }

            var purchaseChanged = warehousePurchasePrice.HasValue
                && !PricesEqual(entity.PurchasePrice, warehousePurchasePrice);
            var retailChanged = warehouseRetailPrice.HasValue
                && !PricesEqual(entity.StoreRetailPriceValue, warehouseRetailPrice);

            // 确认请求必须基于用户看到的完整快照；任一来源或目标值变化都拒绝写入。
            if (
                request.ConfirmRetailPrice
                && !WarehousePriceSnapshotMatches(
                    request,
                    warehousePurchasePrice,
                    warehouseRetailPrice,
                    entity.PurchasePrice,
                    entity.StoreRetailPriceValue,
                    entity.DiscountRate
                )
            )
            {
                return await BuildWarehousePriceConflictAsync(
                    entity,
                    supplierCode,
                    warehousePurchasePrice,
                    warehouseRetailPrice
                );
            }

            var purchaseUpdated = purchaseChanged;
            var retailUpdated = request.ConfirmRetailPrice && retailChanged;
            if (purchaseUpdated || retailUpdated)
            {
                var previousIsAutoPricing = entity.IsAutoPricing;
                var previousIsSpecialProduct = entity.IsSpecialProduct;
                var previousIsActive = entity.IsActive;
                var previousUpdatedAt = entity.UpdatedAt;
                var previousSupplierCode = entity.SupplierCode;
                var previousStoreCode = entity.StoreCode;
                var previousProductCode = entity.ProductCode;

                if (purchaseUpdated)
                {
                    entity.PurchasePrice = warehousePurchasePrice;
                }

                if (retailUpdated)
                {
                    entity.StoreRetailPriceValue = warehouseRetailPrice;
                }

                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = updatedBy;
                // 仅写本入口负责的价格与更新审计列；WHERE 同时承担乐观并发兜底。
                var affectedRows = await _db.Updateable(entity)
                    .UpdateColumns(x => new
                    {
                        x.PurchasePrice,
                        x.StoreRetailPriceValue,
                        x.UpdatedAt,
                        x.UpdatedBy,
                    })
                    .Where(x =>
                        x.UUID == uuid
                        && !x.IsDeleted
                        && x.StoreCode == previousStoreCode
                        && x.ProductCode == previousProductCode
                        && x.SupplierCode == previousSupplierCode
                        && x.PurchasePrice == previousPurchasePrice
                        && x.StoreRetailPriceValue == previousRetailPrice
                        && x.DiscountRate == discountRate
                        && x.IsAutoPricing == previousIsAutoPricing
                        && x.IsSpecialProduct == previousIsSpecialProduct
                        && x.IsActive == previousIsActive
                        && x.UpdatedAt == previousUpdatedAt
                    )
                    .ExecuteCommandAsync();
                if (affectedRows == 0)
                {
                    var latestEntity = await WithWarehouseSyncUpdateLock(
                            _db.Queryable<StoreRetailPrice>()
                                .Where(x => x.UUID == uuid && !x.IsDeleted)
                        )
                        .FirstAsync();
                    if (latestEntity == null)
                    {
                        return ApiResponse<SyncStoreProductWarehousePriceResultDto>.Error(
                            "分店商品记录已变化",
                            "PRICE_VERSION_CONFLICT"
                        );
                    }

                    return await BuildWarehousePriceConflictAsync(
                        latestEntity,
                        supplierCode,
                        warehousePurchasePrice,
                        warehouseRetailPrice
                    );
                }

                await SyncCurrentStoreProjectedPriceRecordsAsync(entity, updatedBy);
            }

            var confirmationRequired = !request.ConfirmRetailPrice && retailChanged;
            var status = confirmationRequired ? "confirmation_required" : "synced";
            var result = await BuildWarehousePriceSyncResultAsync(
                entity,
                supplierCode,
                status,
                purchaseUpdated,
                retailUpdated,
                confirmationRequired,
                warehousePurchasePrice,
                warehouseRetailPrice,
                previousPurchasePrice,
                previousRetailPrice,
                discountRate
            );
            return ApiResponse<SyncStoreProductWarehousePriceResultDto>.OK(result);
        }

        private async Task<ApiResponse<SyncStoreProductWarehousePriceResultDto>> BuildWarehousePriceConflictAsync(
            StoreRetailPrice entity,
            string? supplierCode,
            decimal? warehousePurchasePrice,
            decimal? warehouseRetailPrice
        )
        {
            var retailChanged = warehouseRetailPrice.HasValue
                && !PricesEqual(entity.StoreRetailPriceValue, warehouseRetailPrice);
            var latest = await BuildWarehousePriceSyncResultAsync(
                entity,
                supplierCode,
                retailChanged ? "confirmation_required" : "synced",
                false,
                false,
                retailChanged,
                warehousePurchasePrice,
                warehouseRetailPrice,
                entity.PurchasePrice,
                entity.StoreRetailPriceValue,
                entity.DiscountRate
            );
            return new ApiResponse<SyncStoreProductWarehousePriceResultDto>
            {
                Success = false,
                ErrorCode = "PRICE_VERSION_CONFLICT",
                Message = "仓库或分店价格已变化，请按最新价格重新确认",
                Data = latest,
            };
        }

        private ISugarQueryable<T> WithWarehouseSyncUpdateLock<T>(ISugarQueryable<T> queryable)
        {
            return _db.CurrentConnectionConfig.DbType == DbType.SqlServer
                ? queryable.With(SqlWith.UpdLock)
                : queryable;
        }

        private async Task<SyncStoreProductWarehousePriceResultDto> BuildWarehousePriceSyncResultAsync(
            StoreRetailPrice entity,
            string? supplierCode,
            string status,
            bool purchaseUpdated,
            bool retailUpdated,
            bool retailConfirmationRequired,
            decimal? warehousePurchasePrice,
            decimal? warehouseRetailPrice,
            decimal? previousStorePurchasePrice,
            decimal? previousStoreRetailPrice,
            decimal? discountRate
        )
        {
            return new SyncStoreProductWarehousePriceResultDto
            {
                Status = status,
                PurchaseUpdated = purchaseUpdated,
                RetailUpdated = retailUpdated,
                RetailConfirmationRequired = retailConfirmationRequired,
                StorePrice = await BuildStorePriceDtoAsync(entity, supplierCode),
                WarehousePurchasePrice = warehousePurchasePrice,
                WarehouseRetailPrice = warehouseRetailPrice,
                PreviousStorePurchasePrice = previousStorePurchasePrice,
                PreviousStoreRetailPrice = previousStoreRetailPrice,
                DiscountRate = discountRate,
                PreviousDiscountedRetailPrice = CalculateDiscountedPrice(
                    previousStoreRetailPrice,
                    discountRate
                ),
                NewDiscountedRetailPrice = CalculateDiscountedPrice(
                    warehouseRetailPrice ?? previousStoreRetailPrice,
                    discountRate
                ),
            };
        }

        private static bool WarehousePriceSnapshotMatches(
            SyncStoreProductWarehousePriceRequestDto request,
            decimal? warehousePurchasePrice,
            decimal? warehouseRetailPrice,
            decimal? storePurchasePrice,
            decimal? storeRetailPrice,
            decimal? discountRate
        )
        {
            return PricesEqual(request.ExpectedWarehousePurchasePrice, warehousePurchasePrice)
                && PricesEqual(request.ExpectedWarehouseRetailPrice, warehouseRetailPrice)
                && PricesEqual(request.ExpectedStorePurchasePrice, storePurchasePrice)
                && PricesEqual(request.ExpectedStoreRetailPrice, storeRetailPrice)
                && DiscountRatesEqual(request.ExpectedDiscountRate, discountRate);
        }

        private static decimal? NormalizeSourcePrice(decimal? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var rounded = RoundPrice(value.Value);
            return rounded > 0 ? rounded : null;
        }

        private static bool PricesEqual(decimal? left, decimal? right)
        {
            return RoundNullablePrice(left) == RoundNullablePrice(right);
        }

        private static bool DiscountRatesEqual(decimal? left, decimal? right)
        {
            return RoundNullableDiscountRate(left) == RoundNullableDiscountRate(right);
        }

        private static decimal? RoundNullablePrice(decimal? value)
        {
            return value.HasValue ? RoundPrice(value.Value) : null;
        }

        private static decimal? RoundNullableDiscountRate(decimal? value)
        {
            return value.HasValue
                ? Math.Round(value.Value, 4, MidpointRounding.AwayFromZero)
                : null;
        }

        private static decimal RoundPrice(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal? CalculateDiscountedPrice(decimal? retailPrice, decimal? discountRate)
        {
            if (!retailPrice.HasValue)
            {
                return null;
            }

            // DiscountRate 表示减免比例（例如 0.2 为减 20%），不是支付比例。
            var discountReductionRate = discountRate ?? 0m;
            return RoundPrice(retailPrice.Value * (1m - discountReductionRate));
        }

        public async Task<ApiResponse<StoreProductCodePageDto<StoreProductSetCodeDto>>> GetSetCodesAsync(
            string productCode,
            string? storeCode,
            int page,
            int pageSize,
            string? keyword,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var pageResult = await QueryProductCodesPageAsync(
                    productCode,
                    storeCode,
                    null,
                    page,
                    pageSize,
                    keyword,
                    accessibleStoreCodes
                );
                if (pageResult.ErrorMessage != null)
                {
                    return ApiResponse<StoreProductCodePageDto<StoreProductSetCodeDto>>.Error(pageResult.ErrorMessage);
                }

                var projections = await QueryProjectedStoreMultiCodesAsync(
                    pageResult.SetCodes,
                    pageResult.StoreCode
                );
                var projectionMap = projections.ToDictionary(
                    p => ResolveSetProductCode(p.MultiCodeProductCode, p.UUID),
                    p => p
                );
                var items = pageResult.SetCodes
                    .Select(setCode =>
                    {
                        var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                        projectionMap.TryGetValue(setProductCode, out var projection);
                        return BuildSetCodeDto(setCode, projection);
                    })
                    .ToList();

                return ApiResponse<StoreProductCodePageDto<StoreProductSetCodeDto>>.OK(
                    BuildCodePage(items, pageResult.TotalCount, pageResult.Page, pageResult.PageSize)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取套装条码分页失败: {ProductCode}", productCode);
                return ApiResponse<StoreProductCodePageDto<StoreProductSetCodeDto>>.Error(
                    $"获取套装条码失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<StoreProductCodePageDto<StoreProductMultiCodeDto>>> GetMultiCodesAsync(
            string productCode,
            string? storeCode,
            int page,
            int pageSize,
            string? keyword,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var pageResult = await QueryMultiCodePageAsync(
                    productCode,
                    storeCode,
                    page,
                    pageSize,
                    keyword,
                    accessibleStoreCodes
                );
                if (pageResult.ErrorMessage != null)
                {
                    return ApiResponse<StoreProductCodePageDto<StoreProductMultiCodeDto>>.Error(pageResult.ErrorMessage);
                }

                var projections = await QueryProjectedStoreMultiCodesAsync(
                    pageResult.SetCodes,
                    pageResult.StoreCode
                );
                var projectionMap = projections.ToDictionary(
                    p => ResolveSetProductCode(p.MultiCodeProductCode, p.UUID),
                    p => p
                );
                var pricingContext = await CreatePricingContextAsync();
                var items = pageResult.SetCodes
                    .Select(setCode =>
                    {
                        var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                        projectionMap.TryGetValue(setProductCode, out var projection);
                        return BuildMultiCodeDto(
                            setCode,
                            projection,
                            pageResult.LocalSupplierCode,
                            pricingContext
                        );
                    })
                    .ToList();

                return ApiResponse<StoreProductCodePageDto<StoreProductMultiCodeDto>>.OK(
                    BuildCodePage(items, pageResult.TotalCount, pageResult.Page, pageResult.PageSize)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取多码条码分页失败: {ProductCode}", productCode);
                return ApiResponse<StoreProductCodePageDto<StoreProductMultiCodeDto>>.Error(
                    $"获取多码条码失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<EvaluateStoreProductAutoPricingResultDto>> EvaluateAutoPricingAsync(
            EvaluateStoreProductAutoPricingDto request,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var productCode = request.ProductCode?.Trim();
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.Error("商品编码不能为空");
                }

                var scopedStoreCodes = ResolveScopedStoreCodes(request.StoreCode, accessibleStoreCodes);
                if (scopedStoreCodes != null && scopedStoreCodes.Count == 0)
                {
                    return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.OK(
                        new EvaluateStoreProductAutoPricingResultDto
                        {
                            ProductCode = productCode,
                            StoreCode = request.StoreCode,
                        }
                    );
                }

                var entity = await QueryStorePriceAsync(productCode, scopedStoreCodes);
                if (entity == null)
                {
                    return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.OK(
                        new EvaluateStoreProductAutoPricingResultDto
                        {
                            ProductCode = productCode,
                            StoreCode = request.StoreCode,
                        }
                    );
                }

                var result = new EvaluateStoreProductAutoPricingResultDto
                {
                    ProductCode = productCode,
                    StoreCode = entity.StoreCode,
                    StorePriceUuid = entity.UUID,
                    CurrentRetailPrice = entity.StoreRetailPriceValue,
                    CurrentRetailPriceFormatted = FormatPrice(entity.StoreRetailPriceValue),
                    DiscountRate = entity.DiscountRate,
                    IsAutoPricing = entity.IsAutoPricing,
                    HasValidPurchasePrice = entity.PurchasePrice.HasValue && entity.PurchasePrice.Value > 0,
                    ShouldUpdate = false,
                };

                if (!request.ForceAutoPricing && !entity.IsAutoPricing)
                {
                    return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.OK(result);
                }

                if (!result.HasValidPurchasePrice)
                {
                    return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.OK(result);
                }

                var supplierCode = await ResolveStorePriceSupplierCodeAsync(entity);
                var strategy = await _autoPricingService.FindStrategyForPriceAsync(
                    entity.PurchasePrice!.Value,
                    supplierCode,
                    entity.StoreCode
                );
                var recalculatedRetailPrice = _autoPricingService.CalculateRetailPrice(
                    entity.PurchasePrice.Value,
                    strategy
                );

                result.RecalculatedRetailPrice = recalculatedRetailPrice;
                result.RecalculatedRetailPriceFormatted = FormatPrice(recalculatedRetailPrice);
                result.ShouldUpdate =
                    result.RecalculatedRetailPriceFormatted != result.CurrentRetailPriceFormatted;

                return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "评估自动价失败: {ProductCode}", request.ProductCode);
                return ApiResponse<EvaluateStoreProductAutoPricingResultDto>.Error(
                    $"评估自动价失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<StoreProductTypeUpdateResultDto>> UpdateProductTypeAsync(
            string productCode,
            UpdateStoreProductTypeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var normalizedProductCode = productCode?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedProductCode))
                {
                    return ApiResponse<StoreProductTypeUpdateResultDto>.Error("商品编码不能为空");
                }

                if (request.ProductType < 0 || request.ProductType > 2)
                {
                    return ApiResponse<StoreProductTypeUpdateResultDto>.Error("商品类型无效");
                }

                var scopedStoreCodes = ResolveScopedStoreCodes(request.StoreCode, accessibleStoreCodes);
                if (scopedStoreCodes != null && scopedStoreCodes.Count == 0)
                {
                    return ApiResponse<StoreProductTypeUpdateResultDto>.Error("当前账号或设备无权修改该分店商品");
                }

                var storePriceEntity = await QueryStorePriceAsync(normalizedProductCode, scopedStoreCodes);
                if (scopedStoreCodes != null && storePriceEntity == null)
                {
                    return ApiResponse<StoreProductTypeUpdateResultDto>.Error("当前账号或设备无权修改该分店商品");
                }

                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == normalizedProductCode && !p.IsDeleted)
                    .FirstAsync();

                if (product == null)
                {
                    return ApiResponse<StoreProductTypeUpdateResultDto>.Error("商品不存在");
                }

                var domesticProduct = await _db.Queryable<DomesticProduct>()
                    .Where(dp => dp.ProductCode == normalizedProductCode && !dp.IsDeleted)
                    .FirstAsync();

                var now = DateTime.UtcNow;

                product.ProductType = request.ProductType;
                product.UpdatedAt = now;
                await _db.Updateable(product)
                    .UpdateColumns(p => new { p.ProductType, p.UpdatedAt })
                    .ExecuteCommandAsync();

                if (domesticProduct != null)
                {
                    domesticProduct.ProductType = request.ProductType;
                    domesticProduct.UpdatedAt = now;
                    domesticProduct.UpdatedBy = updatedBy;
                    await _db.Updateable(domesticProduct)
                        .UpdateColumns(dp => new { dp.ProductType, dp.UpdatedAt, dp.UpdatedBy })
                        .ExecuteCommandAsync();
                }

                return ApiResponse<StoreProductTypeUpdateResultDto>.OK(
                    new StoreProductTypeUpdateResultDto
                    {
                        ProductCode = normalizedProductCode,
                        ProductType = request.ProductType,
                        ProductTypeLabel = NormalizeProductTypeLabel(request.ProductType.ToString()),
                    },
                    "保存成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品类型失败: {ProductCode}", productCode);
                return ApiResponse<StoreProductTypeUpdateResultDto>.Error($"更新商品类型失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductMultiCodeDto>> UpdateMultiCodeAsync(
            string uuid,
            UpdateStoreProductMultiCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var entity = await _db.Queryable<StoreMultiCodeProduct>()
                    .Where(x => x.UUID == uuid && !x.IsDeleted)
                    .FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<StoreProductMultiCodeDto>.Error("多码商品记录不存在");
                }

                if (!CanAccessStore(entity.StoreCode, accessibleStoreCodes))
                {
                    return ApiResponse<StoreProductMultiCodeDto>.Error("当前账号或设备无权修改该分店多码");
                }

                if (request.PurchasePrice.HasValue && request.PurchasePrice.Value < 0)
                {
                    return ApiResponse<StoreProductMultiCodeDto>.Error("进货价不能为负数");
                }

                if (request.RetailPrice.HasValue && request.RetailPrice.Value < 0)
                {
                    return ApiResponse<StoreProductMultiCodeDto>.Error("零售价不能为负数");
                }

                entity.PurchasePrice = request.PurchasePrice;
                entity.MultiCodeRetailPrice = request.RetailPrice;
                if (request.IsAutoPricing.HasValue)
                {
                    entity.IsAutoPricing = request.IsAutoPricing.Value;
                }

                if (request.IsSpecialProduct.HasValue)
                {
                    entity.IsSpecialProduct = request.IsSpecialProduct.Value;
                }

                if (request.IsActive.HasValue)
                {
                    entity.IsActive = request.IsActive.Value;
                }

                var supplierCode = await _db.Queryable<StoreRetailPrice>()
                    .Where(x =>
                        x.ProductCode == entity.ProductCode
                        && x.StoreCode == entity.StoreCode
                        && !x.IsDeleted
                    )
                    .Select(x => x.SupplierCode)
                    .FirstAsync();
                if (string.IsNullOrWhiteSpace(supplierCode))
                {
                    supplierCode = await _db.Queryable<Product>()
                        .Where(p => p.ProductCode == entity.ProductCode)
                        .Select(p => p.LocalSupplierCode)
                        .FirstAsync();
                }

                if (entity.IsAutoPricing && entity.PurchasePrice.HasValue)
                {
                    var strategy = await _autoPricingService.FindStrategyForPriceAsync(
                        entity.PurchasePrice.Value,
                        supplierCode,
                        entity.StoreCode
                    );
                    entity.MultiCodeRetailPrice = _autoPricingService.CalculateRetailPrice(
                        entity.PurchasePrice.Value,
                        strategy
                    );
                }

                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = updatedBy;
                await _db.Updateable(entity).ExecuteCommandAsync();

                var dto = await BuildLegacyMultiCodeDtoAsync(entity, supplierCode);
                return ApiResponse<StoreProductMultiCodeDto>.OK(dto, "保存成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店多码失败: {Uuid}", uuid);
                return ApiResponse<StoreProductMultiCodeDto>.Error($"更新分店多码失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductSetCodeDto>> CreateSetCodeAsync(
            CreateStoreProductSetCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                if (!CanAccessStore(request.StoreCode, accessibleStoreCodes))
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("当前账号或设备无权修改该分店商品");
                }

                if (request.ProductType is < 1 or > 2)
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("商品类型无效");
                }

                if (string.IsNullOrWhiteSpace(request.ProductCode) || string.IsNullOrWhiteSpace(request.Barcode))
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("商品编码和条码不能为空");
                }

                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == request.ProductCode && !p.IsDeleted)
                    .FirstAsync();
                if (product == null)
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("商品不存在");
                }

                if (request.ProductType == 1 && (!request.RetailPrice.HasValue || request.RetailPrice.Value < 0))
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("套装零售价不能为空");
                }

                var existingSetNumbers = await _db.Queryable<ProductSetCode>()
                    .Where(x => x.ProductCode == request.ProductCode && !x.IsDeleted)
                    .Select(x => x.SetItemNumber)
                    .ToListAsync();

                var mainStorePrice = await QueryStorePriceByStoreAsync(request.ProductCode, request.StoreCode);
                var normalizedRetailPrice = request.ProductType == 2
                    ? mainStorePrice?.StoreRetailPriceValue
                    : request.RetailPrice;

                var setCode = new ProductSetCode
                {
                    SetCodeId = UuidHelper.GenerateUuid7(),
                    ProductCode = request.ProductCode.Trim(),
                    SetProductCode = UuidHelper.GenerateUuid7(),
                    SetItemNumber = ItemNumberHelper.GenerateSetItemNumber(
                        product.ItemNumber ?? request.ProductCode.Trim(),
                        existingSetNumbers
                    ),
                    SetBarcode = request.Barcode.Trim(),
                    SetPurchasePrice = request.ProductType == 1
                        ? StoreProductMaintenanceSyncHelper.CalculateSetPurchasePrice(
                            mainStorePrice?.PurchasePrice,
                            mainStorePrice?.StoreRetailPriceValue,
                            normalizedRetailPrice
                        )
                        : mainStorePrice?.PurchasePrice,
                    SetRetailPrice = normalizedRetailPrice,
                    SetQuantity = 1,
                    SetType = request.ProductType,
                    IsActive = request.IsActive ?? true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = updatedBy,
                    UpdatedBy = updatedBy,
                    IsDeleted = false,
                };

                await _db.Insertable(setCode).ExecuteCommandAsync();
                await SyncSetCodeAcrossStoresAsync(setCode, updatedBy);

                var refreshed = await QueryProjectedSetCodeAsync(setCode.SetCodeId, request.StoreCode, product.LocalSupplierCode);
                return ApiResponse<StoreProductSetCodeDto>.OK(refreshed, "保存成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新增条码失败: {ProductCode}", request.ProductCode);
                return ApiResponse<StoreProductSetCodeDto>.Error($"新增条码失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductSetCodeDto>> UpdateSetCodeAsync(
            string setCodeId,
            UpdateStoreProductSetCodeDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                if (!CanAccessStore(request.StoreCode, accessibleStoreCodes))
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("当前账号或设备无权修改该分店商品");
                }

                var setCode = await _db.Queryable<ProductSetCode>()
                    .Where(x => x.SetCodeId == setCodeId && !x.IsDeleted)
                    .FirstAsync();
                if (setCode == null)
                {
                    return ApiResponse<StoreProductSetCodeDto>.Error("条码记录不存在");
                }

                var mainStorePrice = await QueryStorePriceByStoreAsync(setCode.ProductCode, request.StoreCode);
                setCode.SetBarcode = request.Barcode?.Trim();
                setCode.SetRetailPrice = setCode.SetType == 2
                    ? mainStorePrice?.StoreRetailPriceValue
                    : request.RetailPrice;
                setCode.SetPurchasePrice = setCode.SetType == 1
                    ? StoreProductMaintenanceSyncHelper.CalculateSetPurchasePrice(
                        mainStorePrice?.PurchasePrice,
                        mainStorePrice?.StoreRetailPriceValue,
                        setCode.SetRetailPrice
                    )
                    : mainStorePrice?.PurchasePrice;
                if (request.IsActive.HasValue)
                {
                    setCode.IsActive = request.IsActive.Value;
                }

                setCode.UpdatedAt = DateTime.UtcNow;
                setCode.UpdatedBy = updatedBy;
                await _db.Updateable(setCode).ExecuteCommandAsync();
                await SyncSetCodeAcrossStoresAsync(setCode, updatedBy);

                var supplierCode = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == setCode.ProductCode && !p.IsDeleted)
                    .Select(p => p.LocalSupplierCode)
                    .FirstAsync();
                var refreshed = await QueryProjectedSetCodeAsync(setCode.SetCodeId, request.StoreCode, supplierCode);
                return ApiResponse<StoreProductSetCodeDto>.OK(refreshed, "保存成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新条码失败: {SetCodeId}", setCodeId);
                return ApiResponse<StoreProductSetCodeDto>.Error($"更新条码失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> DeleteSetCodeAsync(
            string setCodeId,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var setCode = await _db.Queryable<ProductSetCode>()
                    .Where(x => x.SetCodeId == setCodeId && !x.IsDeleted)
                    .FirstAsync();
                if (setCode == null)
                {
                    return ApiResponse<bool>.Error("条码记录不存在");
                }

                if (accessibleStoreCodes != null && accessibleStoreCodes.Count == 0)
                {
                    return ApiResponse<bool>.Error("当前账号或设备无权修改该分店商品");
                }

                setCode.IsDeleted = true;
                setCode.IsActive = false;
                setCode.UpdatedAt = DateTime.UtcNow;
                setCode.UpdatedBy = updatedBy;
                await _db.Updateable(setCode).ExecuteCommandAsync();

                var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                await _db.Deleteable<StoreMultiCodeProduct>()
                    .Where(x => x.MultiCodeProductCode == setProductCode)
                    .ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "删除成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除条码失败: {SetCodeId}", setCodeId);
                return ApiResponse<bool>.Error($"删除条码失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductClearancePriceDto>> UpsertClearancePriceAsync(
            string productCode,
            UpsertStoreProductClearancePriceDto request,
            string updatedBy,
            List<string>? accessibleStoreCodes
        )
        {
            var operation = "update";
            try
            {
                if (!CanAccessStore(request.StoreCode, accessibleStoreCodes))
                {
                    return ApiResponse<StoreProductClearancePriceDto>.Error("当前账号或设备无权修改该分店商品");
                }

                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .FirstAsync();
                if (product == null)
                {
                    return ApiResponse<StoreProductClearancePriceDto>.Error("商品不存在");
                }

                var entity = await _db.Queryable<StoreClearancePrice>()
                    .Where(x =>
                        x.ProductCode == productCode
                        && x.StoreCode == request.StoreCode
                        && !x.IsDeleted
                    )
                    .FirstAsync();

                if (entity == null)
                {
                    operation = "create";
                    entity = new StoreClearancePrice
                    {
                        UUID = UuidHelper.GenerateUuid7(),
                        StoreCode = request.StoreCode,
                        ProductCode = productCode,
                        ClearanceBarcode = await GenerateClearanceBarcodeAsync(request.StoreCode),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy,
                        IsDeleted = false,
                    };
                    entity.ClearancePrice = request.ClearancePrice;
                    await _db.Insertable(entity).ExecuteCommandAsync();
                }
                else
                {
                    entity.ClearancePrice = request.ClearancePrice;
                    entity.UpdatedAt = DateTime.UtcNow;
                    entity.UpdatedBy = updatedBy;
                    await _db.Updateable(entity).ExecuteCommandAsync();
                }

                _logger.LogInformation(
                    "StoreProductMaintenance clearance price saved productCode={ProductCode} storeCode={StoreCode} operation={Operation} clearanceBarcode={ClearanceBarcode} targetTable=StoreClearancePrice noMultiCodeProjection=true",
                    productCode,
                    request.StoreCode,
                    operation,
                    entity.ClearanceBarcode
                );

                return ApiResponse<StoreProductClearancePriceDto>.OK(
                    await BuildClearancePriceDtoAsync(entity),
                    "保存成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "保存清货价失败: productCode={ProductCode} storeCode={StoreCode} operation={Operation}",
                    productCode,
                    request.StoreCode,
                    operation
                );
                return ApiResponse<StoreProductClearancePriceDto>.Error($"保存清货价失败: {ex.Message}");
            }
        }

        private async Task<StoreRetailPrice?> QueryStorePriceAsync(
            string productCode,
            List<string>? accessibleStoreCodes
        )
        {
            var query = _db.Queryable<StoreRetailPrice>()
                .Where(x => x.ProductCode == productCode && !x.IsDeleted);
            if (accessibleStoreCodes != null)
            {
                query = query.Where(x => x.StoreCode != null && accessibleStoreCodes.Contains(x.StoreCode));
            }

            return await query.OrderBy(x => x.StoreCode).FirstAsync();
        }

        private async Task<ProductCodePageQueryResult> QueryProductCodesPageAsync(
            string productCode,
            string? storeCode,
            int? setType,
            int page,
            int pageSize,
            string? keyword,
            List<string>? accessibleStoreCodes
        )
        {
            var normalizedProductCode = productCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedProductCode))
            {
                return ProductCodePageQueryResult.Fail("商品编码不能为空");
            }

            var selectedStoreCodes = ResolveScopedStoreCodes(storeCode, accessibleStoreCodes);
            if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
            {
                return ProductCodePageQueryResult.Fail("当前账号或设备无权访问该分店");
            }

            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == normalizedProductCode && !p.IsDeleted)
                .Select(p => new { p.ProductCode, p.LocalSupplierCode })
                .FirstAsync();
            if (product == null)
            {
                return ProductCodePageQueryResult.Fail("商品不存在");
            }

            var storePriceEntity = await QueryStorePriceAsync(normalizedProductCode, selectedStoreCodes);
            var currentStoreCode = ResolveCurrentStoreCode(storeCode, selectedStoreCodes, storePriceEntity);
            var pageIndex = Math.Max(1, page);
            var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
            RefAsync<int> totalCount = 0;
            var setCodes = await BuildProductSetCodeQuery(normalizedProductCode, setType, keyword)
                .OrderBy(s => s.SetBarcode)
                .Select(s => new ProductSetCode
                {
                    SetCodeId = s.SetCodeId,
                    ProductCode = s.ProductCode,
                    SetProductCode = s.SetProductCode,
                    SetItemNumber = s.SetItemNumber,
                    SetBarcode = s.SetBarcode,
                    SetPurchasePrice = s.SetPurchasePrice,
                    SetRetailPrice = s.SetRetailPrice,
                    SetQuantity = s.SetQuantity,
                    SetType = s.SetType,
                    IsActive = s.IsActive,
                })
                .ToPageListAsync(pageIndex, normalizedPageSize, totalCount);

            return new ProductCodePageQueryResult
            {
                SetCodes = setCodes,
                TotalCount = totalCount,
                Page = pageIndex,
                PageSize = normalizedPageSize,
                StoreCode = currentStoreCode,
                LocalSupplierCode = product.LocalSupplierCode,
            };
        }

        private async Task<ProductCodePageQueryResult> QueryMultiCodePageAsync(
            string productCode,
            string? storeCode,
            int page,
            int pageSize,
            string? keyword,
            List<string>? accessibleStoreCodes
        )
        {
            var normalizedProductCode = productCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedProductCode))
            {
                return ProductCodePageQueryResult.Fail("商品编码不能为空");
            }

            var selectedStoreCodes = ResolveScopedStoreCodes(storeCode, accessibleStoreCodes);
            if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
            {
                return ProductCodePageQueryResult.Fail("当前账号或设备无权访问该分店");
            }

            var product = await _db.Queryable<Product>()
                .Where(p => p.ProductCode == normalizedProductCode && !p.IsDeleted)
                .Select(p => new { p.ProductCode, p.LocalSupplierCode })
                .FirstAsync();
            if (product == null)
            {
                return ProductCodePageQueryResult.Fail("商品不存在");
            }

            var storePriceEntity = await QueryStorePriceAsync(normalizedProductCode, selectedStoreCodes);
            var currentStoreCode = ResolveCurrentStoreCode(storeCode, selectedStoreCodes, storePriceEntity);
            if (string.IsNullOrWhiteSpace(currentStoreCode))
            {
                return ProductCodePageQueryResult.Fail("当前商品没有可用的分店上下文");
            }

            var pageIndex = Math.Max(1, page);
            var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
            RefAsync<int> totalCount = 0;
            var setCodes = await BuildProductSetCodeQuery(normalizedProductCode, null, keyword)
                .OrderBy(s => s.SetBarcode)
                .Select(s => new ProductSetCode
                {
                    SetCodeId = s.SetCodeId,
                    ProductCode = s.ProductCode,
                    SetProductCode = s.SetProductCode,
                    SetItemNumber = s.SetItemNumber,
                    SetBarcode = s.SetBarcode,
                    SetPurchasePrice = s.SetPurchasePrice,
                    SetRetailPrice = s.SetRetailPrice,
                    SetQuantity = s.SetQuantity,
                    SetType = s.SetType,
                    IsActive = s.IsActive,
                })
                .ToPageListAsync(pageIndex, normalizedPageSize, totalCount);

            return new ProductCodePageQueryResult
            {
                SetCodes = setCodes,
                TotalCount = totalCount,
                Page = pageIndex,
                PageSize = normalizedPageSize,
                StoreCode = currentStoreCode,
                LocalSupplierCode = product.LocalSupplierCode,
            };
        }

        private ISugarQueryable<ProductSetCode> BuildProductSetCodeQuery(
            string productCode,
            int? setType,
            string? keyword
        )
        {
            var query = _db.Queryable<ProductSetCode>()
                .Where(s => s.ProductCode == productCode && !s.IsDeleted);
            if (setType.HasValue)
            {
                query = query.Where(s => s.SetType == setType.Value);
            }

            var normalizedKeyword = keyword?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                query = query.Where(s =>
                    (s.SetBarcode != null && s.SetBarcode.Contains(normalizedKeyword))
                    || (s.SetItemNumber != null && s.SetItemNumber.Contains(normalizedKeyword))
                );
            }

            return query;
        }

        private async Task<List<ProductSetCode>> QueryProductSetCodesAsync(
            string productCode,
            int? setType,
            string? keyword
        )
        {
            return await BuildProductSetCodeQuery(productCode, setType, keyword)
                .OrderBy(s => s.SetBarcode)
                .Select(s => new ProductSetCode
                {
                    SetCodeId = s.SetCodeId,
                    ProductCode = s.ProductCode,
                    SetProductCode = s.SetProductCode,
                    SetItemNumber = s.SetItemNumber,
                    SetBarcode = s.SetBarcode,
                    SetPurchasePrice = s.SetPurchasePrice,
                    SetRetailPrice = s.SetRetailPrice,
                    SetQuantity = s.SetQuantity,
                    SetType = s.SetType,
                    IsActive = s.IsActive,
                })
                .ToListAsync();
        }

        private async Task<ProductSetCodeCounts> QueryProductSetCodeCountsAsync(string productCode)
        {
            var rows = await _db.Ado.SqlQueryAsync<ProductSetCodeCounts>(
                """
                SELECT ISNULL(COUNT(1), 0) AS TotalSetCodeCount
                FROM [ProductSetCode]
                WHERE ProductCode = @ProductCode AND IsDeleted = 0
                """,
                new SugarParameter("@ProductCode", productCode)
            );

            return rows.FirstOrDefault() ?? new ProductSetCodeCounts();
        }

        private async Task<FastDetailBaseRow?> QueryFastDetailBaseAsync(
            string productCode,
            string? targetStoreCode
        )
        {
            var rows = await _db.Ado.SqlQueryAsync<FastDetailBaseRow>(
                """
                SELECT TOP 1
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
                    sp.StoreCode AS StorePriceStoreCode,
                    storePriceStore.StoreName AS StoreName,
                    sp.ProductCode AS StorePriceProductCode,
                    sp.StoreProductCode AS StoreProductCode,
                    sp.SupplierCode AS SupplierCode,
                    sp.PurchasePrice AS PurchasePrice,
                    sp.StoreRetailPriceValue AS StoreRetailPriceValue,
                    sp.DiscountRate AS DiscountRate,
                    sp.IsAutoPricing AS IsAutoPricing,
                    sp.IsSpecialProduct AS IsSpecialProduct,
                    sp.IsActive AS StorePriceIsActive,
                    cp.UUID AS ClearanceUuid,
                    cp.StoreCode AS ClearanceStoreCode,
                    clearanceStore.StoreName AS ClearanceStoreName,
                    cp.ProductCode AS ClearanceProductCode,
                    cp.ClearanceBarcode AS ClearanceBarcode,
                    cp.ClearancePrice AS ClearancePrice,
                    ISNULL(codeCounts.TotalSetCodeCount, 0) AS TotalSetCodeCount
                FROM [Product] p
                LEFT JOIN [ProductGrade] pg
                    ON pg.ProductCode = p.ProductCode AND ISNULL(pg.IsDeleted, 0) = 0
                LEFT JOIN [LocalSupplier] ls
                    ON ls.LocalSupplierCode = p.LocalSupplierCode AND ISNULL(ls.IsDeleted, 0) = 0
                OUTER APPLY (
                    SELECT COUNT(1) AS TotalSetCodeCount
                    FROM [ProductSetCode] psc
                    WHERE psc.ProductCode = p.ProductCode
                        AND ISNULL(psc.IsDeleted, 0) = 0
                ) codeCounts
                OUTER APPLY (
                    SELECT TOP 1 srp.*
                    FROM [StoreRetailPrice] srp
                    WHERE srp.ProductCode = p.ProductCode
                        AND ISNULL(srp.IsDeleted, 0) = 0
                        AND (@StoreCode IS NULL OR srp.StoreCode = @StoreCode)
                    ORDER BY srp.StoreCode
                ) sp
                LEFT JOIN [Store] storePriceStore
                    ON storePriceStore.StoreCode = sp.StoreCode
                    AND ISNULL(storePriceStore.IsDeleted, 0) = 0
                OUTER APPLY (
                    SELECT TOP 1 scp.*
                    FROM [StoreClearancePrice] scp
                    WHERE scp.ProductCode = p.ProductCode
                        AND ISNULL(scp.IsDeleted, 0) = 0
                        AND (@StoreCode IS NULL OR scp.StoreCode = @StoreCode)
                    ORDER BY scp.StoreCode
                ) cp
                LEFT JOIN [Store] clearanceStore
                    ON clearanceStore.StoreCode = cp.StoreCode
                    AND ISNULL(clearanceStore.IsDeleted, 0) = 0
                WHERE p.ProductCode = @ProductCode AND ISNULL(p.IsDeleted, 0) = 0
                """,
                new SugarParameter("@ProductCode", productCode),
                new SugarParameter("@StoreCode", (object?)targetStoreCode ?? DBNull.Value)
            );

            return rows.FirstOrDefault();
        }

        private static StoreProductCodePageDto<T> BuildCodePage<T>(
            List<T> items,
            int totalCount,
            int page,
            int pageSize
        )
        {
            return new StoreProductCodePageDto<T>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = page * pageSize < totalCount,
            };
        }

        private async Task<List<LookupHit>> QueryLookupHitsAsync(
            string keyword,
            List<string>? selectedStoreCodes
        )
        {
            var parameters = new List<SugarParameter>
            {
                new("@Keyword", keyword),
            };
            var storeFilterSql = string.Empty;
            if (selectedStoreCodes != null)
            {
                var storeParameterNames = new List<string>();
                for (var i = 0; i < selectedStoreCodes.Count; i++)
                {
                    var parameterName = "@StoreCode" + i;
                    storeParameterNames.Add(parameterName);
                    parameters.Add(new SugarParameter(parameterName, selectedStoreCodes[i]));
                }

                storeFilterSql =
                    storeParameterNames.Count == 0
                        ? " AND 1 = 0"
                        : $" AND c.StoreCode IN ({string.Join(", ", storeParameterNames)})";
            }

            var sql = new StringBuilder();
            sql.AppendLine(
                """
                SELECT
                    p.ProductCode AS ProductCode,
                    p.ItemNumber AS ItemNumber,
                    p.Barcode AS Barcode,
                    'ProductBarcode' AS MatchSource,
                    @Keyword AS MatchValue
                FROM [Product] p
                WHERE p.IsDeleted = 0 AND p.Barcode = @Keyword

                UNION ALL

                SELECT
                    p.ProductCode AS ProductCode,
                    p.ItemNumber AS ItemNumber,
                    p.Barcode AS Barcode,
                    'ItemNumber' AS MatchSource,
                    @Keyword AS MatchValue
                FROM [Product] p
                WHERE p.IsDeleted = 0 AND p.ItemNumber = @Keyword

                UNION ALL

                SELECT
                    s.ProductCode AS ProductCode,
                    s.SetItemNumber AS ItemNumber,
                    s.SetBarcode AS Barcode,
                    'SetBarcode' AS MatchSource,
                    @Keyword AS MatchValue
                FROM [ProductSetCode] s
                WHERE s.IsDeleted = 0 AND s.SetBarcode = @Keyword

                UNION ALL

                SELECT
                    c.ProductCode AS ProductCode,
                    CAST(NULL AS nvarchar(200)) AS ItemNumber,
                    c.ClearanceBarcode AS Barcode,
                    'ClearanceBarcode' AS MatchSource,
                    @Keyword AS MatchValue
                FROM [StoreClearancePrice] c
                WHERE c.IsDeleted = 0 AND c.ClearanceBarcode = @Keyword
                """
            );
            sql.AppendLine(storeFilterSql);

            return await _db.Ado.SqlQueryAsync<LookupHit>(sql.ToString(), parameters.ToArray());
        }

        private async Task<Dictionary<string, LookupProductSnapshot>> QueryLookupProductSnapshotsAsync(
            List<string> productCodes
        )
        {
            if (productCodes.Count == 0)
            {
                return new Dictionary<string, LookupProductSnapshot>(StringComparer.Ordinal);
            }

            var snapshots = await _db.Queryable<Product>()
                .LeftJoin<ProductGrade>((p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                .Where((p, pg) =>
                    !p.IsDeleted
                    && p.ProductCode != null
                    && productCodes.Contains(p.ProductCode)
                )
                .Select((p, pg) => new LookupProductSnapshot
                {
                    ProductCode = p.ProductCode ?? string.Empty,
                    ProductName = p.ProductName,
                    ItemNumber = p.ItemNumber,
                    Barcode = p.Barcode,
                    ProductImage = p.ProductImage,
                    Grade = pg.Grade,
                    ProductType = p.ProductType,
                })
                .ToListAsync();

            return snapshots
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        private async Task<StoreRetailPrice?> QueryStorePriceByStoreAsync(
            string productCode,
            string? storeCode
        )
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                return null;
            }

            return await _db.Queryable<StoreRetailPrice>()
                .Where(x =>
                    x.ProductCode == productCode
                    && x.StoreCode == storeCode
                    && !x.IsDeleted
                )
                .FirstAsync();
        }

        private async Task<string?> ResolveStorePriceSupplierCodeAsync(StoreRetailPrice entity)
        {
            if (!string.IsNullOrWhiteSpace(entity.SupplierCode))
            {
                return entity.SupplierCode;
            }

            return await _db.Queryable<Product>()
                .Where(p => p.ProductCode == entity.ProductCode)
                .Select(p => p.LocalSupplierCode)
                .FirstAsync();
        }

        private async Task<StoreClearancePrice?> QueryClearancePriceAsync(
            string productCode,
            List<string>? accessibleStoreCodes
        )
        {
            var query = _db.Queryable<StoreClearancePrice>()
                .Where(x => x.ProductCode == productCode && !x.IsDeleted);
            if (accessibleStoreCodes != null)
            {
                query = query.Where(x => x.StoreCode != null && accessibleStoreCodes.Contains(x.StoreCode));
            }

            return await query.OrderBy(x => x.StoreCode).FirstAsync();
        }

        private async Task<Dictionary<string, string?>> QueryStoreNamesAsync(params string?[] storeCodes)
        {
            var normalizedCodes = storeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (normalizedCodes.Count == 0)
            {
                return new Dictionary<string, string?>(StringComparer.Ordinal);
            }

            var rows = await _db.Queryable<Store>()
                .Where(s => !s.IsDeleted && s.StoreCode != null && normalizedCodes.Contains(s.StoreCode))
                .Select(s => new { s.StoreCode, s.StoreName })
                .ToListAsync();

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode))
                .GroupBy(x => x.StoreCode!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (string?)group.First().StoreName,
                    StringComparer.Ordinal
                );
        }

        private async Task<StoreProductStorePriceDto> BuildStorePriceDtoAsync(
            StoreRetailPrice entity,
            string? fallbackSupplierCode
        )
        {
            var storeName = await _db.Queryable<Store>()
                .Where(s => s.StoreCode == entity.StoreCode && !s.IsDeleted)
                .Select(s => s.StoreName)
                .FirstAsync();

            var dto = new StoreProductStorePriceDto
            {
                Uuid = entity.UUID,
                StoreCode = entity.StoreCode,
                StoreName = storeName,
                ProductCode = entity.ProductCode,
                StoreProductCode = entity.StoreProductCode,
                SupplierCode = entity.SupplierCode ?? fallbackSupplierCode,
                PurchasePrice = entity.PurchasePrice,
                RetailPrice = entity.StoreRetailPriceValue,
                DiscountRate = entity.DiscountRate,
                IsAutoPricing = entity.IsAutoPricing,
                IsSpecialProduct = entity.IsSpecialProduct,
                IsActive = entity.IsActive,
            };

            await FillPricingFieldsAsync(
                dto.PurchasePrice,
                dto.SupplierCode,
                entity.StoreCode,
                assign: pricing =>
                {
                    dto.Rate = pricing.Rate;
                    dto.StrategySourceLabel = pricing.StrategySourceLabel;
                    dto.StrategyRuleLabel = pricing.StrategyRuleLabel;
                }
            );

            return dto;
        }

        private StoreProductStorePriceDto BuildStorePriceDto(
            StoreRetailPrice entity,
            string? fallbackSupplierCode,
            string? storeName,
            PricingContext? pricingContext
        )
        {
            var dto = new StoreProductStorePriceDto
            {
                Uuid = entity.UUID,
                StoreCode = entity.StoreCode,
                StoreName = storeName,
                ProductCode = entity.ProductCode,
                StoreProductCode = entity.StoreProductCode,
                SupplierCode = entity.SupplierCode ?? fallbackSupplierCode,
                PurchasePrice = entity.PurchasePrice,
                RetailPrice = entity.StoreRetailPriceValue,
                DiscountRate = entity.DiscountRate,
                IsAutoPricing = entity.IsAutoPricing,
                IsSpecialProduct = entity.IsSpecialProduct,
                IsActive = entity.IsActive,
            };

            if (pricingContext != null)
            {
                FillPricingFields(
                    pricingContext,
                    dto.PurchasePrice,
                    dto.SupplierCode,
                    entity.StoreCode,
                    assign: pricing =>
                    {
                        dto.Rate = pricing.Rate;
                        dto.StrategySourceLabel = pricing.StrategySourceLabel;
                        dto.StrategyRuleLabel = pricing.StrategyRuleLabel;
                    }
                );
            }

            return dto;
        }

        private static StoreRetailPrice? BuildStorePriceEntity(FastDetailBaseRow row)
        {
            if (string.IsNullOrWhiteSpace(row.StorePriceUuid))
            {
                return null;
            }

            return new StoreRetailPrice
            {
                UUID = row.StorePriceUuid,
                StoreCode = row.StorePriceStoreCode,
                ProductCode = row.StorePriceProductCode ?? row.ProductCode,
                StoreProductCode = row.StoreProductCode,
                SupplierCode = row.SupplierCode,
                PurchasePrice = row.PurchasePrice,
                StoreRetailPriceValue = row.StoreRetailPriceValue,
                DiscountRate = row.DiscountRate,
                IsAutoPricing = row.IsAutoPricing ?? false,
                IsSpecialProduct = row.IsSpecialProduct ?? false,
                IsActive = row.StorePriceIsActive ?? true,
            };
        }

        private StoreProductMultiCodeDto BuildMultiCodeDto(
            ProductSetCode setCode,
            StoreMultiCodeProduct? entity,
            string? fallbackSupplierCode,
            PricingContext pricingContext
        )
        {
            var dto = new StoreProductMultiCodeDto
            {
                Uuid = entity?.UUID ?? setCode.SetCodeId,
                SetCodeId = setCode.SetCodeId,
                StoreCode = entity?.StoreCode,
                ProductCode = setCode.ProductCode,
                MultiCodeProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId),
                StoreMultiCodeProductCode = entity?.StoreMultiCodeProductCode,
                Barcode = entity?.MultiBarcode ?? setCode.SetBarcode,
                PurchasePrice = entity?.PurchasePrice ?? setCode.SetPurchasePrice,
                RetailPrice = entity?.MultiCodeRetailPrice ?? setCode.SetRetailPrice,
                DiscountRate = entity?.DiscountRate,
                IsAutoPricing = entity?.IsAutoPricing ?? false,
                IsSpecialProduct = entity?.IsSpecialProduct ?? false,
                IsActive = entity?.IsActive ?? setCode.IsActive,
            };

            FillPricingFields(
                pricingContext,
                dto.PurchasePrice,
                fallbackSupplierCode,
                entity?.StoreCode,
                assign: pricing =>
                {
                    dto.Rate = pricing.Rate;
                    dto.StrategySourceLabel = pricing.StrategySourceLabel;
                    dto.StrategyRuleLabel = pricing.StrategyRuleLabel;
                }
            );

            return dto;
        }

        private async Task<StoreProductMultiCodeDto> BuildLegacyMultiCodeDtoAsync(
            StoreMultiCodeProduct entity,
            string? fallbackSupplierCode
        )
        {
            var dto = new StoreProductMultiCodeDto
            {
                Uuid = entity.UUID,
                SetCodeId = string.Empty,
                StoreCode = entity.StoreCode,
                ProductCode = entity.ProductCode,
                MultiCodeProductCode = entity.MultiCodeProductCode,
                StoreMultiCodeProductCode = entity.StoreMultiCodeProductCode,
                Barcode = entity.MultiBarcode,
                PurchasePrice = entity.PurchasePrice,
                RetailPrice = entity.MultiCodeRetailPrice,
                DiscountRate = entity.DiscountRate,
                IsAutoPricing = entity.IsAutoPricing,
                IsSpecialProduct = entity.IsSpecialProduct,
                IsActive = entity.IsActive,
            };

            await FillPricingFieldsAsync(
                dto.PurchasePrice,
                fallbackSupplierCode,
                entity.StoreCode,
                assign: pricing =>
                {
                    dto.Rate = pricing.Rate;
                    dto.StrategySourceLabel = pricing.StrategySourceLabel;
                    dto.StrategyRuleLabel = pricing.StrategyRuleLabel;
                }
            );

            return dto;
        }

        private StoreProductSetCodeDto BuildSetCodeDto(
            ProductSetCode setCode,
            StoreMultiCodeProduct? projection
        )
        {
            return new StoreProductSetCodeDto
            {
                SetCodeId = setCode.SetCodeId,
                ProductCode = setCode.ProductCode,
                SetProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId),
                SetItemNumber = setCode.SetItemNumber,
                SetBarcode = projection?.MultiBarcode ?? setCode.SetBarcode,
                SetPurchasePrice = projection?.PurchasePrice ?? setCode.SetPurchasePrice,
                SetRetailPrice = projection?.MultiCodeRetailPrice ?? setCode.SetRetailPrice,
                SetQuantity = setCode.SetQuantity,
                SetType = setCode.SetType,
                SetTypeDescription = ResolveSetTypeDescription(setCode.SetType),
                IsActive = projection?.IsActive ?? setCode.IsActive,
            };
        }

        private StoreProductClearancePriceDto BuildClearancePriceDto(
            StoreClearancePrice entity,
            string? storeName
        )
        {
            return new StoreProductClearancePriceDto
            {
                Uuid = entity.UUID,
                StoreCode = entity.StoreCode,
                StoreName = storeName,
                ProductCode = entity.ProductCode,
                ClearanceBarcode = entity.ClearanceBarcode,
                ClearancePrice = entity.ClearancePrice,
            };
        }

        private static StoreClearancePrice? BuildClearancePriceEntity(FastDetailBaseRow row)
        {
            if (string.IsNullOrWhiteSpace(row.ClearanceUuid))
            {
                return null;
            }

            return new StoreClearancePrice
            {
                UUID = row.ClearanceUuid,
                StoreCode = row.ClearanceStoreCode,
                ProductCode = row.ClearanceProductCode ?? row.ProductCode,
                ClearanceBarcode = row.ClearanceBarcode,
                ClearancePrice = row.ClearancePrice,
            };
        }

        private async Task<StoreProductClearancePriceDto> BuildClearancePriceDtoAsync(
            StoreClearancePrice entity
        )
        {
            var storeName = await _db.Queryable<Store>()
                .Where(s => s.StoreCode == entity.StoreCode && !s.IsDeleted)
                .Select(s => s.StoreName)
                .FirstAsync();

            return new StoreProductClearancePriceDto
            {
                Uuid = entity.UUID,
                StoreCode = entity.StoreCode,
                StoreName = storeName,
                ProductCode = entity.ProductCode,
                ClearanceBarcode = entity.ClearanceBarcode,
                ClearancePrice = entity.ClearancePrice,
            };
        }

        private async Task FillPricingFieldsAsync(
            decimal? purchasePrice,
            string? supplierCode,
            string? storeCode,
            Action<(decimal? Rate, string? StrategySourceLabel, string? StrategyRuleLabel)> assign
        )
        {
            if (!purchasePrice.HasValue || purchasePrice.Value <= 0)
            {
                assign((null, null, null));
                return;
            }

            var strategy = await _autoPricingService.FindStrategyForPriceAsync(
                purchasePrice.Value,
                supplierCode,
                storeCode
            );

            var rate = _autoPricingService.CalculateRate(purchasePrice.Value, strategy);
            var rule = strategy?.Details?.FirstOrDefault(d =>
                purchasePrice.Value >= d.MinPrice && purchasePrice.Value <= d.MaxPrice
            );

            assign(
                (
                    rate,
                    strategy?.Name ?? "自动定价策略",
                    rule == null ? null : $"{rule.MinPrice:0.##} - {rule.MaxPrice:0.##}"
                )
            );
        }

        private void FillPricingFields(
            PricingContext pricingContext,
            decimal? purchasePrice,
            string? supplierCode,
            string? storeCode,
            Action<(decimal? Rate, string? StrategySourceLabel, string? StrategyRuleLabel)> assign
        )
        {
            if (!purchasePrice.HasValue || purchasePrice.Value <= 0)
            {
                assign((null, null, null));
                return;
            }

            var strategy = FindStrategyForPrice(
                pricingContext,
                purchasePrice.Value,
                supplierCode,
                storeCode
            );
            var rate = _autoPricingService.CalculateRate(purchasePrice.Value, strategy);
            var rule = strategy?.Details?.FirstOrDefault(d =>
                purchasePrice.Value >= d.MinPrice && purchasePrice.Value <= d.MaxPrice
            );

            assign(
                (
                    rate,
                    strategy?.Name ?? "自动定价策略",
                    rule == null ? null : $"{rule.MinPrice:0.##} - {rule.MaxPrice:0.##}"
                )
            );
        }

        private async Task<PricingContext> CreatePricingContextAsync()
        {
            var strategies = await _cache.GetOrCreateAsync(
                PricingStrategiesCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = PricingStrategiesCacheDuration;
                    return await _autoPricingService.GetAllActiveStrategiesAsync();
                }
            );
            return new PricingContext(strategies ?? new List<PricingStrategy>());
        }

        private PricingStrategy? FindStrategyForPrice(
            PricingContext pricingContext,
            decimal purchasePrice,
            string? supplierCode,
            string? storeCode
        )
        {
            var supplierStrategies = string.IsNullOrWhiteSpace(supplierCode)
                ? pricingContext.Empty
                : pricingContext.Strategies
                    .Where(s =>
                        s.Targets?.Any(t =>
                            t.TargetType == "Supplier" && t.TargetCode == supplierCode
                        ) ?? false
                    )
                    .ToList();

            var storeStrategies = string.IsNullOrWhiteSpace(storeCode)
                ? pricingContext.Empty
                : pricingContext.Strategies
                    .Where(s =>
                        s.Targets?.Any(t => t.TargetType == "Store" && t.TargetCode == storeCode)
                        ?? false
                    )
                    .ToList();

            var globalStrategies = pricingContext.Strategies
                .Where(s => s.Level == "Global" || (s.Targets == null || s.Targets.Count == 0))
                .ToList();

            return _autoPricingService.FindBestStrategyForPrice(
                purchasePrice,
                supplierStrategies,
                storeStrategies,
                globalStrategies
            );
        }

        private async Task<List<StoreMultiCodeProduct>> QueryProjectedStoreMultiCodesAsync(
            List<ProductSetCode> setCodes,
            string? storeCode
        )
        {
            if (string.IsNullOrWhiteSpace(storeCode) || setCodes.Count == 0)
            {
                return new List<StoreMultiCodeProduct>();
            }

            var setProductCodes = setCodes
                .Select(s => ResolveSetProductCode(s.SetProductCode, s.SetCodeId))
                .Distinct()
                .ToList();
            var existing = await _db.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.StoreCode == storeCode
                    && x.MultiCodeProductCode != null
                    && setProductCodes.Contains(x.MultiCodeProductCode)
                    && !x.IsDeleted
                )
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
                })
                .ToListAsync();

            var existingMap = existing.ToDictionary(
                x => ResolveSetProductCode(x.MultiCodeProductCode, x.UUID),
                x => x
            );

            return existingMap.Values.OrderBy(x => x.MultiBarcode).ToList();
        }

        private async Task SyncCurrentStoreProjectedRecordsAsync(
            StoreRetailPrice mainStorePrice,
            string updatedBy
        )
        {
            if (string.IsNullOrWhiteSpace(mainStorePrice.StoreCode) || string.IsNullOrWhiteSpace(mainStorePrice.ProductCode))
            {
                return;
            }

            var setCodes = await _db.Queryable<ProductSetCode>()
                .Where(x => x.ProductCode == mainStorePrice.ProductCode && !x.IsDeleted)
                .ToListAsync();
            if (setCodes.Count == 0)
            {
                return;
            }

            var currentStoreRecords = await QueryProjectedStoreMultiCodesAsync(
                setCodes,
                mainStorePrice.StoreCode
            );
            var projectionMap = currentStoreRecords.ToDictionary(
                x => ResolveSetProductCode(x.MultiCodeProductCode, x.UUID),
                x => x
            );
            var inserts = new List<StoreMultiCodeProduct>();
            var updates = new List<StoreMultiCodeProduct>();
            foreach (var setCode in setCodes)
            {
                var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                if (!projectionMap.TryGetValue(setProductCode, out var existing))
                {
                    inserts.Add(
                        BuildProjectedStoreMultiCode(
                            setCode,
                            mainStorePrice,
                            mainStorePrice.StoreCode!,
                            updatedBy
                        )
                    );
                    continue;
                }

                ApplyProjectionValues(existing, setCode, mainStorePrice, updatedBy);
                updates.Add(existing);
            }

            if (inserts.Count > 0)
            {
                await _db.Insertable(inserts).ExecuteCommandAsync();
            }

            if (updates.Count > 0)
            {
                await _db.Updateable(updates).ExecuteCommandAsync();
            }
        }

        private async Task SyncCurrentStoreProjectedPriceRecordsAsync(
            StoreRetailPrice mainStorePrice,
            string updatedBy
        )
        {
            if (
                string.IsNullOrWhiteSpace(mainStorePrice.StoreCode)
                || string.IsNullOrWhiteSpace(mainStorePrice.ProductCode)
            )
            {
                return;
            }

            var setCodes = await WithWarehouseSyncUpdateLock(
                    _db.Queryable<ProductSetCode>()
                        .Where(x => x.ProductCode == mainStorePrice.ProductCode && !x.IsDeleted)
                )
                .ToListAsync();
            if (setCodes.Count == 0)
            {
                return;
            }

            var setProductCodes = setCodes
                .Select(x => ResolveSetProductCode(x.SetProductCode, x.SetCodeId))
                .Distinct()
                .ToList();
            var currentStoreRecords = await WithWarehouseSyncUpdateLock(
                    _db.Queryable<StoreMultiCodeProduct>()
                        .Where(x =>
                            x.StoreCode == mainStorePrice.StoreCode
                            && x.MultiCodeProductCode != null
                            && setProductCodes.Contains(x.MultiCodeProductCode)
                            && !x.IsDeleted
                        )
                )
                .ToListAsync();
            var projectionMap = currentStoreRecords.ToDictionary(
                x => ResolveSetProductCode(x.MultiCodeProductCode, x.UUID),
                x => x
            );
            var inserts = new List<StoreMultiCodeProduct>();
            var updates = new List<StoreMultiCodeProduct>();
            foreach (var setCode in setCodes)
            {
                var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                if (!projectionMap.TryGetValue(setProductCode, out var existing))
                {
                    inserts.Add(
                        BuildProjectedStoreMultiCode(
                            setCode,
                            mainStorePrice,
                            mainStorePrice.StoreCode!,
                            updatedBy
                        )
                    );
                    continue;
                }

                var nextRetailPrice = setCode.SetType == 2
                    ? mainStorePrice.StoreRetailPriceValue
                    : setCode.SetRetailPrice;
                var nextPurchasePrice = setCode.SetType == 2
                    ? mainStorePrice.PurchasePrice
                    : StoreProductMaintenanceSyncHelper.CalculateSetPurchasePrice(
                        mainStorePrice.PurchasePrice,
                        mainStorePrice.StoreRetailPriceValue,
                        setCode.SetRetailPrice
                    );
                if (
                    PricesEqual(existing.PurchasePrice, nextPurchasePrice)
                    && PricesEqual(existing.MultiCodeRetailPrice, nextRetailPrice)
                )
                {
                    continue;
                }

                // 既有派生记录只同步价格，折扣、自动价、特殊商品、启用状态和创建审计全部保留。
                existing.PurchasePrice = nextPurchasePrice;
                existing.MultiCodeRetailPrice = nextRetailPrice;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = updatedBy;
                updates.Add(existing);
            }

            if (inserts.Count > 0)
            {
                await _db.Insertable(inserts).ExecuteCommandAsync();
            }

            if (updates.Count > 0)
            {
                await _db.Updateable(updates)
                    .UpdateColumns(x => new
                    {
                        x.PurchasePrice,
                        x.MultiCodeRetailPrice,
                        x.UpdatedAt,
                        x.UpdatedBy,
                    })
                    .ExecuteCommandAsync();
            }
        }

        private async Task SyncSetCodeAcrossStoresAsync(ProductSetCode setCode, string updatedBy)
        {
            var activeStores = await _db.Queryable<Store>()
                .Where(s => s.IsActive && !s.IsDeleted && s.StoreCode != null)
                .Select(s => s.StoreCode!)
                .ToListAsync();
            if (activeStores.Count == 0)
            {
                return;
            }

            var mainStorePrices = await _db.Queryable<StoreRetailPrice>()
                .Where(x =>
                    x.ProductCode == setCode.ProductCode
                    && x.StoreCode != null
                    && activeStores.Contains(x.StoreCode)
                    && !x.IsDeleted
                )
                .ToListAsync();
            var mainPriceMap = mainStorePrices.ToDictionary(x => x.StoreCode!, x => x);

            var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
            var existing = await _db.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.MultiCodeProductCode == setProductCode
                    && x.StoreCode != null
                    && activeStores.Contains(x.StoreCode)
                    && !x.IsDeleted
                )
                .ToListAsync();
            var existingMap = existing.ToDictionary(x => x.StoreCode!, x => x);

            var inserts = new List<StoreMultiCodeProduct>();
            var updates = new List<StoreMultiCodeProduct>();
            foreach (var activeStoreCode in activeStores)
            {
                mainPriceMap.TryGetValue(activeStoreCode, out var mainPrice);
                if (existingMap.TryGetValue(activeStoreCode, out var current))
                {
                    ApplyProjectionValues(current, setCode, mainPrice, updatedBy);
                    updates.Add(current);
                    continue;
                }

                inserts.Add(BuildProjectedStoreMultiCode(setCode, mainPrice, activeStoreCode, updatedBy));
            }

            if (inserts.Count > 0)
            {
                await _db.Insertable(inserts).ExecuteCommandAsync();
            }

            if (updates.Count > 0)
            {
                await _db.Updateable(updates).ExecuteCommandAsync();
            }
        }

        private StoreMultiCodeProduct BuildProjectedStoreMultiCode(
            ProductSetCode setCode,
            StoreRetailPrice? mainStorePrice,
            string storeCode,
            string updatedBy
        )
        {
            var projection = new StoreMultiCodeProduct
            {
                UUID = UuidHelper.GenerateUuid7(),
                StoreCode = storeCode,
                ProductCode = setCode.ProductCode,
                MultiCodeProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId),
                StoreMultiCodeProductCode = storeCode + ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                IsDeleted = false,
            };
            ApplyProjectionValues(projection, setCode, mainStorePrice, updatedBy);
            return projection;
        }

        private void ApplyProjectionValues(
            StoreMultiCodeProduct projection,
            ProductSetCode setCode,
            StoreRetailPrice? mainStorePrice,
            string updatedBy
        )
        {
            projection.ProductCode = setCode.ProductCode;
            projection.MultiCodeProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
            projection.StoreMultiCodeProductCode = (projection.StoreCode ?? string.Empty) + projection.MultiCodeProductCode;
            projection.MultiBarcode = setCode.SetBarcode;
            projection.DiscountRate = null;
            projection.IsAutoPricing = false;
            projection.IsSpecialProduct = false;
            projection.IsActive = setCode.IsActive;

            if (setCode.SetType == 2)
            {
                projection.PurchasePrice = mainStorePrice?.PurchasePrice;
                projection.MultiCodeRetailPrice = mainStorePrice?.StoreRetailPriceValue;
            }
            else
            {
                projection.MultiCodeRetailPrice = setCode.SetRetailPrice;
                projection.PurchasePrice = StoreProductMaintenanceSyncHelper.CalculateSetPurchasePrice(
                    mainStorePrice?.PurchasePrice,
                    mainStorePrice?.StoreRetailPriceValue,
                    setCode.SetRetailPrice
                );
                if (
                    projection.PurchasePrice == null
                    && setCode.SetRetailPrice.HasValue
                    && (mainStorePrice?.StoreRetailPriceValue == null || mainStorePrice.StoreRetailPriceValue <= 0)
                )
                {
                    _logger.LogWarning(
                        "套装进货价无法自动推导: ProductCode={ProductCode}, StoreCode={StoreCode}, SetCodeId={SetCodeId}",
                        setCode.ProductCode,
                        projection.StoreCode,
                        setCode.SetCodeId
                    );
                }
            }

            projection.UpdatedAt = DateTime.UtcNow;
            projection.UpdatedBy = updatedBy;
        }

        private async Task<StoreProductSetCodeDto> QueryProjectedSetCodeAsync(
            string setCodeId,
            string? storeCode,
            string? fallbackSupplierCode
        )
        {
            var setCode = await _db.Queryable<ProductSetCode>()
                .Where(x => x.SetCodeId == setCodeId && !x.IsDeleted)
                .FirstAsync();
            var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
            var projection = await _db.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.StoreCode == storeCode
                    && x.MultiCodeProductCode == setProductCode
                    && !x.IsDeleted
                )
                .FirstAsync();

            if (setCode.SetType == 2)
            {
                var pricingContext = await CreatePricingContextAsync();
                var multi = BuildMultiCodeDto(
                    setCode,
                    projection,
                    fallbackSupplierCode,
                    pricingContext
                );
                return new StoreProductSetCodeDto
                {
                    SetCodeId = multi.SetCodeId,
                    ProductCode = multi.ProductCode ?? string.Empty,
                    SetProductCode = multi.MultiCodeProductCode ?? string.Empty,
                    SetItemNumber = setCode.SetItemNumber,
                    SetBarcode = multi.Barcode,
                    SetPurchasePrice = multi.PurchasePrice,
                    SetRetailPrice = multi.RetailPrice,
                    SetQuantity = setCode.SetQuantity,
                    SetType = setCode.SetType,
                    SetTypeDescription = ResolveSetTypeDescription(setCode.SetType),
                    IsActive = multi.IsActive,
                };
            }

            return BuildSetCodeDto(setCode, projection);
        }

        private async Task<string> GenerateClearanceBarcodeAsync(string? storeCode)
        {
            if (string.IsNullOrWhiteSpace(storeCode))
            {
                throw new InvalidOperationException("缺少分店代码，无法生成清货条码");
            }

            var localNow = DateTime.Now;
            var storeSegment = StoreProductMaintenanceClearanceBarcodeHelper.NormalizeStoreCodeSegment(
                storeCode
            );
            var dateSegment = StoreProductMaintenanceClearanceBarcodeHelper.FormatDateSegment(localNow);
            var attemptedRandomSegments = new HashSet<int>();
            string? barcode = null;

            for (var attempt = 1; attempt <= StoreProductMaintenanceClearanceBarcodeHelper.MaxRandomAttempts; attempt++)
            {
                var nextRandom = Random.Shared.Next(0, 1000);
                if (!attemptedRandomSegments.Add(nextRandom))
                {
                    continue;
                }

                var candidate = StoreProductMaintenanceClearanceBarcodeHelper.GenerateBarcodeForRandom(
                    storeCode,
                    localNow,
                    nextRandom
                );

                if (!await ClearanceBarcodeExistsAsync(candidate))
                {
                    barcode = candidate;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(barcode))
            {
                throw new InvalidOperationException(
                    $"清货条码生成失败: 分店 {storeCode} 在当天可用随机段已耗尽"
                );
            }

            _logger.LogInformation(
                "StoreProductMaintenance generated clearance barcode storeCode={StoreCode} storeSegment={StoreSegment} dateSegment={DateSegment} attemptedRandomCount={AttemptedRandomCount} maxRandomAttempts={MaxRandomAttempts} clearanceBarcode={ClearanceBarcode}",
                storeCode,
                storeSegment,
                dateSegment,
                attemptedRandomSegments.Count,
                StoreProductMaintenanceClearanceBarcodeHelper.MaxRandomAttempts,
                barcode
            );
            return barcode;
        }

        private async Task<bool> ClearanceBarcodeExistsAsync(string barcode)
        {
            if (
                await _db.Queryable<Product>()
                    .Where(p => !p.IsDeleted && p.Barcode == barcode)
                    .AnyAsync()
            )
            {
                return true;
            }

            if (
                await _db.Queryable<ProductSetCode>()
                    .Where(x => !x.IsDeleted && x.SetBarcode == barcode)
                    .AnyAsync()
            )
            {
                return true;
            }

            return await _db.Queryable<StoreClearancePrice>()
                .Where(x => !x.IsDeleted && x.ClearanceBarcode == barcode)
                .AnyAsync();
        }

        private static string ResolveSetProductCode(string? setProductCode, string fallback)
        {
            return string.IsNullOrWhiteSpace(setProductCode) ? fallback : setProductCode;
        }

        private static string? ResolveCurrentStoreCode(
            string? requestedStoreCode,
            List<string>? selectedStoreCodes,
            StoreRetailPrice? storePriceEntity
        )
        {
            if (!string.IsNullOrWhiteSpace(requestedStoreCode))
            {
                return requestedStoreCode;
            }

            if (!string.IsNullOrWhiteSpace(storePriceEntity?.StoreCode))
            {
                return storePriceEntity.StoreCode;
            }

            return selectedStoreCodes?.FirstOrDefault();
        }

        private static string? ResolveFastDetailTargetStoreCode(
            string? requestedStoreCode,
            List<string>? selectedStoreCodes
        )
        {
            if (!string.IsNullOrWhiteSpace(requestedStoreCode))
            {
                return requestedStoreCode;
            }

            return selectedStoreCodes?.FirstOrDefault();
        }

        private static List<string>? ResolveScopedStoreCodes(
            string? requestedStoreCode,
            List<string>? accessibleStoreCodes
        )
        {
            if (!string.IsNullOrWhiteSpace(requestedStoreCode))
            {
                if (accessibleStoreCodes == null)
                {
                    return new List<string> { requestedStoreCode };
                }

                return accessibleStoreCodes.Contains(requestedStoreCode)
                    ? new List<string> { requestedStoreCode }
                    : new List<string>();
            }

            return accessibleStoreCodes;
        }

        private static bool CanAccessStore(string? storeCode, List<string>? accessibleStoreCodes)
        {
            if (accessibleStoreCodes == null)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(storeCode) && accessibleStoreCodes.Contains(storeCode);
        }

        private static string FormatPrice(decimal? value)
        {
            return value.HasValue ? value.Value.ToString("0.00") : string.Empty;
        }

        private static string FormatStoreScope(List<string>? storeCodes)
        {
            if (storeCodes == null)
            {
                return "ALL";
            }

            return storeCodes.Count == 0 ? "NONE" : string.Join(",", storeCodes);
        }

        private static string? NormalizeProductTypeLabel(string? rawValue)
        {
            return int.TryParse(rawValue, out var productType)
                ? StoreProductMaintenanceSyncHelper.NormalizeProductTypeLabel(productType)
                : rawValue;
        }

        private static string ResolveSetTypeDescription(int setType)
        {
            return setType switch
            {
                1 => "套装",
                2 => "多码",
                _ => "未知类型",
            };
        }

        private sealed class LookupHit
        {
            public string ProductCode { get; set; } = string.Empty;
            public string? ItemNumber { get; set; }
            public string? Barcode { get; set; }
            public string MatchSource { get; set; } = string.Empty;
            public string MatchValue { get; set; } = string.Empty;
        }

        private sealed class LookupProductSnapshot
        {
            public string ProductCode { get; set; } = string.Empty;
            public string ProductName { get; set; } = string.Empty;
            public string? ItemNumber { get; set; }
            public string? Barcode { get; set; }
            public string? ProductImage { get; set; }
            public string? Grade { get; set; }
            public int? ProductType { get; set; }
        }

        private sealed class ProductSetCodeCounts
        {
            public int TotalSetCodeCount { get; set; }
        }

        private sealed class FastDetailBaseRow
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
            public string? StorePriceStoreCode { get; set; }
            public string? StoreName { get; set; }
            public string? StorePriceProductCode { get; set; }
            public string? StoreProductCode { get; set; }
            public string? SupplierCode { get; set; }
            public decimal? PurchasePrice { get; set; }
            public decimal? StoreRetailPriceValue { get; set; }
            public decimal? DiscountRate { get; set; }
            public bool? IsAutoPricing { get; set; }
            public bool? IsSpecialProduct { get; set; }
            public bool? StorePriceIsActive { get; set; }
            public string? ClearanceUuid { get; set; }
            public string? ClearanceStoreCode { get; set; }
            public string? ClearanceStoreName { get; set; }
            public string? ClearanceProductCode { get; set; }
            public string? ClearanceBarcode { get; set; }
            public decimal? ClearancePrice { get; set; }
            public int TotalSetCodeCount { get; set; }
        }

        private sealed class ProductCodePageQueryResult
        {
            public List<ProductSetCode> SetCodes { get; set; } = new();
            public int TotalCount { get; set; }
            public int Page { get; set; }
            public int PageSize { get; set; }
            public string? StoreCode { get; set; }
            public string? LocalSupplierCode { get; set; }
            public string? ErrorMessage { get; set; }

            public static ProductCodePageQueryResult Fail(string message)
            {
                return new ProductCodePageQueryResult { ErrorMessage = message };
            }
        }

        private sealed class PricingContext
        {
            public PricingContext(List<PricingStrategy> strategies)
            {
                Strategies = strategies;
            }

            public List<PricingStrategy> Strategies { get; }

            public List<PricingStrategy> Empty { get; } = new();
        }
    }
}
