using System;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店商品价格服务实现
    /// </summary>
    public class StoreProductPriceReactService : IStoreProductPriceReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<StoreProductPriceReactService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public StoreProductPriceReactService(
            SqlSugarContext context,
            ILogger<StoreProductPriceReactService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 获取分店商品价格网格数据
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>分页结果</returns>
        public async Task<GridResponseDto<StoreProductPriceListDto>> GetGridDataAsync(
            StoreProductPriceQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var pageIndex = query.PageNumber;
                var pageSize = query.PageSize;

                var baseQuery = db.Queryable<Product>()
                    .With(SqlWith.NoLock)
                    .Where(p => p.IsDeleted == false);

                if (!string.IsNullOrWhiteSpace(query.LocalSupplierCode))
                {
                    baseQuery = baseQuery.Where(p => p.LocalSupplierCode == query.LocalSupplierCode);
                }

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var keyword = query.Search.Trim();
                    baseQuery = baseQuery.Where(p =>
                        p.ProductName.Contains(keyword) ||
                        (p.ProductCode != null && p.ProductCode.Contains(keyword)) ||
                        (p.ItemNumber != null && p.ItemNumber.Contains(keyword)) ||
                        (p.Barcode != null && p.Barcode.Contains(keyword))
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.ProductName))
                {
                    baseQuery = baseQuery.Where(p => p.ProductName.Contains(query.ProductName));
                }

                if (!string.IsNullOrWhiteSpace(query.ProductCode))
                {
                    baseQuery = baseQuery.Where(p => p.ProductCode.Contains(query.ProductCode));
                }

                if (!string.IsNullOrWhiteSpace(query.ItemNumber))
                {
                    baseQuery = baseQuery.Where(p => p.ItemNumber.Contains(query.ItemNumber));
                }

                if (!string.IsNullOrWhiteSpace(query.Barcode))
                {
                    baseQuery = baseQuery.Where(p => p.Barcode.Contains(query.Barcode));
                }

                if (query.ProductType.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.ProductType == query.ProductType.Value);
                }

                if (query.IsActive.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.IsActive == query.IsActive.Value);
                }

                if (query.IsSpecialProduct.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.IsSpecialProduct == query.IsSpecialProduct.Value);
                }

                var joinQuery = baseQuery
                    .LeftJoin<StoreRetailPrice>(
                        (p, srp) => p.ProductCode == srp.ProductCode && 
                                     srp.StoreCode == query.StoreCode && 
                                     srp.IsDeleted == false
                    )
                    .LeftJoin<HBLocalSupplier>(
                        (p, srp, sup) => p.LocalSupplierCode == sup.LocalSupplierCode && sup.IsDeleted == false
                    );

                if (query.PurchasePriceGt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.PurchasePrice >= query.PurchasePriceGt.Value);
                }

                if (query.PurchasePriceLt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.PurchasePrice <= query.PurchasePriceLt.Value);
                }

                if (query.RetailPriceGt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.StoreRetailPriceValue >= query.RetailPriceGt.Value);
                }

                if (query.RetailPriceLt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.StoreRetailPriceValue <= query.RetailPriceLt.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.SortBy))
                {
                    var asc = query.SortOrder?.ToLower() == "asc";
                    joinQuery = query.SortBy.ToLower() switch
                    {
                        "productname" => joinQuery.OrderBy((p, srp, sup) => p.ProductName, asc ? OrderByType.Asc : OrderByType.Desc),
                        "productcode" => joinQuery.OrderBy((p, srp, sup) => p.ProductCode, asc ? OrderByType.Asc : OrderByType.Desc),
                        "itemnumber" => joinQuery.OrderBy((p, srp, sup) => p.ItemNumber, asc ? OrderByType.Asc : OrderByType.Desc),
                        "barcode" => joinQuery.OrderBy((p, srp, sup) => p.Barcode, asc ? OrderByType.Asc : OrderByType.Desc),
                        "middlesackagequantity" => joinQuery.OrderBy((p, srp, sup) => p.MiddlePackageQuantity, asc ? OrderByType.Asc : OrderByType.Desc),
                        "purchaseprice" => joinQuery.OrderBy((p, srp, sup) => srp.PurchasePrice, asc ? OrderByType.Asc : OrderByType.Desc),
                        "retailprice" => joinQuery.OrderBy((p, srp, sup) => srp.StoreRetailPriceValue, asc ? OrderByType.Asc : OrderByType.Desc),
                        "discountrate" => joinQuery.OrderBy((p, srp, sup) => srp.DiscountRate, asc ? OrderByType.Asc : OrderByType.Desc),
                        "updatedat" => joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, asc ? OrderByType.Asc : OrderByType.Desc),
                        _ => joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, OrderByType.Desc)
                    };
                }
                else
                {
                    joinQuery = joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, OrderByType.Desc);
                }

                var totalRef = new RefAsync<int>(0);
                var items = await joinQuery
                    .Select(
                        (p, srp, sup) => new StoreProductPriceListDto
                        {
                            ProductCode = p.ProductCode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            LocalSupplierCode = p.LocalSupplierCode,
                            LocalSupplierName = sup.Name,
                            ProductType = p.ProductType,
                            MiddlePackageQuantity = p.MiddlePackageQuantity,
                            IsActive = p.IsActive,
                            UpdatedAt = srp.UpdatedAt,
                            UpdatedBy = srp.UpdatedBy,
                            StoreCode = srp.StoreCode,
                            StorePurchasePrice = srp.PurchasePrice,
                            StoreRetailPrice = srp.StoreRetailPriceValue,
                            IsStoreAutoPricing = srp.IsAutoPricing,
                            IsStoreSpecialProduct = srp.IsSpecialProduct,
                            DiscountRate = srp.DiscountRate
                        }
                    )
                    .ToPageListAsync(pageIndex, pageSize, totalRef);
                return GridResponseDto<StoreProductPriceListDto>.OK(items, totalRef.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreProductPrice Grid 查询失败");
                return GridResponseDto<StoreProductPriceListDto>.Error("查询失败");
            }
        }

        /// <summary>
        /// 批量更新分店商品价格
        /// </summary>
        /// <param name="dto">批量更新数据传输对象</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>API响应</returns>
        public async Task<ApiResponse<object>> BatchUpdateStoreRetailPricesAsync(
            BatchUpdateStoreRetailPriceDto dto,
            string updatedBy
        )
        {
            try
            {
                if (dto.ProductCodes == null || !dto.ProductCodes.Any())
                {
                    return ApiResponse<object>.Error("请选择要更新的分店和商品", "VALIDATION_ERROR");
                }

                // 验证分店编码
                if (string.IsNullOrEmpty(dto.StoreCode))
                {
                    return ApiResponse<object>.Error("请选择要更新的分店", "VALIDATION_ERROR");
                }

                var db = _context.Db;

                await db.Ado.BeginTranAsync();

                try
                {
                    var query = db.Updateable<StoreRetailPrice>()
                        .SetColumnsIF(dto.PurchasePrice.HasValue, x => x.PurchasePrice == dto.PurchasePrice.Value)
                        .SetColumnsIF(dto.StoreRetailPriceValue.HasValue, x => x.StoreRetailPriceValue == dto.StoreRetailPriceValue.Value)
                        .SetColumnsIF(dto.IsAutoPricing.HasValue, x => x.IsAutoPricing == dto.IsAutoPricing.Value)
                        .SetColumnsIF(dto.IsSpecialProduct.HasValue, x => x.IsSpecialProduct == dto.IsSpecialProduct.Value)
                        .SetColumnsIF(dto.DiscountRate.HasValue, x => x.DiscountRate == dto.DiscountRate.Value)
                        .SetColumns(x => x.UpdatedAt == DateTime.Now)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .Where(x => dto.ProductCodes.Contains(x.ProductCode) && x.IsDeleted == false && x.StoreCode == dto.StoreCode);

                    var affectedRows = await query.ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    return ApiResponse<object>.CreateSuccess($"成功更新 {affectedRows} 条记录");
                }
                catch (Exception)
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分店零售价失败");
                return ApiResponse<object>.Error("批量更新失败", "DATABASE_ERROR", ex.Message);
            }
        }

        /// <summary>
        /// 同步商品价格到其他分店
        /// </summary>
        /// <param name="dto">同步数据传输对象</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>API响应</returns>
        public async Task<ApiResponse<object>> SyncToOtherStoresAsync(
            SyncToOtherStoresDto dto,
            string updatedBy
        )
        {
            try
            {
                if (dto.ProductCodes == null || !dto.ProductCodes.Any())
                {
                    return ApiResponse<object>.Error("请选择要同步的商品", "VALIDATION_ERROR");
                }

                if (dto.TargetStoreCodes == null || !dto.TargetStoreCodes.Any())
                {
                    return ApiResponse<object>.Error("请至少选择一个目标分店", "VALIDATION_ERROR");
                }

                var hasAnySyncField = dto.SyncPurchasePrice || dto.SyncRetailPrice || 
                                      dto.SyncIsAutoPricing || dto.SyncIsSpecialProduct || dto.SyncDiscountRate;

                if (!hasAnySyncField)
                {
                    return ApiResponse<object>.Error("请至少选择一个要同步的字段", "VALIDATION_ERROR");
                }

                var db = _context.Db;

                await db.Ado.BeginTranAsync();

                try
                {
                    var sourcePrices = await db.Queryable<StoreRetailPrice>()
                        .With(SqlWith.NoLock)
                        .Where(x => x.StoreCode == dto.SourceStoreCode && 
                                   dto.ProductCodes.Contains(x.ProductCode) && 
                                   x.IsDeleted == false)
                        .ToListAsync();

                    if (!sourcePrices.Any())
                    {
                        await db.Ado.RollbackTranAsync();
                        return ApiResponse<object>.Error("未找到源分店的价格数据", "NOT_FOUND");
                    }

                    var sourcePriceMap = sourcePrices.ToDictionary(x => x.ProductCode);

                    var updateable = db.Updateable<StoreRetailPrice>()
                        .SetColumns(x => x.UpdatedAt == DateTime.Now)
                        .SetColumns(x => x.UpdatedBy == updatedBy);

                    if (dto.Mode == SyncModeConstants.Overwrite)
                    {
                        if (dto.SyncPurchasePrice)
                        {
                            updateable.SetColumns(x =>
                                x.PurchasePrice == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.PurchasePrice)
                            );
                        }

                        if (dto.SyncRetailPrice)
                        {
                            updateable.SetColumns(x =>
                                x.StoreRetailPriceValue == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.StoreRetailPriceValue)
                            );
                        }

                        if (dto.SyncIsAutoPricing)
                        {
                            updateable.SetColumns(x =>
                                x.IsAutoPricing == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.IsAutoPricing)
                            );
                        }

                        if (dto.SyncIsSpecialProduct)
                        {
                            updateable.SetColumns(x =>
                                x.IsSpecialProduct == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.IsSpecialProduct)
                            );
                        }

                        if (dto.SyncDiscountRate)
                        {
                            updateable.SetColumns(x =>
                                x.DiscountRate == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.DiscountRate)
                            );
                        }
                    }
                    else if (dto.Mode == SyncModeConstants.OnlyUpdateNull)
                    {
                        if (dto.SyncPurchasePrice)
                        {
                            updateable.SetColumns(x =>
                                x.PurchasePrice == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.PurchasePrice)
                            );
                            updateable.Where(x => x.PurchasePrice == null);
                        }

                        if (dto.SyncRetailPrice)
                        {
                            updateable.SetColumns(x =>
                                x.StoreRetailPriceValue == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.StoreRetailPriceValue)
                            );
                            updateable.Where(x => x.StoreRetailPriceValue == null);
                        }

                        if (dto.SyncIsAutoPricing)
                        {
                            updateable.SetColumns(x =>
                                x.IsAutoPricing == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.IsAutoPricing)
                            );
                            updateable.Where(x => x.IsAutoPricing == false);
                        }

                        if (dto.SyncIsSpecialProduct)
                        {
                            updateable.SetColumns(x =>
                                x.IsSpecialProduct == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.IsSpecialProduct)
                            );
                            updateable.Where(x => x.IsSpecialProduct == false);
                        }

                        if (dto.SyncDiscountRate)
                        {
                            updateable.SetColumns(x =>
                                x.DiscountRate == SqlFunc.Subqueryable<StoreRetailPrice>()
                                    .Where(s => s.StoreCode == dto.SourceStoreCode &&
                                               s.ProductCode == x.ProductCode &&
                                               s.IsDeleted == false)
                                    .Select(s => s.DiscountRate)
                            );
                            updateable.Where(x => x.DiscountRate == null);
                        }
                    }

                    var affectedRows = await updateable
                        .Where(x => dto.TargetStoreCodes.Contains(x.StoreCode) &&
                                   dto.ProductCodes.Contains(x.ProductCode) &&
                                   x.IsDeleted == false)
                        .ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    return ApiResponse<object>.CreateSuccess($"成功同步 {affectedRows} 条记录");
            }
            catch (Exception)
            {
                await db.Ado.RollbackTranAsync();
                throw;
            }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步到其他分店失败");
                return ApiResponse<object>.Error("同步失败", "DATABASE_ERROR", ex.Message);
            }
        }
    }
}
