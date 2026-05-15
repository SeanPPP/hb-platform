using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
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
                    return ApiResponse<List<StoreProductLookupItemDto>>.Error("查询内容不能为空");
                }

                var selectedStoreCodes = ResolveScopedStoreCodes(request.StoreCode, accessibleStoreCodes);
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    return ApiResponse<List<StoreProductLookupItemDto>>.OK(new List<StoreProductLookupItemDto>());
                }

                var items = new List<StoreProductLookupItemDto>();

                var productMatches = await _db.Queryable<Product>()
                    .LeftJoin<ProductGrade>((p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((p, pg) => !p.IsDeleted)
                    .Where((p, pg) =>
                        p.ProductCode == keyword
                        || p.ItemNumber == keyword
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
                            p.ProductCode == keyword ? "ProductCode"
                            : p.ItemNumber == keyword ? "ItemNumber"
                            : "ProductBarcode",
                        MatchValue = keyword,
                    })
                    .ToListAsync();
                items.AddRange(productMatches);

                var multiMatchesQuery = _db.Queryable<StoreMultiCodeProduct>()
                    .LeftJoin<Product>((m, p) => m.ProductCode == p.ProductCode)
                    .LeftJoin<ProductGrade>((m, p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((m, p, pg) => !m.IsDeleted && !p.IsDeleted)
                    .Where((m, p, pg) =>
                        m.MultiBarcode == keyword
                        || m.MultiCodeProductCode == keyword
                        || m.StoreMultiCodeProductCode == keyword
                    );

                if (selectedStoreCodes != null)
                {
                    multiMatchesQuery = multiMatchesQuery.Where((m, p, pg) =>
                        m.StoreCode != null && selectedStoreCodes.Contains(m.StoreCode)
                    );
                }

                var multiMatches = await multiMatchesQuery
                    .Select((m, p, pg) => new StoreProductLookupItemDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductName = p.ProductName,
                        ItemNumber = p.ItemNumber,
                        Barcode = m.MultiBarcode ?? p.Barcode,
                        ProductImage = p.ProductImage,
                        Grade = pg.Grade,
                        ProductTypeLabel = null,
                        MatchSource =
                            m.MultiBarcode == keyword ? "MultiBarcode"
                            : m.MultiCodeProductCode == keyword ? "MultiCodeProductCode"
                            : "StoreMultiCodeProductCode",
                        MatchValue = keyword,
                    })
                    .ToListAsync();
                items.AddRange(multiMatches);

                var setMatches = await _db.Queryable<ProductSetCode>()
                    .LeftJoin<Product>((s, p) => s.ProductCode == p.ProductCode)
                    .LeftJoin<ProductGrade>((s, p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((s, p, pg) => !s.IsDeleted && !p.IsDeleted)
                    .Where((s, p, pg) =>
                        s.SetBarcode == keyword
                        || s.SetItemNumber == keyword
                        || s.SetProductCode == keyword
                    )
                    .Select((s, p, pg) => new StoreProductLookupItemDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductName = p.ProductName,
                        ItemNumber = s.SetItemNumber,
                        Barcode = s.SetBarcode ?? p.Barcode,
                        ProductImage = p.ProductImage,
                        Grade = pg.Grade,
                        ProductTypeLabel = null,
                        MatchSource =
                            s.SetBarcode == keyword ? "SetBarcode"
                            : s.SetItemNumber == keyword ? "SetItemNumber"
                            : "SetProductCode",
                        MatchValue = keyword,
                    })
                    .ToListAsync();
                items.AddRange(setMatches);

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
                if (selectedStoreCodes != null && selectedStoreCodes.Count == 0)
                {
                    return ApiResponse<StoreProductDetailDto>.Error("当前账号或设备无权访问该分店");
                }

                var product = await _db.Queryable<Product>()
                    .LeftJoin<ProductGrade>((p, pg) => p.ProductCode == pg.ProductCode && !pg.IsDeleted)
                    .Where((p, pg) => p.ProductCode == productCode && !p.IsDeleted)
                    .Select((p, pg) => new
                    {
                        p.ProductCode,
                        p.ProductName,
                        p.ItemNumber,
                        p.Barcode,
                        p.ProductImage,
                        p.ProductType,
                        p.LocalSupplierCode,
                        Grade = pg.Grade,
                    })
                    .FirstAsync();

                if (product == null)
                {
                    return ApiResponse<StoreProductDetailDto>.Error("商品不存在");
                }

                StoreProductStorePriceDto? storePrice = null;
                if (selectedStoreCodes == null || selectedStoreCodes.Count > 0)
                {
                    var storePriceEntity = await QueryStorePriceAsync(productCode, selectedStoreCodes);
                    if (storePriceEntity != null)
                    {
                        storePrice = await BuildStorePriceDtoAsync(storePriceEntity, product.LocalSupplierCode);
                    }
                }

                var multiCodeQuery = _db.Queryable<StoreMultiCodeProduct>()
                    .Where(m => m.ProductCode == productCode && !m.IsDeleted);
                if (selectedStoreCodes != null)
                {
                    multiCodeQuery = multiCodeQuery.Where(m =>
                        m.StoreCode != null && selectedStoreCodes.Contains(m.StoreCode)
                    );
                }

                var multiCodeEntities = await multiCodeQuery
                    .OrderBy(m => m.MultiBarcode)
                    .ToListAsync();

                var multiCodes = new List<StoreProductMultiCodeDto>();
                foreach (var entity in multiCodeEntities)
                {
                    multiCodes.Add(await BuildMultiCodeDtoAsync(entity, product.LocalSupplierCode));
                }

                var setCodes = await _db.Queryable<ProductSetCode>()
                    .Where(s => s.ProductCode == productCode && !s.IsDeleted)
                    .OrderBy(s => s.SetBarcode)
                    .Select(s => new StoreProductSetCodeDto
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
                        SetTypeDescription = s.SetTypeDescription,
                        IsActive = s.IsActive,
                    })
                    .ToListAsync();

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
                    StorePrice = storePrice,
                    MultiCodes = multiCodes,
                    SetCodes = setCodes,
                };

                return ApiResponse<StoreProductDetailDto>.OK(detail);
            }
            catch (Exception ex)
            {
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

                entity.PurchasePrice = request.PurchasePrice;
                entity.StoreRetailPriceValue = request.RetailPrice;
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

                var dto = await BuildStorePriceDtoAsync(entity, supplierCode);
                return ApiResponse<StoreProductStorePriceDto>.OK(dto, "保存成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店商品失败: {Uuid}", uuid);
                return ApiResponse<StoreProductStorePriceDto>.Error($"更新分店商品失败: {ex.Message}");
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

                var dto = await BuildMultiCodeDtoAsync(entity, supplierCode);
                return ApiResponse<StoreProductMultiCodeDto>.OK(dto, "保存成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店多码失败: {Uuid}", uuid);
                return ApiResponse<StoreProductMultiCodeDto>.Error($"更新分店多码失败: {ex.Message}");
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
            StoreMultiCodeProduct entity,
            string? fallbackSupplierCode
        )
        {
            var dto = new StoreProductMultiCodeDto
            {
                Uuid = entity.UUID,
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

        private static string? NormalizeProductTypeLabel(string? rawValue)
        {
            return rawValue switch
            {
                "1" => "单品",
                "2" => "多码",
                "3" => "套装",
                _ => rawValue,
            };
        }
    }
}
