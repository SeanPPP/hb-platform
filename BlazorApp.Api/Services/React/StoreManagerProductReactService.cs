using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreManagerProductReactService : IStoreManagerProductReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<StoreManagerProductReactService> _logger;

        public StoreManagerProductReactService(
            SqlSugarContext context,
            ILogger<StoreManagerProductReactService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ApiResponse<List<StoreDto>>> GetAuthorizedStoresAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;

                var storeCodes = await db.Queryable<UserStore>()
                    .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                    .Where((us, s) => us.UserGUID == userGuid && us.IsDeleted == false)
                    .Select((us, s) => s.StoreCode)
                    .Distinct()
                    .ToListAsync();

                if (!storeCodes.Any())
                {
                    return ApiResponse<List<StoreDto>>.Error("没有找到关联的分店");
                }

                var stores = await db.Queryable<Store>()
                    .Where(s => storeCodes.Contains(s.StoreCode) && s.IsDeleted == false)
                    .Select(s => new StoreDto
                    {
                        StoreCode = s.StoreCode,
                        StoreName = s.StoreName,
                        IsActive = s.IsActive,
                    })
                    .ToListAsync();

                return ApiResponse<List<StoreDto>>.OK(stores);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店长有权限的分店列表失败");
                return ApiResponse<List<StoreDto>>.Error($"获取分店列表失败: {ex.Message}");
            }
        }

        public async Task<
            StoreManagerPagedListDto<StoreManagerProductListItemDto>
        > GetProductPagedListAsync(StoreManagerProductFilterDto filter)
        {
            var db = _context.Db;
            var sw = Stopwatch.StartNew();

            var query = db.Queryable<Product>()
                .InnerJoin<StoreRetailPrice>((p, srp) => p.ProductCode == srp.ProductCode)
                .InnerJoin<HBLocalSupplier>(
                    (p, srp, ls) => srp.SupplierCode == ls.LocalSupplierCode
                )
                .Where((p, srp, ls) => filter.StoreCodes!.Contains(srp.StoreCode!));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var keyword = filter.Search.Trim();
                query = query.Where(
                    (p, srp, ls) =>
                        (p.ProductName != null && p.ProductName.Contains(keyword))
                        || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        || (p.Barcode != null && p.Barcode.Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierName))
            {
                var supplierName = filter.SupplierName.Trim();
                query = query.Where(
                    (p, srp, ls) => ls.Name != null && ls.Name.Contains(supplierName)
                );
            }

            if (filter.IsAutoPricing.HasValue)
            {
                query = query.Where(
                    (p, srp, ls) => srp.IsAutoPricing == filter.IsAutoPricing.Value
                );
            }

            if (filter.MinPurchasePrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.PurchasePrice >= filter.MinPurchasePrice.Value
                );
            if (filter.MaxPurchasePrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.PurchasePrice <= filter.MaxPurchasePrice.Value
                );
            if (filter.MinRetailPrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.StoreRetailPriceValue >= filter.MinRetailPrice.Value
                );
            if (filter.MaxRetailPrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.StoreRetailPriceValue <= filter.MaxRetailPrice.Value
                );
            if (filter.MinDiscountRate.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.DiscountRate >= filter.MinDiscountRate.Value
                );
            if (filter.MaxDiscountRate.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.DiscountRate <= filter.MaxDiscountRate.Value
                );

            if (!string.IsNullOrWhiteSpace(filter.SortBy))
            {
                var isDesc = filter.SortOrder?.ToLower() == "desc";
                query = filter.SortBy.ToLower() switch
                {
                    "purchaseprice" => query.OrderBy(
                        (p, srp, ls) => srp.PurchasePrice,
                        isDesc ? OrderByType.Desc : OrderByType.Asc
                    ),
                    "retailprice" => query.OrderBy(
                        (p, srp, ls) => srp.StoreRetailPriceValue,
                        isDesc ? OrderByType.Desc : OrderByType.Asc
                    ),
                    "discountrate" => query.OrderBy(
                        (p, srp, ls) => srp.DiscountRate,
                        isDesc ? OrderByType.Desc : OrderByType.Asc
                    ),
                    _ => query.OrderBy((p, srp, ls) => p.ProductName, OrderByType.Asc),
                };
            }
            else
            {
                query = query.OrderBy((p, srp, ls) => p.ProductName, OrderByType.Asc);
            }

            var total = await query.CountAsync();
            var items = await query
                .Select(
                    (p, srp, ls) =>
                        new StoreManagerProductListItemDto
                        {
                            ProductCode = p.ProductCode!,
                            ProductName = p.ProductName!,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductImage = p.ProductImage,
                        }
                )
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new StoreManagerPagedListDto<StoreManagerProductListItemDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        public async Task<ApiResponse<StoreManagerProductDetailDto>> GetProductDetailAsync(
            string productCode,
            List<string> authorizedStoreCodes
        )
        {
            try
            {
                var db = _context.Db;

                var product = await db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .Select<StoreManagerProductListItemDto>(p => new StoreManagerProductListItemDto
                    {
                        ProductCode = p.ProductCode!,
                        ProductName = p.ProductName!,
                        ItemNumber = p.ItemNumber,
                        Barcode = p.Barcode,
                        ProductImage = p.ProductImage,
                    })
                    .FirstAsync();

                if (product == null)
                {
                    return ApiResponse<StoreManagerProductDetailDto>.Error("商品不存在");
                }

                var storePrices = await db.Queryable<StoreRetailPrice>()
                    .LeftJoin<Store>((srp, s) => srp.StoreCode == s.StoreCode)
                    .Where(
                        (srp, s) =>
                            srp.ProductCode == productCode
                            && authorizedStoreCodes.Contains(srp.StoreCode!)
                            && srp.IsDeleted == false
                            && s.IsDeleted == false
                    )
                    .Select<StoreManagerStorePriceDto>(
                        (srp, s) =>
                            new StoreManagerStorePriceDto
                            {
                                UUID = srp.UUID,
                                StoreCode = srp.StoreCode!,
                                StoreName = s.StoreName!,
                                ProductCode = srp.ProductCode!,
                                PurchasePrice = srp.PurchasePrice,
                                StoreRetailPriceValue = srp.StoreRetailPriceValue,
                                IsAutoPricing = srp.IsAutoPricing,
                            }
                    )
                    .ToListAsync();

                var multiCodePrices = await db.Queryable<StoreMultiCodeProduct>()
                    .LeftJoin<Store>((smcp, s) => smcp.StoreCode == s.StoreCode)
                    .Where(
                        (smcp, s) =>
                            smcp.ProductCode == productCode
                            && authorizedStoreCodes.Contains(smcp.StoreCode!)
                            && smcp.IsDeleted == false
                            && s.IsDeleted == false
                    )
                    .Select<StoreManagerMultiCodePriceDto>(
                        (smcp, s) =>
                            new StoreManagerMultiCodePriceDto
                            {
                                UUID = smcp.UUID,
                                StoreCode = smcp.StoreCode!,
                                ProductCode = smcp.ProductCode!,
                                MultiBarcode = smcp.MultiBarcode,
                                PurchasePrice = smcp.PurchasePrice,
                                MultiCodeRetailPrice = smcp.MultiCodeRetailPrice,
                                IsAutoPricing = smcp.IsAutoPricing,
                            }
                    )
                    .ToListAsync();

                var detail = new StoreManagerProductDetailDto
                {
                    Product = product,
                    StorePrices = storePrices,
                    MultiCodePrices = multiCodePrices,
                };

                return ApiResponse<StoreManagerProductDetailDto>.OK(detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
                return ApiResponse<StoreManagerProductDetailDto>.Error(
                    $"获取商品详情失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<StoreManagerStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            StoreManagerUpdatePriceDto dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var exists = await db.Queryable<StoreRetailPrice>()
                    .Where(srp => srp.UUID == uuid && srp.IsDeleted == false)
                    .FirstAsync();

                if (exists == null)
                {
                    return ApiResponse<StoreManagerStorePriceDto>.Error("分店价格记录不存在");
                }

                if (dto.PurchasePrice.HasValue)
                    exists.PurchasePrice = dto.PurchasePrice.Value;
                if (dto.StoreRetailPriceValue.HasValue)
                    exists.StoreRetailPriceValue = dto.StoreRetailPriceValue.Value;
                if (dto.IsAutoPricing.HasValue)
                    exists.IsAutoPricing = dto.IsAutoPricing.Value;

                exists.UpdatedBy = updatedBy;
                exists.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(exists).ExecuteCommandAsync();

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == exists.StoreCode)
                    .FirstAsync();

                var result = new StoreManagerStorePriceDto
                {
                    UUID = exists.UUID,
                    StoreCode = exists.StoreCode!,
                    StoreName = store?.StoreName ?? "",
                    ProductCode = exists.ProductCode!,
                    PurchasePrice = exists.PurchasePrice,
                    StoreRetailPriceValue = exists.StoreRetailPriceValue,
                    IsAutoPricing = exists.IsAutoPricing,
                };

                return ApiResponse<StoreManagerStorePriceDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店价格失败: {UUID}", uuid);
                return ApiResponse<StoreManagerStorePriceDto>.Error(
                    $"更新分店价格失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<BatchOperationReactResult>> BatchUpdateStorePricesAsync(
            List<StoreManagerUpdatePriceDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var result = new BatchOperationReactResult
                {
                    SuccessCount = 0,
                    FailedCount = 0,
                    Errors = new List<string>(),
                };

                foreach (var item in items)
                {
                    try
                    {
                        var exists = await db.Queryable<StoreRetailPrice>()
                            .Where(srp => srp.UUID == item.UUID && srp.IsDeleted == false)
                            .FirstAsync();

                        if (exists == null)
                        {
                            result.FailedCount++;
                            result.Errors.Add($"UUID {item.UUID} 的记录不存在");
                            continue;
                        }

                        if (item.PurchasePrice.HasValue)
                            exists.PurchasePrice = item.PurchasePrice.Value;
                        if (item.StoreRetailPriceValue.HasValue)
                            exists.StoreRetailPriceValue = item.StoreRetailPriceValue.Value;
                        if (item.IsAutoPricing.HasValue)
                            exists.IsAutoPricing = item.IsAutoPricing.Value;

                        exists.UpdatedBy = updatedBy;
                        exists.UpdatedAt = DateTime.UtcNow;

                        await db.Updateable(exists).ExecuteCommandAsync();
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        result.Errors.Add($"UUID {item.UUID} 更新失败: {ex.Message}");
                    }
                }

                return ApiResponse<BatchOperationReactResult>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分店价格失败");
                return ApiResponse<BatchOperationReactResult>.Error($"批量更新失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreManagerMultiCodePriceDto>> UpdateMultiCodePriceAsync(
            string uuid,
            StoreManagerUpdateMultiCodePriceDto dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var exists = await db.Queryable<StoreMultiCodeProduct>()
                    .Where(smcp => smcp.UUID == uuid && smcp.IsDeleted == false)
                    .FirstAsync();

                if (exists == null)
                {
                    return ApiResponse<StoreManagerMultiCodePriceDto>.Error("多码价格记录不存在");
                }

                if (dto.PurchasePrice.HasValue)
                    exists.PurchasePrice = dto.PurchasePrice.Value;
                if (dto.MultiCodeRetailPrice.HasValue)
                    exists.MultiCodeRetailPrice = dto.MultiCodeRetailPrice.Value;
                if (dto.IsAutoPricing.HasValue)
                    exists.IsAutoPricing = dto.IsAutoPricing.Value;

                exists.UpdatedBy = updatedBy;
                exists.UpdatedAt = DateTime.Now;

                await db.Updateable(exists).ExecuteCommandAsync();

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == exists.StoreCode)
                    .FirstAsync();

                var result = new StoreManagerMultiCodePriceDto
                {
                    UUID = exists.UUID,
                    StoreCode = exists.StoreCode!,
                    ProductCode = exists.ProductCode!,
                    MultiBarcode = exists.MultiBarcode,
                    PurchasePrice = exists.PurchasePrice,
                    MultiCodeRetailPrice = exists.MultiCodeRetailPrice,
                    IsAutoPricing = exists.IsAutoPricing,
                };

                return ApiResponse<StoreManagerMultiCodePriceDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新多码价格失败: {UUID}", uuid);
                return ApiResponse<StoreManagerMultiCodePriceDto>.Error(
                    $"更新多码价格失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<BatchOperationReactResult>> BatchUpdateMultiCodePricesAsync(
            List<StoreManagerUpdateMultiCodePriceDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var result = new BatchOperationReactResult
                {
                    SuccessCount = 0,
                    FailedCount = 0,
                    Errors = new List<string>(),
                };

                foreach (var item in items)
                {
                    try
                    {
                        var exists = await db.Queryable<StoreMultiCodeProduct>()
                            .Where(smcp => smcp.UUID == item.UUID && smcp.IsDeleted == false)
                            .FirstAsync();

                        if (exists == null)
                        {
                            result.FailedCount++;
                            result.Errors.Add($"UUID {item.UUID} 的记录不存在");
                            continue;
                        }

                        if (item.PurchasePrice.HasValue)
                            exists.PurchasePrice = item.PurchasePrice.Value;
                        if (item.MultiCodeRetailPrice.HasValue)
                            exists.MultiCodeRetailPrice = item.MultiCodeRetailPrice.Value;
                        if (item.IsAutoPricing.HasValue)
                            exists.IsAutoPricing = item.IsAutoPricing.Value;

                        exists.UpdatedBy = updatedBy;
                        exists.UpdatedAt = DateTime.UtcNow;

                        await db.Updateable(exists).ExecuteCommandAsync();
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        result.Errors.Add($"UUID {item.UUID} 更新失败: {ex.Message}");
                    }
                }

                return ApiResponse<BatchOperationReactResult>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新多码价格失败");
                return ApiResponse<BatchOperationReactResult>.Error($"批量更新失败: {ex.Message}");
            }
        }
    }
}
