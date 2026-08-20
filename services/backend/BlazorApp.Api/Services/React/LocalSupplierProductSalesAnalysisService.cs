using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    internal sealed class LocalSupplierProductSalesStatisticRow
    {
        public DateTime Date { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public int TotalQuantity { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// 澳洲本地商品销量分析服务。
    /// 进货只读本地进货主子表，销量只读商品分店日销售统计；
    /// 不读取/返回统计状态元数据，也不回扫 POSM。
    /// </summary>
    public class LocalSupplierProductSalesAnalysisService : ILocalSupplierProductSalesAnalysisService
    {
        private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromSeconds(60);

        private readonly ISqlSugarClient _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<LocalSupplierProductSalesAnalysisService> _logger;

        public LocalSupplierProductSalesAnalysisService(
            SqlSugarContext context,
            IMemoryCache cache,
            ILogger<LocalSupplierProductSalesAnalysisService> logger
        )
        {
            _db = context.Db;
            _cache = cache;
            _logger = logger;
        }

        public async Task<ApiResponse<LocalSupplierProductSalesOptionsDto>> GetOptionsAsync(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var cacheKey = BuildOptionsCacheKey(scopedStoreCodes);
            return await ComputeWithCacheAsync(cacheKey, forceRefresh: false, () =>
                ComputeOptionsAsync(scopedStoreCodes)
            );
        }

        public async Task<
            ApiResponse<
                LocalSupplierProductSalesPagedDto<LocalSupplierProductSalesCandidateDto>
            >
        > GetCandidatesAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var cacheKey = BuildCacheKey("candidates", request, scopedStoreCodes);
            return await ComputeWithCacheAsync(
                cacheKey,
                request.ForceRefresh,
                () => ComputeCandidatesAsync(request, scopedStoreCodes)
            );
        }

        public async Task<ApiResponse<LocalSupplierProductSalesSummaryResponseDto>> GetSummaryAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var cacheKey = BuildCacheKey("summary", request, scopedStoreCodes);
            return await ComputeWithCacheAsync(
                cacheKey,
                request.ForceRefresh,
                () => ComputeSummaryAsync(request, scopedStoreCodes)
            );
        }

        public async Task<ApiResponse<List<LocalSupplierProductSalesDailyDto>>> GetProductDailyAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var cacheKey = BuildCacheKey("product-daily", request, scopedStoreCodes);
            return await ComputeWithCacheAsync(
                cacheKey,
                request.ForceRefresh,
                () => ComputeProductDailyAsync(request, scopedStoreCodes)
            );
        }

        public async Task<ApiResponse<LocalSupplierProductSalesInvoiceDetailPageDto>>
            GetInvoiceDetailsAsync(
                LocalSupplierProductSalesAnalysisRequest request,
                IReadOnlyList<string>? scopedStoreCodes
            )
        {
            var cacheKey = BuildCacheKey("invoice-details", request, scopedStoreCodes);
            return await ComputeWithCacheAsync(
                cacheKey,
                request.ForceRefresh,
                () => ComputeInvoiceDetailsAsync(request, scopedStoreCodes)
            );
        }

        public async Task<ApiResponse<List<LocalSupplierProductSalesBranchDto>>> GetBranchesAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var cacheKey = BuildCacheKey("branches", request, scopedStoreCodes);
            return await ComputeWithCacheAsync(
                cacheKey,
                request.ForceRefresh,
                () => ComputeBranchesAsync(request, scopedStoreCodes)
            );
        }

        public async Task<ApiResponse<List<LocalSupplierProductSalesBranchDailyDto>>>
            GetBranchDailyAsync(
                LocalSupplierProductSalesAnalysisRequest request,
                IReadOnlyList<string>? scopedStoreCodes
            )
        {
            var cacheKey = BuildCacheKey("branch-daily", request, scopedStoreCodes);
            return await ComputeWithCacheAsync(
                cacheKey,
                request.ForceRefresh,
                () => ComputeBranchDailyAsync(request, scopedStoreCodes)
            );
        }

        private async Task<ApiResponse<T>> ComputeWithCacheAsync<T>(
            string cacheKey,
            bool forceRefresh,
            Func<Task<T>> compute
        )
        {
            if (
                !forceRefresh
                && _cache.TryGetValue<ApiResponse<T>>(cacheKey, out var cached)
                && cached is not null
            )
            {
                return cached;
            }

            T data;
            try
            {
                data = await compute();
            }
            catch (LocalSupplierProductSalesAnalysisValidationException ex)
            {
                return ApiResponse<T>.Error(ex.Message, "VALIDATION_ERROR");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "本地商品销量分析查询失败 CacheKey={CacheKey}", cacheKey);
                return ApiResponse<T>.Error("本地商品销量分析查询失败", "QUERY_ERROR");
            }

            var response = ApiResponse<T>.OK(data);
            _cache.Set(
                cacheKey,
                response,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = SuccessCacheDuration,
                }
            );
            return response;
        }

        private async Task<LocalSupplierProductSalesOptionsDto> ComputeOptionsAsync(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            var categories = data.Categories
                .Select(category => new LocalSupplierProductSalesCategoryOptionDto
                {
                    Guid = category.CategoryGUID,
                    Name = category.CategoryName ?? category.ChineseName,
                })
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var supplierOptions = data.Rows
                .Select(row => row.SupplierCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .Select(code => new LocalSupplierProductSalesSupplierOptionDto
                {
                    Code = code,
                    Name = data.SupplierByCode.TryGetValue(code, out var supplier)
                        ? supplier.Name
                        : null,
                })
                .ToList();

            return new LocalSupplierProductSalesOptionsDto
            {
                WarehouseCategories = categories,
                Suppliers = supplierOptions,
            };
        }

        private async Task<
            LocalSupplierProductSalesPagedDto<LocalSupplierProductSalesCandidateDto>
        > ComputeCandidatesAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            var codes = ApplyFilter(
                data.CandidateInfoByCode.Keys.OrderBy(code => code, StringComparer.OrdinalIgnoreCase),
                data,
                request.Filter
            ).ToList();

            var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
            var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);
            var items = codes
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(code => data.CandidateInfoByCode[code])
                .ToList();

            return new LocalSupplierProductSalesPagedDto<LocalSupplierProductSalesCandidateDto>
            {
                Items = items,
                Total = codes.Count,
                PageNumber = pageNumber,
                PageSize = pageSize,
            };
        }

        private async Task<LocalSupplierProductSalesSummaryResponseDto> ComputeSummaryAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var (startDate, endDate) = LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                request.Filter.StartDate,
                request.Filter.EndDate
            );

            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            var codes = ApplyFilter(
                data.CandidateInfoByCode.Keys,
                data,
                request.Filter
            );
            codes = LocalSupplierProductSalesAnalysisLogic.ApplySelection(codes, request.Selection);
            var sales = await LoadSalesDataAsync(
                scopedStoreCodes,
                startDate,
                endDate,
                codes.ToList()
            );

            var purchaseRows = data.Rows
                .Where(row => row.PurchaseDate >= startDate && row.PurchaseDate <= endDate)
                .ToList();
            var purchaseByCode = purchaseRows
                .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        Quantity = group.Sum(row => row.Quantity),
                        Amount = group.Sum(row => row.Amount),
                    },
                    StringComparer.OrdinalIgnoreCase
                );
            var salesByCode = sales
                .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        Quantity = group.Sum(row => (decimal)row.TotalQuantity),
                        Amount = group.Sum(row => row.TotalAmount),
                    },
                    StringComparer.OrdinalIgnoreCase
                );

            var rows = codes
                .Select(code =>
                {
                    var info = data.CandidateInfoByCode[code];
                    purchaseByCode.TryGetValue(code, out var purchase);
                    salesByCode.TryGetValue(code, out var sale);
                    var purchaseQuantity = purchase?.Quantity ?? 0m;
                    var purchaseAmount = purchase?.Amount ?? 0m;
                    var netSalesQuantity = sale?.Quantity ?? 0m;
                    var netSalesAmount = sale?.Amount ?? 0m;
                    return new LocalSupplierProductSalesSummaryRowDto
                    {
                        ProductCode = info.ProductCode,
                        ItemNumber = info.ItemNumber,
                        Barcode = info.Barcode,
                        ProductName = info.ProductName,
                        ImageUrl = info.ImageUrl,
                        WarehouseCategoryGuid = info.WarehouseCategoryGuid,
                        WarehouseCategoryName = info.WarehouseCategoryName,
                        Suppliers = ResolveSuppliers(code, data),
                        PurchaseQuantity = purchaseQuantity,
                        PurchaseAmount = purchaseAmount,
                        NetSalesQuantity = netSalesQuantity,
                        NetSalesAmount = netSalesAmount,
                        SellThroughRate =
                            LocalSupplierProductSalesAnalysisLogic.CalculateSellThroughRate(
                                purchaseQuantity,
                                netSalesQuantity
                            ),
                    };
                })
                .ToList();

            var sorted = LocalSupplierProductSalesAnalysisLogic.SortSummaryRows(
                rows,
                request.SortBy,
                request.SortDirection
            );
            var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
            var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);

            return new LocalSupplierProductSalesSummaryResponseDto
            {
                Totals = LocalSupplierProductSalesAnalysisLogic.ComputeSummaryTotals(sorted),
                Items = sorted
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList(),
                Total = sorted.Count,
                PageNumber = pageNumber,
                PageSize = pageSize,
            };
        }

        private async Task<List<LocalSupplierProductSalesDailyDto>> ComputeProductDailyAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var currentProductCode = RequireCurrentProductCode(request);
            var (startDate, endDate) = LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                request.Filter.StartDate,
                request.Filter.EndDate
            );

            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            if (!data.CandidateInfoByCode.ContainsKey(currentProductCode))
            {
                return new List<LocalSupplierProductSalesDailyDto>();
            }

            var sales = await LoadSalesDataAsync(
                scopedStoreCodes,
                startDate,
                endDate,
                new List<string> { currentProductCode }
            );

            var purchaseDaily = data.Rows
                .Where(row =>
                    row.ProductCode == currentProductCode
                    && row.PurchaseDate >= startDate
                    && row.PurchaseDate <= endDate
                )
                .GroupBy(row => row.PurchaseDate)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        Quantity = group.Sum(row => row.Quantity),
                        Amount = group.Sum(row => row.Amount),
                    }
                );
            var salesDaily = sales
                .Where(row => row.ProductCode == currentProductCode)
                .GroupBy(row => row.Date.Date)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        Quantity = group.Sum(row => (decimal)row.TotalQuantity),
                        Amount = group.Sum(row => row.TotalAmount),
                    }
                );

            var dates = purchaseDaily.Keys.Concat(salesDaily.Keys).Distinct().OrderBy(date => date);
            var rows = dates
                .Select(date =>
                {
                    purchaseDaily.TryGetValue(date, out var purchase);
                    salesDaily.TryGetValue(date, out var sale);
                    return new LocalSupplierProductSalesDailyDto
                    {
                        Date = date,
                        PurchaseQuantity = purchase?.Quantity ?? 0m,
                        PurchaseAmount = purchase?.Amount ?? 0m,
                        NetSalesQuantity = sale?.Quantity ?? 0m,
                        NetSalesAmount = sale?.Amount ?? 0m,
                    };
                })
                .ToList();

            return LocalSupplierProductSalesAnalysisLogic.BuildProductDailySeries(
                rows,
                startDate,
                endDate
            );
        }

        private async Task<LocalSupplierProductSalesInvoiceDetailPageDto> ComputeInvoiceDetailsAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var currentProductCode = RequireCurrentProductCode(request);
            var (startDate, endDate) = LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                request.Filter.StartDate,
                request.Filter.EndDate
            );

            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            var rows = data.Rows
                .Where(row =>
                    row.ProductCode == currentProductCode
                    && row.PurchaseDate >= startDate
                    && row.PurchaseDate <= endDate
                )
                .OrderByDescending(row => row.PurchaseDate)
                .ThenBy(row => row.DetailGUID, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
            var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500);
            var items = rows
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(row => new LocalSupplierProductSalesInvoiceDetailDto
                {
                    DetailGUID = row.DetailGUID,
                    InvoiceGUID = row.InvoiceGUID,
                    InvoiceNo = row.InvoiceNo,
                    StoreCode = row.StoreCode,
                    StoreName =
                        row.StoreCode != null
                        && data.StoreByCode.TryGetValue(row.StoreCode, out var store)
                            ? store.StoreName
                            : null,
                    SupplierCode = row.SupplierCode,
                    SupplierName =
                        row.SupplierCode != null
                        && data.SupplierByCode.TryGetValue(row.SupplierCode, out var supplier)
                            ? supplier.Name
                            : null,
                    PurchaseDate = row.PurchaseDate,
                    ProductCode = row.ProductCode,
                    ProductName = row.ProductName,
                    Quantity = row.Quantity,
                    PurchasePrice = row.PurchasePrice,
                    Amount = row.Amount,
                })
                .ToList();

            return new LocalSupplierProductSalesInvoiceDetailPageDto
            {
                Items = items,
                Total = rows.Count,
                PageNumber = pageNumber,
                PageSize = pageSize,
            };
        }

        private async Task<List<LocalSupplierProductSalesBranchDto>> ComputeBranchesAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var currentProductCode = RequireCurrentProductCode(request);
            var (startDate, endDate) = LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                request.Filter.StartDate,
                request.Filter.EndDate
            );

            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            if (!data.CandidateInfoByCode.ContainsKey(currentProductCode))
            {
                return new List<LocalSupplierProductSalesBranchDto>();
            }

            var sales = await LoadSalesDataAsync(
                scopedStoreCodes,
                startDate,
                endDate,
                new List<string> { currentProductCode }
            );

            var rows = sales
                .Where(row => row.ProductCode == currentProductCode)
                .GroupBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var quantity = group.Sum(row => (decimal)row.TotalQuantity);
                    var amount = group.Sum(row => row.TotalAmount);
                    return new LocalSupplierProductSalesBranchDto
                    {
                        BranchCode = group.Key,
                        BranchName = data.StoreByCode.TryGetValue(group.Key, out var store)
                            ? store.StoreName
                            : null,
                        NetSalesQuantity = quantity,
                        NetSalesAmount = amount,
                        AverageUnitPrice =
                            LocalSupplierProductSalesAnalysisLogic.CalculateAverageUnitPrice(
                                quantity,
                                amount
                            ),
                    };
                })
                .OrderByDescending(row => row.NetSalesQuantity)
                .ThenBy(row => row.BranchCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return rows;
        }

        private async Task<List<LocalSupplierProductSalesBranchDailyDto>> ComputeBranchDailyAsync(
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var currentProductCode = RequireCurrentProductCode(request);
            var branchCode = LocalSupplierProductSalesAnalysisLogic.NormalizeText(request.BranchCode);
            if (string.IsNullOrWhiteSpace(branchCode))
            {
                throw new LocalSupplierProductSalesAnalysisValidationException(
                    "branchCode 不能为空。"
                );
            }

            var (startDate, endDate) = LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                request.Filter.StartDate,
                request.Filter.EndDate
            );

            var data = await LoadPurchaseDataAsync(scopedStoreCodes);
            if (!data.CandidateInfoByCode.ContainsKey(currentProductCode))
            {
                return new List<LocalSupplierProductSalesBranchDailyDto>();
            }

            var sales = await LoadSalesDataAsync(
                scopedStoreCodes,
                startDate,
                endDate,
                new List<string> { currentProductCode }
            );
            var rows = sales
                .Where(row =>
                    row.BranchCode == branchCode && row.ProductCode == currentProductCode
                )
                .GroupBy(row => row.Date.Date)
                .Select(group => new LocalSupplierProductSalesBranchDailyDto
                {
                    Date = group.Key,
                    NetSalesQuantity = group.Sum(row => (decimal)row.TotalQuantity),
                    NetSalesAmount = group.Sum(row => row.TotalAmount),
                })
                .ToList();

            return LocalSupplierProductSalesAnalysisLogic.BuildBranchDailySeries(
                rows,
                startDate,
                endDate
            );
        }

        private static string RequireCurrentProductCode(
            LocalSupplierProductSalesAnalysisRequest request
        )
        {
            var code = LocalSupplierProductSalesAnalysisLogic.NormalizeText(
                request.CurrentProductCode
            );
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new LocalSupplierProductSalesAnalysisValidationException(
                    "currentProductCode 不能为空。"
                );
            }

            return code;
        }

        private IEnumerable<string> ApplyFilter(
            IEnumerable<string> candidateCodes,
            LocalSupplierPurchaseData data,
            LocalSupplierProductSalesAnalysisFilterDto filter
        )
        {
            var result = candidateCodes;

            var keyword = LocalSupplierProductSalesAnalysisLogic.NormalizeText(filter.Keyword);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                result = result.Where(code =>
                {
                    var info = data.CandidateInfoByCode[code];
                    return ContainsIgnoreCase(info.ItemNumber, keyword)
                        || ContainsIgnoreCase(info.Barcode, keyword)
                        || ContainsIgnoreCase(info.ProductName, keyword)
                        || ContainsIgnoreCase(code, keyword);
                });
            }

            var categoryGuids = LocalSupplierProductSalesAnalysisLogic.ResolveCategoryGuids(
                filter
            );
            if (categoryGuids.Count > 0)
            {
                var expanded = LocalSupplierProductSalesAnalysisLogic.ExpandCategoryGuids(
                    data.Categories,
                    categoryGuids
                );
                result = result.Where(code =>
                {
                    var categoryGuid = data.CandidateInfoByCode[code].WarehouseCategoryGuid;
                    return categoryGuid != null && expanded.Contains(categoryGuid);
                });
            }

            var supplierCodes = LocalSupplierProductSalesAnalysisLogic.ResolveSupplierCodes(
                filter
            );
            if (supplierCodes.Count > 0)
            {
                var supplierSet = supplierCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var productCodesBySupplier = data.Rows
                    .Where(row => row.SupplierCode != null && supplierSet.Contains(row.SupplierCode))
                    .Select(row => row.ProductCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                result = result.Where(productCodesBySupplier.Contains);
            }

            var documentKeyword = LocalSupplierProductSalesAnalysisLogic.NormalizeText(
                filter.DocumentKeyword
            );
            if (!string.IsNullOrWhiteSpace(documentKeyword))
            {
                var productCodesByDocument = data.Rows
                    .Where(row =>
                        ContainsIgnoreCase(row.InvoiceNo, documentKeyword)
                        || ContainsIgnoreCase(row.Remarks, documentKeyword)
                    )
                    .Select(row => row.ProductCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                result = result.Where(productCodesByDocument.Contains);
            }

            return result;
        }

        private static List<LocalSupplierProductSalesSupplierRefDto> ResolveSuppliers(
            string productCode,
            LocalSupplierPurchaseData data
        )
        {
            return data.Rows
                .Where(row =>
                    row.ProductCode == productCode && !string.IsNullOrWhiteSpace(row.SupplierCode)
                )
                .Select(row => row.SupplierCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .Select(code => new LocalSupplierProductSalesSupplierRefDto
                {
                    Code = code,
                    Name = data.SupplierByCode.TryGetValue(code, out var supplier)
                        ? supplier.Name
                        : null,
                })
                .ToList();
        }

        private async Task<LocalSupplierPurchaseData> LoadPurchaseDataAsync(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var headers = await LoadHeadersAsync(scopedStoreCodes);
            if (headers.Count == 0)
            {
                return new LocalSupplierPurchaseData();
            }

            var headerGuids = headers.Select(header => header.InvoiceGUID).Distinct().ToList();
            var details = new List<StoreLocalSupplierInvoiceDetails>();
            foreach (var batch in LocalSupplierProductSalesAnalysisLogic.BatchCodes(headerGuids))
            {
                details.AddRange(
                    await _db
                        .Queryable<StoreLocalSupplierInvoiceDetails>()
                        .Where(detail =>
                            detail.IsDeleted == false
                            && detail.InvoiceGUID != null
                            && batch.Contains(detail.InvoiceGUID)
                        )
                        .ToListAsync()
                );
            }

            var candidateCodes = details
                .Where(detail => !string.IsNullOrWhiteSpace(detail.ProductCode))
                .Select(detail => detail.ProductCode!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var products = await LoadProductsAsync(candidateCodes);
            var categories = await _db
                .Queryable<WarehouseCategory>()
                .Where(category => category.IsDeleted == false)
                .ToListAsync();
            var suppliers = await _db.Queryable<HBLocalSupplier>().ToListAsync();
            var stores = await _db
                .Queryable<Store>()
                .Where(store => store.IsDeleted == false)
                .ToListAsync();

            var headerByGuid = headers.ToDictionary(header => header.InvoiceGUID);
            var productByCode = products
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                .GroupBy(product => product.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase
                );
            var supplierByCode = suppliers
                .Where(supplier => !string.IsNullOrWhiteSpace(supplier.LocalSupplierCode))
                .GroupBy(
                    supplier => supplier.LocalSupplierCode!.Trim(),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase
                );
            var storeByCode = stores
                .Where(store => !string.IsNullOrWhiteSpace(store.StoreCode))
                .GroupBy(store => store.StoreCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase
                );
            var categoryByGuid = categories.ToDictionary(
                category => category.CategoryGUID,
                StringComparer.OrdinalIgnoreCase
            );
            var scopedStoreSet = scopedStoreCodes is null
                ? null
                : LocalSupplierProductSalesAnalysisLogic
                    .NormalizeCodes(scopedStoreCodes)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = new List<LocalSupplierPurchaseRow>();
            foreach (var detail in details)
            {
                var productCode = LocalSupplierProductSalesAnalysisLogic.NormalizeText(
                    detail.ProductCode
                );
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(detail.InvoiceGUID))
                {
                    continue;
                }

                if (!headerByGuid.TryGetValue(detail.InvoiceGUID, out var header))
                {
                    continue;
                }

                var effectiveStoreCode =
                    LocalSupplierProductSalesAnalysisLogic.NormalizeText(detail.StoreCode)
                    ?? LocalSupplierProductSalesAnalysisLogic.NormalizeText(header.StoreCode);
                if (
                    scopedStoreSet is not null
                    && (
                        string.IsNullOrWhiteSpace(effectiveStoreCode)
                        || !scopedStoreSet.Contains(effectiveStoreCode)
                    )
                )
                {
                    continue;
                }

                productByCode.TryGetValue(productCode, out var product);
                rows.Add(
                    new LocalSupplierPurchaseRow
                    {
                        DetailGUID = detail.DetailGUID,
                        InvoiceGUID = detail.InvoiceGUID,
                        InvoiceNo = header.InvoiceNo,
                        Remarks = header.Remarks,
                        StoreCode = effectiveStoreCode,
                        SupplierCode = LocalSupplierProductSalesAnalysisLogic.ResolveSupplierCode(
                            detail.SupplierCode,
                            header.SupplierCode
                        ),
                        ProductCode = productCode,
                        ItemNumber =
                            LocalSupplierProductSalesAnalysisLogic.NormalizeText(product?.ItemNumber)
                            ?? LocalSupplierProductSalesAnalysisLogic.NormalizeText(
                                detail.ItemNumber
                            ),
                        Barcode =
                            LocalSupplierProductSalesAnalysisLogic.NormalizeText(product?.Barcode)
                            ?? LocalSupplierProductSalesAnalysisLogic.NormalizeText(detail.Barcode),
                        ProductName =
                            LocalSupplierProductSalesAnalysisLogic.NormalizeText(
                                product?.ProductName
                            )
                            ?? LocalSupplierProductSalesAnalysisLogic.NormalizeText(
                                detail.ProductName
                            ),
                        ImageUrl = product?.ProductImage,
                        WarehouseCategoryGuid = product?.WarehouseCategoryGUID,
                        PurchaseDate = LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseDate(
                            header.InboundDate,
                            header.OrderDate,
                            header.CreatedAt
                        ),
                        Quantity = detail.Quantity ?? 0m,
                        PurchasePrice = detail.PurchasePrice,
                        Amount = LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseAmount(
                            detail.Amount,
                            detail.Quantity,
                            detail.PurchasePrice
                        ),
                    }
                );
            }

            return new LocalSupplierPurchaseData
            {
                Rows = rows,
                Categories = categories,
                SupplierByCode = supplierByCode,
                StoreByCode = storeByCode,
                CandidateInfoByCode = BuildCandidateInfo(rows, categoryByGuid),
            };
        }

        private async Task<List<StoreLocalSupplierInvoice>> LoadHeadersAsync(
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var query = _db
                .Queryable<StoreLocalSupplierInvoice>()
                .Where(header => header.IsDeleted == false);

            if (scopedStoreCodes is not null)
            {
                var stores = scopedStoreCodes.ToList();
                if (stores.Count == 0)
                {
                    return new List<StoreLocalSupplierInvoice>();
                }

                query = query.Where(header =>
                    header.StoreCode != null && stores.Contains(header.StoreCode)
                );
            }

            return await query.ToListAsync();
        }

        private async Task<List<Product>> LoadProductsAsync(List<string> productCodes)
        {
            if (productCodes.Count == 0)
            {
                return new List<Product>();
            }

            var products = new List<Product>();
            foreach (var batch in LocalSupplierProductSalesAnalysisLogic.BatchCodes(productCodes))
            {
                products.AddRange(
                    await _db
                        .Queryable<Product>()
                        .Where(product =>
                            product.IsDeleted == false
                            && product.ProductCode != null
                            && batch.Contains(product.ProductCode)
                        )
                        .ToListAsync()
                );
            }

            return products;
        }

        private async Task<List<LocalSupplierProductSalesStatisticRow>> LoadSalesDataAsync(
            IReadOnlyList<string>? scopedStoreCodes,
            DateTime startDate,
            DateTime endDate,
            List<string> productCodes
        )
        {
            if (productCodes.Count == 0)
            {
                return new List<LocalSupplierProductSalesStatisticRow>();
            }

            var endDateExclusive = endDate.Date.AddDays(1);
            var stores =
                scopedStoreCodes is null ? null : scopedStoreCodes.ToList();
            if (stores is { Count: 0 })
            {
                return new List<LocalSupplierProductSalesStatisticRow>();
            }

            var result = new List<LocalSupplierProductSalesStatisticRow>();
            foreach (var batch in LocalSupplierProductSalesAnalysisLogic.BatchCodes(productCodes))
            {
                var query = _db
                    .Queryable<ProductStoreDailySalesStatistic>()
                    .Where(row =>
                        row.Date >= startDate.Date
                        && row.Date < endDateExclusive
                        && batch.Contains(row.ProductCode)
                    );
                if (stores is not null)
                {
                    query = query.Where(row => stores.Contains(row.BranchCode));
                }

                var rows = await query
                    .Select(row => new LocalSupplierProductSalesStatisticRow
                    {
                        Date = row.Date,
                        BranchCode = row.BranchCode,
                        ProductCode = row.ProductCode,
                        TotalQuantity = row.TotalQuantity,
                        TotalAmount = row.TotalAmount,
                    })
                    .ToListAsync();
                result.AddRange(rows);
            }

            return result;
        }

        private static Dictionary<string, LocalSupplierProductSalesCandidateDto> BuildCandidateInfo(
            List<LocalSupplierPurchaseRow> rows,
            Dictionary<string, WarehouseCategory> categoryByGuid
        )
        {
            var map = new Dictionary<string, LocalSupplierProductSalesCandidateDto>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var row in rows)
            {
                if (map.ContainsKey(row.ProductCode))
                {
                    continue;
                }

                var categoryName =
                    row.WarehouseCategoryGuid != null
                    && categoryByGuid.TryGetValue(row.WarehouseCategoryGuid, out var category)
                        ? category.CategoryName ?? category.ChineseName
                        : null;
                map[row.ProductCode] = new LocalSupplierProductSalesCandidateDto
                {
                    ProductCode = row.ProductCode,
                    ItemNumber = row.ItemNumber,
                    Barcode = row.Barcode,
                    ProductName = row.ProductName,
                    ImageUrl = row.ImageUrl,
                    WarehouseCategoryGuid = row.WarehouseCategoryGuid,
                    WarehouseCategoryName = categoryName,
                };
            }

            return map;
        }

        private static bool ContainsIgnoreCase(string? source, string value)
        {
            return source != null && source.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildOptionsCacheKey(IReadOnlyList<string>? scopedStoreCodes)
        {
            return "LocalSupplierProductSalesAnalysis:options:"
                + HashParts(BuildStoreScopeCachePart(scopedStoreCodes));
        }

        private static string BuildStoreScopeCachePart(IReadOnlyList<string>? scopedStoreCodes)
        {
            if (scopedStoreCodes is null)
            {
                return "<all>";
            }

            var normalized = LocalSupplierProductSalesAnalysisLogic
                .NormalizeCodes(scopedStoreCodes)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return normalized.Count == 0 ? "<none>" : string.Join(",", normalized);
        }

        private static string BuildCacheKey(
            string segment,
            LocalSupplierProductSalesAnalysisRequest request,
            IReadOnlyList<string>? scopedStoreCodes
        )
        {
            var parts = new[]
            {
                request.Filter.StartDate.ToString("yyyy-MM-dd"),
                request.Filter.EndDate.ToString("yyyy-MM-dd"),
                request.Filter.Keyword ?? "",
                request.Filter.CategoryGuid ?? "",
                string.Join(",", (request.Filter.WarehouseCategoryGuids ?? new List<string>()).OrderBy(x => x)),
                request.Filter.SupplierCode ?? "",
                string.Join(",", (request.Filter.SupplierCodes ?? new List<string>()).OrderBy(x => x)),
                request.Filter.DocumentKeyword ?? "",
                request.Selection.Mode ?? "",
                string.Join(",", (request.Selection.IncludedProductCodes ?? new List<string>()).OrderBy(x => x)),
                string.Join(",", (request.Selection.ExcludedProductCodes ?? new List<string>()).OrderBy(x => x)),
                request.CurrentProductCode ?? "",
                request.BranchCode ?? "",
                request.PageNumber.ToString(),
                request.PageSize.ToString(),
                request.SortBy ?? "",
                request.SortDirection ?? "",
                BuildStoreScopeCachePart(scopedStoreCodes),
            };
            return "LocalSupplierProductSalesAnalysis:" + segment + ":" + HashParts(string.Join("|", parts));
        }

        private static string HashParts(string value)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes)[..16];
        }

        private sealed class LocalSupplierPurchaseRow
        {
            public string DetailGUID { get; set; } = string.Empty;
            public string InvoiceGUID { get; set; } = string.Empty;
            public string? InvoiceNo { get; set; }
            public string? Remarks { get; set; }
            public string? StoreCode { get; set; }
            public string? SupplierCode { get; set; }
            public string ProductCode { get; set; } = string.Empty;
            public string? ItemNumber { get; set; }
            public string? Barcode { get; set; }
            public string? ProductName { get; set; }
            public string? ImageUrl { get; set; }
            public string? WarehouseCategoryGuid { get; set; }
            public DateTime PurchaseDate { get; set; }
            public decimal Quantity { get; set; }
            public decimal? PurchasePrice { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class LocalSupplierPurchaseData
        {
            public List<LocalSupplierPurchaseRow> Rows { get; set; } = new();
            public List<WarehouseCategory> Categories { get; set; } = new();
            public Dictionary<string, HBLocalSupplier> SupplierByCode { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Store> StoreByCode { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, LocalSupplierProductSalesCandidateDto> CandidateInfoByCode
            {
                get;
                set;
            } = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>本地商品销量分析参数错误。</summary>
    public sealed class LocalSupplierProductSalesAnalysisValidationException : Exception
    {
        public LocalSupplierProductSalesAnalysisValidationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>本地商品销量分析纯逻辑，便于单元测试。</summary>
    public static class LocalSupplierProductSalesAnalysisLogic
    {
        public const int CodeBatchSize = 500;
        public const int MaxDateRangeDays = 366;

        public static (DateTime StartDate, DateTime EndDate) ValidateDateRange(
            DateTime startDate,
            DateTime endDate,
            DateTime? utcNow = null
        )
        {
            if (startDate == DateTime.MinValue || endDate == DateTime.MinValue)
            {
                throw new LocalSupplierProductSalesAnalysisValidationException(
                    "开始日期和结束日期不能为空。"
                );
            }

            var start = startDate.Date;
            var end = endDate.Date;
            if (start > end)
            {
                throw new LocalSupplierProductSalesAnalysisValidationException(
                    "开始日期不能晚于结束日期。"
                );
            }

            // 布里斯班无夏令时，固定 UTC+10；结束日最多允许到布里斯班昨天。
            var brisbaneYesterday = (utcNow ?? DateTime.UtcNow).AddHours(10).Date.AddDays(-1);
            if (end > brisbaneYesterday)
            {
                throw new LocalSupplierProductSalesAnalysisValidationException(
                    "结束日期不能晚于昨天。"
                );
            }

            if ((int)(end - start).TotalDays + 1 > MaxDateRangeDays)
            {
                throw new LocalSupplierProductSalesAnalysisValidationException(
                    "日期范围不能超过 366 个自然日。"
                );
            }

            return (start, end);
        }

        public static DateTime ResolvePurchaseDate(
            DateTime? inboundDate,
            DateTime? orderDate,
            DateTime createdAt
        )
        {
            return (inboundDate ?? orderDate ?? createdAt).Date;
        }

        public static string? ResolveSupplierCode(
            string? detailSupplierCode,
            string? headerSupplierCode
        )
        {
            var detail = NormalizeText(detailSupplierCode);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return detail;
            }

            return NormalizeText(headerSupplierCode);
        }

        public static decimal ResolvePurchaseAmount(
            decimal? amount,
            decimal? quantity,
            decimal? purchasePrice
        )
        {
            return amount ?? (quantity ?? 0m) * (purchasePrice ?? 0m);
        }

        public static decimal? CalculateSellThroughRate(
            decimal purchaseQuantity,
            decimal netSalesQuantity
        )
        {
            return purchaseQuantity == 0m ? null : netSalesQuantity / purchaseQuantity * 100m;
        }

        public static decimal? CalculateAverageUnitPrice(
            decimal netSalesQuantity,
            decimal netSalesAmount
        )
        {
            return netSalesQuantity == 0m ? null : netSalesAmount / netSalesQuantity;
        }

        public static List<string> ResolveCategoryGuids(
            LocalSupplierProductSalesAnalysisFilterDto filter
        )
        {
            return MergeSingleAndList(filter.CategoryGuid, filter.WarehouseCategoryGuids);
        }

        public static List<string> ResolveSupplierCodes(
            LocalSupplierProductSalesAnalysisFilterDto filter
        )
        {
            return MergeSingleAndList(filter.SupplierCode, filter.SupplierCodes);
        }

        public static HashSet<string> ExpandCategoryGuids(
            IEnumerable<WarehouseCategory> categories,
            IEnumerable<string>? selectedGuids
        )
        {
            var childrenByParent = categories
                .GroupBy(category => category.ParentGUID ?? string.Empty)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(category => category.CategoryGUID).ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            foreach (var guid in NormalizeCodes(selectedGuids))
            {
                queue.Enqueue(guid);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!result.Add(current))
                {
                    continue;
                }

                if (childrenByParent.TryGetValue(current, out var children))
                {
                    foreach (var child in children)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return result;
        }

        public static List<string> ApplySelection(
            IEnumerable<string> productCodes,
            LocalSupplierProductSalesSelectionDto selection
        )
        {
            var codes = productCodes.ToList();
            var mode = string.IsNullOrWhiteSpace(selection.Mode)
                ? "allFiltered"
                : selection.Mode.Trim();
            if (string.Equals(mode, "included", StringComparison.OrdinalIgnoreCase))
            {
                var included = NormalizeCodes(selection.IncludedProductCodes)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return codes.Where(included.Contains).ToList();
            }

            var excluded = NormalizeCodes(selection.ExcludedProductCodes)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return codes.Where(code => !excluded.Contains(code)).ToList();
        }

        public static List<List<string>> BatchCodes(IEnumerable<string>? codes)
        {
            return NormalizeCodes(codes).Chunk(CodeBatchSize).Select(chunk => chunk.ToList()).ToList();
        }

        public static List<LocalSupplierProductSalesSummaryRowDto> SortSummaryRows(
            IEnumerable<LocalSupplierProductSalesSummaryRowDto> rows,
            string? sortBy,
            string? sortDirection
        )
        {
            var list = rows.ToList();
            var ascending = string.Equals(
                sortDirection,
                "asc",
                StringComparison.OrdinalIgnoreCase
            );
            var field = string.IsNullOrWhiteSpace(sortBy) ? null : sortBy.Trim().ToLowerInvariant();

            IOrderedEnumerable<LocalSupplierProductSalesSummaryRowDto> ordered = field switch
            {
                "purchasequantity" => ascending
                    ? list.OrderBy(row => row.PurchaseQuantity)
                    : list.OrderByDescending(row => row.PurchaseQuantity),
                "purchaseamount" => ascending
                    ? list.OrderBy(row => row.PurchaseAmount)
                    : list.OrderByDescending(row => row.PurchaseAmount),
                "netsalesamount" => ascending
                    ? list.OrderBy(row => row.NetSalesAmount)
                    : list.OrderByDescending(row => row.NetSalesAmount),
                "sellthroughrate" => ascending
                    ? list.OrderBy(row => row.SellThroughRate)
                    : list.OrderByDescending(row => row.SellThroughRate),
                "productcode" => ascending
                    ? list.OrderBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
                    : list.OrderByDescending(row => row.ProductCode, StringComparer.OrdinalIgnoreCase),
                _ => ascending
                    ? list.OrderBy(row => row.NetSalesQuantity)
                    : list.OrderByDescending(row => row.NetSalesQuantity),
            };

            return ordered.ThenBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static LocalSupplierProductSalesSummaryTotalsDto ComputeSummaryTotals(
            IEnumerable<LocalSupplierProductSalesSummaryRowDto> rows
        )
        {
            var list = rows.ToList();
            var purchaseQuantity = list.Sum(row => row.PurchaseQuantity);
            var purchaseAmount = list.Sum(row => row.PurchaseAmount);
            var netSalesQuantity = list.Sum(row => row.NetSalesQuantity);
            var netSalesAmount = list.Sum(row => row.NetSalesAmount);
            return new LocalSupplierProductSalesSummaryTotalsDto
            {
                PurchaseQuantity = purchaseQuantity,
                PurchaseAmount = purchaseAmount,
                NetSalesQuantity = netSalesQuantity,
                NetSalesAmount = netSalesAmount,
                SellThroughRate = CalculateSellThroughRate(purchaseQuantity, netSalesQuantity),
            };
        }

        public static List<LocalSupplierProductSalesDailyDto> BuildProductDailySeries(
            IEnumerable<LocalSupplierProductSalesDailyDto> rows,
            DateTime startDate,
            DateTime endDate
        )
        {
            var byDate = rows
                .GroupBy(row => row.Date.Date)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        PurchaseQuantity = group.Sum(row => row.PurchaseQuantity),
                        PurchaseAmount = group.Sum(row => row.PurchaseAmount),
                        NetSalesQuantity = group.Sum(row => row.NetSalesQuantity),
                        NetSalesAmount = group.Sum(row => row.NetSalesAmount),
                    }
                );

            var series = new List<LocalSupplierProductSalesDailyDto>();
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                byDate.TryGetValue(date, out var aggregate);
                var netSalesQuantity = aggregate?.NetSalesQuantity ?? 0m;
                var netSalesAmount = aggregate?.NetSalesAmount ?? 0m;
                series.Add(
                    new LocalSupplierProductSalesDailyDto
                    {
                        Date = date,
                        PurchaseQuantity = aggregate?.PurchaseQuantity ?? 0m,
                        PurchaseAmount = aggregate?.PurchaseAmount ?? 0m,
                        NetSalesQuantity = netSalesQuantity,
                        NetSalesAmount = netSalesAmount,
                        AverageUnitPrice = CalculateAverageUnitPrice(
                            netSalesQuantity,
                            netSalesAmount
                        ),
                    }
                );
            }

            return series;
        }

        public static List<LocalSupplierProductSalesBranchDailyDto> BuildBranchDailySeries(
            IEnumerable<LocalSupplierProductSalesBranchDailyDto> rows,
            DateTime startDate,
            DateTime endDate
        )
        {
            var byDate = rows
                .GroupBy(row => row.Date.Date)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        NetSalesQuantity = group.Sum(row => row.NetSalesQuantity),
                        NetSalesAmount = group.Sum(row => row.NetSalesAmount),
                    }
                );

            var series = new List<LocalSupplierProductSalesBranchDailyDto>();
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                byDate.TryGetValue(date, out var aggregate);
                var netSalesQuantity = aggregate?.NetSalesQuantity ?? 0m;
                var netSalesAmount = aggregate?.NetSalesAmount ?? 0m;
                series.Add(
                    new LocalSupplierProductSalesBranchDailyDto
                    {
                        Date = date,
                        NetSalesQuantity = netSalesQuantity,
                        NetSalesAmount = netSalesAmount,
                        AverageUnitPrice = CalculateAverageUnitPrice(
                            netSalesQuantity,
                            netSalesAmount
                        ),
                    }
                );
            }

            return series;
        }

        public static List<string> NormalizeCodes(IEnumerable<string>? codes)
        {
            return codes?
                .Select(NormalizeText)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        public static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<string> MergeSingleAndList(string? single, List<string>? list)
        {
            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(single))
            {
                values.Add(single.Trim());
            }

            if (list is not null)
            {
                values.AddRange(
                    list
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item.Trim())
                );
            }

            return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
