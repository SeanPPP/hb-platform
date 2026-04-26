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

        #region 筛选辅助方法

        /// <summary>
        /// 应用文本筛选到查询
        /// </summary>
        private void ApplyTextFilter(
            ref ISugarQueryable<Product> query,
            string? value,
            TextFilterType filterType,
            string fieldName
        )
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var keyword = value.Trim().ToLower();
            var paramName = $"p_{fieldName}";

            switch (filterType)
            {
                case TextFilterType.equals:
                    query = query.Where(
                        $"LOWER({fieldName}) = @{paramName}",
                        new Dictionary<string, object> { { paramName, keyword } }
                    );
                    break;
                case TextFilterType.notEquals:
                    query = query.Where(
                        $"LOWER({fieldName}) != @{paramName}",
                        new Dictionary<string, object> { { paramName, keyword } }
                    );
                    break;
                case TextFilterType.startsWith:
                    query = query.Where(
                        $"LOWER({fieldName}) LIKE @{paramName}",
                        new Dictionary<string, object> { { paramName, keyword + "%" } }
                    );
                    break;
                case TextFilterType.endsWith:
                    query = query.Where(
                        $"LOWER({fieldName}) LIKE @{paramName}",
                        new Dictionary<string, object> { { paramName, "%" + keyword } }
                    );
                    break;
                case TextFilterType.notContains:
                    query = query.Where(
                        $"LOWER({fieldName}) NOT LIKE @{paramName}",
                        new Dictionary<string, object> { { paramName, "%" + keyword + "%" } }
                    );
                    break;
                case TextFilterType.contains:
                default:
                    query = query.Where(
                        $"LOWER({fieldName}) LIKE @{paramName}",
                        new Dictionary<string, object> { { paramName, "%" + keyword + "%" } }
                    );
                    break;
            }
        }

        /// <summary>
        /// 应用数字范围筛选到查询（decimal类型）
        /// </summary>
        private void ApplyNumberFilter(
            ref ISugarQueryable<Product> query,
            decimal? minValue,
            decimal? maxValue,
            NumberFilterType filterType,
            string fieldName
        )
        {
            if (!minValue.HasValue && !maxValue.HasValue)
                return;

            switch (filterType)
            {
                case NumberFilterType.equals when minValue.HasValue:
                    query = query.Where($"{fieldName} = @0", minValue.Value);
                    break;
                case NumberFilterType.notEquals when minValue.HasValue:
                    query = query.Where($"{fieldName} != @0", minValue.Value);
                    break;
                case NumberFilterType.greaterThan when minValue.HasValue:
                    query = query.Where($"{fieldName} > @0", minValue.Value);
                    break;
                case NumberFilterType.greaterThanOrEqual when minValue.HasValue:
                    query = query.Where($"{fieldName} >= @0", minValue.Value);
                    break;
                case NumberFilterType.lessThan when minValue.HasValue:
                    query = query.Where($"{fieldName} < @0", minValue.Value);
                    break;
                case NumberFilterType.lessThanOrEqual when minValue.HasValue:
                    query = query.Where($"{fieldName} <= @0", minValue.Value);
                    break;
                case NumberFilterType.between:
                    if (minValue.HasValue)
                        query = query.Where($"{fieldName} >= @0", minValue.Value);
                    if (maxValue.HasValue)
                        query = query.Where($"{fieldName} <= @0", maxValue.Value);
                    break;
            }
        }

        /// <summary>
        /// 应用数字范围筛选到查询（int类型）
        /// </summary>
        private void ApplyNumberFilter(
            ref ISugarQueryable<Product> query,
            int? minValue,
            int? maxValue,
            NumberFilterType filterType,
            string fieldName
        )
        {
            if (!minValue.HasValue && !maxValue.HasValue)
                return;

            switch (filterType)
            {
                case NumberFilterType.equals when minValue.HasValue:
                    query = query.Where($"{fieldName} = @0", minValue.Value);
                    break;
                case NumberFilterType.notEquals when minValue.HasValue:
                    query = query.Where($"{fieldName} != @0", minValue.Value);
                    break;
                case NumberFilterType.greaterThan when minValue.HasValue:
                    query = query.Where($"{fieldName} > @0", minValue.Value);
                    break;
                case NumberFilterType.greaterThanOrEqual when minValue.HasValue:
                    query = query.Where($"{fieldName} >= @0", minValue.Value);
                    break;
                case NumberFilterType.lessThan when minValue.HasValue:
                    query = query.Where($"{fieldName} < @0", minValue.Value);
                    break;
                case NumberFilterType.lessThanOrEqual when minValue.HasValue:
                    query = query.Where($"{fieldName} <= @0", minValue.Value);
                    break;
                case NumberFilterType.between:
                    if (minValue.HasValue)
                        query = query.Where($"{fieldName} >= @0", minValue.Value);
                    if (maxValue.HasValue)
                        query = query.Where($"{fieldName} <= @0", maxValue.Value);
                    break;
            }
        }

        #endregion

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

                if (query.ProductCategoryGUIDs != null && query.ProductCategoryGUIDs.Count > 0)
                {
                    q = q.Where(p => query.ProductCategoryGUIDs.Contains(p.ProductCategoryGUID));
                }

                if (query.ProductType.HasValue)
                {
                    q = q.Where(p => p.ProductType == query.ProductType.Value);
                }

                #region 文本字段高级筛选

                ApplyTextFilter(ref q, query.ItemNumber, query.ItemNumberFilterType, "ItemNumber");
                ApplyTextFilter(ref q, query.Barcode, query.BarcodeFilterType, "Barcode");
                ApplyTextFilter(
                    ref q,
                    query.ProductName,
                    query.ProductNameFilterType,
                    "ProductName"
                );
                ApplyTextFilter(ref q, query.UpdatedBy, query.UpdatedByFilterType, "UpdatedBy");

                #endregion

                #region 数字字段高级筛选

                ApplyNumberFilter(
                    ref q,
                    query.PurchasePriceMin,
                    query.PurchasePriceMax,
                    query.PurchasePriceFilterType,
                    "PurchasePrice"
                );
                ApplyNumberFilter(
                    ref q,
                    query.RetailPriceMin,
                    query.RetailPriceMax,
                    query.RetailPriceFilterType,
                    "RetailPrice"
                );
                ApplyNumberFilter(
                    ref q,
                    query.MiddlePackageQuantityMin,
                    query.MiddlePackageQuantityMax,
                    query.MiddlePackageQuantityFilterType,
                    "MiddlePackageQuantity"
                );

                #endregion

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
        /// 创建商品（事务内插入 Product，并为全部启用分店默认创建 StoreRetailPrice）
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

                await _db.Ado.UseTranAsync(async () =>
                {
                    await _db.Insertable(product).ExecuteCommandAsync();

                    var storeCodes = await _db.Queryable<Store>()
                        .Where(s => s.IsActive == true && s.IsDeleted == false)
                        .Select(s => s.StoreCode)
                        .ToListAsync();

                    var supplierCode = !string.IsNullOrWhiteSpace(product.LocalSupplierCode)
                        ? product.LocalSupplierCode
                        : "200";
                    var now = DateTime.Now;
                    var currentUser =
                        _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

                    var storePriceList = new List<StoreRetailPrice>();
                    foreach (var storeCode in storeCodes ?? Enumerable.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(storeCode))
                            continue;

                        storePriceList.Add(
                            new StoreRetailPrice
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = storeCode,
                                ProductCode = product.ProductCode,
                                StoreProductCode =
                                    storeCode + (product.ProductCode ?? string.Empty),
                                SupplierCode = supplierCode,
                                PurchasePrice = product.PurchasePrice,
                                StoreRetailPriceValue = product.RetailPrice,
                                DiscountRate = null,
                                IsActive = product.IsActive,
                                IsAutoPricing = product.IsAutoPricing,
                                IsSpecialProduct = product.IsSpecialProduct,
                                CreatedAt = now,
                                UpdatedAt = now,
                                CreatedBy = currentUser,
                                UpdatedBy = currentUser,
                                IsDeleted = false,
                            }
                        );
                    }

                    if (storePriceList.Count > 0)
                        await _db.Insertable(storePriceList).ExecuteCommandAsync();
                });

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
        /// 更新商品（改码时级联更新 StoreMultiCodeProduct、StoreRetailPrice、ProductSetCode 的 ProductCode）
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

                var newProductCode = dto.ProductCode?.Trim();
                var isCodeChange =
                    !string.IsNullOrEmpty(newProductCode) && newProductCode != productCode;

                if (isCodeChange)
                {
                    await _db.Ado.UseTranAsync(async () =>
                    {
                        await UpdateProductAndCascadeProductCodeAsync(
                            productCode,
                            product,
                            dto,
                            newProductCode!
                        );
                    });
                    return await GetByIdAsync(newProductCode!);
                }

                // 未改码：仅更新主表
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

                await _db.Updateable<StoreRetailPrice>()
                    .SetColumns(srp => srp.IsAutoPricing == product.IsAutoPricing)
                    .Where(srp => srp.ProductCode == product.ProductCode)
                    .ExecuteCommandAsync();

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

        private async Task UpdateProductAndCascadeProductCodeAsync(
            string oldProductCode,
            Product product,
            UpdateProductDto dto,
            string newProductCode
        )
        {
            product.ProductCode = newProductCode;
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
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
            product.UpdatedBy = currentUser;

            await _db.Updateable(product).ExecuteCommandAsync();

            await _db.Updateable<StoreRetailPrice>()
                .SetColumns(srp => srp.IsAutoPricing == product.IsAutoPricing)
                .Where(srp => srp.ProductCode == newProductCode)
                .ExecuteCommandAsync();

            await _db.Updateable<StoreMultiCodeProduct>()
                .SetColumns(m => m.ProductCode == newProductCode)
                .Where(m => m.ProductCode == oldProductCode)
                .ExecuteCommandAsync();

            await _db.Updateable<StoreRetailPrice>()
                .SetColumns(s => s.ProductCode == newProductCode)
                .Where(s => s.ProductCode == oldProductCode)
                .ExecuteCommandAsync();

            await _db.Updateable<ProductSetCode>()
                .SetColumns(psc => psc.ProductCode == newProductCode)
                .Where(psc => psc.ProductCode == oldProductCode)
                .ExecuteCommandAsync();
        }

        /// <summary>
        /// 删除商品（支持软删除和物理删除）
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="isSoftDelete">true=软删除，false=物理删除</param>
        public async Task<ApiResponse<bool>> DeleteAsync(string productCode, bool isSoftDelete = true)
        {
            try
            {
                var productDeleted = 0;
                await _db.Ado.UseTranAsync(async () =>
                {
                    productDeleted = await CascadeDeleteProductAsync(productCode, isSoftDelete);
                });

                var deleteType = isSoftDelete ? "软删除" : "物理删除";
                return new ApiResponse<bool>
                {
                    Success = productDeleted > 0,
                    Data = productDeleted > 0,
                    Message = productDeleted > 0 ? $"{deleteType}成功" : "商品不存在",
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
        /// 级联删除商品及其关联表（ProductSetCode、StoreMultiCodeProduct、StoreRetailPrice、Product）
        /// 支持软删除（标记IsDeleted）和物理删除
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="isSoftDelete">true=软删除，false=物理删除</param>
        /// <returns>删除的 Product 行数（0 或 1）</returns>
        private async Task<int> CascadeDeleteProductAsync(string productCode, bool isSoftDelete)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return 0;

            var now = DateTime.Now;
            var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";

            if (isSoftDelete)
            {
                // 软删除：标记 IsDeleted = true
                
                // 1. 获取关联的多码商品编码
                var multiCodeProductCodes = await _db.Queryable<StoreMultiCodeProduct>()
                    .Where(m => m.ProductCode == productCode)
                    .Select(m => m.MultiCodeProductCode)
                    .ToListAsync();

                // 2. 软删除 ProductSetCode（主商品编码）
                await _db.Updateable<ProductSetCode>()
                    .SetColumns(psc => psc.IsDeleted == true)
                    .Where(psc => psc.ProductCode == productCode)
                    .ExecuteCommandAsync();

                // 3. 软删除 ProductSetCode（多码商品编码）
                if (multiCodeProductCodes != null && multiCodeProductCodes.Any())
                {
                    await _db.Updateable<ProductSetCode>()
                        .SetColumns(psc => psc.IsDeleted == true)
                        .Where(psc => psc.SetProductCode != null && multiCodeProductCodes.Contains(psc.SetProductCode))
                        .ExecuteCommandAsync();
                }

                // 4. 软删除 StoreMultiCodeProduct
                await _db.Updateable<StoreMultiCodeProduct>()
                    .SetColumns(m => m.IsDeleted == true)
                    .Where(m => m.ProductCode == productCode)
                    .ExecuteCommandAsync();

                // 5. 软删除 StoreRetailPrice
                await _db.Updateable<StoreRetailPrice>()
                    .SetColumns(s => s.IsDeleted == true)
                    .Where(s => s.ProductCode == productCode)
                    .ExecuteCommandAsync();

                // 6. 软删除 Product
                var productRows = await _db.Updateable<Product>()
                    .SetColumns(p => p.IsDeleted == true)
                    .Where(p => p.ProductCode == productCode)
                    .ExecuteCommandAsync();

                return productRows;
            }
            else
            {
                // 物理删除：彻底从数据库删除

                // 1. ProductSetCode：主商品编码为此商品 或 SetProductCode 属于该商品的多码
                await _db.Deleteable<ProductSetCode>()
                    .Where(psc => psc.ProductCode == productCode)
                    .ExecuteCommandAsync();

                var multiCodeProductCodes = await _db.Queryable<StoreMultiCodeProduct>()
                    .Where(m => m.ProductCode == productCode)
                    .Select(m => m.MultiCodeProductCode)
                    .ToListAsync();
                if (multiCodeProductCodes != null && multiCodeProductCodes.Any())
                {
                    await _db.Deleteable<ProductSetCode>()
                        .Where(psc =>
                            psc.SetProductCode != null
                            && multiCodeProductCodes.Contains(psc.SetProductCode)
                        )
                        .ExecuteCommandAsync();
                }

                // 2. StoreMultiCodeProduct
                await _db.Deleteable<StoreMultiCodeProduct>()
                    .Where(m => m.ProductCode == productCode)
                    .ExecuteCommandAsync();

                // 3. StoreRetailPrice
                await _db.Deleteable<StoreRetailPrice>()
                    .Where(s => s.ProductCode == productCode)
                    .ExecuteCommandAsync();

                // 4. Product
                var productRows = await _db.Deleteable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .ExecuteCommandAsync();

                return productRows;
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
                            if (item.IsAutoPricing.HasValue)
                                product.IsAutoPricing = item.IsAutoPricing.Value;
                            if (item.ProductCategoryGUID != null)
                                product.ProductCategoryGUID = item.ProductCategoryGUID;
                            if (item.LocalSupplierCode != null)
                                product.LocalSupplierCode = item.LocalSupplierCode;

                            product.UpdatedAt = DateTime.Now;
                            var currentUser =
                                _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
                            product.UpdatedBy = currentUser;

                            await _db.Updateable(product).ExecuteCommandAsync();

                            if (item.IsAutoPricing.HasValue)
                            {
                                await _db.Updateable<StoreRetailPrice>()
                                    .SetColumns(srp =>
                                        srp.IsAutoPricing == item.IsAutoPricing.Value
                                    )
                                    .Where(srp => srp.ProductCode == item.ProductCode)
                                    .ExecuteCommandAsync();
                            }

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
        /// 批量删除商品（使用事务，支持软删除和物理删除）
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <param name="isSoftDelete">true=软删除，false=物理删除</param>
        public async Task<ApiResponse<BatchOperationReactResult>> BatchDeleteAsync(
            List<string> productCodes,
            bool isSoftDelete = true
        )
        {
            var result = new BatchOperationReactResult();
            var deleteType = isSoftDelete ? "软删除" : "物理删除";

            try
            {
                // 使用事务
                await _db.Ado.UseTranAsync(async () =>
                {
                    foreach (var code in productCodes)
                    {
                        try
                        {
                            var productDeleted = await CascadeDeleteProductAsync(code, isSoftDelete);
                            if (productDeleted > 0)
                                result.SuccessCount++;
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
                            _logger.LogError(ex, "批量{deleteType}单个商品失败: {ProductCode}", deleteType, code);
                        }
                    }
                });

                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = true,
                    Data = result,
                    Message =
                        $"批量{deleteType}完成: 成功{result.SuccessCount}条，失败{result.FailedCount}条",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量{deleteType}商品事务失败");
                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = false,
                    Message = $"批量{deleteType}失败: {ex.Message}",
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
                    query = query.Where((p, sp, s, ls) => filter.StoreCodes.Contains(sp.StoreCode));
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
                        (p, sp, s, ls) => p.UpdatedBy != null && p.UpdatedBy.Contains(name)
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
