using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreProductMaintenanceReactService : IStoreProductMaintenanceReactService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<StoreProductMaintenanceReactService> _logger;
        private readonly IAutoPricingService _autoPricingService;

        public StoreProductMaintenanceReactService(
            SqlSugarContext context,
            ILogger<StoreProductMaintenanceReactService> logger,
            IAutoPricingService autoPricingService
        )
        {
            _db = context.Db;
            _logger = logger;
            _autoPricingService = autoPricingService;
        }

        public async Task<ApiResponse<List<StoreProductLookupItemDto>>> LookupAsync(
            StoreProductLookupRequestDto request,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                var keyword = request.Keyword?.Trim();
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    Console.WriteLine("[StoreProductMaintenance][LookupService] empty keyword");
                    return ApiResponse<List<StoreProductLookupItemDto>>.Error("查询内容不能为空");
                }

                var selectedStoreCodes = ResolveScopedStoreCodes(request.StoreCode, accessibleStoreCodes);
                Console.WriteLine(
                    $"[StoreProductMaintenance][LookupService] keyword='{keyword}', requestedStore='{request.StoreCode}', scope={FormatStoreScope(selectedStoreCodes)}"
                );
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    Console.WriteLine(
                        $"[StoreProductMaintenance][LookupService] keyword='{keyword}', no accessible store scope"
                    );
                    return ApiResponse<List<StoreProductLookupItemDto>>.OK(new List<StoreProductLookupItemDto>());
                }

                var items = new List<StoreProductLookupItemDto>();

                var productMatches = await _db.Queryable<Product>()
                    .LeftJoin<ProductGrade>((p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((p, pg) => !p.IsDeleted)
                    .Where((p, pg) =>
                        p.ItemNumber == keyword
                        || p.Barcode == keyword
                    )
                    .Select((p, pg) => new StoreProductLookupItemDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductName = p.ProductName,
                        ItemNumber = p.ItemNumber,
                        Barcode = p.Barcode,
                        ProductImage = p.ProductImage,
                        Grade = pg.Grade,
                        ProductTypeLabel = null,
                        MatchSource =
                            p.ItemNumber == keyword ? "ItemNumber"
                            : "ProductBarcode",
                        MatchValue = keyword,
                    })
                    .ToListAsync();
                Console.WriteLine(
                    $"[StoreProductMaintenance][LookupService] productMatches={productMatches.Count}"
                );
                items.AddRange(productMatches);

                var setMatches = await _db.Queryable<ProductSetCode>()
                    .LeftJoin<Product>((s, p) => s.ProductCode == p.ProductCode)
                    .LeftJoin<ProductGrade>((s, p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((s, p, pg) => !s.IsDeleted && !p.IsDeleted)
                    .Where((s, p, pg) => s.SetBarcode == keyword)
                    .Select((s, p, pg) => new StoreProductLookupItemDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductName = p.ProductName,
                        ItemNumber = s.SetItemNumber,
                        Barcode = s.SetBarcode ?? p.Barcode,
                        ProductImage = p.ProductImage,
                        Grade = pg.Grade,
                        ProductTypeLabel = null,
                        MatchSource = "SetBarcode",
                        MatchValue = keyword,
                    })
                    .ToListAsync();
                Console.WriteLine(
                    $"[StoreProductMaintenance][LookupService] setMatches={setMatches.Count}"
                );
                items.AddRange(setMatches);

                var clearanceMatchesQuery = _db.Queryable<StoreClearancePrice>()
                    .LeftJoin<Product>((c, p) => c.ProductCode == p.ProductCode)
                    .LeftJoin<ProductGrade>((c, p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((c, p, pg) =>
                        !c.IsDeleted
                        && !p.IsDeleted
                        && c.ClearanceBarcode == keyword
                    );

                if (selectedStoreCodes != null)
                {
                    clearanceMatchesQuery = clearanceMatchesQuery.Where((c, p, pg) =>
                        c.StoreCode != null && selectedStoreCodes.Contains(c.StoreCode)
                    );
                }

                var clearanceMatches = await clearanceMatchesQuery
                    .Select((c, p, pg) => new StoreProductLookupItemDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductName = p.ProductName,
                        ItemNumber = p.ItemNumber,
                        Barcode = c.ClearanceBarcode,
                        ProductImage = p.ProductImage,
                        Grade = pg.Grade,
                        ProductTypeLabel = null,
                        MatchSource = "ClearanceBarcode",
                        MatchValue = keyword,
                    })
                    .ToListAsync();
                Console.WriteLine(
                    $"[StoreProductMaintenance][LookupService] clearanceMatches={clearanceMatches.Count}"
                );
                items.AddRange(clearanceMatches);

                var normalized = items
                    .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                    .GroupBy(item => $"{item.ProductCode}|{item.MatchSource}|{item.Barcode}|{item.ItemNumber}")
                    .Select(group =>
                    {
                        var first = group.First();
                        first.ProductTypeLabel = NormalizeProductTypeLabel(first.ProductTypeLabel);
                        return first;
                    })
                    .OrderBy(item => item.ProductName)
                    .ToList();
                Console.WriteLine(
                    $"[StoreProductMaintenance][LookupService] keyword='{keyword}', finalCount={normalized.Count}"
                );

                return ApiResponse<List<StoreProductLookupItemDto>>.OK(normalized);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[StoreProductMaintenance][LookupService] error keyword='{request.Keyword}': {ex.Message}"
                );
                _logger.LogError(ex, "商品查询失败: {Keyword}", request.Keyword);
                return ApiResponse<List<StoreProductLookupItemDto>>.Error($"商品查询失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreProductDetailDto>> GetDetailAsync(
            string productCode,
            string? storeCode,
            List<string>? accessibleStoreCodes
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    return ApiResponse<StoreProductDetailDto>.Error("商品编码不能为空");
                }

                var selectedStoreCodes = ResolveScopedStoreCodes(storeCode, accessibleStoreCodes);
                Console.WriteLine(
                    $"[StoreProductMaintenance][DetailService] productCode='{productCode}', requestedStore='{storeCode}', scope={FormatStoreScope(selectedStoreCodes)}"
                );
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    Console.WriteLine(
                        $"[StoreProductMaintenance][DetailService] productCode='{productCode}', no accessible store scope"
                    );
                    return ApiResponse<StoreProductDetailDto>.Error("当前账号或设备无权访问该分店");
                }

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

                if (product == null)
                {
                    Console.WriteLine(
                        $"[StoreProductMaintenance][DetailService] productCode='{productCode}', product not found"
                    );
                    return ApiResponse<StoreProductDetailDto>.Error("商品不存在");
                }
                Console.WriteLine(
                    $"[StoreProductMaintenance][DetailService] productCode='{productCode}', product found"
                );

                StoreProductStorePriceDto? storePrice = null;
                StoreRetailPrice? storePriceEntity = null;
                if (selectedStoreCodes == null || selectedStoreCodes.Count > 0)
                {
                    storePriceEntity = await QueryStorePriceAsync(productCode, selectedStoreCodes);
                    Console.WriteLine(
                        $"[StoreProductMaintenance][DetailService] productCode='{productCode}', storePriceFound={storePriceEntity != null}"
                    );
                    if (storePriceEntity != null)
                    {
                        storePrice = await BuildStorePriceDtoAsync(storePriceEntity, product.LocalSupplierCode);
                    }
                }

                StoreProductClearancePriceDto? clearancePrice = null;
                if (selectedStoreCodes == null || selectedStoreCodes.Count > 0)
                {
                    var clearancePriceEntity = await QueryClearancePriceAsync(productCode, selectedStoreCodes);
                    Console.WriteLine(
                        $"[StoreProductMaintenance][DetailService] productCode='{productCode}', clearancePriceFound={clearancePriceEntity != null}"
                    );
                    if (clearancePriceEntity != null)
                    {
                        clearancePrice = await BuildClearancePriceDtoAsync(clearancePriceEntity);
                    }
                }

                var productSetCodes = await _db.Queryable<ProductSetCode>()
                    .Where(s => s.ProductCode == productCode && !s.IsDeleted)
                    .OrderBy(s => s.SetBarcode)
                    .ToListAsync();
                Console.WriteLine(
                    $"[StoreProductMaintenance][DetailService] productCode='{productCode}', productSetCodeCount={productSetCodes.Count}"
                );

                var currentStoreCode = ResolveCurrentStoreCode(storeCode, selectedStoreCodes, storePriceEntity);
                var projections = await EnsureProjectedStoreMultiCodesAsync(
                    productSetCodes,
                    storePriceEntity,
                    currentStoreCode,
                    "system"
                );
                var projectionMap = projections.ToDictionary(
                    p => ResolveSetProductCode(p.MultiCodeProductCode, p.UUID),
                    p => p
                );

                var setCodes = new List<StoreProductSetCodeDto>();
                var multiCodes = new List<StoreProductMultiCodeDto>();
                foreach (var setCode in productSetCodes)
                {
                    var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                    projectionMap.TryGetValue(setProductCode, out var projection);
                    if (setCode.SetType == 2)
                    {
                        multiCodes.Add(await BuildMultiCodeDtoAsync(setCode, projection, product.LocalSupplierCode));
                        continue;
                    }

                    setCodes.Add(BuildSetCodeDto(setCode, projection));
                }
                Console.WriteLine(
                    $"[StoreProductMaintenance][DetailService] productCode='{productCode}', setCodeCount={setCodes.Count}, multiCodeCount={multiCodes.Count}"
                );

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
                };
                Console.WriteLine(
                    $"[StoreProductMaintenance][DetailService] productCode='{productCode}', detail returned"
                );

                return ApiResponse<StoreProductDetailDto>.OK(detail);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[StoreProductMaintenance][DetailService] error productCode='{productCode}': {ex.Message}"
                );
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
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
                    entity = new StoreClearancePrice
                    {
                        UUID = UuidHelper.GenerateUuid7(),
                        StoreCode = request.StoreCode,
                        ProductCode = productCode,
                        ClearanceBarcode = await GenerateClearanceBarcodeAsync(product.LocalSupplierCode),
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

                return ApiResponse<StoreProductClearancePriceDto>.OK(
                    await BuildClearancePriceDtoAsync(entity),
                    "保存成功"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存清货价失败: {ProductCode}", productCode);
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

        private async Task<StoreProductMultiCodeDto> BuildMultiCodeDtoAsync(
            ProductSetCode setCode,
            StoreMultiCodeProduct? entity,
            string? fallbackSupplierCode
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

            await FillPricingFieldsAsync(
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

        private async Task<List<StoreMultiCodeProduct>> EnsureProjectedStoreMultiCodesAsync(
            List<ProductSetCode> setCodes,
            StoreRetailPrice? mainStorePrice,
            string? storeCode,
            string updatedBy
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
                .ToListAsync();

            var existingMap = existing.ToDictionary(
                x => ResolveSetProductCode(x.MultiCodeProductCode, x.UUID),
                x => x
            );
            var inserts = new List<StoreMultiCodeProduct>();
            foreach (var setCode in setCodes)
            {
                var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                if (existingMap.ContainsKey(setProductCode))
                {
                    continue;
                }

                var projection = BuildProjectedStoreMultiCode(setCode, mainStorePrice, storeCode, updatedBy);
                inserts.Add(projection);
                existingMap[setProductCode] = projection;
            }

            if (inserts.Count > 0)
            {
                await _db.Insertable(inserts).ExecuteCommandAsync();
            }

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

            var currentStoreRecords = await EnsureProjectedStoreMultiCodesAsync(
                setCodes,
                mainStorePrice,
                mainStorePrice.StoreCode,
                updatedBy
            );
            var projectionMap = currentStoreRecords.ToDictionary(
                x => ResolveSetProductCode(x.MultiCodeProductCode, x.UUID),
                x => x
            );
            var updates = new List<StoreMultiCodeProduct>();
            foreach (var setCode in setCodes)
            {
                var setProductCode = ResolveSetProductCode(setCode.SetProductCode, setCode.SetCodeId);
                if (!projectionMap.TryGetValue(setProductCode, out var existing))
                {
                    continue;
                }

                ApplyProjectionValues(existing, setCode, mainStorePrice, updatedBy);
                updates.Add(existing);
            }

            if (updates.Count > 0)
            {
                await _db.Updateable(updates).ExecuteCommandAsync();
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
                var multi = await BuildMultiCodeDtoAsync(setCode, projection, fallbackSupplierCode);
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

        private async Task<string> GenerateClearanceBarcodeAsync(string? supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
            {
                throw new InvalidOperationException("商品缺少供应商编码，无法生成清货条码");
            }

            var existingBarcodes = await _db.Queryable<Product>()
                .Where(p => !p.IsDeleted && p.Barcode != null)
                .Select(p => p.Barcode!)
                .ToListAsync();
            var setBarcodes = await _db.Queryable<ProductSetCode>()
                .Where(x => !x.IsDeleted && x.SetBarcode != null)
                .Select(x => x.SetBarcode!)
                .ToListAsync();
            var clearanceBarcodes = await _db.Queryable<StoreClearancePrice>()
                .Where(x => !x.IsDeleted && x.ClearanceBarcode != null)
                .Select(x => x.ClearanceBarcode!)
                .ToListAsync();
            existingBarcodes.AddRange(setBarcodes);
            existingBarcodes.AddRange(clearanceBarcodes);
            return BarcodeHelper.GenerateEAN13Barcode(supplierCode, 0, existingBarcodes);
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
    }
}
