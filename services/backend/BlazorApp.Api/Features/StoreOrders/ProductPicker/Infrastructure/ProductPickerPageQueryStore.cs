using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

internal sealed class ProductPickerPageQueryStore(
    SqlSugarContext context,
    ProductPickerProductEnricher productEnricher,
    IProductPickerLocationLookup locationLookup,
    ILogger<ProductPickerPageQueryStore> logger
)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<PagedListReactDto<StoreOrderProductDto>> GetPagedListAsync(
        ProductPickerPageInput input
    )
    {
        var filter = input.Filter;
        var normalizedGrades = input.NormalizedGrades.ToList();
        var totalStopwatch = Stopwatch.StartNew();

        if (filter.ExcludeExistingWarehouseProducts)
        {
            return await GetProductMasterRowsNotInWarehouseAsync(filter, normalizedGrades);
        }

        if (!string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID))
        {
            return await GetWarehouseProductRowsForOrderPickerAsync(filter, normalizedGrades);
        }

        if (ProductPickerRules.IsDefaultHomePageProductFilter(filter, normalizedGrades))
        {
            return await GetDefaultHomePageProductPageAsync(filter, normalizedGrades);
        }

        var includeInactiveForQuickAdd =
            ProductPickerRules.ShouldIncludeInactiveWarehouseProductsForQuickAdd(filter);
        var query = ProductPickerQueryBuilder.CreateDefaultWarehouseProductQuery(
            _db,
            includeInactiveForQuickAdd
        );
        var searchFilter = ProductPickerRules.CreateProductSearchFilter(filter);
        var categoryFilterCount = 0;

        if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
        {
            var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
            categoryFilterCount = categoryIds.Count;
            logger.LogInformation(
                "Category Filter: Found {Count} categories (including self) for root {CategoryGUID}",
                categoryIds.Count,
                filter.CategoryGUID
            );
            query = query.Where(
                (product, warehouseProduct, category, supplier) =>
                    product.WarehouseCategoryGUID != null
                    && categoryIds.Contains(product.WarehouseCategoryGUID)
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.LocalSupplierCode))
        {
            var supplierCode = filter.LocalSupplierCode.Trim();
            query = query.Where(
                (product, warehouseProduct, category, supplier) =>
                    product.LocalSupplierCode == supplierCode
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
        {
            var supplierCode = filter.SupplierCode.Trim();
            query = query.Where(
                (product, warehouseProduct, category, supplier) =>
                    SqlFunc.Subqueryable<DomesticProduct>()
                        .Where(domesticProduct =>
                            domesticProduct.ProductCode == product.ProductCode
                            && domesticProduct.SupplierCode == supplierCode
                            && !domesticProduct.IsDeleted
                        )
                        .Any()
            );
        }

        var locationProductCodes = await LookupManualLocationProductCodesAsync(filter);
        query = ProductPickerQueryBuilder.ApplyWarehouseProductSearch(
            query,
            searchFilter,
            locationProductCodes
        );

        if (normalizedGrades.Count > 0)
        {
            query = query.Where(
                (product, warehouseProduct, category, supplier) =>
                    SqlFunc.Subqueryable<ProductGrade>()
                        .Where(grade =>
                            grade.ProductCode == product.ProductCode
                            && !grade.IsDeleted
                            && normalizedGrades.Contains(grade.Grade)
                        )
                        .Any()
            );
        }

        query = ProductPickerQueryBuilder.ApplyWarehouseProductColumnFilters(
            query,
            filter.ColumnFilters
        );
        query = ProductPickerQueryBuilder.ApplyWarehouseProductSort(query, filter);

        var countStopwatch = Stopwatch.StartNew();
        var total = await query.Clone().CountAsync();
        countStopwatch.Stop();

        var listStopwatch = Stopwatch.StartNew();
        var items = await QueryWarehouseProductItemsByPagedProductCodesAsync(query, filter);
        listStopwatch.Stop();

        var gradeStopwatch = Stopwatch.StartNew();
        await productEnricher.PopulateGradesAsync(_db, items, normalizedGrades);
        gradeStopwatch.Stop();

        totalStopwatch.Stop();
        logger.LogInformation(
            "[shop-home-perf] stage=products.service.done pageNumber={PageNumber} pageSize={PageSize} category={CategoryGUID} categoryCount={CategoryCount} searchMode={SearchMode} keywordLength={KeywordLength} gradeCount={GradeCount} total={Total} itemCount={ItemCount} countMs={CountMs} listMs={ListMs} gradeMs={GradeMs} totalMs={TotalMs}",
            filter.PageNumber,
            filter.PageSize,
            filter.CategoryGUID,
            categoryFilterCount,
            searchFilter.Mode,
            filter.ItemNumber?.Length ?? 0,
            normalizedGrades.Count,
            total,
            items.Count,
            countStopwatch.ElapsedMilliseconds,
            listStopwatch.ElapsedMilliseconds,
            gradeStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds
        );

        return new PagedListReactDto<StoreOrderProductDto>
        {
            Items = items,
            Total = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    internal async Task<PagedListReactDto<StoreOrderProductDto>> GetHomePageAsync(
        ProductPickerHomePageInput input,
        CancellationToken cancellationToken = default
    )
    {
        // 使用独立连接和短命令超时，后台预热不能占住正常请求的共享连接。
        using var homePageDb = CreateHomePageQueryConnection();
        var originalTimeout = homePageDb.Ado.CommandTimeOut;

        try
        {
            homePageDb.Ado.CommandTimeOut = originalTimeout > 0
                ? Math.Min(
                    originalTimeout,
                    ProductPickerRules.HomePageQueryCommandTimeoutSeconds
                )
                : ProductPickerRules.HomePageQueryCommandTimeoutSeconds;

            var total = 0;
            if (input.Mode == ProductPickerHomePageMode.AccurateCache)
            {
                total = await ProductPickerQueryBuilder
                    .CreateDefaultWarehouseProductBaseQuery(homePageDb)
                    .CountAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var items = await QueryDefaultHomePageProductItemsAsync(
                homePageDb,
                pageNumber: 1,
                input.PageSize,
                cancellationToken
            );

            cancellationToken.ThrowIfCancellationRequested();
            await productEnricher.PopulateGradesAsync(
                homePageDb,
                items,
                Array.Empty<string>(),
                cancellationToken
            );

            return new PagedListReactDto<StoreOrderProductDto>
            {
                Items = items,
                // 轻量键不额外 Count；准确首页键仍返回数据库总数。
                Total = input.Mode == ProductPickerHomePageMode.LightweightWarmUp
                    ? items.Count
                    : total,
                PageNumber = 1,
                PageSize = input.PageSize,
            };
        }
        finally
        {
            homePageDb.Ado.CommandTimeOut = originalTimeout;
        }
    }

    private async Task<PagedListReactDto<StoreOrderProductDto>>
        GetProductMasterRowsNotInWarehouseAsync(
            StoreOrderFilterDto filter,
            List<string> normalizedGrades
        )
    {
        var query = _db.Queryable<Product>()
            .LeftJoin<WarehouseCategory>(
                (product, category) =>
                    product.WarehouseCategoryGUID == category.CategoryGUID
            )
            .LeftJoin<HBLocalSupplier>(
                (product, category, supplier) =>
                    product.LocalSupplierCode == supplier.LocalSupplierCode
                    && !supplier.IsDeleted
            )
            .Where(
                (product, category, supplier) =>
                    product.IsActive
                    && !product.IsDeleted
                    && product.ProductCode != null
                    && !SqlFunc.Subqueryable<WarehouseProduct>()
                        .Where(warehouseProduct =>
                            warehouseProduct.ProductCode == product.ProductCode
                            && !warehouseProduct.IsDeleted
                        )
                        .Any()
            );

        if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
        {
            var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
            query = query.Where(
                (product, category, supplier) =>
                    product.WarehouseCategoryGUID != null
                    && categoryIds.Contains(product.WarehouseCategoryGUID)
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.LocalSupplierCode))
        {
            var supplierCode = filter.LocalSupplierCode.Trim();
            query = query.Where(
                (product, category, supplier) =>
                    product.LocalSupplierCode == supplierCode
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.ExcludeOrderGUID))
        {
            var orderGuid = filter.ExcludeOrderGUID.Trim();
            query = query.Where(
                (product, category, supplier) =>
                    !SqlFunc.Subqueryable<WareHouseOrderDetails>()
                        .Where(detail =>
                            detail.OrderGUID == orderGuid
                            && detail.ProductCode == product.ProductCode
                            && !detail.IsDeleted
                        )
                        .Any()
            );
        }

        query = ProductPickerQueryBuilder.ApplyProductMasterSearch(
            query,
            ProductPickerRules.CreateProductSearchFilter(filter)
        );

        if (normalizedGrades.Count > 0)
        {
            query = query.Where(
                (product, category, supplier) =>
                    SqlFunc.Subqueryable<ProductGrade>()
                        .Where(grade =>
                            grade.ProductCode == product.ProductCode
                            && !grade.IsDeleted
                            && normalizedGrades.Contains(grade.Grade)
                        )
                        .Any()
            );
        }

        query = ProductPickerQueryBuilder.ApplyProductMasterColumnFilters(
            query,
            filter.ColumnFilters
        );
        query = ProductPickerQueryBuilder.ApplyProductMasterSort(query, filter);

        var total = await query.CountAsync();
        var items = await query
            .Select(
                (product, category, supplier) =>
                    new StoreOrderProductDto
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        CategoryName = category.CategoryName,
                        WarehouseCategoryGUID = product.WarehouseCategoryGUID,
                        LocalSupplierCode = product.LocalSupplierCode,
                        LocalSupplierName = supplier.Name,
                        OEMPrice = 0,
                        MinOrderQuantity = 1,
                        StockQuantity = 0,
                        PackQty = product.MiddlePackageQuantity,
                        ImportPrice = product.PurchasePrice ?? 0,
                    }
            )
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        await productEnricher.PopulateGradesAsync(_db, items, normalizedGrades);

        return new PagedListReactDto<StoreOrderProductDto>
        {
            Items = items,
            Total = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    private async Task<PagedListReactDto<StoreOrderProductDto>>
        GetWarehouseProductRowsForOrderPickerAsync(
            StoreOrderFilterDto filter,
            List<string> normalizedGrades
        )
    {
        var orderGuid = filter.ExcludeOrderGUID!.Trim();
        var query = _db.Queryable<Product>()
            .InnerJoin<WarehouseProduct>(
                (product, warehouseProduct) =>
                    product.ProductCode == warehouseProduct.ProductCode
            )
            .LeftJoin<WarehouseCategory>(
                (product, warehouseProduct, category) =>
                    product.WarehouseCategoryGUID == category.CategoryGUID
            )
            .Where(
                (product, warehouseProduct, category) =>
                    !product.IsDeleted
                    && !warehouseProduct.IsDeleted
                    && product.ProductCode != null
                    && !SqlFunc.Subqueryable<WareHouseOrderDetails>()
                        .Where(detail =>
                            detail.OrderGUID == orderGuid
                            && detail.ProductCode == product.ProductCode
                            && !detail.IsDeleted
                        )
                        .Any()
            );

        if (!string.IsNullOrWhiteSpace(filter.CategoryGUID))
        {
            var categoryIds = GetAllSubCategoryIds(filter.CategoryGUID);
            query = query.Where(
                (product, warehouseProduct, category) =>
                    product.WarehouseCategoryGUID != null
                    && categoryIds.Contains(product.WarehouseCategoryGUID)
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
        {
            var supplierCode = filter.SupplierCode.Trim();
            query = query.Where(
                (product, warehouseProduct, category) =>
                    SqlFunc.Subqueryable<DomesticProduct>()
                        .Where(domesticProduct =>
                            domesticProduct.SupplierCode == supplierCode
                            && !domesticProduct.IsDeleted
                            && SqlFunc.Subqueryable<ChinaSupplier>()
                                .Where(supplier =>
                                    supplier.SupplierCode == domesticProduct.SupplierCode
                                    && !supplier.IsDeleted
                                )
                                .Any()
                            && (
                                domesticProduct.ProductCode == product.ProductCode
                                || (
                                    domesticProduct.HBProductNo != null
                                    && product.ItemNumber != null
                                    && domesticProduct.HBProductNo == product.ItemNumber
                                )
                                || (
                                    domesticProduct.Barcode != null
                                    && product.Barcode != null
                                    && domesticProduct.Barcode == product.Barcode
                                )
                            )
                        )
                        .Any()
            );
        }

        query = ProductPickerQueryBuilder.ApplyOrderPickerProductSearch(
            query,
            ProductPickerRules.CreateProductSearchFilter(filter)
        );

        if (normalizedGrades.Count > 0)
        {
            query = query.Where(
                (product, warehouseProduct, category) =>
                    SqlFunc.Subqueryable<ProductGrade>()
                        .Where(grade =>
                            grade.ProductCode == product.ProductCode
                            && !grade.IsDeleted
                            && normalizedGrades.Contains(grade.Grade)
                        )
                        .Any()
            );
        }

        query = ProductPickerQueryBuilder.ApplyWarehouseProductColumnFilters(
            query,
            filter.ColumnFilters
        );
        query = ProductPickerQueryBuilder.ApplyWarehouseProductSort(query, filter);

        var total = await query.CountAsync();
        var items = await query
            .Select(
                (product, warehouseProduct, category) =>
                    new StoreOrderProductDto
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        CategoryName = category.CategoryName,
                        WarehouseCategoryGUID = product.WarehouseCategoryGUID,
                        LocalSupplierCode = product.LocalSupplierCode,
                        OEMPrice = warehouseProduct.OEMPrice,
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                        StockQuantity = warehouseProduct.StockQuantity ?? 0,
                        PackQty = product.MiddlePackageQuantity,
                        ImportPrice = warehouseProduct.ImportPrice,
                    }
            )
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        await productEnricher.PopulateDomesticSuppliersForOrderPickerAsync(items);
        await productEnricher.PopulateGradesAsync(_db, items, normalizedGrades);

        return new PagedListReactDto<StoreOrderProductDto>
        {
            Items = items,
            Total = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    private async Task<PagedListReactDto<StoreOrderProductDto>>
        GetDefaultHomePageProductPageAsync(
            StoreOrderFilterDto filter,
            List<string> normalizedGrades
        )
    {
        var totalStopwatch = Stopwatch.StartNew();
        var countStopwatch = Stopwatch.StartNew();
        var total = await ProductPickerQueryBuilder
            .CreateDefaultWarehouseProductBaseQuery(_db)
            .CountAsync();
        countStopwatch.Stop();

        var listStopwatch = Stopwatch.StartNew();
        var items = await QueryDefaultHomePageProductItemsAsync(
            _db,
            filter.PageNumber,
            filter.PageSize
        );
        listStopwatch.Stop();

        var gradeStopwatch = Stopwatch.StartNew();
        await productEnricher.PopulateGradesAsync(_db, items, normalizedGrades);
        gradeStopwatch.Stop();

        totalStopwatch.Stop();
        logger.LogInformation(
            "[shop-home-perf] stage=products.service.done pageNumber={PageNumber} pageSize={PageSize} category={CategoryGUID} keywordLength={KeywordLength} gradeCount={GradeCount} total={Total} itemCount={ItemCount} countMs={CountMs} listMs={ListMs} gradeMs={GradeMs} totalMs={TotalMs}",
            filter.PageNumber,
            filter.PageSize,
            filter.CategoryGUID,
            filter.ItemNumber?.Length ?? 0,
            normalizedGrades.Count,
            total,
            items.Count,
            countStopwatch.ElapsedMilliseconds,
            listStopwatch.ElapsedMilliseconds,
            gradeStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds
        );

        return new PagedListReactDto<StoreOrderProductDto>
        {
            Items = items,
            Total = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
        };
    }

    private async Task<List<StoreOrderProductDto>> QueryDefaultHomePageProductItemsAsync(
        ISqlSugarClient db,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedPageNumber = Math.Max(pageNumber, 1);
        var normalizedPageSize = Math.Max(pageSize, 1);
        var pageKeys = await ProductPickerQueryBuilder
            .CreateDefaultWarehouseProductBaseQuery(db)
            .OrderBy((product, warehouseProduct) => product.ItemNumber, OrderByType.Asc)
            .Select(
                (product, warehouseProduct) =>
                    new ProductPageKey
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                    }
            )
            .Skip((normalizedPageNumber - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();

        var productCodes = pageKeys
            .Select(item => item.ProductCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productCodes.Count == 0)
        {
            return new List<StoreOrderProductDto>();
        }

        var orderMap = pageKeys
            .Select((item, index) => new { item.ProductCode, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Index,
                StringComparer.OrdinalIgnoreCase
            );

        // 先分页 ProductCode，再只回查首屏字段，避免 join 后投影整个商品池。
        var items = await ProductPickerQueryBuilder
            .CreateDefaultWarehouseProductQuery(db)
            .Where(
                (product, warehouseProduct, category, supplier) =>
                    product.ProductCode != null
                    && productCodes.Contains(product.ProductCode)
            )
            .Select(
                (product, warehouseProduct, category, supplier) =>
                    new StoreOrderProductDto
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        CategoryName = category.CategoryName,
                        WarehouseCategoryGUID = product.WarehouseCategoryGUID,
                        LocalSupplierCode = product.LocalSupplierCode,
                        LocalSupplierName = supplier.Name,
                        OEMPrice = warehouseProduct.OEMPrice,
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                        StockQuantity = warehouseProduct.StockQuantity ?? 0,
                        PackQty = product.MiddlePackageQuantity,
                        ImportPrice = warehouseProduct.ImportPrice,
                    }
            )
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();

        return items
            .OrderBy(item =>
                orderMap.TryGetValue(item.ProductCode, out var order)
                    ? order
                    : int.MaxValue
            )
            .ToList();
    }

    private static async Task<List<StoreOrderProductDto>>
        QueryWarehouseProductItemsByPagedProductCodesAsync(
            ISugarQueryable<
                Product,
                WarehouseProduct,
                WarehouseCategory,
                HBLocalSupplier
            > query,
            StoreOrderFilterDto filter
        )
    {
        var normalizedPageNumber = Math.Max(filter.PageNumber, 1);
        var normalizedPageSize = Math.Max(filter.PageSize, 1);
        var pageKeys = await query
            .Clone()
            .Select(
                (product, warehouseProduct, category, supplier) =>
                    new ProductPageKey
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                    }
            )
            .Skip((normalizedPageNumber - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var productCodes = pageKeys
            .Select(item => item.ProductCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productCodes.Count == 0)
        {
            return new List<StoreOrderProductDto>();
        }

        var orderMap = pageKeys
            .Select((item, index) => new { item.ProductCode, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Index,
                StringComparer.OrdinalIgnoreCase
            );

        var items = await query
            .Clone()
            .Where(
                (product, warehouseProduct, category, supplier) =>
                    product.ProductCode != null
                    && productCodes.Contains(product.ProductCode)
            )
            .Select(
                (product, warehouseProduct, category, supplier) =>
                    new StoreOrderProductDto
                    {
                        ProductCode = product.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        CategoryName = category.CategoryName,
                        WarehouseCategoryGUID = product.WarehouseCategoryGUID,
                        LocalSupplierCode = product.LocalSupplierCode,
                        LocalSupplierName = supplier.Name,
                        OEMPrice = warehouseProduct.OEMPrice,
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                        StockQuantity = warehouseProduct.StockQuantity ?? 0,
                        PackQty = product.MiddlePackageQuantity,
                        ImportPrice = warehouseProduct.ImportPrice,
                    }
            )
            .ToListAsync();

        return items
            .OrderBy(item =>
                orderMap.TryGetValue(item.ProductCode, out var order)
                    ? order
                    : int.MaxValue
            )
            .ToList();
    }

    private async Task<List<string>> LookupManualLocationProductCodesAsync(
        StoreOrderFilterDto filter
    )
    {
        var identifier = ProductPickerRules.GetManualLocationLookupIdentifier(filter);
        if (identifier == null || !locationLookup.IsEnabled)
        {
            return new List<string>();
        }

        var lookupResult = await locationLookup.LookupAsync(identifier);
        return lookupResult?.ProductCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? new List<string>();
    }

    private List<string> GetAllSubCategoryIds(string categoryGuid)
    {
        try
        {
            var allCategories = _db.Queryable<WarehouseCategory>()
                .Select(category => new WarehouseCategory
                {
                    CategoryGUID = category.CategoryGUID,
                    ParentGUID = category.ParentGUID,
                })
                .ToList();
            var result = new List<string> { categoryGuid };
            var seen = new HashSet<string>(StringComparer.Ordinal) { categoryGuid };
            var childrenByParent = allCategories
                .Where(category =>
                    !string.IsNullOrWhiteSpace(category.ParentGUID)
                    && !string.IsNullOrWhiteSpace(category.CategoryGUID)
                )
                .GroupBy(category => category.ParentGUID!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(category => category.CategoryGUID).ToList(),
                    StringComparer.Ordinal
                );
            AddSubCategories(categoryGuid, childrenByParent, seen, result);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get subcategories for {CategoryGuid}", categoryGuid);
            return new List<string> { categoryGuid };
        }
    }

    private static void AddSubCategories(
        string parentGuid,
        IReadOnlyDictionary<string, List<string>> childrenByParent,
        HashSet<string> seen,
        List<string> result
    )
    {
        if (!childrenByParent.TryGetValue(parentGuid, out var children))
        {
            return;
        }

        foreach (var childGuid in children)
        {
            if (!string.IsNullOrEmpty(childGuid) && seen.Add(childGuid))
            {
                result.Add(childGuid);
                AddSubCategories(childGuid, childrenByParent, seen, result);
            }
        }
    }

    private ISqlSugarClient CreateHomePageQueryConnection()
    {
        var config = _db.CurrentConnectionConfig;
        var moreSettings = config.MoreSettings;
        var concurrentDb = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = config.ConnectionString,
                DbType = config.DbType,
                IsAutoCloseConnection = false,
                InitKeyType = config.InitKeyType,
                MoreSettings = new ConnMoreSettings
                {
                    IsAutoRemoveDataCache = moreSettings?.IsAutoRemoveDataCache ?? false,
                    IsWithNoLockQuery = moreSettings?.IsWithNoLockQuery ?? false,
                    SqlServerCodeFirstNvarchar =
                        moreSettings?.SqlServerCodeFirstNvarchar ?? false,
                    DefaultCacheDurationInSeconds = 0,
                },
                ConfigureExternalServices = config.ConfigureExternalServices,
            }
        );
        concurrentDb.Ado.CommandTimeOut = _db.Ado.CommandTimeOut;
        // 保留原首页预热连接的慢 SQL 指标标签，避免切片迁移造成观测断点。
        SqlPerformanceAttachmentService.Attach(
            concurrentDb,
            "SqlSugarContext.StoreOrderHomePageWarmUp"
        );
        return concurrentDb;
    }

    private sealed class ProductPageKey
    {
        public string ProductCode { get; set; } = string.Empty;

        public string? ItemNumber { get; set; }
    }
}
