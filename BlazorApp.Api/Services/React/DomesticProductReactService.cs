using System.Linq.Expressions;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// React 国内商品服务（文件名以 ReactService 结尾）
    /// 当前为适配器实现，委托给原有 IDomesticProductService。
    /// </summary>
    public class DomesticProductReactService : IDomesticProductReactService
    {
        private readonly SqlSugarContext _context;
        private readonly HBSalesSqlSugarContext _hbSalesContext;
        private readonly IMapper _mapper;
        private readonly ILogger<DomesticProductReactService> _logger;
        private readonly ItemBarcodeService _itemBarcodeService;

        public DomesticProductReactService(
            SqlSugarContext context,
            HBSalesSqlSugarContext hbSalesContext,
            IMapper mapper,
            ILogger<DomesticProductReactService> logger,
            ItemBarcodeService itemBarcodeService
        )
        {
            _context = context;
            _hbSalesContext = hbSalesContext;
            _mapper = mapper;
            _logger = logger;
            _itemBarcodeService = itemBarcodeService;
        }

        // ==================== React React-Data-Grid 专用方法 ====================
        public async Task<GridResponseDto<DomesticProductDto>> GetGridDataAsync(
            GridRequestDto request
        )
        {
            try
            {
                var db = _context.Db;

                // 构建基础查询
                var query = db.Queryable<DomesticProduct>()
                    .LeftJoin<ChinaSupplier>((p, s) => p.SupplierCode == s.SupplierCode)
                    .Where(p => !p.IsDeleted);

                // 全局搜索（OR）
                if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
                {
                    var keyword = request.GlobalSearch.Trim();
                    query = query.Where(
                        (p, s) =>
                            (p.ProductName != null && p.ProductName.Contains(keyword))
                            || (p.HBProductNo != null && p.HBProductNo.Contains(keyword))
                            || (p.Barcode != null && p.Barcode.Contains(keyword))
                            || (
                                p.EnglishProductName != null
                                && p.EnglishProductName.Contains(keyword)
                            )
                            || (s.SupplierName != null && s.SupplierName.Contains(keyword))
                            || (p.SupplierCode != null && p.SupplierCode.Contains(keyword))
                    );
                }

                // 列过滤（AND）
                if (request.FilterModel != null && request.FilterModel.Any())
                {
                    query = ApplyAgGridFilters(query, request.FilterModel);
                }

                // 排序
                if (request.SortModel != null && request.SortModel.Any())
                {
                    query = ApplyAgGridSorts(query, request.SortModel);
                }
                else
                {
                    query = query.OrderBy(p => p.UpdatedAt, OrderByType.Desc);
                }

                // 统计总数（复制主查询条件）
                var countQuery = db.Queryable<DomesticProduct>()
                    .LeftJoin<ChinaSupplier>((p, s) => p.SupplierCode == s.SupplierCode)
                    .Where(p => !p.IsDeleted);

                if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
                {
                    var keyword = request.GlobalSearch.Trim();
                    countQuery = countQuery.Where(
                        (p, s) =>
                            (p.ProductName != null && p.ProductName.Contains(keyword))
                            || (p.HBProductNo != null && p.HBProductNo.Contains(keyword))
                            || (p.Barcode != null && p.Barcode.Contains(keyword))
                            || (
                                p.EnglishProductName != null
                                && p.EnglishProductName.Contains(keyword)
                            )
                            || (p.SupplierCode != null && p.SupplierCode.Contains(keyword))
                            || (s.SupplierName != null && s.SupplierName.Contains(keyword))
                    );
                }

                var total = await countQuery.CountAsync();

                // 分页查询
                var items = await query
                    .Select(
                        (p, s) =>
                            new DomesticProductDto
                            {
                                ProductCode = p.ProductCode,
                                SupplierCode = p.SupplierCode,
                                SupplierName = SqlFunc.IsNull(s.SupplierName, string.Empty),
                                ProductName = p.ProductName,
                                EnglishProductName = p.EnglishProductName,
                                HBProductNo = p.HBProductNo,
                                Barcode = p.Barcode,
                                ProductSpecification = p.ProductSpecification,
                                ProductType = p.ProductType,
                                DomesticPrice = p.DomesticPrice,
                                OEMPrice = p.OEMPrice,
                                ImportPrice = p.ImportPrice,
                                PackingQuantity = p.PackingQuantity,
                                UnitVolume = p.UnitVolume,
                                MiddlePackQuantity = p.MiddlePackQuantity,
                                PackingSize = null,
                                Material = null,
                                Remarks = null,
                                ProductImage = p.ProductImage,
                                IsActive = p.IsActive,
                                CreatedAt = p.CreatedAt,
                                UpdatedAt = p.UpdatedAt,
                            }
                    )
                    .Skip(request.StartRow)
                    .Take(request.PageSize)
                    .ToListAsync();

                // 批量加载套装数量信息
                if (items.Any())
                {
                    var setProductCodes = items
                        .Where(x => x.ProductType > 0)
                        .Select(x => x.ProductCode)
                        .ToList();
                    if (setProductCodes.Any())
                    {
                        var setItemCounts = await db.Queryable<DomesticSetProduct>()
                            .Where(x => setProductCodes.Contains(x.ProductCode))
                            .GroupBy(x => x.ProductCode)
                            .Select(g => new
                            {
                                ProductCode = g.ProductCode,
                                Count = SqlFunc.AggregateCount(1),
                            })
                            .ToListAsync();

                        var setCountDict = setItemCounts.ToDictionary(
                            x => x.ProductCode,
                            x => x.Count
                        );
                        foreach (var item in items.Where(x => x.ProductType > 0))
                        {
                            if (setCountDict.TryGetValue(item.ProductCode, out var count))
                            {
                                item.SetProducts = Enumerable
                                    .Repeat(new DomesticSetProductDto(), count)
                                    .ToList();
                            }
                        }
                    }
                }

                _logger.LogInformation(
                    "React-Data-Grid查询成功: 总数={Total}, 返回={Count}",
                    total,
                    items.Count
                );
                return GridResponseDto<DomesticProductDto>.OK(items, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Grid 查询失败");
                return GridResponseDto<DomesticProductDto>.Error("查询失败: " + ex.Message);
            }
        }

        // 过滤与排序辅助方法
        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyAgGridFilters(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            Dictionary<string, FilterModelDto> filterModel
        )
        {
            foreach (var filter in filterModel)
            {
                var columnId = filter.Key;
                var filterConfig = filter.Value;
                if (filterConfig == null || filterConfig.FilterType == null)
                    continue;

                switch (filterConfig.FilterType.ToLower())
                {
                    case "text":
                        query = ApplyTextFilter(query, columnId, filterConfig);
                        break;
                    case "number":
                        query = ApplyNumberFilter(query, columnId, filterConfig);
                        break;
                    case "set":
                        query = ApplySetFilter(query, columnId, filterConfig);
                        break;
                }
            }
            return query;
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyTextFilter(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            string columnId,
            FilterModelDto filter
        )
        {
            if (filter.Filter == null)
                return query;

            var filterValue = filter.Filter ?? string.Empty;
            var operation = filter.Type?.ToLower() ?? "contains";

            switch (columnId)
            {
                case "supplierCode":
                    return ApplyTextOperationDirect(
                        query,
                        operation,
                        filterValue,
                        p => p.SupplierCode
                    );
                case "name":
                    return ApplyTextOperationDirect(
                        query,
                        operation,
                        filterValue,
                        p => p.ProductName
                    );
                case "nameEn":
                    return ApplyTextOperationDirect(
                        query,
                        operation,
                        filterValue,
                        p => p.EnglishProductName
                    );
                case "itemNumber":
                    return ApplyTextOperationDirect(
                        query,
                        operation,
                        filterValue,
                        p => p.HBProductNo
                    );
                case "barcode":
                    return ApplyTextOperationDirect(query, operation, filterValue, p => p.Barcode);
                case "supplierName":
                    return query.Where(
                        (p, s) => ApplyTextCondition(s.SupplierName, filter.Type, filterValue)
                    );
                default:
                    return query;
            }
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyTextOperationDirect(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            string operation,
            string value,
            Expression<Func<DomesticProduct, string?>> fieldSelector
        )
        {
            var param = fieldSelector.Parameters[0];
            var member = fieldSelector.Body;

            return operation switch
            {
                "equals" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.Equal(member, Expression.Constant(value, typeof(string))),
                        param
                    )
                ),
                "notequal" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.NotEqual(member, Expression.Constant(value, typeof(string))),
                        param
                    )
                ),
                "contains" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.AndAlso(
                            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                            Expression.Call(
                                member,
                                typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                Expression.Constant(value)
                            )
                        ),
                        param
                    )
                ),
                "notcontains" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.OrElse(
                            Expression.Equal(member, Expression.Constant(null, typeof(string))),
                            Expression.Not(
                                Expression.Call(
                                    member,
                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                    Expression.Constant(value)
                                )
                            )
                        ),
                        param
                    )
                ),
                "startswith" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.AndAlso(
                            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                            Expression.Call(
                                member,
                                typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!,
                                Expression.Constant(value)
                            )
                        ),
                        param
                    )
                ),
                "endswith" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.AndAlso(
                            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                            Expression.Call(
                                member,
                                typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!,
                                Expression.Constant(value)
                            )
                        ),
                        param
                    )
                ),
                "blank" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.Call(typeof(string), "IsNullOrEmpty", null, member),
                        param
                    )
                ),
                "notblank" => query.Where(
                    Expression.Lambda<Func<DomesticProduct, bool>>(
                        Expression.Not(
                            Expression.Call(typeof(string), "IsNullOrEmpty", null, member)
                        ),
                        param
                    )
                ),
                _ => query,
            };
        }

        private bool ApplyTextCondition(string? fieldValue, string? operation, string value)
        {
            return operation?.ToLower() switch
            {
                "equals" => fieldValue == value,
                "notequal" => fieldValue != value,
                "contains" => fieldValue?.Contains(value) ?? false,
                "notcontains" => !(fieldValue?.Contains(value) ?? false),
                "startswith" => fieldValue?.StartsWith(value) ?? false,
                "endswith" => fieldValue?.EndsWith(value) ?? false,
                "blank" => string.IsNullOrEmpty(fieldValue),
                "notblank" => !string.IsNullOrEmpty(fieldValue),
                _ => true,
            };
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyNumberFilter(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            string columnId,
            FilterModelDto filter
        )
        {
            if (filter.Filter == null)
                return query;

            decimal? filterValue = null;
            if (decimal.TryParse(filter.Filter, out var parsed))
            {
                filterValue = parsed;
            }

            if (!filterValue.HasValue)
                return query;

            return columnId switch
            {
                "domesticPrice" => ApplyNumberOperation(
                    query,
                    filter.Type,
                    p => p.DomesticPrice,
                    filterValue.Value,
                    filter.FilterTo
                ),
                "labelPrice" => ApplyNumberOperation(
                    query,
                    filter.Type,
                    p => p.OEMPrice,
                    filterValue.Value,
                    filter.FilterTo
                ),
                "oemPrice" => ApplyNumberOperation(
                    query,
                    filter.Type,
                    p => p.OEMPrice,
                    filterValue.Value,
                    filter.FilterTo
                ),
                "importPrice" => ApplyNumberOperation(
                    query,
                    filter.Type,
                    p => p.ImportPrice,
                    filterValue.Value,
                    filter.FilterTo
                ),
                "packingQty" => ApplyIntNumberOperation(
                    query,
                    filter.Type,
                    p => p.PackingQuantity,
                    (int)filterValue.Value,
                    filter.FilterTo
                ),
                "volume" => ApplyNumberOperation(
                    query,
                    filter.Type,
                    p => p.UnitVolume,
                    filterValue.Value,
                    filter.FilterTo
                ),
                "middlePackQty" => ApplyNumberOperation(
                    query,
                    filter.Type,
                    p => p.MiddlePackQuantity,
                    filterValue.Value,
                    filter.FilterTo
                ),
                _ => query,
            };
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyNumberOperation(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            string? operation,
            Expression<Func<DomesticProduct, decimal?>> fieldSelector,
            decimal value,
            object? filterTo
        )
        {
            var parameter = fieldSelector.Parameters[0];
            var member = fieldSelector.Body;
            var constantValue = Expression.Convert(Expression.Constant(value), typeof(decimal?));

            Expression? condition = operation?.ToLower() switch
            {
                "equals" => Expression.Equal(member, constantValue),
                "notequal" => Expression.NotEqual(member, constantValue),
                "lessthan" => Expression.LessThan(member, constantValue),
                "lessthanorequal" => Expression.LessThanOrEqual(member, constantValue),
                "greaterthan" => Expression.GreaterThan(member, constantValue),
                "greaterthanorequal" => Expression.GreaterThanOrEqual(member, constantValue),
                "inrange" => filterTo != null
                && decimal.TryParse(filterTo.ToString(), out var toValue)
                    ? Expression.AndAlso(
                        Expression.GreaterThanOrEqual(member, constantValue),
                        Expression.LessThanOrEqual(
                            member,
                            Expression.Convert(Expression.Constant(toValue), typeof(decimal?))
                        )
                    )
                    : null,
                _ => null,
            };

            if (condition == null)
                return query;

            var lambda = Expression.Lambda<Func<DomesticProduct, bool>>(condition, parameter);
            return query.Where(lambda);
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyIntNumberOperation(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            string? operation,
            Expression<Func<DomesticProduct, int?>> fieldSelector,
            int value,
            object? filterTo
        )
        {
            var parameter = fieldSelector.Parameters[0];
            var member = fieldSelector.Body;
            var constantValue = Expression.Convert(Expression.Constant(value), typeof(int?));

            Expression? condition = operation?.ToLower() switch
            {
                "equals" => Expression.Equal(member, constantValue),
                "notequal" => Expression.NotEqual(member, constantValue),
                "lessthan" => Expression.LessThan(member, constantValue),
                "lessthanorequal" => Expression.LessThanOrEqual(member, constantValue),
                "greaterthan" => Expression.GreaterThan(member, constantValue),
                "greaterthanorequal" => Expression.GreaterThanOrEqual(member, constantValue),
                "inrange" => filterTo != null && int.TryParse(filterTo.ToString(), out var toValue)
                    ? Expression.AndAlso(
                        Expression.GreaterThanOrEqual(member, constantValue),
                        Expression.LessThanOrEqual(
                            member,
                            Expression.Convert(Expression.Constant(toValue), typeof(int?))
                        )
                    )
                    : null,
                _ => null,
            };

            if (condition == null)
                return query;

            var lambda = Expression.Lambda<Func<DomesticProduct, bool>>(condition, parameter);
            return query.Where(lambda);
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplySetFilter(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            string columnId,
            FilterModelDto filter
        )
        {
            if (filter.Values == null || !filter.Values.Any())
                return query;

            if (columnId == "productType")
            {
                var typeNames = filter.Values;
                return query.Where(p => typeNames.Contains(p.ProductType.ToString()));
            }

            return query;
        }

        private ISugarQueryable<DomesticProduct, ChinaSupplier> ApplyAgGridSorts(
            ISugarQueryable<DomesticProduct, ChinaSupplier> query,
            List<SortModelDto> sortModel
        )
        {
            if (!sortModel.Any())
                return query;

            var firstSort = sortModel.First();
            var isAsc = firstSort.Sort.ToLower() == "asc";

            query = firstSort.ColId switch
            {
                "supplierCode" => query.OrderBy(
                    p => p.SupplierCode,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "supplierName" => query.OrderBy(
                    (p, s) => s.SupplierName,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "name" => query.OrderBy(
                    p => p.ProductName,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "nameEn" => query.OrderBy(
                    p => p.EnglishProductName,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "itemNumber" => query.OrderBy(
                    p => p.HBProductNo,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "barcode" => query.OrderBy(
                    p => p.Barcode,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "productType" => query.OrderBy(
                    p => p.ProductType,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "domesticPrice" => query.OrderBy(
                    p => p.DomesticPrice,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "labelPrice" => query.OrderBy(
                    p => p.OEMPrice,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "importPrice" => query.OrderBy(
                    p => p.ImportPrice,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "packingQty" => query.OrderBy(
                    p => p.PackingQuantity,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "volume" => query.OrderBy(
                    p => p.UnitVolume,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "middlePackQty" => query.OrderBy(
                    p => p.MiddlePackQuantity,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "createdAt" => query.OrderBy(
                    p => p.CreatedAt,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                "updatedAt" => query.OrderBy(
                    p => p.UpdatedAt,
                    isAsc ? OrderByType.Asc : OrderByType.Desc
                ),
                _ => query.OrderBy(p => p.UpdatedAt, OrderByType.Desc),
            };

            return query;
        }

        // ==================== 批量验证/创建/检测/导入确认 ====================
        public async Task<ApiResponse<object>> BatchValidateProductsAsync(
            BatchCreateDomesticProductDto dto
        )
        {
            try
            {
                var db = _context.Db;
                var validProducts = new List<object>();
                var invalidProducts = new List<object>();

                var supplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == dto.SupplierCode && !s.IsDeleted)
                    .FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<object>.Error("供应商不存在", "SUPPLIER_NOT_FOUND");
                }

                if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
                {
                    var prefix = await db.Queryable<ProductPrefixCode>()
                        .Where(p =>
                            p.PrefixCode == dto.PrefixCode
                            && p.SupplierCode == dto.SupplierCode
                            && !p.IsDeleted
                        )
                        .FirstAsync();
                    if (prefix == null)
                    {
                        return ApiResponse<object>.Error(
                            "前缀不存在或不属于该供应商",
                            "PREFIX_NOT_FOUND"
                        );
                    }
                }

                var existingProductNames = await db.Queryable<DomesticProduct>()
                    .Where(p => p.SupplierCode == dto.SupplierCode && !p.IsDeleted)
                    .Select(p => p.ProductName)
                    .ToListAsync();

                var existingNameSet = new HashSet<string>(
                    existingProductNames.Where(n => !string.IsNullOrWhiteSpace(n))!,
                    StringComparer.OrdinalIgnoreCase
                );

                for (int i = 0; i < dto.Products.Count; i++)
                {
                    var product = dto.Products[i];
                    var errors = new Dictionary<string, List<string>>();
                    var rowNumber = i + 1;

                    if (string.IsNullOrWhiteSpace(product.ProductName))
                    {
                        errors.Add("productName", new List<string> { "商品名称不能为空" });
                    }
                    else if (product.ProductName.Length > 200)
                    {
                        errors.Add(
                            "productName",
                            new List<string> { "商品名称长度不能超过200字符" }
                        );
                    }
                    else if (existingNameSet.Contains(product.ProductName))
                    {
                        errors.Add("productName", new List<string> { "商品名称已存在" });
                    }

                    if (product.DomesticPrice.HasValue)
                    {
                        if (product.DomesticPrice.Value < 0)
                            errors.Add(
                                "domesticPrice",
                                new List<string> { "国内价格必须为非负数" }
                            );
                    }
                    if (product.OEMPrice.HasValue)
                    {
                        if (product.OEMPrice.Value < 0)
                            errors.Add("oemPrice", new List<string> { "贴牌价格必须为非负数" });
                    }
                    if (product.PackingQuantity.HasValue)
                    {
                        if (product.PackingQuantity.Value <= 0)
                            errors.Add(
                                "packingQuantity",
                                new List<string> { "装箱数必须为正整数" }
                            );
                    }
                    if (product.UnitVolume.HasValue)
                    {
                        if (product.UnitVolume.Value < 0)
                            errors.Add("unitVolume", new List<string> { "单件体积必须为非负数" });
                    }
                    if (product.MiddlePackQuantity.HasValue)
                    {
                        if (product.MiddlePackQuantity.Value <= 0)
                            errors.Add(
                                "middlePackQuantity",
                                new List<string> { "中包数必须为正整数" }
                            );
                    }

                    if (errors.Any())
                    {
                        invalidProducts.Add(
                            new
                            {
                                rowNumber,
                                productName = product.ProductName,
                                errors,
                            }
                        );
                    }
                    else
                    {
                        validProducts.Add(new { rowNumber, productName = product.ProductName });
                    }
                }

                _logger.LogInformation(
                    "批量验证完成: 有效{Valid}件, 无效{Invalid}件",
                    validProducts.Count,
                    invalidProducts.Count
                );
                return ApiResponse<object>.OK(new { validProducts, invalidProducts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量验证商品失败");
                return ApiResponse<object>.Error("批量验证商品失败", "BATCH_VALIDATE_ERROR");
            }
        }

        public async Task<ApiResponse<List<DomesticProductDto>>> BatchCreateDomesticProductsAsync(
            BatchCreateDomesticProductDto dto
        )
        {
            try
            {
                var db = _context.Db;

                var supplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == dto.SupplierCode && !s.IsDeleted)
                    .FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<List<DomesticProductDto>>.Error(
                        "供应商不存在",
                        "SUPPLIER_NOT_FOUND"
                    );
                }

                string? prefixName = null;
                if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
                {
                    var prefix = await db.Queryable<ProductPrefixCode>()
                        .Where(p =>
                            p.PrefixCode == dto.PrefixCode
                            && p.SupplierCode == dto.SupplierCode
                            && !p.IsDeleted
                        )
                        .FirstAsync();
                    if (prefix == null)
                    {
                        return ApiResponse<List<DomesticProductDto>>.Error(
                            "前缀不存在或不属于该供应商",
                            "PREFIX_NOT_FOUND"
                        );
                    }
                    prefixName = prefix.PrefixName;
                }

                var productType = (ProductTypeEnum)(
                    dto.Products.FirstOrDefault()?.ProductType ?? 0
                );
                var itemNumberBarcodeList =
                    await _itemBarcodeService.GenerateBatchItemNumbersAndBarcodesAsync(
                        dto.SupplierCode,
                        productType,
                        dto.Products.Count,
                        prefixName
                    );

                var products = new List<DomesticProduct>();
                var now = DateTime.Now;
                for (int i = 0; i < dto.Products.Count; i++)
                {
                    var productItem = dto.Products[i];
                    var product = _mapper.Map<DomesticProduct>(productItem);
                    product.ProductCode = UuidHelper.GenerateUuid7();
                    product.SupplierCode = dto.SupplierCode;
                    product.HBProductNo = itemNumberBarcodeList[i].itemNumber;
                    product.Barcode = itemNumberBarcodeList[i].barcode;

                    if (
                        string.IsNullOrWhiteSpace(product.ProductImage)
                        && !string.IsNullOrWhiteSpace(product.HBProductNo)
                        && !product.HBProductNo.StartsWith(
                            "http://",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !product.HBProductNo.StartsWith(
                            "https://",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        product.ProductImage =
                            $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{product.HBProductNo}.jpg";
                    }

                    product.IsActive = true;
                    product.CreatedAt = now;
                    product.UpdatedAt = now;
                    product.CreatedBy = "System";
                    product.UpdatedBy = "System";
                    products.Add(product);
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    await BatchOperationHelper.BatchInsertAsync(
                        db,
                        products,
                        BatchOperationHelper.GetRecommendedBatchSize(products.Count, 2)
                    );

                    ProductPrefixCode? prefix = null;
                    if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
                    {
                        prefix = await db.Queryable<ProductPrefixCode>()
                            .Where(p => p.PrefixCode == dto.PrefixCode && !p.IsDeleted)
                            .FirstAsync();
                    }

                    var batchNumber = UuidHelper.GenerateUuid7();
                    var creationLogs = new List<DomesticProductCreationLog>();
                    foreach (var product in products)
                    {
                        var log = new DomesticProductCreationLog
                        {
                            LogId = UuidHelper.GenerateUuid7(),
                            ProductCode = product.ProductCode,
                            SupplierCode = dto.SupplierCode,
                            SupplierName = supplier.SupplierName,
                            HBProductNo = product.HBProductNo ?? string.Empty,
                            Barcode = product.Barcode,
                            ProductName = product.ProductName,
                            PrefixCode = dto.PrefixCode,
                            PrefixName = prefix?.PrefixName,
                            CreationType = "Batch",
                            BatchNumber = batchNumber,
                            CreatedBy = "System",
                            CreatedAt = now,
                        };
                        creationLogs.Add(log);
                    }

                    await BatchOperationHelper.BatchInsertAsync(
                        db,
                        creationLogs,
                        BatchOperationHelper.GetRecommendedBatchSize(creationLogs.Count, 2)
                    );
                    await db.Ado.CommitTranAsync();

                    var productDtos = _mapper.Map<List<DomesticProductDto>>(products);
                    foreach (var productDto in productDtos)
                        productDto.SupplierName = supplier.SupplierName;
                    _logger.LogInformation(
                        "批量创建国内商品成功，SupplierCode: {SupplierCode}, Count: {Count}, BatchNumber: {BatchNumber}",
                        dto.SupplierCode,
                        products.Count,
                        batchNumber
                    );
                    return ApiResponse<List<DomesticProductDto>>.OK(productDtos);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量创建商品事务失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建国内商品失败");
                return ApiResponse<List<DomesticProductDto>>.Error(
                    "批量创建国内商品失败",
                    "BATCH_CREATE_PRODUCTS_ERROR"
                );
            }
        }

        public async Task<
            ApiResponse<List<BatchProductDetectionResultDto>>
        > BatchDetectProductsAsync(BatchProductDetectionDto dto)
        {
            try
            {
                var db = _context.Db;
                if (dto.Products == null || !dto.Products.Any())
                {
                    return ApiResponse<List<BatchProductDetectionResultDto>>.Error(
                        "检测商品列表不能为空",
                        "EMPTY_PRODUCT_LIST"
                    );
                }
                var supplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == dto.SupplierCode && !s.IsDeleted)
                    .FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<List<BatchProductDetectionResultDto>>.Error(
                        "供应商不存在",
                        "SUPPLIER_NOT_FOUND"
                    );
                }

                var results = new List<BatchProductDetectionResultDto>();
                var hbProductNos = dto
                    .Products.Where(p => !string.IsNullOrWhiteSpace(p.HBProductNo))
                    .Select(p => p.HBProductNo!)
                    .Distinct()
                    .ToList();
                var existingProducts = await db.Queryable<DomesticProduct>()
                    .LeftJoin<ChinaSupplier>((p, s) => p.SupplierCode == s.SupplierCode)
                    .Where(
                        (p, s) =>
                            p.SupplierCode == dto.SupplierCode
                            && hbProductNos.Contains(p.HBProductNo!)
                            && !p.IsDeleted
                    )
                    .Select(
                        (p, s) =>
                            new DomesticProduct
                            {
                                ProductCode = p.ProductCode,
                                SupplierCode = p.SupplierCode,
                                ProductName = p.ProductName,
                                EnglishProductName = p.EnglishProductName,
                                HBProductNo = p.HBProductNo,
                                Barcode = p.Barcode,
                                ProductSpecification = p.ProductSpecification,
                                ProductType = p.ProductType,
                                DomesticPrice = p.DomesticPrice,
                                OEMPrice = p.OEMPrice,
                                ImportPrice = p.ImportPrice,
                                PackingQuantity = p.PackingQuantity,
                                UnitVolume = p.UnitVolume,
                                MiddlePackQuantity = p.MiddlePackQuantity,
                                ProductImage = p.ProductImage,
                                IsActive = p.IsActive,
                                CreatedAt = p.CreatedAt,
                                UpdatedAt = p.UpdatedAt,
                                Supplier = new ChinaSupplier { SupplierName = s.SupplierName },
                            }
                    )
                    .ToListAsync();

                // 收集需要更新图片的商品
                var productsToUpdateImage = new Dictionary<string, string>();

                foreach (var inputProduct in dto.Products)
                {
                    var result = new BatchProductDetectionResultDto
                    {
                        InputData = inputProduct,
                        SupplierCode = dto.SupplierCode,
                        SupplierName = supplier.SupplierName,
                    };

                    DomesticProduct? existingProduct = null;
                    if (!string.IsNullOrWhiteSpace(inputProduct.HBProductNo))
                    {
                        existingProduct = existingProducts.FirstOrDefault(p =>
                            p.HBProductNo == inputProduct.HBProductNo
                        );
                    }

                    if (existingProduct != null)
                    {
                        result.IsNewProduct = false;
                        result.ExistingData = _mapper.Map<DomesticProductDto>(existingProduct);
                        result.ExistingData.SupplierName = supplier.SupplierName;

                        // 自动生成图片 URL（如果 ProductImage 为空且有 HBProductNo）
                        if (
                            string.IsNullOrWhiteSpace(result.ExistingData.ProductImage)
                            && !string.IsNullOrWhiteSpace(result.ExistingData.HBProductNo)
                        )
                        {
                            var imageUrl =
                                $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{result.ExistingData.HBProductNo}.jpg";
                            result.ExistingData.ProductImage = imageUrl;

                            // 收集需要更新的商品
                            productsToUpdateImage[existingProduct.ProductCode] = imageUrl;
                        }

                        result.HasChanges = CheckForChanges(inputProduct, existingProduct);
                        result.ChangeList = GetChangeList(inputProduct, existingProduct);
                    }
                    else
                    {
                        result.IsNewProduct = true;
                        result.ExistingData = null;
                        result.HasChanges = false;
                        result.ChangeList = new List<string>();
                    }
                    results.Add(result);
                }

                // 批量更新图片到数据库
                if (productsToUpdateImage.Any())
                {
                    foreach (var update in productsToUpdateImage)
                    {
                        await db.Updateable<DomesticProduct>()
                            .SetColumns(p => p.ProductImage == update.Value)
                            .SetColumns(p => p.UpdatedAt == DateTime.Now)
                            .Where(p => p.ProductCode == update.Key)
                            .ExecuteCommandAsync();
                    }
                    _logger.LogInformation(
                        "批量更新商品图片完成，数量: {Count}",
                        productsToUpdateImage.Count
                    );
                }

                _logger.LogInformation(
                    "批量检测商品完成，SupplierCode: {SupplierCode}, 检测数量: {Count}, 新商品: {NewCount}, 已存在: {ExistingCount}",
                    dto.SupplierCode,
                    results.Count,
                    results.Count(r => r.IsNewProduct),
                    results.Count(r => !r.IsNewProduct)
                );
                return ApiResponse<List<BatchProductDetectionResultDto>>.OK(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量检测商品失败");
                return ApiResponse<List<BatchProductDetectionResultDto>>.Error(
                    "批量检测商品失败",
                    "BATCH_DETECT_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 批量创建和更新国内商品（React专用）
        /// 支持同时创建新商品和更新现有商品
        /// </summary>
        public async Task<
            ApiResponse<BatchProductOperationResultDto>
        > BatchCreateAndUpdateProductsAsync(BatchProductOperationDto dto)
        {
            try
            {
                var db = _context.Db;
                var result = new BatchProductOperationResultDto
                {
                    CreatedProducts = new List<DomesticProductDto>(),
                    UpdatedProducts = new List<DomesticProductDto>(),
                    Errors = new List<string>(),
                };

                // 验证供应商是否存在
                var supplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == dto.SupplierCode && !s.IsDeleted)
                    .FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<BatchProductOperationResultDto>.Error(
                        "供应商不存在",
                        "SUPPLIER_NOT_FOUND"
                    );
                }

                var now = DateTime.Now;
                var currentUser = "System";
                // 开启事务
                await db.Ado.BeginTranAsync();
                try
                {
                    // 处理新建商品
                    if (dto.NewProducts?.Any() == true)
                    {
                        var newProducts = new List<DomesticProduct>();

                        var needGenerateProducts = dto
                            .NewProducts.Where(p =>
                                string.IsNullOrWhiteSpace(p.HBProductNo)
                                && string.IsNullOrWhiteSpace(p.Barcode)
                            )
                            .ToList();

                        List<(string itemNumber, string barcode)>? itemNumberBarcodeList = null;
                        int generateIndex = 0;

                        if (needGenerateProducts.Any())
                        {
                            itemNumberBarcodeList =
                                await _itemBarcodeService.GenerateBatchItemNumbersAndBarcodesAsync(
                                    dto.SupplierCode,
                                    ProductTypeEnum.Normal,
                                    needGenerateProducts.Count
                                );
                        }

                        foreach (var newProductDto in dto.NewProducts)
                        {
                            try
                            {
                                var product = _mapper.Map<DomesticProduct>(newProductDto);
                                product.ProductCode = UuidHelper.GenerateUuid7();
                                product.SupplierCode = dto.SupplierCode;

                                if (
                                    string.IsNullOrWhiteSpace(product.HBProductNo)
                                    && string.IsNullOrWhiteSpace(product.Barcode)
                                )
                                {
                                    if (itemNumberBarcodeList != null)
                                    {
                                        var (itemNumber, barcode) = itemNumberBarcodeList[
                                            generateIndex++
                                        ];
                                        product.HBProductNo = itemNumber;
                                        product.Barcode = barcode;
                                    }
                                }

                                if (
                                    string.IsNullOrWhiteSpace(product.ProductImage)
                                    && !string.IsNullOrWhiteSpace(product.HBProductNo)
                                    && !product.HBProductNo.StartsWith(
                                        "http://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    && !product.HBProductNo.StartsWith(
                                        "https://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    product.ProductImage =
                                        $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{product.HBProductNo}.jpg";
                                }

                                product.IsActive = true;
                                product.CreatedAt = now;
                                product.UpdatedAt = now;
                                product.CreatedBy = currentUser;
                                product.UpdatedBy = currentUser;
                                newProducts.Add(product);

                                var productDto = _mapper.Map<DomesticProductDto>(product);
                                productDto.SupplierName = supplier.SupplierName;
                                result.CreatedProducts.Add(productDto);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "创建单个商品失败: {ProductName}",
                                    newProductDto.ProductName
                                );
                                result.Errors.Add(
                                    $"创建商品失败: {newProductDto.ProductName} - {ex.Message}"
                                );
                            }
                        }

                        // 批量插入新商品
                        if (newProducts.Any())
                        {
                            await db.Insertable(newProducts).ExecuteCommandAsync();
                        }
                    }

                    // 处理更新商品
                    if (dto.UpdateProducts?.Any() == true)
                    {
                        // 获取所有要更新的商品编码
                        var productCodes = dto.UpdateProducts.Select(u => u.ProductCode).ToList();
                        // 批量查询现有商品
                        var existingProducts = await db.Queryable<DomesticProduct>()
                            .Where(p => productCodes.Contains(p.ProductCode) && !p.IsDeleted)
                            .ToListAsync();

                        // 使用字典提高查找效率
                        var existingProductDict = existingProducts.ToDictionary(p => p.ProductCode);

                        // 收集需要更新的条形码
                        var barcodesToUpdate = dto
                            .UpdateProducts.Where(u => !string.IsNullOrWhiteSpace(u.Barcode))
                            .Select(u => u.Barcode!)
                            .ToList();

                        // 批量检查条形码是否重复
                        var duplicateBarcodes = await db.Queryable<DomesticProduct>()
                            .Where(p =>
                                barcodesToUpdate.Contains(p.Barcode!)
                                && !p.IsDeleted
                                && !productCodes.Contains(p.ProductCode)
                            )
                            .Select(p => p.Barcode)
                            .ToListAsync();

                        // 收集需要批量更新的商品
                        var productsToUpdate = new List<DomesticProduct>();

                        foreach (var updateDto in dto.UpdateProducts)
                        {
                            try
                            {
                                // 检查商品是否存在
                                if (
                                    !existingProductDict.TryGetValue(
                                        updateDto.ProductCode,
                                        out var existingProduct
                                    )
                                )
                                {
                                    result.Errors.Add(
                                        $"更新商品失败: 商品不存在 {updateDto.ProductCode}"
                                    );
                                    continue;
                                }

                                var hasChanges = false;
                                var changedFields = new List<string>();

                                // 更新商品名称
                                if (
                                    !string.IsNullOrWhiteSpace(updateDto.ProductName)
                                    && !string.Equals(
                                        updateDto.ProductName,
                                        existingProduct.ProductName
                                    )
                                )
                                {
                                    existingProduct.ProductName = updateDto.ProductName;
                                    hasChanges = true;
                                    changedFields.Add("ProductName");
                                }
                                // 更新商品英文名称
                                if (
                                    !string.IsNullOrWhiteSpace(updateDto.EnglishProductName)
                                    && !string.Equals(
                                        updateDto.EnglishProductName,
                                        existingProduct.EnglishProductName
                                    )
                                )
                                {
                                    existingProduct.EnglishProductName =
                                        updateDto.EnglishProductName;
                                    hasChanges = true;
                                    changedFields.Add("EnglishProductName");
                                }

                                // 更新条形码
                                if (
                                    !string.IsNullOrWhiteSpace(updateDto.Barcode)
                                    && !string.Equals(updateDto.Barcode, existingProduct.Barcode)
                                )
                                {
                                    if (duplicateBarcodes.Contains(updateDto.Barcode))
                                    {
                                        result.Errors.Add(
                                            $"更新商品失败: 条形码重复 {updateDto.Barcode} (ProductCode: {updateDto.ProductCode})"
                                        );
                                    }
                                    else
                                    {
                                        existingProduct.Barcode = updateDto.Barcode;
                                        hasChanges = true;
                                        changedFields.Add("Barcode");
                                    }
                                }

                                // 更新国内价格
                                if (
                                    updateDto.DomesticPrice.HasValue
                                    && existingProduct.DomesticPrice != updateDto.DomesticPrice
                                )
                                {
                                    if (updateDto.DomesticPrice.Value < 0)
                                    {
                                        result.Errors.Add(
                                            $"更新商品失败: 国内价格不能为负 (ProductCode: {updateDto.ProductCode})"
                                        );
                                    }
                                    else
                                    {
                                        existingProduct.DomesticPrice = updateDto.DomesticPrice;
                                        hasChanges = true;
                                        changedFields.Add("DomesticPrice");
                                    }
                                }
                                // 更新贴牌价格
                                if (
                                    updateDto.OEMPrice.HasValue
                                    && existingProduct.OEMPrice != updateDto.OEMPrice
                                )
                                {
                                    if (updateDto.OEMPrice.Value < 0)
                                    {
                                        result.Errors.Add(
                                            $"更新商品失败: 贴牌价格不能为负 (ProductCode: {updateDto.ProductCode})"
                                        );
                                    }
                                    else
                                    {
                                        existingProduct.OEMPrice = updateDto.OEMPrice;
                                        hasChanges = true;
                                        changedFields.Add("OEMPrice");
                                    }
                                }
                                // 更新单件装箱数
                                if (
                                    updateDto.PackingQuantity.HasValue
                                    && existingProduct.PackingQuantity != updateDto.PackingQuantity
                                )
                                {
                                    if (updateDto.PackingQuantity.Value <= 0)
                                    {
                                        result.Errors.Add(
                                            $"更新商品失败: 单件装箱数必须大于0 (ProductCode: {updateDto.ProductCode})"
                                        );
                                    }
                                    else
                                    {
                                        existingProduct.PackingQuantity = updateDto.PackingQuantity;
                                        hasChanges = true;
                                        changedFields.Add("PackingQuantity");
                                    }
                                }
                                // 更新单件体积
                                if (
                                    updateDto.UnitVolume.HasValue
                                    && existingProduct.UnitVolume != updateDto.UnitVolume
                                )
                                {
                                    if (updateDto.UnitVolume.Value < 0)
                                    {
                                        result.Errors.Add(
                                            $"更新商品失败: 单件体积不能为负 (ProductCode: {updateDto.ProductCode})"
                                        );
                                    }
                                    else
                                    {
                                        existingProduct.UnitVolume = updateDto.UnitVolume;
                                        hasChanges = true;
                                        changedFields.Add("UnitVolume");
                                    }
                                }
                                // 更新中包数量
                                if (
                                    updateDto.MiddlePackQuantity.HasValue
                                    && existingProduct.MiddlePackQuantity
                                        != updateDto.MiddlePackQuantity
                                )
                                {
                                    if (updateDto.MiddlePackQuantity.Value <= 0)
                                    {
                                        result.Errors.Add(
                                            $"更新商品失败: 中包数量必须大于0 (ProductCode: {updateDto.ProductCode})"
                                        );
                                    }
                                    else
                                    {
                                        existingProduct.MiddlePackQuantity =
                                            updateDto.MiddlePackQuantity;
                                        hasChanges = true;
                                        changedFields.Add("MiddlePackQuantity");
                                    }
                                }

                                // 如果有变更，添加到批量更新列表
                                if (hasChanges)
                                {
                                    existingProduct.UpdatedAt = now;
                                    existingProduct.UpdatedBy = currentUser;
                                    productsToUpdate.Add(existingProduct);

                                    result.UpdatedChanges.Add(
                                        new ProductChangeInfo
                                        {
                                            ProductCode = existingProduct.ProductCode,
                                            ChangeList = changedFields,
                                        }
                                    );
                                }
                                else
                                {
                                    _logger.LogInformation(
                                        "跳过更新（无变更）: {ProductCode}",
                                        updateDto.ProductCode
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(
                                    ex,
                                    "更新单个商品失败: {ProductCode}",
                                    updateDto.ProductCode
                                );
                                result.Errors.Add(
                                    $"更新商品失败: {updateDto.ProductCode} - {ex.Message}"
                                );
                            }
                        }

                        // 批量更新到数据库
                        if (productsToUpdate.Any())
                        {
                            await db.Updateable(productsToUpdate).ExecuteCommandAsync();
                            foreach (var product in productsToUpdate)
                            {
                                var productDto = _mapper.Map<DomesticProductDto>(product);
                                productDto.SupplierName = supplier.SupplierName;
                                result.UpdatedProducts.Add(productDto);
                            }
                        }
                    }

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation(
                        "批量操作商品完成，SupplierCode: {SupplierCode}, 新建: {CreatedCount}, 更新: {UpdatedCount}, 错误: {ErrorCount}",
                        dto.SupplierCode,
                        result.CreatedProducts.Count,
                        result.UpdatedProducts.Count,
                        result.Errors.Count
                    );
                    return ApiResponse<BatchProductOperationResultDto>.OK(result);
                }
                catch
                {
                    // 回滚事务
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量操作商品失败");
                return ApiResponse<BatchProductOperationResultDto>.Error(
                    "批量操作商品失败",
                    "BATCH_OPERATION_PRODUCTS_ERROR"
                );
            }
        }

        /// <summary>
        /// 更新单个国内商品（React专用）
        /// </summary>
        public async Task<ApiResponse<DomesticProductDto>> UpdateDomesticProductAsync(
            string productCode,
            UpdateDomesticProductDto dto
        )
        {
            try
            {
                var db = _context.Db;
                // 查询商品
                var product = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .FirstAsync();
                if (product == null)
                {
                    return ApiResponse<DomesticProductDto>.Error("商品不存在", "PRODUCT_NOT_FOUND");
                }
                // 映射更新字段
                _mapper.Map(dto, product);
                product.UpdatedAt = DateTime.Now;
                product.UpdatedBy = "System";
                // 更新到数据库
                await db.Updateable(product).ExecuteCommandAsync();
                // 查询供应商信息
                var supplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == product.SupplierCode)
                    .FirstAsync();
                var productDto = _mapper.Map<DomesticProductDto>(product);
                productDto.SupplierName = supplier?.SupplierName;
                _logger.LogInformation("更新国内商品成功，ProductCode: {ProductCode}", productCode);
                return ApiResponse<DomesticProductDto>.OK(productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新国内商品失败，ProductCode: {ProductCode}", productCode);
                return ApiResponse<DomesticProductDto>.Error(
                    "更新国内商品失败",
                    "UPDATE_PRODUCT_ERROR"
                );
            }
        }

        /// <summary>
        /// 批量更新国内商品（React专用）
        /// 使用批量数据库操作，提高更新效率
        /// </summary>
        public async Task<
            ApiResponse<BatchProductOperationResultDto>
        > BatchUpdateDomesticProductsAsync(BatchUpdateDomesticProductsDto dto)
        {
            try
            {
                var db = _context.Db;
                var result = new BatchProductOperationResultDto
                {
                    CreatedProducts = new List<DomesticProductDto>(),
                    UpdatedProducts = new List<DomesticProductDto>(),
                    Errors = new List<string>(),
                };

                // 验证商品列表不为空
                if (dto.Products?.Any() != true)
                {
                    return ApiResponse<BatchProductOperationResultDto>.Error(
                        "商品列表不能为空",
                        "NO_PRODUCTS_TO_UPDATE"
                    );
                }

                // 获取所有要更新的商品编码
                var productCodes = dto.Products.Select(u => u.ProductCode).ToList();
                // 批量查询现有商品
                var existingProducts = await db.Queryable<DomesticProduct>()
                    .Where(p => productCodes.Contains(p.ProductCode) && !p.IsDeleted)
                    .ToListAsync();

                // 使用字典提高查找效率
                var existingProductDict = existingProducts.ToDictionary(p => p.ProductCode);

                // 收集需要更新的条形码
                var barcodesToUpdate = dto
                    .Products.Where(u => !string.IsNullOrWhiteSpace(u.Barcode))
                    .Select(u => u.Barcode!)
                    .ToList();

                // 批量检查条形码是否重复
                var duplicateBarcodes = await db.Queryable<DomesticProduct>()
                    .Where(p =>
                        barcodesToUpdate.Contains(p.Barcode!)
                        && !p.IsDeleted
                        && !productCodes.Contains(p.ProductCode)
                    )
                    .Select(p => p.Barcode)
                    .ToListAsync();

                // 收集需要批量更新的商品
                var productsToUpdate = new List<DomesticProduct>();
                var now = DateTime.Now;
                var currentUser = "System";

                // 遍历每个更新请求
                foreach (var updateDto in dto.Products)
                {
                    try
                    {
                        // 检查商品是否存在
                        if (
                            !existingProductDict.TryGetValue(
                                updateDto.ProductCode,
                                out var existingProduct
                            )
                        )
                        {
                            result.Errors.Add($"更新商品失败: 商品不存在 {updateDto.ProductCode}");
                            continue;
                        }

                        var hasChanges = false;
                        var changedFields = new List<string>();

                        // 更新商品名称
                        if (
                            !string.IsNullOrWhiteSpace(updateDto.ProductName)
                            && !string.Equals(updateDto.ProductName, existingProduct.ProductName)
                        )
                        {
                            existingProduct.ProductName = updateDto.ProductName;
                            hasChanges = true;
                            changedFields.Add("ProductName");
                        }
                        // 更新商品英文名称
                        if (
                            !string.IsNullOrWhiteSpace(updateDto.EnglishProductName)
                            && !string.Equals(
                                updateDto.EnglishProductName,
                                existingProduct.EnglishProductName
                            )
                        )
                        {
                            existingProduct.EnglishProductName = updateDto.EnglishProductName;
                            hasChanges = true;
                            changedFields.Add("EnglishProductName");
                        }

                        // 更新条形码
                        if (
                            !string.IsNullOrWhiteSpace(updateDto.Barcode)
                            && !string.Equals(updateDto.Barcode, existingProduct.Barcode)
                        )
                        {
                            if (duplicateBarcodes.Contains(updateDto.Barcode))
                            {
                                result.Errors.Add(
                                    $"更新商品失败: 条形码重复 {updateDto.Barcode} (ProductCode: {updateDto.ProductCode})"
                                );
                            }
                            else
                            {
                                existingProduct.Barcode = updateDto.Barcode;
                                hasChanges = true;
                                changedFields.Add("Barcode");
                            }
                        }

                        // 更新国内价格
                        if (
                            updateDto.DomesticPrice.HasValue
                            && existingProduct.DomesticPrice != updateDto.DomesticPrice
                        )
                        {
                            if (updateDto.DomesticPrice.Value < 0)
                            {
                                result.Errors.Add(
                                    $"更新商品失败: 国内价格不能为负 (ProductCode: {updateDto.ProductCode})"
                                );
                            }
                            else
                            {
                                existingProduct.DomesticPrice = updateDto.DomesticPrice;
                                hasChanges = true;
                                changedFields.Add("DomesticPrice");
                            }
                        }
                        // 更新贴牌价格
                        if (
                            updateDto.OEMPrice.HasValue
                            && existingProduct.OEMPrice != updateDto.OEMPrice
                        )
                        {
                            if (updateDto.OEMPrice.Value < 0)
                            {
                                result.Errors.Add(
                                    $"更新商品失败: 贴牌价格不能为负 (ProductCode: {updateDto.ProductCode})"
                                );
                            }
                            else
                            {
                                existingProduct.OEMPrice = updateDto.OEMPrice;
                                hasChanges = true;
                                changedFields.Add("OEMPrice");
                            }
                        }
                        // 更新单件装箱数
                        if (
                            updateDto.PackingQuantity.HasValue
                            && existingProduct.PackingQuantity != updateDto.PackingQuantity
                        )
                        {
                            if (updateDto.PackingQuantity.Value <= 0)
                            {
                                result.Errors.Add(
                                    $"更新商品失败: 单件装箱数必须大于0 (ProductCode: {updateDto.ProductCode})"
                                );
                            }
                            else
                            {
                                existingProduct.PackingQuantity = updateDto.PackingQuantity;
                                hasChanges = true;
                                changedFields.Add("PackingQuantity");
                            }
                        }
                        // 更新单件体积
                        if (
                            updateDto.UnitVolume.HasValue
                            && existingProduct.UnitVolume != updateDto.UnitVolume
                        )
                        {
                            if (updateDto.UnitVolume.Value < 0)
                            {
                                result.Errors.Add(
                                    $"更新商品失败: 单件体积不能为负 (ProductCode: {updateDto.ProductCode})"
                                );
                            }
                            else
                            {
                                existingProduct.UnitVolume = updateDto.UnitVolume;
                                hasChanges = true;
                                changedFields.Add("UnitVolume");
                            }
                        }
                        // 更新中包数量
                        if (
                            updateDto.MiddlePackQuantity.HasValue
                            && existingProduct.MiddlePackQuantity != updateDto.MiddlePackQuantity
                        )
                        {
                            if (updateDto.MiddlePackQuantity.Value <= 0)
                            {
                                result.Errors.Add(
                                    $"更新商品失败: 中包数量必须大于0 (ProductCode: {updateDto.ProductCode})"
                                );
                            }
                            else
                            {
                                existingProduct.MiddlePackQuantity = updateDto.MiddlePackQuantity;
                                hasChanges = true;
                                changedFields.Add("MiddlePackQuantity");
                            }
                        }

                        // 如果有变更，添加到批量更新列表
                        if (hasChanges)
                        {
                            existingProduct.UpdatedAt = now;
                            existingProduct.UpdatedBy = currentUser;
                            productsToUpdate.Add(existingProduct);

                            result.UpdatedChanges.Add(
                                new ProductChangeInfo
                                {
                                    ProductCode = existingProduct.ProductCode,
                                    ChangeList = changedFields,
                                }
                            );
                        }
                        else
                        {
                            _logger.LogInformation(
                                "跳过更新（无变更）: {ProductCode}",
                                updateDto.ProductCode
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "更新单个商品失败: {ProductCode}",
                            updateDto.ProductCode
                        );
                        result.Errors.Add($"更新商品失败: {updateDto.ProductCode} - {ex.Message}");
                    }
                }

                // 批量更新到数据库
                if (productsToUpdate.Any())
                {
                    await db.Updateable(productsToUpdate).ExecuteCommandAsync();
                    // 查询供应商信息用于填充返回结果
                    var supplierCodes = productsToUpdate
                        .Select(p => p.SupplierCode)
                        .Distinct()
                        .ToList();
                    var suppliers = await db.Queryable<ChinaSupplier>()
                        .Where(s => supplierCodes.Contains(s.SupplierCode))
                        .ToListAsync();
                    var supplierDict = suppliers.ToDictionary(s => s.SupplierCode);

                    // 构建返回的更新商品列表
                    foreach (var product in productsToUpdate)
                    {
                        var productDto = _mapper.Map<DomesticProductDto>(product);
                        if (supplierDict.TryGetValue(product.SupplierCode, out var supplier))
                        {
                            productDto.SupplierName = supplier.SupplierName;
                        }
                        result.UpdatedProducts.Add(productDto);
                    }
                }

                _logger.LogInformation(
                    "批量更新商品完成，更新: {UpdatedCount}, 错误: {ErrorCount}",
                    result.UpdatedProducts.Count,
                    result.Errors.Count
                );
                return ApiResponse<BatchProductOperationResultDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品失败");
                return ApiResponse<BatchProductOperationResultDto>.Error(
                    "批量更新商品失败",
                    "BATCH_UPDATE_PRODUCTS_ERROR"
                );
            }
        }

        public async Task<ApiResponse<bool>> BatchDeleteAsync(List<string> productCodes)
        {
            try
            {
                if (productCodes == null || !productCodes.Any())
                {
                    return ApiResponse<bool>.Error("请选择要删除的商品", "NO_ITEMS_SELECTED");
                }
                var db = _context.Db;
                var result = await db.Updateable<DomesticProduct>()
                    .SetColumns(p => p.IsDeleted == true)
                    .SetColumns(p => p.UpdatedAt == DateTime.Now)
                    .Where(p => productCodes.Contains(p.ProductCode))
                    .ExecuteCommandAsync();
                _logger.LogInformation("批量删除商品成功: {Count} 件", result);
                return ApiResponse<bool>.OK(true, $"成功删除 {result} 件商品");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除商品失败");
                return ApiResponse<bool>.Error("删除失败", "BATCH_DELETE_ERROR");
            }
        }

        public async Task<ApiResponse<List<DomesticSetProductDto>>> GetSetItemsAsync(
            string productCode
        )
        {
            try
            {
                var db = _context.Db;
                var setProduct = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .FirstAsync();
                if (setProduct == null)
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "商品不存在",
                        "PRODUCT_NOT_FOUND"
                    );
                }
                if (setProduct.ProductType != 1)
                {
                    return ApiResponse<List<DomesticSetProductDto>>.Error(
                        "该商品不是套装商品",
                        "NOT_SET_PRODUCT"
                    );
                }
                var items = await db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.ProductCode == productCode && !sp.IsDeleted)
                    .Select(sp => new DomesticSetProductDto
                    {
                        SetProductCode = sp.SetProductCode,
                        ProductCode = sp.ProductCode,
                        ProductNo = sp.ProductNo,
                        SetProductNo = sp.SetProductNo,
                        SetBarcode = sp.SetBarcode,
                        DomesticPrice = sp.DomesticPrice,
                        OEMPrice = sp.OEMPrice,
                        ImportPrice = sp.ImportPrice,
                        Remarks = sp.Remarks,
                        CreatedAt = sp.CreatedAt,
                        UpdatedAt = sp.UpdatedAt,
                        CreatedBy = sp.CreatedBy,
                        UpdatedBy = sp.UpdatedBy,
                        IsDeleted = sp.IsDeleted,
                    })
                    .ToListAsync();
                return ApiResponse<List<DomesticSetProductDto>>.OK(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取套装商品信息失败: ProductCode={ProductCode}",
                    productCode
                );
                return ApiResponse<List<DomesticSetProductDto>>.Error(
                    "获取失败",
                    "GET_SET_ITEMS_ERROR"
                );
            }
        }

        public async Task<ApiResponse<bool>> UpdateSetItemsAsync(
            string productCode,
            List<SetItemUpdateDto> items
        )
        {
            try
            {
                var db = _context.Db;
                var product = await db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .FirstAsync();
                if (product == null)
                {
                    return ApiResponse<bool>.Error("商品不存在", "PRODUCT_NOT_FOUND");
                }
                if (product.ProductType != 1)
                {
                    return ApiResponse<bool>.Error("该商品不是套装商品", "NOT_SET_PRODUCT");
                }

                var existingItems = await db.Queryable<DomesticSetProduct>()
                    .Where(sp => sp.ProductCode == productCode && !sp.IsDeleted)
                    .ToListAsync();
                var existingItemsDict = existingItems.ToDictionary(x => x.SetProductCode);

                var currentUser = "System";
                var now = DateTime.UtcNow;

                var requestedSetProductCodes = new HashSet<string>();
                var itemsToUpdate = new List<DomesticSetProduct>();
                var itemsToInsert = new List<DomesticSetProduct>();

                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.SetProductCode))
                    {
                        requestedSetProductCodes.Add(item.SetProductCode);
                        if (
                            existingItemsDict.TryGetValue(item.SetProductCode, out var existingItem)
                        )
                        {
                            existingItem.SetProductNo =
                                item.SetProductNo ?? existingItem.SetProductNo;
                            existingItem.SetBarcode = item.SetBarcode;
                            existingItem.DomesticPrice = item.DomesticPrice;
                            existingItem.ImportPrice = item.ImportPrice;
                            existingItem.OEMPrice = item.OEMPrice;
                            existingItem.UpdatedAt = now;
                            existingItem.UpdatedBy = currentUser;
                            itemsToUpdate.Add(existingItem);
                        }
                    }
                    else
                    {
                        string setProductNo = item.SetProductNo ?? string.Empty;
                        string? setBarcode = item.SetBarcode;
                        if (
                            string.IsNullOrWhiteSpace(setProductNo)
                            || string.IsNullOrWhiteSpace(setBarcode)
                        )
                        {
                            var productType = (ProductTypeEnum)product.ProductType;
                            var (newItemNumber, newBarcode) =
                                await _itemBarcodeService.GenerateSetItemNumberAndBarcodeAsync(
                                    product.HBProductNo ?? string.Empty,
                                    productType
                                );
                            if (string.IsNullOrWhiteSpace(setProductNo))
                                setProductNo = newItemNumber;
                            if (string.IsNullOrWhiteSpace(setBarcode))
                                setBarcode = newBarcode;
                        }

                        var newItem = new DomesticSetProduct
                        {
                            SetProductCode = UuidHelper.GenerateUuid7(),
                            ProductCode = productCode,
                            ProductNo = product.HBProductNo,
                            SetProductNo = setProductNo,
                            SetBarcode = setBarcode,
                            DomesticPrice = item.DomesticPrice,
                            ImportPrice = item.ImportPrice,
                            OEMPrice = item.OEMPrice,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = currentUser,
                            UpdatedBy = currentUser,
                            IsDeleted = false,
                        };
                        itemsToInsert.Add(newItem);
                        requestedSetProductCodes.Add(newItem.SetProductCode);
                    }
                }

                var itemsToDelete = existingItems
                    .Where(x => !requestedSetProductCodes.Contains(x.SetProductCode))
                    .ToList();
                foreach (var item in itemsToDelete)
                {
                    item.IsDeleted = true;
                    item.UpdatedAt = now;
                    item.UpdatedBy = currentUser;
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    if (itemsToUpdate.Any())
                        await db.Updateable(itemsToUpdate).ExecuteCommandAsync();
                    if (itemsToInsert.Any())
                        await db.Insertable(itemsToInsert).ExecuteCommandAsync();
                    if (itemsToDelete.Any())
                        await db.Updateable(itemsToDelete)
                            .UpdateColumns(x => new
                            {
                                x.IsDeleted,
                                x.UpdatedAt,
                                x.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation(
                        "套装商品更新成功: ProductCode={ProductCode}, Updated={UpdateCount}, Inserted={InsertCount}, Deleted={DeleteCount}",
                        productCode,
                        itemsToUpdate.Count,
                        itemsToInsert.Count,
                        itemsToDelete.Count
                    );
                    return ApiResponse<bool>.OK(true, "保存成功");
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(
                        ex,
                        "套装商品更新事务失败: ProductCode={ProductCode}",
                        productCode
                    );
                    return ApiResponse<bool>.Error("保存失败，请重试", "UPDATE_TRANSACTION_ERROR");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新套装商品信息失败: ProductCode={ProductCode}",
                    productCode
                );
                return ApiResponse<bool>.Error("保存失败", "UPDATE_SET_ITEMS_ERROR");
            }
        }

        public async Task<ApiResponse<BatchCreateSetProductsResultDto>> BatchCreateSetProductsAsync(
            BatchCreateSetProductsDto dto
        )
        {
            try
            {
                var db = _context.Db;
                var result = new BatchCreateSetProductsResultDto();
                var createdProducts = new List<DomesticProductDto>();
                var errors = new List<string>();
                var totalSetItems = 0;

                var supplier = await db.Queryable<ChinaSupplier>()
                    .FirstAsync(s => s.SupplierCode == dto.SupplierCode);
                if (supplier == null)
                {
                    return ApiResponse<BatchCreateSetProductsResultDto>.Error(
                        $"供应商 {dto.SupplierCode} 不存在",
                        "SUPPLIER_NOT_FOUND"
                    );
                }

                if (dto.SetPrices.Count != dto.SetType)
                {
                    return ApiResponse<BatchCreateSetProductsResultDto>.Error(
                        $"套装价格数量({dto.SetPrices.Count})与套装规格({dto.SetType})不匹配",
                        "PRICE_COUNT_MISMATCH"
                    );
                }

                var existingBarcodes = await db.Queryable<DomesticProduct>()
                    .Where(p =>
                        p.SupplierCode == dto.SupplierCode && p.Barcode != null && !p.IsDeleted
                    )
                    .Select(p => p.Barcode!)
                    .ToListAsync();
                var existingSetBarcodes = await db.Queryable<DomesticSetProduct>()
                    .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
                    .Where(
                        (sp, p) =>
                            p.SupplierCode == dto.SupplierCode
                            && sp.SetBarcode != null
                            && !sp.IsDeleted
                    )
                    .Select((sp, p) => sp.SetBarcode!)
                    .ToListAsync();
                existingBarcodes.AddRange(existingSetBarcodes);

                string? prefixName = null;
                if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
                {
                    var prefix = await db.Queryable<ProductPrefixCode>()
                        .Where(p =>
                            p.PrefixCode == dto.PrefixCode
                            && p.SupplierCode == dto.SupplierCode
                            && !p.IsDeleted
                        )
                        .FirstAsync();
                    if (prefix == null)
                    {
                        return ApiResponse<BatchCreateSetProductsResultDto>.Error(
                            "前缀不存在或不属于该供应商",
                            "PREFIX_NOT_FOUND"
                        );
                    }
                    prefixName = prefix.PrefixName;
                }

                try
                {
                    db.Ado.BeginTran();

                    var productNoBarcodeList =
                        await _itemBarcodeService.GenerateBatchItemNumbersAndBarcodesAsync(
                            dto.SupplierCode,
                            ProductTypeEnum.Normal,
                            dto.Products.Count,
                            prefixName
                        );

                    var allDomesticProducts = new List<DomesticProduct>();
                    var allSetProducts = new List<DomesticSetProduct>();
                    var allCreationLogs = new List<DomesticProductCreationLog>();

                    foreach (var product in dto.Products)
                    {
                        try
                        {
                            var index = dto.Products.IndexOf(product);
                            var productNo = productNoBarcodeList[index].itemNumber;
                            var barcode = productNoBarcodeList[index].barcode;

                            var productCode = Guid.NewGuid().ToString();
                            var domesticProduct = new DomesticProduct
                            {
                                ProductCode = productCode,
                                SupplierCode = dto.SupplierCode,
                                ProductName = product.ProductName,
                                EnglishProductName = product.EnglishProductName,
                                ProductSpecification = product.ProductSpecification,
                                HBProductNo = productNo,
                                Barcode = barcode,
                                ProductType = 1,
                                IsActive = true,
                                IsDeleted = false,
                                CreatedAt = DateTime.Now,
                            };
                            allDomesticProducts.Add(domesticProduct);

                            var setItemNumberBarcodeList =
                                await _itemBarcodeService.GenerateBatchSetItemNumbersAndBarcodesAsync(
                                    productNo,
                                    ProductTypeEnum.Set,
                                    dto.SetType
                                );

                            for (int i = 0; i < dto.SetType; i++)
                            {
                                var setPriceItem = dto.SetPrices[i];
                                var setProductNo = setItemNumberBarcodeList[i].itemNumber;
                                var setBarcode = setItemNumberBarcodeList[i].barcode;
                                var setProduct = new DomesticSetProduct
                                {
                                    SetProductCode = Guid.NewGuid().ToString(),
                                    ProductCode = productCode,
                                    ProductNo = productNo,
                                    SetProductNo = setProductNo,
                                    SetBarcode = setBarcode,
                                    DomesticPrice = setPriceItem.DomesticPrice,
                                    ImportPrice = setPriceItem.ImportPrice,
                                    OEMPrice = setPriceItem.OEMPrice,
                                    IsDeleted = false,
                                    CreatedAt = DateTime.Now,
                                };
                                allSetProducts.Add(setProduct);
                            }

                            var creationLog = new DomesticProductCreationLog
                            {
                                LogId = Guid.NewGuid().ToString(),
                                ProductCode = productCode,
                                SupplierCode = dto.SupplierCode,
                                ProductName = product.ProductName,
                                HBProductNo = productNo,
                                PrefixCode = dto.PrefixCode,
                                CreationType = "BatchSetProducts",
                                Remark = $"套装商品批量创建，套装规格：套{dto.SetType}",
                                CreatedAt = DateTime.Now,
                            };
                            allCreationLogs.Add(creationLog);

                            var productDto = _mapper.Map<DomesticProductDto>(domesticProduct);
                            createdProducts.Add(productDto);
                            totalSetItems += dto.SetType;
                            _logger.LogInformation(
                                "成功创建套装商品: {ProductName}, 货号: {ProductNo}, 套装数: {SetCount}",
                                product.ProductName,
                                productNo,
                                dto.SetType
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "创建商品失败: {ProductName}",
                                product.ProductName
                            );
                            errors.Add($"创建商品 '{product.ProductName}' 失败: {ex.Message}");
                        }
                    }

                    if (allDomesticProducts.Any())
                        await db.Insertable(allDomesticProducts).ExecuteCommandAsync();
                    if (allSetProducts.Any())
                        await db.Insertable(allSetProducts).ExecuteCommandAsync();
                    if (allCreationLogs.Any())
                        await db.Insertable(allCreationLogs).ExecuteCommandAsync();

                    db.Ado.CommitTran();

                    result.CreatedProducts = createdProducts;
                    result.SuccessCount = createdProducts.Count;
                    result.FailureCount = dto.Products.Count - createdProducts.Count;
                    result.TotalSetItems = totalSetItems;
                    result.Errors = errors;

                    _logger.LogInformation(
                        "批量创建套装商品完成: 成功{SuccessCount}个, 失败{FailureCount}个, 总套装明细{TotalSetItems}个",
                        result.SuccessCount,
                        result.FailureCount,
                        result.TotalSetItems
                    );
                    return new ApiResponse<BatchCreateSetProductsResultDto>
                    {
                        Success = true,
                        Data = result,
                        Message =
                            $"批量创建完成：成功{result.SuccessCount}个商品，共{result.TotalSetItems}个套装明细",
                    };
                }
                catch (Exception ex)
                {
                    db.Ado.RollbackTran();
                    _logger.LogError(ex, "批量创建套装商品事务失败");
                    return ApiResponse<BatchCreateSetProductsResultDto>.Error(
                        $"批量创建失败: {ex.Message}",
                        "BATCH_CREATE_ERROR"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建套装商品失败");
                return ApiResponse<BatchCreateSetProductsResultDto>.Error(
                    "批量创建套装商品失败",
                    "BATCH_CREATE_SET_PRODUCTS_ERROR"
                );
            }
        }

        // ============== 私有辅助方法（检测变更、生成货号/条码） ==============
        private bool CheckForChanges(
            BatchProductInputDto inputProduct,
            DomesticProduct existingProduct
        )
        {
            if (
                !string.IsNullOrWhiteSpace(inputProduct.ProductName)
                && inputProduct.ProductName != existingProduct.ProductName
            )
                return true;
            if (
                !string.IsNullOrWhiteSpace(inputProduct.EnglishProductName)
                && inputProduct.EnglishProductName != existingProduct.EnglishProductName
            )
                return true;
            if (
                !string.IsNullOrWhiteSpace(inputProduct.Barcode)
                && inputProduct.Barcode != existingProduct.Barcode
            )
                return true;
            if (
                inputProduct.DomesticPrice.HasValue
                && inputProduct.DomesticPrice != existingProduct.DomesticPrice
            )
                return true;
            if (inputProduct.OEMPrice.HasValue && inputProduct.OEMPrice != existingProduct.OEMPrice)
                return true;
            if (
                inputProduct.PackingQuantity.HasValue
                && inputProduct.PackingQuantity != existingProduct.PackingQuantity
            )
                return true;
            if (
                inputProduct.UnitVolume.HasValue
                && inputProduct.UnitVolume != existingProduct.UnitVolume
            )
                return true;
            if (
                inputProduct.MiddlePackQuantity.HasValue
                && inputProduct.MiddlePackQuantity != existingProduct.MiddlePackQuantity
            )
                return true;
            return false;
        }

        private List<string> GetChangeList(
            BatchProductInputDto inputProduct,
            DomesticProduct existingProduct
        )
        {
            var changes = new List<string>();
            if (
                !string.IsNullOrWhiteSpace(inputProduct.ProductName)
                && inputProduct.ProductName != existingProduct.ProductName
            )
                changes.Add("ProductName");
            if (
                !string.IsNullOrWhiteSpace(inputProduct.EnglishProductName)
                && inputProduct.EnglishProductName != existingProduct.EnglishProductName
            )
                changes.Add("EnglishProductName");
            if (
                !string.IsNullOrWhiteSpace(inputProduct.Barcode)
                && inputProduct.Barcode != existingProduct.Barcode
            )
                changes.Add("Barcode");
            if (
                inputProduct.DomesticPrice.HasValue
                && inputProduct.DomesticPrice != existingProduct.DomesticPrice
            )
                changes.Add("DomesticPrice");
            if (inputProduct.OEMPrice.HasValue && inputProduct.OEMPrice != existingProduct.OEMPrice)
                changes.Add("OEMPrice");
            if (
                inputProduct.PackingQuantity.HasValue
                && inputProduct.PackingQuantity != existingProduct.PackingQuantity
            )
                changes.Add("PackingQuantity");
            if (
                inputProduct.UnitVolume.HasValue
                && inputProduct.UnitVolume != existingProduct.UnitVolume
            )
                changes.Add("UnitVolume");
            if (
                inputProduct.MiddlePackQuantity.HasValue
                && inputProduct.MiddlePackQuantity != existingProduct.MiddlePackQuantity
            )
                changes.Add("MiddlePackQuantity");
            return changes;
        }

        public async Task<ApiResponse<SyncResult>> SyncSelectedToHBSalesAsync(
            List<string> productCodes
        )
        {
            try
            {
                var result = new SyncResult { StartTime = DateTime.Now };

                if (productCodes == null || !productCodes.Any())
                {
                    return ApiResponse<SyncResult>.Error("请选择要同步的商品", "NO_PRODUCTS");
                }

                _logger.LogInformation(
                    "[HBSalesSync] 开始同步商品到HBSales，数量: {Count}",
                    productCodes.Count
                );

                var products = await _context
                    .Db.Queryable<DomesticProduct>()
                    .Where(p => productCodes.Contains(p.ProductCode) && !p.IsDeleted)
                    .ToListAsync();

                if (!products.Any())
                {
                    return ApiResponse<SyncResult>.Error(
                        "未找到有效的商品数据",
                        "NO_VALID_PRODUCTS"
                    );
                }

                var successCount = 0;
                var failCount = 0;
                var addedCount = 0;
                var updatedCount = 0;
                var errors = new List<string>();

                try
                {
                    var hbSalesDb = _hbSalesContext.Db;
                    var syncProductCodes = products.Select(p => p.ProductCode).ToList();

                    // 检查HBSales中存在的商品
                    var existingProducts = await hbSalesDb
                        .Queryable<BlazorApp.Shared.Models.HqEntities.CPT_DIC_商品信息字典表>()
                        .Where(x => syncProductCodes.Contains(x.商品编码))
                        .Select(x => x.商品编码)
                        .ToListAsync();

                    var existingCodes = existingProducts.ToHashSet();
                    var notExistingCodes = syncProductCodes
                        .Where(code => !existingCodes.Contains(code))
                        .ToList();

                    // 插入不存在的商品
                    if (notExistingCodes.Any())
                    {
                        var productsToInsert = products
                            .Where(p => notExistingCodes.Contains(p.ProductCode))
                            .Select(
                                p => new BlazorApp.Shared.Models.HqEntities.CPT_DIC_商品信息字典表
                                {
                                    商品编码 = p.ProductCode,
                                    中文名称 = p.ProductName,
                                    英文名称 = p.EnglishProductName,
                                    供应商编码 = p.SupplierCode,
                                    HB货号 = p.HBProductNo,
                                    条形码 = p.Barcode,
                                    规格 = p.ProductSpecification,
                                    商品类型 = p.ProductType,
                                    国内价格 = p.DomesticPrice,
                                    进口价格 = p.ImportPrice,
                                    贴牌价格 = p.OEMPrice,
                                    单件装箱数 = p.PackingQuantity,
                                    单件体积 = p.UnitVolume,
                                    中包数量 = p.MiddlePackQuantity,
                                    商品图片 = p.ProductImage,
                                    FGC_Creator = "HBweb",
                                    FGC_CreateDate = DateTime.Now,
                                    FGC_LastModifier = "HBweb",
                                    FGC_LastModifyDate = DateTime.Now,
                                }
                            )
                            .ToList();

                        var insertResult = await hbSalesDb
                            .Insertable(productsToInsert)
                            .ExecuteCommandAsync();
                        addedCount = insertResult;

                        _logger.LogInformation(
                            "[HBSalesSync] 批量插入新商品，插入数量: {Count}",
                            insertResult
                        );
                    }

                    // 批量更新存在的商品
                    var productsToUpdate = products
                        .Where(p => existingCodes.Contains(p.ProductCode))
                        .ToList();

                    if (productsToUpdate.Any())
                    {
                        // 批量更新（使用ExecuteCommandWithSql进行原生SQL批量更新）
                        var updateSqlParts = new List<string>();
                        var sqlParams = new List<SugarParameter>();

                        // 供应商编码
                        var supplierCodes = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.SupplierCode))
                            .ToList();
                        if (supplierCodes.Any())
                        {
                            var cases = string.Join(
                                "",
                                supplierCodes.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN '{p.SupplierCode.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add(
                                $"[供应商编码] = CASE [商品编码] {cases}ELSE [供应商编码] END"
                            );
                        }

                        // 中文名称
                        var productNames = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.ProductName))
                            .ToList();
                        if (productNames.Any())
                        {
                            var cases = string.Join(
                                "",
                                productNames.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN N'{p.ProductName.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add(
                                $"[中文名称] = CASE [商品编码] {cases}ELSE [中文名称] END"
                            );
                        }

                        // 英文名称
                        var englishNames = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.EnglishProductName))
                            .ToList();
                        if (englishNames.Any())
                        {
                            var cases = string.Join(
                                "",
                                englishNames.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN N'{p.EnglishProductName.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add(
                                $"[英文名称] = CASE [商品编码] {cases}ELSE [英文名称] END"
                            );
                        }

                        // HB货号
                        var hbProductNos = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.HBProductNo))
                            .ToList();
                        if (hbProductNos.Any())
                        {
                            var cases = string.Join(
                                "",
                                hbProductNos.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN '{p.HBProductNo.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add(
                                $"[HB货号] = CASE [商品编码] {cases}ELSE [HB货号] END"
                            );
                        }

                        // 条形码
                        var barcodes = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.Barcode))
                            .ToList();
                        if (barcodes.Any())
                        {
                            var cases = string.Join(
                                "",
                                barcodes.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN '{p.Barcode.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add(
                                $"[条形码] = CASE [商品编码] {cases}ELSE [条形码] END"
                            );
                        }

                        // 规格
                        var specs = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.ProductSpecification))
                            .ToList();
                        if (specs.Any())
                        {
                            var cases = string.Join(
                                "",
                                specs.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN N'{p.ProductSpecification.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add($"[规格] = CASE [商品编码] {cases}ELSE [规格] END");
                        }

                        // 商品类型
                        var productTypes = productsToUpdate.Where(p => p.ProductType > 0).ToList();
                        if (productTypes.Any())
                        {
                            var cases = string.Join(
                                "",
                                productTypes.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.ProductType} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[商品类型] = CASE [商品编码] {cases}ELSE [商品类型] END"
                            );
                        }

                        // 国内价格
                        var domesticPrices = productsToUpdate
                            .Where(p => p.DomesticPrice.HasValue)
                            .ToList();
                        if (domesticPrices.Any())
                        {
                            var cases = string.Join(
                                "",
                                domesticPrices.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.DomesticPrice.Value} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[国内价格] = CASE [商品编码] {cases}ELSE [国内价格] END"
                            );
                        }

                        // 贴牌价格
                        var oemPrices = productsToUpdate.Where(p => p.OEMPrice.HasValue).ToList();
                        if (oemPrices.Any())
                        {
                            var cases = string.Join(
                                "",
                                oemPrices.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.OEMPrice.Value} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[贴牌价格] = CASE [商品编码] {cases}ELSE [贴牌价格] END"
                            );
                        }

                        // 进口价格
                        var importPrices = productsToUpdate
                            .Where(p => p.ImportPrice.HasValue)
                            .ToList();
                        if (importPrices.Any())
                        {
                            var cases = string.Join(
                                "",
                                importPrices.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.ImportPrice.Value} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[进口价格] = CASE [商品编码] {cases}ELSE [进口价格] END"
                            );
                        }

                        // 单件装箱数
                        var packingQuantities = productsToUpdate
                            .Where(p => p.PackingQuantity.HasValue)
                            .ToList();
                        if (packingQuantities.Any())
                        {
                            var cases = string.Join(
                                "",
                                packingQuantities.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.PackingQuantity.Value} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[单件装箱数] = CASE [商品编码] {cases}ELSE [单件装箱数] END"
                            );
                        }

                        // 单件体积
                        var unitVolumes = productsToUpdate
                            .Where(p => p.UnitVolume.HasValue)
                            .ToList();
                        if (unitVolumes.Any())
                        {
                            var cases = string.Join(
                                "",
                                unitVolumes.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.UnitVolume.Value} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[单件体积] = CASE [商品编码] {cases}ELSE [单件体积] END"
                            );
                        }

                        // 中包数量
                        var middlePackQuantities = productsToUpdate
                            .Where(p => p.MiddlePackQuantity.HasValue)
                            .ToList();
                        if (middlePackQuantities.Any())
                        {
                            var cases = string.Join(
                                "",
                                middlePackQuantities.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN {p.MiddlePackQuantity.Value} "
                                )
                            );
                            updateSqlParts.Add(
                                $"[中包数量] = CASE [商品编码] {cases}ELSE [中包数量] END"
                            );
                        }

                        // 商品图片
                        var productImages = productsToUpdate
                            .Where(p => !string.IsNullOrWhiteSpace(p.ProductImage))
                            .ToList();
                        if (productImages.Any())
                        {
                            var cases = string.Join(
                                "",
                                productImages.Select(p =>
                                    $"WHEN '{p.ProductCode}' THEN '{p.ProductImage.Replace("'", "''")}' "
                                )
                            );
                            updateSqlParts.Add(
                                $"[商品图片] = CASE [商品编码] {cases}ELSE [商品图片] END"
                            );
                        }

                        // 审计字段更新
                        updateSqlParts.Add(
                            $"[FGC_LastModifier] = 'HBweb', [FGC_LastModifyDate] = GETDATE()"
                        );

                        if (updateSqlParts.Any())
                        {
                            var codesStr = string.Join(
                                "','",
                                productsToUpdate.Select(p => p.ProductCode.Replace("'", "''"))
                            );
                            var sql =
                                $"UPDATE [CPT_DIC_商品信息字典表] SET {string.Join(", ", updateSqlParts)} WHERE [商品编码] IN ('{codesStr}')";

                            var updateResult = await hbSalesDb.Ado.ExecuteCommandAsync(
                                sql,
                                sqlParams.ToArray()
                            );

                            updatedCount = updateResult;

                            _logger.LogInformation(
                                "[HBSalesSync] 批量更新完成，更新数量: {Count}",
                                updateResult
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HBSalesSync] 批量同步异常");
                    throw;
                }

                result.AddedCount = addedCount;
                result.UpdatedCount = updatedCount;
                result.ErrorCount = failCount;
                result.IsSuccess = failCount == 0;
                result.Message =
                    failCount == 0
                        ? $"同步成功：新增 {addedCount} 条，更新 {updatedCount} 条"
                        : $"同步完成：新增 {addedCount} 条，更新 {updatedCount} 条，失败 {failCount} 条";
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;

                _logger.LogInformation(
                    "[HBSalesSync] 同步完成：成功{Success}条，失败{Fail}条，耗时{Seconds:F1}s",
                    successCount,
                    failCount,
                    result.Duration.TotalSeconds
                );

                return ApiResponse<SyncResult>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HBSalesSync] 同步商品到HBSales异常");
                return ApiResponse<SyncResult>.Error("同步失败", "SYNC_ERROR");
            }
        }
    }
}
