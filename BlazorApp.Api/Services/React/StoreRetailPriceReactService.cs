using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Repositories.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreRetailPriceReactService : IStoreRetailPriceReactService
    {
        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IStoreRetailPriceRepository _storeRetailPriceRepository;
        private readonly ILogger<StoreRetailPriceReactService> _logger;

        public StoreRetailPriceReactService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            IStoreRetailPriceRepository storeRetailPriceRepository,
            ILogger<StoreRetailPriceReactService> logger
        )
        {
            _context = context;
            _hqContext = hqContext;
            _storeRetailPriceRepository = storeRetailPriceRepository;
            _logger = logger;
        }

        public async Task<GridResponseDto<StoreRetailPriceListDto>> GetGridDataAsync(
            GridRequestDto request
        )
        {
            try
            {
                var db = _context.Db;
                var reqId = Guid.NewGuid().ToString("N");
                Action<string, SugarParameter[]> logExec = (sql, pars) =>
                {
                    _logger.LogInformation($"[{reqId}] SQL: {sql}");
                    if (pars != null && pars.Any())
                    {
                        _logger.LogInformation(
                            $"[{reqId}] 参数: {string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}"))}"
                        );
                    }
                };
                Action<string, SugarParameter[]> logExecuted = (sql, pars) =>
                {
                    _logger.LogInformation($"[{reqId}] 执行完成");
                };
                db.Aop.OnLogExecuting = logExec;
                db.Aop.OnLogExecuted = logExecuted;
                var swTotal = Stopwatch.StartNew();
                var sw = Stopwatch.StartNew();
                var pageIndex = (request.StartRow / request.PageSize) + 1;
                var pageSize = request.PageSize;

                var baseQuery = db.Queryable<StoreRetailPrice>()
                    .With(SqlWith.NoLock)
                    .Where(p => p.IsDeleted == false);
                _logger.LogInformation($"[{reqId}] 构建基础查询耗时(ms): {sw.ElapsedMilliseconds}");
                sw.Restart();

                if (
                    request.FilterModel != null
                    && request.FilterModel.TryGetValue("storeCode", out var fStore)
                    && fStore.FilterType?.ToLower() == "text"
                    && (fStore.Type ?? "equals").ToLower() == "equals"
                    && !string.IsNullOrWhiteSpace(fStore.Filter)
                )
                {
                    var v = fStore.Filter.Trim();
                    baseQuery = baseQuery.Where(p => p.StoreCode == v);
                }
                if (
                    request.FilterModel != null
                    && request.FilterModel.TryGetValue("supplierCode", out var fSup)
                    && fSup.FilterType?.ToLower() == "text"
                    && (fSup.Type ?? "equals").ToLower() == "equals"
                    && !string.IsNullOrWhiteSpace(fSup.Filter)
                )
                {
                    var v = fSup.Filter.Trim();
                    baseQuery = baseQuery.Where(p => p.SupplierCode == v);
                }
                if (
                    request.FilterModel != null
                    && request.FilterModel.TryGetValue("productCode", out var fProd)
                    && fProd.FilterType?.ToLower() == "text"
                    && (fProd.Type ?? "equals").ToLower() == "equals"
                    && !string.IsNullOrWhiteSpace(fProd.Filter)
                )
                {
                    var v = fProd.Filter.Trim();
                    baseQuery = baseQuery.Where(p => p.ProductCode == v);
                }

                var query = baseQuery
                    .InnerJoin<Product>(
                        (p, prod) => p.ProductCode == prod.ProductCode && prod.IsDeleted == false
                    )
                    .LeftJoin<HBLocalSupplier>(
                        (p, prod, sup) =>
                            p.SupplierCode == sup.LocalSupplierCode && sup.IsDeleted == false
                    )
                    .LeftJoin<Store>(
                        (p, prod, sup, st) => p.StoreCode == st.StoreCode && st.IsDeleted == false
                    );
                _logger.LogInformation($"[{reqId}] 应用过滤耗时(ms): {sw.ElapsedMilliseconds}");
                sw.Restart();

                if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
                {
                    var keyword = request.GlobalSearch.Trim();
                    var longEnough = keyword.Length >= 2;
                    query = query.Where(
                        (p, prod, sup, st) =>
                            (
                                p.StoreCode != null
                                && (
                                    longEnough
                                        ? p.StoreCode.StartsWith(keyword)
                                        : p.StoreCode.Contains(keyword)
                                )
                            )
                            || (st.StoreName != null && st.StoreName.Contains(keyword))
                            || (
                                p.SupplierCode != null
                                && (
                                    longEnough
                                        ? p.SupplierCode.StartsWith(keyword)
                                        : p.SupplierCode.Contains(keyword)
                                )
                            )
                            || (sup.Name != null && sup.Name.Contains(keyword))
                            || (
                                p.ProductCode != null
                                && (
                                    longEnough
                                        ? p.ProductCode.StartsWith(keyword)
                                        : p.ProductCode.Contains(keyword)
                                )
                            )
                            || (prod.ProductName != null && prod.ProductName.Contains(keyword))
                            || (
                                prod.ItemNumber != null
                                && (
                                    longEnough
                                        ? prod.ItemNumber.StartsWith(keyword)
                                        : prod.ItemNumber.Contains(keyword)
                                )
                            )
                    );
                }

                if (request.FilterModel != null && request.FilterModel.Any())
                {
                    foreach (var kv in request.FilterModel)
                    {
                        var col = kv.Key;
                        var f = kv.Value;
                        if (f == null || f.FilterType == null)
                            continue;
                        var type = f.FilterType.ToLower();
                        if (type == "text" && f.Filter != null)
                        {
                            var v = f.Filter?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(v))
                                continue;
                            var op = (f.Type ?? "contains").ToLower();
                            switch (col)
                            {
                                case "storeCode":
                                    query = ApplyText(query, op, v, x => x.StoreCode);
                                    break;
                                case "storeName":
                                    query = op switch
                                    {
                                        "equals" => query.Where(
                                            (p, prod, sup, st) => st.StoreName == v
                                        ),
                                        "notequal" => query.Where(
                                            (p, prod, sup, st) => st.StoreName != v
                                        ),
                                        "contains" => query.Where(
                                            (p, prod, sup, st) =>
                                                st.StoreName != null && st.StoreName.Contains(v)
                                        ),
                                        "notcontains" => query.Where(
                                            (p, prod, sup, st) =>
                                                st.StoreName == null || !st.StoreName.Contains(v)
                                        ),
                                        "startswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                st.StoreName != null && st.StoreName.StartsWith(v)
                                        ),
                                        "endswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                st.StoreName != null && st.StoreName.EndsWith(v)
                                        ),
                                        "blank" => query.Where(
                                            (p, prod, sup, st) => string.IsNullOrEmpty(st.StoreName)
                                        ),
                                        "notblank" => query.Where(
                                            (p, prod, sup, st) =>
                                                !string.IsNullOrEmpty(st.StoreName)
                                        ),
                                        _ => query,
                                    };
                                    break;
                                case "supplierCode":
                                    query = ApplyText(query, op, v, x => x.SupplierCode);
                                    break;
                                case "supplierName":
                                    query = op switch
                                    {
                                        "equals" => query.Where(
                                            (p, prod, sup, st) => sup.Name == v
                                        ),
                                        "notequal" => query.Where(
                                            (p, prod, sup, st) => sup.Name != v
                                        ),
                                        "contains" => query.Where(
                                            (p, prod, sup, st) =>
                                                sup.Name != null && sup.Name.Contains(v)
                                        ),
                                        "notcontains" => query.Where(
                                            (p, prod, sup, st) =>
                                                sup.Name == null || !sup.Name.Contains(v)
                                        ),
                                        "startswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                sup.Name != null && sup.Name.StartsWith(v)
                                        ),
                                        "endswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                sup.Name != null && sup.Name.EndsWith(v)
                                        ),
                                        "blank" => query.Where(
                                            (p, prod, sup, st) => string.IsNullOrEmpty(sup.Name)
                                        ),
                                        "notblank" => query.Where(
                                            (p, prod, sup, st) => !string.IsNullOrEmpty(sup.Name)
                                        ),
                                        _ => query,
                                    };
                                    break;
                                case "productCode":
                                    query = ApplyText(query, op, v, x => x.ProductCode);
                                    break;
                                case "productName":
                                    query = op switch
                                    {
                                        "equals" => query.Where(
                                            (p, prod, sup, st) => prod.ProductName == v
                                        ),
                                        "notequal" => query.Where(
                                            (p, prod, sup, st) => prod.ProductName != v
                                        ),
                                        "contains" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ProductName != null
                                                && prod.ProductName.Contains(v)
                                        ),
                                        "notcontains" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ProductName == null
                                                || !prod.ProductName.Contains(v)
                                        ),
                                        "startswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ProductName != null
                                                && prod.ProductName.StartsWith(v)
                                        ),
                                        "endswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ProductName != null
                                                && prod.ProductName.EndsWith(v)
                                        ),
                                        "blank" => query.Where(
                                            (p, prod, sup, st) =>
                                                string.IsNullOrEmpty(prod.ProductName)
                                        ),
                                        "notblank" => query.Where(
                                            (p, prod, sup, st) =>
                                                !string.IsNullOrEmpty(prod.ProductName)
                                        ),
                                        _ => query,
                                    };
                                    break;
                                case "itemNumber":
                                    query = op switch
                                    {
                                        "equals" => query.Where(
                                            (p, prod, sup, st) => prod.ItemNumber == v
                                        ),
                                        "notequal" => query.Where(
                                            (p, prod, sup, st) => prod.ItemNumber != v
                                        ),
                                        "contains" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ItemNumber != null
                                                && prod.ItemNumber.Contains(v)
                                        ),
                                        "notcontains" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ItemNumber == null
                                                || !prod.ItemNumber.Contains(v)
                                        ),
                                        "startswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ItemNumber != null
                                                && prod.ItemNumber.StartsWith(v)
                                        ),
                                        "endswith" => query.Where(
                                            (p, prod, sup, st) =>
                                                prod.ItemNumber != null
                                                && prod.ItemNumber.EndsWith(v)
                                        ),
                                        "blank" => query.Where(
                                            (p, prod, sup, st) =>
                                                string.IsNullOrEmpty(prod.ItemNumber)
                                        ),
                                        "notblank" => query.Where(
                                            (p, prod, sup, st) =>
                                                !string.IsNullOrEmpty(prod.ItemNumber)
                                        ),
                                        _ => query,
                                    };
                                    break;
                            }
                        }
                        else if (type == "number" && f.Filter != null)
                        {
                            var sv = f.Filter?.ToString()?.Trim();
                            if (decimal.TryParse(sv, out var dv))
                            {
                                var op = f.Type?.ToLower();
                                switch (col)
                                {
                                    case "purchasePrice":
                                        query = ApplyNumber(
                                            query,
                                            op,
                                            x => x.PurchasePrice,
                                            dv,
                                            f.FilterTo
                                        );
                                        break;
                                    case "storeRetailPriceValue":
                                        query = ApplyNumber(
                                            query,
                                            op,
                                            x => x.StoreRetailPriceValue,
                                            dv,
                                            f.FilterTo
                                        );
                                        break;
                                    case "discountRate":
                                        query = ApplyNumber(
                                            query,
                                            op,
                                            x => x.DiscountRate,
                                            dv,
                                            f.FilterTo
                                        );
                                        break;
                                }
                            }
                        }
                        else if (type == "set" && f.Values != null && f.Values.Any())
                        {
                            switch (col)
                            {
                                case "isActive":
                                    var boolsA = f
                                        .Values.Select(v =>
                                            v.Equals("true", StringComparison.OrdinalIgnoreCase)
                                        )
                                        .ToList();
                                    query = query.Where(
                                        (p, prod, sup, st) => boolsA.Contains(p.IsActive)
                                    );
                                    break;
                                case "isAutoPricing":
                                    var boolsB = f
                                        .Values.Select(v =>
                                            v.Equals("true", StringComparison.OrdinalIgnoreCase)
                                        )
                                        .ToList();
                                    query = query.Where(
                                        (p, prod, sup, st) => boolsB.Contains(p.IsAutoPricing)
                                    );
                                    break;
                                case "isSpecialProduct":
                                    var boolsC = f
                                        .Values.Select(v =>
                                            v.Equals("true", StringComparison.OrdinalIgnoreCase)
                                        )
                                        .ToList();
                                    query = query.Where(
                                        (p, prod, sup, st) => boolsC.Contains(prod.IsSpecialProduct)
                                    );
                                    break;
                            }
                        }
                    }
                }

                if (request.SortModel != null && request.SortModel.Any())
                {
                    var s = request.SortModel.First();
                    var asc = s.Sort.ToLower() == "asc";
                    query = s.ColId switch
                    {
                        "storeCode" => query.OrderBy(
                            (p, prod, sup, st) => p.StoreCode,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "storeName" => query.OrderBy(
                            (p, prod, sup, st) => st.StoreName,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "supplierCode" => query.OrderBy(
                            (p, prod, sup, st) => p.SupplierCode,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "supplierName" => query.OrderBy(
                            (p, prod, sup, st) => sup.Name,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "productCode" => query.OrderBy(
                            (p, prod, sup, st) => p.ProductCode,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "productName" => query.OrderBy(
                            (p, prod, sup, st) => prod.ProductName,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "itemNumber" => query.OrderBy(
                            (p, prod, sup, st) => prod.ItemNumber,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "barcode" => query.OrderBy(
                            (p, prod, sup, st) => prod.Barcode,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "purchasePrice" => query.OrderBy(
                            (p, prod, sup, st) => p.PurchasePrice,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "storeRetailPriceValue" => query.OrderBy(
                            (p, prod, sup, st) => p.StoreRetailPriceValue,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "discountRate" => query.OrderBy(
                            (p, prod, sup, st) => p.DiscountRate,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "isSpecialProduct" => query.OrderBy(
                            (p, prod, sup, st) => prod.IsSpecialProduct,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "isAutoPricing" => query.OrderBy(
                            (p, prod, sup, st) => p.IsAutoPricing,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "isActive" => query.OrderBy(
                            (p, prod, sup, st) => p.IsActive,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "updatedAt" => query.OrderBy(
                            (p, prod, sup, st) => p.UpdatedAt,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "createdAt" => query.OrderBy(
                            (p, prod, sup, st) => p.CreatedAt,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        _ => query.OrderBy((p, prod, sup, st) => p.CreatedAt, OrderByType.Desc),
                    };
                }
                else
                {
                    query = query.OrderBy((p, prod, sup, st) => p.CreatedAt, OrderByType.Desc);
                }
                _logger.LogInformation($"[{reqId}] 排序耗时(ms): {sw.ElapsedMilliseconds}");
                sw.Restart();

                var joinOnlyFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "supplierName",
                    "productName",
                    "itemNumber",
                    "storeName",
                    "isSpecialProduct",
                };
                var hasJoinOnlyFilters =
                    request.FilterModel != null
                    && request.FilterModel.Keys.Any(k => joinOnlyFilterKeys.Contains(k));
                var requiresJoinCount =
                    hasJoinOnlyFilters || !string.IsNullOrWhiteSpace(request.GlobalSearch);

                if (requiresJoinCount)
                {
                    var totalRef = new RefAsync<int>(0);
                    var swQuery = Stopwatch.StartNew();
                    var items = await query
                        .Select(
                            (p, prod, sup, st) =>
                                new StoreRetailPriceListDto
                                {
                                    UUID = p.UUID,
                                    StoreCode = p.StoreCode,
                                    StoreName = st.StoreName,
                                    SupplierCode = p.SupplierCode,
                                    SupplierName = sup.Name,
                                    ProductCode = p.ProductCode,
                                    ProductName = prod.ProductName,
                                    ProductImage = prod.ProductImage,
                                    ItemNumber = prod.ItemNumber,
                                    Barcode = prod.Barcode,
                                    PurchasePrice = p.PurchasePrice,
                                    StoreRetailPriceValue = p.StoreRetailPriceValue,
                                    DiscountRate = p.DiscountRate,
                                    IsActive = p.IsActive,
                                    IsAutoPricing = p.IsAutoPricing,
                                    UpdatedBy = p.UpdatedBy,
                                    UpdatedAt = p.UpdatedAt,
                                    IsSpecialProduct = prod.IsSpecialProduct,
                                }
                        )
                        .ToPageListAsync(pageIndex, pageSize, totalRef);
                    swQuery.Stop();
                    _logger.LogInformation(
                        $"[{reqId}] 分页查询耗时(ms): {swQuery.ElapsedMilliseconds}"
                    );
                    _logger.LogInformation($"[{reqId}] 总耗时(ms): {swTotal.ElapsedMilliseconds}");
                    db.Aop.OnLogExecuting = null;
                    db.Aop.OnLogExecuted = null;
                    return GridResponseDto<StoreRetailPriceListDto>.OK(items, totalRef.Value);
                }
                else
                {
                    var swCount = Stopwatch.StartNew();
                    var total = await baseQuery.CountAsync();
                    swCount.Stop();
                    _logger.LogInformation(
                        $"[{reqId}] 计数耗时(ms): {swCount.ElapsedMilliseconds}"
                    );
                    var swList = Stopwatch.StartNew();
                    var items = await query
                        .Select(
                            (p, prod, sup, st) =>
                                new StoreRetailPriceListDto
                                {
                                    UUID = p.UUID,
                                    StoreCode = p.StoreCode,
                                    StoreName = st.StoreName,
                                    SupplierCode = p.SupplierCode,
                                    SupplierName = sup.Name,
                                    ProductCode = p.ProductCode,
                                    ProductName = prod.ProductName,
                                    ProductImage = prod.ProductImage,
                                    ItemNumber = prod.ItemNumber,
                                    Barcode = prod.Barcode,
                                    PurchasePrice = p.PurchasePrice,
                                    StoreRetailPriceValue = p.StoreRetailPriceValue,
                                    DiscountRate = p.DiscountRate,
                                    IsActive = p.IsActive,
                                    IsAutoPricing = p.IsAutoPricing,
                                    UpdatedBy = p.UpdatedBy,
                                    UpdatedAt = p.UpdatedAt,
                                    IsSpecialProduct = prod.IsSpecialProduct,
                                }
                        )
                        .Skip(request.StartRow)
                        .Take(request.PageSize)
                        .ToListAsync();
                    swList.Stop();
                    _logger.LogInformation(
                        $"[{reqId}] 列表查询耗时(ms): {swList.ElapsedMilliseconds}"
                    );
                    _logger.LogInformation($"[{reqId}] 总耗时(ms): {swTotal.ElapsedMilliseconds}");
                    db.Aop.OnLogExecuting = null;
                    db.Aop.OnLogExecuted = null;
                    return GridResponseDto<StoreRetailPriceListDto>.OK(items, total);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreRetailPrice Grid 查询失败");
                return GridResponseDto<StoreRetailPriceListDto>.Error("查询失败");
            }
        }

        public async Task<ApiResponse<StoreRetailPriceDetailDto>> GetByUuidAsync(string uuid)
        {
            try
            {
                var db = _context.Db;
                var reqId = Guid.NewGuid().ToString("N");
                Action<string, SugarParameter[]> logExec = (sql, pars) =>
                {
                    _logger.LogInformation($"[{reqId}] SQL: {sql}");
                    if (pars != null && pars.Any())
                    {
                        _logger.LogInformation(
                            $"[{reqId}] 参数: {string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}"))}"
                        );
                    }
                };
                Action<string, SugarParameter[]> logExecuted = (sql, pars) =>
                {
                    _logger.LogInformation($"[{reqId}] 执行完成");
                };
                db.Aop.OnLogExecuting = logExec;
                db.Aop.OnLogExecuted = logExecuted;
                var sw = Stopwatch.StartNew();
                var item = await _storeRetailPriceRepository.GetDetailByUuidAsync(uuid);
                _logger.LogInformation($"[{reqId}] 详情查询耗时(ms): {sw.ElapsedMilliseconds}");
                db.Aop.OnLogExecuting = null;
                db.Aop.OnLogExecuted = null;

                if (item == null)
                    return ApiResponse<StoreRetailPriceDetailDto>.Error("数据不存在", "NOT_FOUND");

                return ApiResponse<StoreRetailPriceDetailDto>.OK(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取详情失败");
                return ApiResponse<StoreRetailPriceDetailDto>.Error("获取失败", "GET_ERROR");
            }
        }

        public async Task<ApiResponse<StoreRetailPriceDetailDto>> CreateAsync(
            CreateStoreRetailPriceDto dto,
            string createdBy
        )
        {
            try
            {
                var db = _context.Db;
                var store = await db.Queryable<Store>()
                    .Where(x => x.StoreCode == dto.StoreCode && x.IsDeleted == false)
                    .FirstAsync();
                var supplier = await db.Queryable<HBLocalSupplier>()
                    .Where(x => x.LocalSupplierCode == dto.SupplierCode && x.IsDeleted == false)
                    .FirstAsync();
                var product = await db.Queryable<Product>()
                    .Where(x => x.ProductCode == dto.ProductCode && x.IsDeleted == false)
                    .FirstAsync();
                if (store == null || supplier == null || product == null)
                    return ApiResponse<StoreRetailPriceDetailDto>.Error(
                        "分店/供应商/商品不存在",
                        "REF_NOT_FOUND"
                    );

                var now = DateTime.UtcNow;
                var entity = new StoreRetailPrice
                {
                    UUID = UuidHelper.GenerateUuid7(),
                    StoreCode = dto.StoreCode,
                    ProductCode = dto.ProductCode,
                    SupplierCode = dto.SupplierCode,
                    StoreProductCode = UuidHelper.GenerateUuid7(),
                    PurchasePrice = dto.PurchasePrice,
                    StoreRetailPriceValue = dto.StoreRetailPriceValue,
                    DiscountRate = dto.DiscountRate,
                    IsActive = dto.IsActive ?? true,
                    IsAutoPricing = dto.IsAutoPricing ?? false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy,
                    IsDeleted = false,
                };

                await db.Insertable(entity).ExecuteCommandAsync();

                var detail = await GetByUuidAsync(entity.UUID);
                if (!detail.Success)
                    return ApiResponse<StoreRetailPriceDetailDto>.Error("创建失败", "CREATE_ERROR");
                return detail;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建失败");
                return ApiResponse<StoreRetailPriceDetailDto>.Error("创建失败", "CREATE_ERROR");
            }
        }

        public async Task<ApiResponse<StoreRetailPriceDetailDto>> UpdateAsync(
            string uuid,
            UpdateStoreRetailPriceDto dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var entity = await db.Queryable<StoreRetailPrice>()
                    .Where(x => x.UUID == uuid && x.IsDeleted == false)
                    .FirstAsync();
                if (entity == null)
                    return ApiResponse<StoreRetailPriceDetailDto>.Error("数据不存在", "NOT_FOUND");

                if (string.IsNullOrWhiteSpace(entity.StoreProductCode))
                    entity.StoreProductCode = UuidHelper.GenerateUuid7();
                if (dto.PurchasePrice.HasValue)
                    entity.PurchasePrice = dto.PurchasePrice;
                if (dto.StoreRetailPriceValue.HasValue)
                    entity.StoreRetailPriceValue = dto.StoreRetailPriceValue;
                if (dto.DiscountRate.HasValue)
                    entity.DiscountRate = dto.DiscountRate;
                if (dto.IsActive.HasValue)
                    entity.IsActive = dto.IsActive.Value;
                if (dto.IsAutoPricing.HasValue)
                    entity.IsAutoPricing = dto.IsAutoPricing.Value;
                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = updatedBy;

                await db.Updateable(entity).ExecuteCommandAsync();

                var detail = await GetByUuidAsync(entity.UUID);
                if (!detail.Success)
                    return ApiResponse<StoreRetailPriceDetailDto>.Error("更新失败", "UPDATE_ERROR");
                return detail;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新失败");
                return ApiResponse<StoreRetailPriceDetailDto>.Error("更新失败", "UPDATE_ERROR");
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string uuid, string updatedBy)
        {
            try
            {
                var result = await _storeRetailPriceRepository.SoftDeleteByUuidAsync(
                    uuid,
                    updatedBy
                );
                return ApiResponse<bool>.OK(result > 0, result > 0 ? "删除成功" : "未找到数据");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除失败");
                return ApiResponse<bool>.Error("删除失败", "DELETE_ERROR");
            }
        }

        public async Task<ApiResponse<BatchResultDto>> BatchUpsertAsync(
            List<StoreRetailPriceUpsertItemDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var insertList = new List<StoreRetailPrice>();
                var updateList = new List<StoreRetailPrice>();
                var errors = new List<string>();

                _logger.LogInformation(
                    $"BatchUpsertAsync 开始, 操作人: {updatedBy}, 数据条数: {items.Count}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var uuids = items
                        .Select(it => it.UUID)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Select(u => u!.Trim())
                        .Distinct()
                        .ToList();
                    var storeCodes = items
                        .Select(it => it.StoreCode)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim())
                        .Distinct()
                        .ToList();
                    var productCodes = items
                        .Select(it => it.ProductCode)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p!.Trim())
                        .Distinct()
                        .ToList();

                    _logger.LogInformation(
                        $"查询条件 - UUIDs: {uuids.Count}, StoreCodes: {string.Join(",", storeCodes)}, ProductCodes: {string.Join(",", productCodes)}"
                    );

                    var q = _storeRetailPriceRepository.QueryActive();
                    if (uuids.Any())
                        q = q.Where(x => x.UUID != null && uuids.Contains(x.UUID));
                    if (storeCodes.Any() && productCodes.Any())
                        q = q.Where(x =>
                            x.StoreCode != null
                            && storeCodes.Contains(x.StoreCode)
                            && x.ProductCode != null
                            && productCodes.Contains(x.ProductCode)
                        );
                    var existing = await q.ToListAsync();
                    _logger.LogInformation($"查询到现有记录: {existing.Count} 条");

                    var byUuid = existing
                        .Where(x => !string.IsNullOrWhiteSpace(x.UUID))
                        .ToDictionary(x => x.UUID);

                    // 使用 StoreCode + ProductCode 组合判断是否已存在分店价格
                    var byKey = existing
                        .GroupBy(x => $"{x.StoreCode}|{x.ProductCode}")
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).First()
                        );

                    if (existing.Count != byKey.Count)
                    {
                        _logger.LogWarning(
                            $"检测到重复数据: 查询到 {existing.Count} 条记录,去重后 {byKey.Count} 条"
                        );
                    }

                    foreach (var it in items)
                    {
                        try
                        {
                            StoreRetailPrice? entity = null;
                            if (
                                !string.IsNullOrWhiteSpace(it.UUID)
                                && byUuid.TryGetValue(it.UUID!, out var foundByUuid)
                            )
                                entity = foundByUuid;
                            if (entity == null)
                            {
                                var sc = it.StoreCode?.Trim();
                                var pc = it.ProductCode?.Trim();
                                if (string.IsNullOrWhiteSpace(sc) || string.IsNullOrWhiteSpace(pc))
                                {
                                    throw new Exception("缺少关键键值(storeCode/productCode)");
                                }
                                var key = $"{sc}|{pc}";
                                if (byKey.TryGetValue(key, out var foundByKey))
                                    entity = foundByKey;
                            }
                            if (entity == null)
                            {
                                entity = new StoreRetailPrice
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = it.StoreCode,
                                    ProductCode = it.ProductCode,
                                    SupplierCode = it.SupplierCode,
                                    StoreProductCode = it.StoreCode + it.ProductCode,
                                    PurchasePrice = it.PurchasePrice,
                                    StoreRetailPriceValue = it.StoreRetailPriceValue,
                                    DiscountRate = it.DiscountRate,
                                    IsActive = it.IsActive ?? true,
                                    IsAutoPricing = it.IsAutoPricing ?? false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                    CreatedBy = updatedBy,
                                    UpdatedBy = updatedBy,
                                    IsDeleted = false,
                                };
                                insertList.Add(entity);
                                _logger.LogDebug(
                                    $"准备插入: StoreCode={entity.StoreCode}, ProductCode={entity.ProductCode}, SupplierCode={entity.SupplierCode}"
                                );
                            }
                            else
                            {
                                // 只更新非空字段
                                if (string.IsNullOrWhiteSpace(entity.StoreProductCode))
                                    entity.StoreProductCode = UuidHelper.GenerateUuid7();
                                if (it.PurchasePrice.HasValue)
                                    entity.PurchasePrice = it.PurchasePrice;
                                if (it.StoreRetailPriceValue.HasValue)
                                    entity.StoreRetailPriceValue = it.StoreRetailPriceValue;
                                if (it.DiscountRate.HasValue)
                                    entity.DiscountRate = it.DiscountRate;
                                if (it.IsActive.HasValue)
                                    entity.IsActive = it.IsActive.Value;
                                if (it.IsAutoPricing.HasValue)
                                    entity.IsAutoPricing = it.IsAutoPricing.Value;
                                entity.UpdatedAt = now;
                                entity.UpdatedBy = updatedBy;
                                updateList.Add(entity);
                                _logger.LogDebug(
                                    $"准备更新: UUID={entity.UUID}, StoreCode={entity.StoreCode}, ProductCode={entity.ProductCode}, SupplierCode={entity.SupplierCode}"
                                );
                            }
                        }
                        catch (Exception exItem)
                        {
                            var errorMsg =
                                $"处理数据失败: {exItem.Message}, 数据: {System.Text.Json.JsonSerializer.Serialize(it)}";
                            errors.Add(errorMsg);
                            _logger.LogError(exItem, errorMsg);
                        }
                    }

                    if (insertList.Any())
                    {
                        var inserted = await db.Insertable(insertList).ExecuteCommandAsync();
                        _logger.LogInformation($"插入完成: {inserted} 条");
                    }
                    if (updateList.Any())
                    {
                        var updated = await db.Updateable(updateList).ExecuteCommandAsync();
                        _logger.LogInformation($"更新完成: {updated} 条");
                    }
                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    var result = new BatchResultDto
                    {
                        Inserted = insertList.Count,
                        Updated = updateList.Count,
                        Failed = errors.Count,
                        Errors = errors,
                    };
                    return ApiResponse<BatchResultDto>.OK(result, "批量保存成功");
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量保存事务失败, 事务已回滚");
                    return ApiResponse<BatchResultDto>.Error(
                        $"批量保存失败: {ex.Message}",
                        "BATCH_UPSERT_ERROR"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量保存失败");
                return ApiResponse<BatchResultDto>.Error(
                    $"批量保存失败: {ex.Message}",
                    "BATCH_UPSERT_ERROR"
                );
            }
        }

        public async Task<ApiResponse<bool>> BatchDeleteAsync(List<string> uuids, string updatedBy)
        {
            try
            {
                var db = _context.Db;
                await db.Ado.BeginTranAsync();
                try
                {
                    var count = await _storeRetailPriceRepository.SoftDeleteByUuidsAsync(
                        uuids,
                        updatedBy
                    );
                    await db.Ado.CommitTranAsync();
                    return ApiResponse<bool>.OK(true, "成功删除 " + count + " 条");
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量删除事务失败");
                    return ApiResponse<bool>.Error("批量删除失败", "BATCH_DELETE_ERROR");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除失败");
                return ApiResponse<bool>.Error("批量删除失败", "BATCH_DELETE_ERROR");
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateSpecialFlagAsync(
            List<string> productCodes,
            bool isSpecial,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                await db.Ado.BeginTranAsync();
                try
                {
                    var count = await db.Updateable<Product>()
                        .SetColumns(x => x.IsSpecialProduct == isSpecial)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .SetColumns(x => x.UpdatedAt == DateTime.UtcNow)
                        .Where(x => x.ProductCode != null && productCodes.Contains(x.ProductCode))
                        .ExecuteCommandAsync();
                    await db.Ado.CommitTranAsync();
                    return ApiResponse<bool>.OK(true, "已更新 " + count + " 条产品特殊标记");
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量更新特殊产品标记失败");
                    return ApiResponse<bool>.Error("批量更新失败", "BATCH_UPDATE_SPECIAL_ERROR");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新特殊产品标记失败");
                return ApiResponse<bool>.Error("批量更新失败", "BATCH_UPDATE_SPECIAL_ERROR");
            }
        }

        public async Task<ApiResponse<List<StoreRetailPriceListDto>>> GetListByUuidsAsync(
            List<string> uuids
        )
        {
            try
            {
                var items = await _storeRetailPriceRepository.GetListByUuidsAsync(uuids);

                return ApiResponse<List<StoreRetailPriceListDto>>.OK(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按UUID批量获取列表失败");
                return ApiResponse<List<StoreRetailPriceListDto>>.Error(
                    "查询失败",
                    "BATCH_BY_UUIDS_ERROR"
                );
            }
        }

        public async Task<ApiResponse<BatchResultDto>> BatchDeleteByProductCodesAsync(
            List<string> productCodes,
            List<string> storeCodes,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var errors = new List<string>();
                var deleted = 0;

                _logger.LogInformation(
                    $"BatchDeleteByProductCodesAsync 开始, 操作人: {updatedBy}, 商品编码数量: {productCodes.Count}, 分店数量: {storeCodes.Count}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var q = _storeRetailPriceRepository.QueryActive();

                    if (productCodes.Any())
                    {
                        q = q.Where(x =>
                            x.ProductCode != null && productCodes.Contains(x.ProductCode)
                        );
                    }

                    if (storeCodes.Any())
                    {
                        q = q.Where(x => x.StoreCode != null && storeCodes.Contains(x.StoreCode));
                    }

                    var toDelete = await q.ToListAsync();
                    _logger.LogInformation($"查询到待删除记录: {toDelete.Count} 条");

                    if (toDelete.Any())
                    {
                        foreach (var entity in toDelete)
                        {
                            entity.IsDeleted = true;
                            entity.UpdatedAt = now;
                            entity.UpdatedBy = updatedBy;
                        }

                        deleted = await db.Updateable(toDelete).ExecuteCommandAsync();
                        _logger.LogInformation($"已删除: {deleted} 条记录");
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    var result = new BatchResultDto
                    {
                        Inserted = 0,
                        Updated = 0,
                        Failed = errors.Count,
                        Errors = errors,
                    };

                    return ApiResponse<BatchResultDto>.OK(result, $"成功删除 {deleted} 条记录");
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量删除事务失败, 事务已回滚");
                    return ApiResponse<BatchResultDto>.Error(
                        $"批量删除失败: {ex.Message}",
                        "BATCH_DELETE_BY_PRODUCT_CODES_ERROR"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除失败");
                return ApiResponse<BatchResultDto>.Error(
                    $"批量删除失败: {ex.Message}",
                    "BATCH_DELETE_BY_PRODUCT_CODES_ERROR"
                );
            }
        }

        private ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, Store> ApplyText(
            ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, Store> query,
            string operation,
            string value,
            System.Linq.Expressions.Expression<System.Func<StoreRetailPrice, string?>> selector
        )
        {
            var oldParam = selector.Parameters[0];
            var newParam = System.Linq.Expressions.Expression.Parameter(
                typeof(StoreRetailPrice),
                "p"
            );
            var member = new ParamReplaceVisitor(oldParam, newParam).Visit(selector.Body);
            return operation switch
            {
                "equals" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.Equal(
                            member,
                            System.Linq.Expressions.Expression.Constant(value, typeof(string))
                        ),
                        newParam
                    )
                ),
                "notequal" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.NotEqual(
                            member,
                            System.Linq.Expressions.Expression.Constant(value, typeof(string))
                        ),
                        newParam
                    )
                ),
                "contains" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "notcontains" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.OrElse(
                            System.Linq.Expressions.Expression.Equal(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Not(
                                System.Linq.Expressions.Expression.Call(
                                    member,
                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                    System.Linq.Expressions.Expression.Constant(value)
                                )
                            )
                        ),
                        newParam
                    )
                ),
                "startswith" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "endswith" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "blank" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.Call(
                            typeof(string),
                            "IsNullOrEmpty",
                            null,
                            member
                        ),
                        newParam
                    )
                ),
                "notblank" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.Not(
                            System.Linq.Expressions.Expression.Call(
                                typeof(string),
                                "IsNullOrEmpty",
                                null,
                                member
                            )
                        ),
                        newParam
                    )
                ),
                _ => query,
            };
        }

        private bool ApplyTextEval(string? fieldValue, string operation, string value)
        {
            switch (operation)
            {
                case "equals":
                    return fieldValue == value;
                case "notequal":
                    return fieldValue != value;
                case "contains":
                    return fieldValue?.Contains(value) ?? false;
                case "notcontains":
                    return !(fieldValue?.Contains(value) ?? false);
                case "startswith":
                    return fieldValue?.StartsWith(value) ?? false;
                case "endswith":
                    return fieldValue?.EndsWith(value) ?? false;
                case "blank":
                    return string.IsNullOrEmpty(fieldValue);
                case "notblank":
                    return !string.IsNullOrEmpty(fieldValue);
                default:
                    return true;
            }
        }

        private sealed class ParamReplaceVisitor : System.Linq.Expressions.ExpressionVisitor
        {
            private readonly System.Linq.Expressions.ParameterExpression _source;
            private readonly System.Linq.Expressions.ParameterExpression _target;

            public ParamReplaceVisitor(
                System.Linq.Expressions.ParameterExpression source,
                System.Linq.Expressions.ParameterExpression target
            )
            {
                _source = source;
                _target = target;
            }

            protected override System.Linq.Expressions.Expression VisitParameter(
                System.Linq.Expressions.ParameterExpression node
            )
            {
                return node == _source ? _target : base.VisitParameter(node);
            }
        }

        private ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, Store> ApplyNumber(
            ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, Store> query,
            string? operation,
            System.Linq.Expressions.Expression<System.Func<StoreRetailPrice, decimal?>> selector,
            decimal value,
            object? filterTo
        )
        {
            var oldParam = selector.Parameters[0];
            var newParam = System.Linq.Expressions.Expression.Parameter(
                typeof(StoreRetailPrice),
                "p"
            );
            var member = new ParamReplaceVisitor(oldParam, newParam).Visit(selector.Body);
            var constantValue = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Constant(value),
                typeof(decimal?)
            );
            System.Linq.Expressions.Expression? condition = operation switch
            {
                "equals" => System.Linq.Expressions.Expression.Equal(member, constantValue),
                "notequal" => System.Linq.Expressions.Expression.NotEqual(member, constantValue),
                "lessthan" => System.Linq.Expressions.Expression.LessThan(member, constantValue),
                "lessthanorequal" => System.Linq.Expressions.Expression.LessThanOrEqual(
                    member,
                    constantValue
                ),
                "greaterthan" => System.Linq.Expressions.Expression.GreaterThan(
                    member,
                    constantValue
                ),
                "greaterthanorequal" => System.Linq.Expressions.Expression.GreaterThanOrEqual(
                    member,
                    constantValue
                ),
                "inrange" => filterTo != null
                && decimal.TryParse(filterTo.ToString(), out var toValue)
                    ? System.Linq.Expressions.Expression.AndAlso(
                        System.Linq.Expressions.Expression.GreaterThanOrEqual(
                            member,
                            constantValue
                        ),
                        System.Linq.Expressions.Expression.LessThanOrEqual(
                            member,
                            System.Linq.Expressions.Expression.Convert(
                                System.Linq.Expressions.Expression.Constant(toValue),
                                typeof(decimal?)
                            )
                        )
                    )
                    : null,
                _ => null,
            };
            if (condition == null)
                return query;
            var lambda = System.Linq.Expressions.Expression.Lambda<System.Func<
                StoreRetailPrice,
                bool
            >>(condition, newParam);
            return query.Where(lambda);
        }

        private ISugarQueryable<
            StoreRetailPrice,
            Product,
            HBLocalSupplier,
            ChinaSupplier,
            Store
        > ApplyNumber5(
            ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, ChinaSupplier, Store> query,
            string? operation,
            System.Linq.Expressions.Expression<System.Func<StoreRetailPrice, decimal?>> selector,
            decimal value,
            object? filterTo
        )
        {
            var parameter = selector.Parameters[0];
            var member = selector.Body;
            var constantValue = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Constant(value),
                typeof(decimal?)
            );
            System.Linq.Expressions.Expression? condition = operation switch
            {
                "equals" => System.Linq.Expressions.Expression.Equal(member, constantValue),
                "notequal" => System.Linq.Expressions.Expression.NotEqual(member, constantValue),
                "lessthan" => System.Linq.Expressions.Expression.LessThan(member, constantValue),
                "lessthanorequal" => System.Linq.Expressions.Expression.LessThanOrEqual(
                    member,
                    constantValue
                ),
                "greaterthan" => System.Linq.Expressions.Expression.GreaterThan(
                    member,
                    constantValue
                ),
                "greaterthanorequal" => System.Linq.Expressions.Expression.GreaterThanOrEqual(
                    member,
                    constantValue
                ),
                "inrange" => filterTo != null
                && decimal.TryParse(filterTo.ToString(), out var toValue)
                    ? System.Linq.Expressions.Expression.AndAlso(
                        System.Linq.Expressions.Expression.GreaterThanOrEqual(
                            member,
                            constantValue
                        ),
                        System.Linq.Expressions.Expression.LessThanOrEqual(
                            member,
                            System.Linq.Expressions.Expression.Convert(
                                System.Linq.Expressions.Expression.Constant(toValue),
                                typeof(decimal?)
                            )
                        )
                    )
                    : null,
                _ => null,
            };
            if (condition == null)
                return query;
            var lambda = System.Linq.Expressions.Expression.Lambda<System.Func<
                StoreRetailPrice,
                bool
            >>(condition, parameter);
            return query.Where(lambda);
        }

        public async Task<ApiResponse<BatchResultDto>> UpsertForActiveStoresAsync(
            List<StoreRetailPriceUpsertForActiveStoresItemDto> items,
            string updatedBy
        )
        {
            try
            {
                var stores = await _storeRetailPriceRepository.GetActiveStoreCodesAsync();
                if (stores == null)
                    stores = new List<string>();
                var upserts = new List<StoreRetailPriceUpsertItemDto>();
                foreach (var it in items)
                {
                    foreach (var sc in stores)
                    {
                        upserts.Add(
                            new StoreRetailPriceUpsertItemDto
                            {
                                StoreCode = sc,
                                ProductCode = it.ProductCode,
                                SupplierCode = "200",
                                PurchasePrice = it.PurchasePrice,
                                StoreRetailPriceValue = it.StoreRetailPriceValue,
                                DiscountRate = it.DiscountRate,
                                IsActive = it.IsActive ?? true,
                                IsAutoPricing = it.IsAutoPricing ?? false,
                            }
                        );
                    }
                }
                return await BatchUpsertAsync(upserts, updatedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启用分店批量上架失败");
                return ApiResponse<BatchResultDto>.Error(
                    "批量保存失败",
                    "UPSERT_ACTIVE_STORES_ERROR"
                );
            }
        }

        private ISugarQueryable<
            StoreRetailPrice,
            Product,
            HBLocalSupplier,
            ChinaSupplier,
            Store
        > ApplyText5(
            ISugarQueryable<StoreRetailPrice, Product, HBLocalSupplier, ChinaSupplier, Store> query,
            string operation,
            string value,
            System.Linq.Expressions.Expression<System.Func<StoreRetailPrice, string?>> selector
        )
        {
            var oldParam = selector.Parameters[0];
            var newParam = System.Linq.Expressions.Expression.Parameter(
                typeof(StoreRetailPrice),
                "p"
            );
            var member = new ParamReplaceVisitor(oldParam, newParam).Visit(selector.Body);
            return operation switch
            {
                "equals" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.Equal(
                            member,
                            System.Linq.Expressions.Expression.Constant(value, typeof(string))
                        ),
                        newParam
                    )
                ),
                "notequal" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.NotEqual(
                            member,
                            System.Linq.Expressions.Expression.Constant(value, typeof(string))
                        ),
                        newParam
                    )
                ),
                "contains" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "notcontains" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.OrElse(
                            System.Linq.Expressions.Expression.Equal(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Not(
                                System.Linq.Expressions.Expression.Call(
                                    member,
                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                    System.Linq.Expressions.Expression.Constant(value)
                                )
                            )
                        ),
                        newParam
                    )
                ),
                "startswith" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "endswith" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "blank" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.Call(
                            typeof(string),
                            "IsNullOrEmpty",
                            null,
                            member
                        ),
                        newParam
                    )
                ),
                "notblank" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<StoreRetailPrice, bool>>(
                        System.Linq.Expressions.Expression.Not(
                            System.Linq.Expressions.Expression.Call(
                                typeof(string),
                                "IsNullOrEmpty",
                                null,
                                member
                            )
                        ),
                        newParam
                    )
                ),
                _ => query,
            };
        }

        public async Task<ApiResponse<SyncRetailPriceFromHqResult>> SyncFromHqAsync(
            List<string>? storeCodes,
            DateTime? startDate
        )
        {
            var sw = Stopwatch.StartNew();
            var result = new SyncRetailPriceFromHqResult();
            var db = _context.Db;
            var originalTimeout = db.Ado.CommandTimeOut;
            db.Ado.CommandTimeOut = 300;

            try
            {
                var effectiveStartDate = startDate ?? DateTime.Now.AddDays(-30);
                _logger.LogInformation(
                    "开始从HQ增量同步零售价，起始日期: {StartDate}，指定分店: {StoreCount}",
                    effectiveStartDate,
                    storeCodes?.Count ?? 0
                );

                _hqContext.CheckConnection();

                var activeStoreCodes = await db.Queryable<Store>()
                    .Where(s => s.IsActive && !s.IsDeleted)
                    .Select(s => s.StoreCode!)
                    .ToListAsync();

                var targetStoreCodes = (storeCodes != null && storeCodes.Any())
                    ? storeCodes.Intersect(activeStoreCodes).ToList()
                    : activeStoreCodes;

                var hqQuery = _hqContext
                    .DIC_商品零售价表Db.AsQueryable()
                    .Where(r =>
                        r.H使用状态 == true
                        && r.FGC_LastModifyDate >= effectiveStartDate
                        && targetStoreCodes.Contains(r.H分店代码)
                    );

                var hqPrices = await hqQuery.ToListAsync();
                _logger.LogInformation("HQ查询到 {Count} 条零售价记录", hqPrices.Count);

                if (!hqPrices.Any())
                {
                    return ApiResponse<SyncRetailPriceFromHqResult>.OK(result);
                }

                var hqStoreCodes = hqPrices.Select(r => r.H分店代码).Distinct().ToList();
                var hqProductCodes = hqPrices.Select(r => r.H商品编码).Distinct().ToList();

                var existingDict = new Dictionary<(string, string), StoreRetailPrice>();
                foreach (var storeBatch in hqStoreCodes.Chunk(100))
                {
                    var batch = await db.Queryable<StoreRetailPrice>()
                        .Where(p =>
                            storeBatch.Contains(p.StoreCode!)
                            && !p.IsDeleted
                            && hqProductCodes.Contains(p.ProductCode!)
                        )
                        .ToListAsync();
                    foreach (var item in batch)
                    {
                        if (item.StoreCode != null && item.ProductCode != null)
                            existingDict[(item.StoreCode, item.ProductCode)] = item;
                    }
                }

                var toAdd = new List<StoreRetailPrice>();
                var toUpdate = new List<StoreRetailPrice>();
                var now = DateTime.UtcNow;

                foreach (var hq in hqPrices)
                {
                    var key = (hq.H分店代码, hq.H商品编码);
                    var entity = MapHqToLocal(hq, now);

                    if (existingDict.TryGetValue(key, out var existing))
                    {
                        if (hq.FGC_LastModifyDate > (existing.UpdatedAt ?? DateTime.MinValue))
                        {
                            existing.StoreProductCode = entity.StoreProductCode;
                            existing.SupplierCode = entity.SupplierCode;
                            existing.PurchasePrice = entity.PurchasePrice;
                            existing.StoreRetailPriceValue = entity.StoreRetailPriceValue;
                            existing.DiscountRate = entity.DiscountRate;
                            existing.IsAutoPricing = entity.IsAutoPricing;
                            existing.IsSpecialProduct = entity.IsSpecialProduct;
                            existing.IsActive = true;
                            existing.UpdatedAt = now;
                            toUpdate.Add(existing);
                        }
                    }
                    else
                    {
                        toAdd.Add(entity);
                    }
                }

                const int batchSize = 1000;
                foreach (var batch in toAdd.Chunk(batchSize))
                {
                    await db.Insertable(batch.ToList()).ExecuteCommandAsync();
                }
                result.AddedCount = toAdd.Count;

                foreach (var batch in toUpdate.Chunk(batchSize))
                {
                    await db.Updateable(batch.ToList()).ExecuteCommandAsync();
                }
                result.UpdatedCount = toUpdate.Count;

                result.TotalProcessed = toAdd.Count + toUpdate.Count;
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;

                _logger.LogInformation(
                    "HQ零售价同步完成：新增 {Added}，更新 {Updated}，耗时 {Ms}ms",
                    result.AddedCount,
                    result.UpdatedCount,
                    result.DurationMs
                );

                return ApiResponse<SyncRetailPriceFromHqResult>.OK(result);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
                result.Errors.Add(ex.Message);
                _logger.LogError(ex, "从HQ同步零售价失败");
                return ApiResponse<SyncRetailPriceFromHqResult>.Error(
                    "从HQ同步零售价失败: " + ex.Message
                );
            }
            finally
            {
                db.Ado.CommandTimeOut = originalTimeout;
            }
        }

        private static StoreRetailPrice MapHqToLocal(DIC_商品零售价表 hq, DateTime now)
        {
            return new StoreRetailPrice
            {
                UUID = hq.HGUID ?? UuidHelper.GenerateUuid7(),
                StoreCode = hq.H分店代码,
                ProductCode = hq.H商品编码,
                StoreProductCode = hq.H分店商品编码,
                SupplierCode = hq.H分店供应商编码,
                PurchasePrice = hq.H进货价,
                StoreRetailPriceValue = hq.H分店零售价,
                DiscountRate = hq.H动态销售数量 > 0 ? (decimal?)hq.H动态销售数量 : null,
                IsActive = hq.H使用状态,
                IsAutoPricing = hq.H是否自动定价,
                IsSpecialProduct = hq.H是否特殊商品,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false,
            };
        }
    }
}
