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
                            UpdatedAt = p.UpdatedAt,
                            UpdatedBy = p.UpdatedBy,
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

                    foreach (var targetStoreCode in dto.TargetStoreCodes)
                    {
                        var targetRecords = await db.Queryable<StoreRetailPrice>()
                            .With(SqlWith.NoLock)
                            .Where(x => x.StoreCode == targetStoreCode && 
                                       dto.ProductCodes.Contains(x.ProductCode) && 
                                       x.IsDeleted == false)
                            .ToListAsync();

                        foreach (var targetRecord in targetRecords)
                        {
                            if (!sourcePriceMap.TryGetValue(targetRecord.ProductCode, out var sourcePrice))
                            {
                                continue;
                            }

                            var updateable = db.Updateable<StoreRetailPrice>()
                                .SetColumns(x => x.UpdatedAt == DateTime.Now)
                                .SetColumns(x => x.UpdatedBy == updatedBy);

                            if (dto.SyncPurchasePrice && dto.Mode == SyncModeConstants.Overwrite)
                            {
                                updateable.SetColumns(x => x.PurchasePrice == sourcePrice.PurchasePrice);
                            }
                            else if (dto.SyncPurchasePrice && dto.Mode == SyncModeConstants.OnlyUpdateNull && !targetRecord.PurchasePrice.HasValue)
                            {
                                updateable.SetColumns(x => x.PurchasePrice == sourcePrice.PurchasePrice);
                            }

                            if (dto.SyncRetailPrice && dto.Mode == SyncModeConstants.Overwrite)
                            {
                                updateable.SetColumns(x => x.StoreRetailPriceValue == sourcePrice.StoreRetailPriceValue);
                            }
                            else if (dto.SyncRetailPrice && dto.Mode == SyncModeConstants.OnlyUpdateNull && !targetRecord.StoreRetailPriceValue.HasValue)
                            {
                                updateable.SetColumns(x => x.StoreRetailPriceValue == sourcePrice.StoreRetailPriceValue);
                            }

                            if (dto.SyncIsAutoPricing && dto.Mode == SyncModeConstants.Overwrite)
                            {
                                updateable.SetColumns(x => x.IsAutoPricing == sourcePrice.IsAutoPricing);
                            }
                            else if (dto.SyncIsAutoPricing && dto.Mode == SyncModeConstants.OnlyUpdateNull && !targetRecord.IsAutoPricing)
                            {
                                updateable.SetColumns(x => x.IsAutoPricing == sourcePrice.IsAutoPricing);
                            }

                            if (dto.SyncIsSpecialProduct && dto.Mode == SyncModeConstants.Overwrite)
                            {
                                updateable.SetColumns(x => x.IsSpecialProduct == sourcePrice.IsSpecialProduct);
                            }
                            else if (dto.SyncIsSpecialProduct && dto.Mode == SyncModeConstants.OnlyUpdateNull && !targetRecord.IsSpecialProduct)
                            {
                                updateable.SetColumns(x => x.IsSpecialProduct == sourcePrice.IsSpecialProduct);
                            }

                            if (dto.SyncDiscountRate && dto.Mode == SyncModeConstants.Overwrite)
                            {
                                updateable.SetColumns(x => x.DiscountRate == sourcePrice.DiscountRate);
                            }
                            else if (dto.SyncDiscountRate && dto.Mode == SyncModeConstants.OnlyUpdateNull && !targetRecord.DiscountRate.HasValue)
                            {
                                updateable.SetColumns(x => x.DiscountRate == sourcePrice.DiscountRate);
                            }

                            updateable.Where(x => x.UUID == targetRecord.UUID);
                            await updateable.ExecuteCommandAsync();
                        }
                }

                await db.Ado.CommitTranAsync();

                var totalSynced = dto.ProductCodes.Count * dto.TargetStoreCodes.Count;
                return ApiResponse<object>.CreateSuccess($"成功同步 {totalSynced} 条记录");
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
