using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// Product React专用服务实现
    /// 提供Product表的CRUD操作、分页查询、排序和过滤功能
    /// </summary>
    public class ProductReactService : IProductReactService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<ProductReactService> _logger;
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

        public ProductReactService(
            SqlSugarContext context,
            ILogger<ProductReactService> logger,
            Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor
        )
        {
            _db = context.Db;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// 分页查询商品列表（支持排序和过滤）
        /// </summary>
        public async Task<PagedListReactDto<ProductDto>> GetPagedListAsync(
            ProductReactFilterDto query
        )
        {
            try
            {
                var q = _db.Queryable<Product>();

                // 应用过滤条件
                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var keyword = query.Search.Trim().ToLower();
                    q = q.Where(p =>
                        (p.ProductName != null && p.ProductName.ToLower().Contains(keyword))
                        || (p.ProductCode != null && p.ProductCode.ToLower().Contains(keyword))
                        || (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.LocalSupplierCode))
                {
                    q = q.Where(p => p.LocalSupplierCode == query.LocalSupplierCode);
                }

                if (query.IsActive.HasValue)
                {
                    q = q.Where(p => p.IsActive == query.IsActive.Value);
                }

                if (query.IsSpecialProduct.HasValue)
                {
                    q = q.Where(p => p.IsSpecialProduct == query.IsSpecialProduct.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.WarehouseCategoryGUID))
                {
                    q = q.Where(p => p.WarehouseCategoryGUID == query.WarehouseCategoryGUID);
                }

                if (query.ProductType.HasValue)
                {
                    q = q.Where(p => p.ProductType == query.ProductType.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.UpdatedBy))
                {
                    var name = query.UpdatedBy.Trim().ToLower();
                    q = q.Where(p => p.UpdatedBy != null && p.UpdatedBy.ToLower().Contains(name));
                }

                if (query.MinPrice.HasValue)
                {
                    q = q.Where(p => p.RetailPrice >= query.MinPrice.Value);
                }

                if (query.MaxPrice.HasValue)
                {
                    q = q.Where(p => p.RetailPrice <= query.MaxPrice.Value);
                }

                // 应用排序
                if (!string.IsNullOrWhiteSpace(query.SortBy))
                {
                    var isDesc = query.SortOrder?.ToLower() == "desc";
                    var sortField = query.SortBy.ToLower();

                    switch (sortField)
                    {
                        case "productname":
                            q = isDesc
                                ? q.OrderBy(p => p.ProductName, OrderByType.Desc)
                                : q.OrderBy(p => p.ProductName, OrderByType.Asc);
                            break;
                        case "itemnumber":
                            q = isDesc
                                ? q.OrderBy(p => p.ItemNumber, OrderByType.Desc)
                                : q.OrderBy(p => p.ItemNumber, OrderByType.Asc);
                            break;
                        case "retailprice":
                            q = isDesc
                                ? q.OrderBy(p => p.RetailPrice, OrderByType.Desc)
                                : q.OrderBy(p => p.RetailPrice, OrderByType.Asc);
                            break;
                        case "purchaseprice":
                            q = isDesc
                                ? q.OrderBy(p => p.PurchasePrice, OrderByType.Desc)
                                : q.OrderBy(p => p.PurchasePrice, OrderByType.Asc);
                            break;
                        case "producttype":
                            q = isDesc
                                ? q.OrderBy(p => p.ProductType, OrderByType.Desc)
                                : q.OrderBy(p => p.ProductType, OrderByType.Asc);
                            break;
                        case "createdat":
                            q = isDesc
                                ? q.OrderBy(p => p.CreatedAt, OrderByType.Desc)
                                : q.OrderBy(p => p.CreatedAt, OrderByType.Asc);
                            break;
                        case "updatedat":
                            q = isDesc
                                ? q.OrderBy(p => p.UpdatedAt, OrderByType.Desc)
                                : q.OrderBy(p => p.UpdatedAt, OrderByType.Asc);
                            break;
                        default:
                            q = q.OrderBy(p => p.UpdatedAt, OrderByType.Desc);
                            break;
                    }
                }
                else
                {
                    q = q.OrderBy(p => p.UpdatedAt, OrderByType.Desc);
                }

                // 获取总数
                var total = await q.CountAsync();

                // 分页
                var items = await q.Skip((query.PageNumber - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .Select(p => new ProductDto
                    {
                        ProductCode = p.ProductCode ?? string.Empty,
                        ProductCategoryGUID = p.ProductCategoryGUID ?? string.Empty,
                        LocalSupplierCode = p.LocalSupplierCode,
                        ItemNumber = p.ItemNumber,
                        Barcode = p.Barcode,
                        ProductName = p.ProductName,
                        ProductType = p.ProductType,
                        MiddlePackageQuantity = p.MiddlePackageQuantity,
                        PurchasePrice = p.PurchasePrice,
                        RetailPrice = p.RetailPrice,
                        IsAutoPricing = p.IsAutoPricing,
                        ProductImage = p.ProductImage,
                        IsActive = p.IsActive,
                        IsSpecialProduct = p.IsSpecialProduct,
                        WarehouseCategoryGUID = p.WarehouseCategoryGUID,
                        UpdatedAt = p.UpdatedAt,
                        UpdatedBy = p.UpdatedBy,
                    })
                    .ToListAsync();

                return new PagedListReactDto<ProductDto>
                {
                    Items = items,
                    Total = total,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页查询商品失败");
                throw;
            }
        }

        /// <summary>
        /// 根据ProductCode获取商品详情
        /// </summary>
        public async Task<ApiResponse<ProductDto>> GetByIdAsync(string productCode)
        {
            try
            {
                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .FirstAsync();

                if (product == null)
                {
                    return new ApiResponse<ProductDto> { Success = false, Message = "商品不存在" };
                }

                var dto = new ProductDto
                {
                    ProductCode = product.ProductCode ?? string.Empty,
                    ProductCategoryGUID = product.ProductCategoryGUID ?? string.Empty,
                    LocalSupplierCode = product.LocalSupplierCode,
                    ItemNumber = product.ItemNumber,
                    Barcode = product.Barcode,
                    ProductName = product.ProductName,
                    ProductType = product.ProductType,
                    MiddlePackageQuantity = product.MiddlePackageQuantity,
                    PurchasePrice = product.PurchasePrice,
                    RetailPrice = product.RetailPrice,
                    IsAutoPricing = product.IsAutoPricing,
                    ProductImage = product.ProductImage,
                    IsActive = product.IsActive,
                    IsSpecialProduct = product.IsSpecialProduct,
                    WarehouseCategoryGUID = product.WarehouseCategoryGUID,
                    UpdatedAt = product.UpdatedAt,
                    UpdatedBy = product.UpdatedBy,
                };

                return new ApiResponse<ProductDto> { Success = true, Data = dto };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
                return new ApiResponse<ProductDto>
                {
                    Success = false,
                    Message = $"获取商品详情失败: {ex.Message}",
                };
            }
        }

        /// <summary>
        /// 创建商品
        /// </summary>
        public async Task<ApiResponse<ProductDto>> CreateAsync(CreateProductDto dto)
        {
            try
            {
                var product = new Product
                {
                    ProductCode = dto.ProductCode,
                    ProductCategoryGUID = dto.ProductCategoryGUID,
                    LocalSupplierCode = dto.LocalSupplierCode,
                    ItemNumber = dto.ItemNumber,
                    Barcode = dto.Barcode,
                    ProductName = dto.ProductName,
                    ProductType = dto.ProductType,
                    MiddlePackageQuantity = dto.MiddlePackageQuantity,
                    PurchasePrice = dto.PurchasePrice,
                    RetailPrice = dto.RetailPrice,
                    IsAutoPricing = dto.IsAutoPricing,
                    ProductImage = dto.ProductImage,
                    IsActive = dto.IsActive,
                    IsSpecialProduct = dto.IsSpecialProduct,
                    WarehouseCategoryGUID = dto.WarehouseCategoryGUID,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await _db.Insertable(product).ExecuteCommandAsync();

                var resultDto = await GetByIdAsync(product.ProductCode);
                return resultDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                return new ApiResponse<ProductDto>
                {
                    Success = false,
                    Message = $"创建商品失败: {ex.Message}",
                };
            }
        }

        /// <summary>
        /// 更新商品
        /// </summary>
        public async Task<ApiResponse<ProductDto>> UpdateAsync(
            string productCode,
            UpdateProductDto dto
        )
        {
            try
            {
                var product = await _db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .FirstAsync();

                if (product == null)
                {
                    return new ApiResponse<ProductDto> { Success = false, Message = "商品不存在" };
                }

                // 更新字段
                product.ProductCategoryGUID = dto.ProductCategoryGUID;
                product.LocalSupplierCode = dto.LocalSupplierCode;
                product.ItemNumber = dto.ItemNumber;
                product.Barcode = dto.Barcode;
                product.ProductName = dto.ProductName;
                product.ProductType = dto.ProductType;
                product.MiddlePackageQuantity = dto.MiddlePackageQuantity;
                product.PurchasePrice = dto.PurchasePrice;
                product.RetailPrice = dto.RetailPrice;
                product.IsAutoPricing = dto.IsAutoPricing;
                product.ProductImage = dto.ProductImage;
                product.IsActive = dto.IsActive;
                product.IsSpecialProduct = dto.IsSpecialProduct;
                product.WarehouseCategoryGUID = dto.WarehouseCategoryGUID;
                product.UpdatedAt = DateTime.Now;
                var currentUser =
                    _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                product.UpdatedBy = currentUser;

                await _db.Updateable(product).ExecuteCommandAsync();

                return await GetByIdAsync(productCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败: {ProductCode}", productCode);
                return new ApiResponse<ProductDto>
                {
                    Success = false,
                    Message = $"更新商品失败: {ex.Message}",
                };
            }
        }

        /// <summary>
        /// 删除商品
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteAsync(string productCode)
        {
            try
            {
                var result = await _db.Deleteable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .ExecuteCommandAsync();

                return new ApiResponse<bool>
                {
                    Success = result > 0,
                    Data = result > 0,
                    Message = result > 0 ? "删除成功" : "商品不存在",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败: {ProductCode}", productCode);
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"删除商品失败: {ex.Message}",
                };
            }
        }

        /// <summary>
        /// 批量更新商品（使用事务）
        /// </summary>
        public async Task<ApiResponse<BatchOperationReactResult>> BatchUpdateAsync(
            List<BatchUpdateProductReactDto> items
        )
        {
            var result = new BatchOperationReactResult();

            try
            {
                // 使用事务
                await _db.Ado.UseTranAsync(async () =>
                {
                    foreach (var item in items)
                    {
                        try
                        {
                            var product = await _db.Queryable<Product>()
                                .Where(p => p.ProductCode == item.ProductCode)
                                .FirstAsync();

                            if (product == null)
                            {
                                result.Errors.Add($"商品不存在: {item.ProductCode}");
                                result.FailedCount++;
                                continue;
                            }

                            // 只更新提供的字段
                            if (item.ProductName != null)
                                product.ProductName = item.ProductName;
                            if (item.EnglishName != null)
                                product.EnglishName = item.EnglishName;
                            if (item.RetailPrice.HasValue)
                                product.RetailPrice = item.RetailPrice;
                            if (item.PurchasePrice.HasValue)
                                product.PurchasePrice = item.PurchasePrice;
                            if (item.IsActive.HasValue)
                                product.IsActive = item.IsActive.Value;
                            if (item.MiddlePackageQuantity.HasValue)
                                product.MiddlePackageQuantity = item.MiddlePackageQuantity;

                            product.UpdatedAt = DateTime.Now;
                            var currentUser =
                                _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                            product.UpdatedBy = currentUser;

                            await _db.Updateable(product).ExecuteCommandAsync();
                            result.SuccessCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"{item.ProductCode}: {ex.Message}");
                            result.FailedCount++;
                            _logger.LogError(
                                ex,
                                "批量更新单个商品失败: {ProductCode}",
                                item.ProductCode
                            );
                        }
                    }
                });

                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = true,
                    Data = result,
                    Message =
                        $"批量更新完成: 成功{result.SuccessCount}条，失败{result.FailedCount}条",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品事务失败");
                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = false,
                    Message = $"批量更新失败: {ex.Message}",
                    Data = result,
                };
            }
        }

        /// <summary>
        /// 批量删除商品（使用事务）
        /// </summary>
        public async Task<ApiResponse<BatchOperationReactResult>> BatchDeleteAsync(
            List<string> productCodes
        )
        {
            var result = new BatchOperationReactResult();

            try
            {
                // 使用事务
                await _db.Ado.UseTranAsync(async () =>
                {
                    foreach (var code in productCodes)
                    {
                        try
                        {
                            var deleteResult = await _db.Deleteable<Product>()
                                .Where(p => p.ProductCode == code)
                                .ExecuteCommandAsync();

                            if (deleteResult > 0)
                            {
                                result.SuccessCount++;
                            }
                            else
                            {
                                result.Errors.Add($"商品不存在: {code}");
                                result.FailedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"{code}: {ex.Message}");
                            result.FailedCount++;
                            _logger.LogError(ex, "批量删除单个商品失败: {ProductCode}", code);
                        }
                    }
                });

                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = true,
                    Data = result,
                    Message =
                        $"批量删除完成: 成功{result.SuccessCount}条，失败{result.FailedCount}条",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除商品事务失败");
                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = false,
                    Message = $"批量删除失败: {ex.Message}",
                    Data = result,
                };
            }
        }

        /// <summary>
        /// 高级过滤查询商品列表（支持商品信息表与分店价格表的组合过滤）
        /// 性能优化:使用 INNER JOIN + 索引 + 分页,确保大数据量下的响应速度
        /// </summary>
        public async Task<PagedProductPriceListDto> GetPriceFilteredPagedListAsync(
            ProductPriceFilterDto filter
        )
        {
            try
            {
                var sw = Stopwatch.StartNew();

                // 与商品主表 INNER JOIN
                var query = _db.Queryable<Product>()
                    .InnerJoin<StoreRetailPrice>((p, sp) => p.ProductCode == sp.ProductCode)
                    .InnerJoin<Store>((p, sp, s) => sp.StoreCode == s.StoreCode)
                    .LeftJoin<HBLocalSupplier>(
                        (p, sp, s, ls) => sp.SupplierCode == ls.LocalSupplierCode
                    );

                var filterStart = sw.ElapsedMilliseconds;

                // 应用分店过滤条件(优化:先过滤StoreCode)
                if (filter.StoreCodes != null && filter.StoreCodes.Any())
                {
                    query = query.Where(
                        (p, sp, s, ls) => filter.StoreCodes.Contains(sp.StoreCode)
                    );
                }

                // 应用商品主表过滤条件
                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    var keyword = filter.Search.Trim();
                    query = query.Where(
                        (p, sp, s, ls) =>
                            (p.ProductName != null && p.ProductName.Contains(keyword))
                            || (p.ProductCode != null && p.ProductCode.Contains(keyword))
                            || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                            || (p.Barcode != null && p.Barcode.Contains(keyword))
                    );
                }

                if (!string.IsNullOrWhiteSpace(filter.LocalSupplierCode))
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.LocalSupplierCode == filter.LocalSupplierCode
                    );
                }

                if (filter.IsActive.HasValue)
                {
                    query = query.Where((p, sp, s, ls) => p.IsActive == filter.IsActive.Value);
                }

                if (filter.IsSpecialProduct.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.IsSpecialProduct == filter.IsSpecialProduct.Value
                    );
                }

                if (!string.IsNullOrWhiteSpace(filter.WarehouseCategoryGUID))
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.WarehouseCategoryGUID == filter.WarehouseCategoryGUID
                    );
                }

                if (filter.ProductType.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.ProductType == filter.ProductType.Value
                    );
                }

                if (!string.IsNullOrWhiteSpace(filter.UpdatedBy))
                {
                    var name = filter.UpdatedBy.Trim();
                    query = query.Where(
                        (p, sp, s, ls) =>
                            p.UpdatedBy != null && p.UpdatedBy.Contains(name)
                    );
                }

                // 商品主表价格区间过滤 (使用 BETWEEN 语法,支持闭区间)
                if (filter.ProductPurchasePriceMin.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.PurchasePrice >= filter.ProductPurchasePriceMin.Value
                    );
                }

                if (filter.ProductPurchasePriceMax.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.PurchasePrice <= filter.ProductPurchasePriceMax.Value
                    );
                }

                if (filter.ProductRetailPriceMin.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.RetailPrice >= filter.ProductRetailPriceMin.Value
                    );
                }

                if (filter.ProductRetailPriceMax.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => p.RetailPrice <= filter.ProductRetailPriceMax.Value
                    );
                }

                // 分店价格表过滤条件
                if (filter.StorePurchasePriceMin.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => sp.PurchasePrice >= filter.StorePurchasePriceMin.Value
                    );
                }

                if (filter.StorePurchasePriceMax.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => sp.PurchasePrice <= filter.StorePurchasePriceMax.Value
                    );
                }

                if (filter.StoreRetailPriceMin.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) =>
                            sp.StoreRetailPriceValue >= filter.StoreRetailPriceMin.Value
                    );
                }

                if (filter.StoreRetailPriceMax.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) =>
                            sp.StoreRetailPriceValue <= filter.StoreRetailPriceMax.Value
                    );
                }

                if (filter.StoreDiscountRateMin.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => sp.DiscountRate >= filter.StoreDiscountRateMin.Value
                    );
                }

                if (filter.StoreDiscountRateMax.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => sp.DiscountRate <= filter.StoreDiscountRateMax.Value
                    );
                }

                if (filter.StoreIsActive.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => sp.IsActive == filter.StoreIsActive.Value
                    );
                }

                if (filter.StoreIsAutoPricing.HasValue)
                {
                    query = query.Where(
                        (p, sp, s, ls) => sp.IsAutoPricing == filter.StoreIsAutoPricing.Value
                    );
                }

                _logger.LogDebug(
                    "过滤条件应用耗时: {ElapsedMs}ms",
                    sw.ElapsedMilliseconds - filterStart
                );

                // 应用排序
                if (!string.IsNullOrWhiteSpace(filter.SortBy))
                {
                    var isDesc = filter.SortOrder?.ToLower() == "desc";
                    var sortField = filter.SortBy.ToLower();

                    switch (sortField)
                    {
                        case "productcode":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.ProductCode, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.ProductCode, OrderByType.Asc);
                            break;
                        case "productname":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.ProductName, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.ProductName, OrderByType.Asc);
                            break;
                        case "itemnumber":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.ItemNumber, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.ItemNumber, OrderByType.Asc);
                            break;
                        case "productpurchaseprice":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.PurchasePrice, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.PurchasePrice, OrderByType.Asc);
                            break;
                        case "productretailprice":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.RetailPrice, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.RetailPrice, OrderByType.Asc);
                            break;
                        case "storepurchaseprice":
                            query = isDesc
                                ? query.OrderBy(
                                    (p, sp, s, ls) => sp.PurchasePrice,
                                    OrderByType.Desc
                                )
                                : query.OrderBy(
                                    (p, sp, s, ls) => sp.PurchasePrice,
                                    OrderByType.Asc
                                );
                            break;
                        case "storeretailprice":
                            query = isDesc
                                ? query.OrderBy(
                                    (p, sp, s, ls) => sp.StoreRetailPriceValue,
                                    OrderByType.Desc
                                )
                                : query.OrderBy(
                                    (p, sp, s, ls) => sp.StoreRetailPriceValue,
                                    OrderByType.Asc
                                );
                            break;
                        case "storediscountrate":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => sp.DiscountRate, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => sp.DiscountRate, OrderByType.Asc);
                            break;
                        case "createdat":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.CreatedAt, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.CreatedAt, OrderByType.Asc);
                            break;
                        case "updatedat":
                            query = isDesc
                                ? query.OrderBy((p, sp, s, ls) => p.UpdatedAt, OrderByType.Desc)
                                : query.OrderBy((p, sp, s, ls) => p.UpdatedAt, OrderByType.Asc);
                            break;
                        default:
                            query = query.OrderBy((p, sp, s, ls) => p.UpdatedAt, OrderByType.Desc);
                            break;
                    }
                }
                else
                {
                    query = query.OrderBy((p, sp, s, ls) => p.UpdatedAt, OrderByType.Desc);
                }

                var countStart = sw.ElapsedMilliseconds;
                var totalQuery = query.Clone();
                var total = await totalQuery.CountAsync();
                _logger.LogDebug(
                    "Count查询耗时: {ElapsedMs}ms, 总数: {Total}",
                    sw.ElapsedMilliseconds - countStart,
                    total
                );

                var selectStart = sw.ElapsedMilliseconds;
                var items = await query
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .Select<ProductPriceListItemDto>(
                        "p.UUID,p.ProductCode,p.ItemNumber,p.Barcode,p.ProductName,p.EnglishName,p.LocalSupplierCode,p.ProductType,p.MiddlePackageQuantity,p.PurchasePrice as ProductPurchasePrice,p.RetailPrice as ProductRetailPrice,p.IsAutoPricing,p.IsSpecialProduct,p.IsActive,p.ProductImage,p.WarehouseCategoryGUID,p.UpdatedBy,p.UpdatedAt,sp.StoreCode,s.StoreName,sp.StoreProductCode,sp.SupplierCode,ls.Name as SupplierName,sp.PurchasePrice as StorePurchasePrice,sp.StoreRetailPriceValue as StoreRetailPrice,sp.DiscountRate as StoreDiscountRate,sp.IsActive as StoreIsActive,sp.IsAutoPricing as StoreIsAutoPricing"
                    )
                    .ToListAsync();

                _logger.LogDebug(
                    "Select查询耗时: {ElapsedMs}ms, 返回: {Count}",
                    sw.ElapsedMilliseconds - selectStart,
                    items.Count
                );

                var elapsedMs = sw.ElapsedMilliseconds;
                _logger.LogInformation(
                    "高级过滤查询完成, 耗时: {ElapsedMs}ms, 总数: {Total}, 返回: {Count}",
                    elapsedMs,
                    total,
                    items.Count
                );

                // 性能警告
                if (elapsedMs > 500)
                {
                    _logger.LogWarning(
                        "高级过滤查询性能警告: 耗时 {ElapsedMs}ms 超过500ms阈值",
                        elapsedMs
                    );
                }

                return new PagedProductPriceListDto
                {
                    Items = items,
                    Total = total,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "高级过滤查询商品失败");
                throw;
            }
        }
    }
}
