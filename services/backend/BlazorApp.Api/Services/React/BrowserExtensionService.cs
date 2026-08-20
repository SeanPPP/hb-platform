using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Models;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class BrowserExtensionService : IBrowserExtensionService
{
    private readonly ISqlSugarClient _db;
    private readonly ISqlSugarClient _posmDb;
    private readonly IOptionsSnapshot<BrowserExtensionOptions> _options;
    private readonly ILogger<BrowserExtensionService> _logger;
    private readonly TimeProvider _timeProvider;

    public BrowserExtensionService(
        SqlSugarContext context,
        POSMSqlSugarContext posmContext,
        IOptionsSnapshot<BrowserExtensionOptions> options,
        ILogger<BrowserExtensionService> logger,
        TimeProvider? timeProvider = null
    )
    {
        _db = context.Db;
        _posmDb = posmContext.Db;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public BrowserExtensionReleaseDto GetRelease() =>
        BrowserExtensionProfileCatalog.BuildRelease(_options.Value);

    public BrowserExtensionSupplierProfilesDto GetSupplierProfiles() =>
        BrowserExtensionProfileCatalog.BuildProfiles(_options.Value);

    public async Task<BrowserExtensionProductSummaryBatchDto> GetProductSummariesAsync(
        BrowserExtensionProductSummaryBatchRequestDto request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var storeCode = NormalizeCode(request.StoreCode, "分店代码");
        var supplierCode = NormalizeCode(request.SupplierCode, "供应商代码");
        EnsureSupplierEnabled(supplierCode);
        var itemNumbers = BrowserExtensionPurchaseCycleSqlBuilder.NormalizeItemNumbers(
            request.ItemNumbers
        );
        var today = await ResolveStoreTodayAsync(storeCode);
        var items = await QuerySummariesAsync(storeCode, supplierCode, itemNumbers, today);

        return new BrowserExtensionProductSummaryBatchDto
        {
            StoreCode = storeCode,
            SupplierCode = supplierCode,
            EndDate = today,
            Items = items,
        };
    }

    public async Task<BrowserExtensionPurchaseCyclesDto> GetPurchaseCyclesAsync(
        BrowserExtensionPurchaseCyclesRequestDto request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var storeCode = NormalizeCode(request.StoreCode, "分店代码");
        var supplierCode = NormalizeCode(request.SupplierCode, "供应商代码");
        EnsureSupplierEnabled(supplierCode);
        var itemNumber = BrowserExtensionPurchaseCycleSqlBuilder.NormalizeItemNumbers(
            new[] { request.ItemNumber }
        )[0];
        var today = await ResolveStoreTodayAsync(storeCode);
        var summary = (
            await QuerySummariesAsync(storeCode, supplierCode, new[] { itemNumber }, today)
        ).Single();

        var response = new BrowserExtensionPurchaseCyclesDto
        {
            StoreCode = storeCode,
            SupplierCode = supplierCode,
            ItemNumber = itemNumber,
            MatchStatus = summary.MatchStatus,
            ProductCode = summary.ProductCode,
            ProductName = summary.ProductName,
            EndDate = today,
            SalesStatisticLastUpdate = summary.SalesStatisticLastUpdate,
            LatestPurchaseDate = summary.LatestPurchaseDate,
            LatestPurchaseQuantity = summary.LatestPurchaseQuantity,
            SalesSinceLatestPurchase = summary.SalesSinceLatestPurchase,
        };

        if (summary.MatchStatus != BrowserExtensionMatchStatuses.Matched)
        {
            return response;
        }

        var purchaseQuery = BrowserExtensionPurchaseCycleSqlBuilder.BuildPurchaseLines(
            storeCode,
            supplierCode,
            itemNumber,
            today.AddMonths(-12),
            today
        );
        var purchaseRows = await _db.Ado.SqlQueryAsync<PurchaseSqlRow>(
            purchaseQuery.Sql,
            purchaseQuery.Parameters.ToArray()
        );
        var purchases = purchaseRows
            .Where(row => row.PurchaseDate.HasValue)
            .Select(row =>
                new BrowserExtensionPurchaseLine
                {
                    PurchaseDate = DateOnly.FromDateTime(row.PurchaseDate!.Value),
                    InvoiceNumber = row.InvoiceNumber,
                    Quantity = row.Quantity,
                    PurchasePrice = row.PurchasePrice,
                    Amount = row.Amount,
                    ProductCode = row.ProductCode,
                    ProductName = row.ProductName,
                }
            )
            .ToList();

        if (purchases.Count == 0)
        {
            return response;
        }

        var productCodes = purchases
            .Select(row => row.ProductCode)
            .Append(summary.ProductCode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sales = new List<BrowserExtensionSalesLine>();
        if (productCodes.Count > 0)
        {
            var selectedPurchaseDates = purchases
                .Select(row => row.PurchaseDate)
                .Distinct()
                .OrderByDescending(value => value)
                .Take(BrowserExtensionPurchaseCycleCalculator.MaximumCycles)
                .ToList();
            var salesStartDate = selectedPurchaseDates.Min();
            var salesQuery = BrowserExtensionPurchaseCycleSqlBuilder.BuildSales(
                storeCode,
                supplierCode,
                productCodes,
                salesStartDate,
                today
            );
            var salesRows = await _db.Ado.SqlQueryAsync<SalesSqlRow>(
                salesQuery.Sql,
                salesQuery.Parameters.ToArray()
            );
            sales = salesRows
                .Where(row => row.Date.HasValue)
                .Select(row =>
                    new BrowserExtensionSalesLine
                    {
                        Date = DateOnly.FromDateTime(row.Date!.Value),
                        ProductCode = row.ProductCode,
                        Quantity = row.Quantity,
                        Amount = row.Amount,
                        StatisticLastUpdate = row.StatisticLastUpdate,
                    }
                )
                .ToList();
            response.SalesStatisticLastUpdate = sales
                .Select(row => row.StatisticLastUpdate)
                .Where(value => value.HasValue)
                .DefaultIfEmpty(summary.SalesStatisticLastUpdate)
                .Max();
        }

        response.Cycles = BrowserExtensionPurchaseCycleCalculator.Build(purchases, sales, today);
        return response;
    }

    public async Task<BrowserExtensionStoreOptionsDto> GetEnabledStoresAsync(
        IReadOnlyCollection<string> relatedStoreCodes
    )
    {
        var relatedCodes = (relatedStoreCodes ?? Array.Empty<string>())
            .Select(code => code?.Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (relatedCodes.Count == 0)
        {
            return new BrowserExtensionStoreOptionsDto();
        }

        var enabledPosStoreCodes = await QueryEnabledPosStoreCodesAsync();
        var allowedStoreCodes = enabledPosStoreCodes
            .Where(relatedCodes.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowedStoreCodes.Count == 0)
        {
            return new BrowserExtensionStoreOptionsDto();
        }

        var stores = await _db.Queryable<Store>()
            .Where(store => store.IsActive && !store.IsDeleted)
            .ToListAsync();

        return new BrowserExtensionStoreOptionsDto
        {
            Stores = stores
                .Where(store => allowedStoreCodes.Contains(store.StoreCode?.Trim() ?? string.Empty))
                .OrderBy(store => store.StoreName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(store => store.StoreCode, StringComparer.OrdinalIgnoreCase)
                .Select(store => new BrowserExtensionStoreOptionDto
                {
                    StoreCode = store.StoreCode.Trim(),
                    StoreName = string.IsNullOrWhiteSpace(store.StoreName)
                        ? store.StoreCode.Trim()
                        : store.StoreName.Trim(),
                })
                .ToList(),
        };
    }

    public async Task<BrowserExtensionSupplierTopSalesDto> GetSupplierTopSalesAsync(
        BrowserExtensionSupplierTopSalesRequestDto request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var supplierCode = NormalizeCode(request.SupplierCode, "供应商代码");
        var days = BrowserExtensionRankingLogic.NormalizeDays(request.Days);
        EnsureSupplierEnabled(supplierCode);

        var today = ResolveRankingToday();
        var startDate = BrowserExtensionRankingLogic.ResolveStartDate(today, days);
        var enabledStoreCodes = await QueryEnabledPosStoreCodesAsync();
        var response = new BrowserExtensionSupplierTopSalesDto
        {
            SupplierCode = supplierCode,
            Days = days,
            StartDate = startDate,
            EndDate = today,
            EnabledStoreCount = enabledStoreCodes.Count,
        };
        if (enabledStoreCodes.Count == 0)
        {
            return response;
        }

        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateExclusive = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var salesRows = await _db.Queryable<ProductStoreDailySalesStatistic>()
            .Where(row =>
                enabledStoreCodes.Contains(row.BranchCode)
                && row.SupplierCode == supplierCode
                && row.Date >= startDateTime
                && row.Date < endDateExclusive
            )
            .GroupBy(row => row.ProductCode)
            .Select(row => new BrowserExtensionSupplierSalesAggregate
            {
                ProductCode = row.ProductCode,
                ProductName = SqlFunc.AggregateMax(row.ProductName),
                SalesQuantity = SqlFunc.AggregateSum(row.TotalQuantity),
                SalesAmount = SqlFunc.AggregateSum(row.TotalAmount),
                SalesStatisticLastUpdate = SqlFunc.AggregateMax(row.UpdateTime),
            })
            .ToListAsync();
        response.TotalProductCount = salesRows.Count(row =>
            !string.IsNullOrWhiteSpace(row.ProductCode) && row.SalesQuantity > 0m
        );
        response.SalesStatisticLastUpdate = salesRows
            .Select(row => row.SalesStatisticLastUpdate)
            .Where(value => value.HasValue)
            .DefaultIfEmpty()
            .Max();

        var ranked = BrowserExtensionRankingLogic.RankTopDecile(salesRows);
        if (ranked.Count == 0)
        {
            return response;
        }

        var productCodes = ranked.Select(row => row.ProductCode).ToList();
        var products = await _db.Queryable<Product>()
            .Where(product =>
                !product.IsDeleted
                && product.ProductCode != null
                && productCodes.Contains(product.ProductCode)
            )
            .ToListAsync();
        var productByCode = products
            .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
            .GroupBy(product => product.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(product => product.UpdatedAt ?? product.CreatedAt)
                    .First(),
                StringComparer.OrdinalIgnoreCase
            );

        response.Items = ranked
            .Select(row =>
            {
                productByCode.TryGetValue(row.ProductCode, out var product);
                var itemNumber = product?.ItemNumber?.Trim();
                var productName = product?.ProductName?.Trim();
                return new BrowserExtensionSupplierTopSalesItemDto
                {
                    Rank = row.Rank,
                    ItemNumber = string.IsNullOrWhiteSpace(itemNumber)
                        ? row.ProductCode
                        : itemNumber,
                    ProductCode = row.ProductCode,
                    ProductName = !string.IsNullOrWhiteSpace(productName)
                        ? productName
                        : row.ProductName?.Trim() ?? row.ProductCode,
                    ImageUrl = product?.ProductImage?.Trim(),
                    SalesQuantity = row.SalesQuantity,
                    AverageSellingPrice = row.AverageSellingPrice,
                };
            })
            .ToList();

        _logger.LogInformation(
            "浏览器订货助手供应商热销排行查询完成 SupplierCode={SupplierCode} Days={Days} StoreCount={StoreCount} ProductCount={ProductCount} TopCount={TopCount}",
            supplierCode,
            days,
            enabledStoreCodes.Count,
            response.TotalProductCount,
            response.Items.Count
        );
        return response;
    }

    private async Task<List<BrowserExtensionProductSummaryDto>> QuerySummariesAsync(
        string storeCode,
        string supplierCode,
        IReadOnlyList<string> itemNumbers,
        DateOnly today
    )
    {
        var query = BrowserExtensionPurchaseCycleSqlBuilder.BuildSummary(
            storeCode,
            supplierCode,
            itemNumbers,
            today
        );
        var rows = await _db.Ado.SqlQueryAsync<SummarySqlRow>(
            query.Sql,
            query.Parameters.ToArray()
        );
        var byItemNumber = rows.ToDictionary(
            row => row.ItemNumber,
            StringComparer.OrdinalIgnoreCase
        );

        _logger.LogInformation(
            "浏览器订货助手批量摘要查询完成 StoreCode={StoreCode} SupplierCode={SupplierCode} ItemCount={ItemCount}",
            storeCode,
            supplierCode,
            itemNumbers.Count
        );

        return itemNumbers
            .Select(itemNumber =>
            {
                if (!byItemNumber.TryGetValue(itemNumber, out var row))
                {
                    return new BrowserExtensionProductSummaryDto { ItemNumber = itemNumber };
                }

                return new BrowserExtensionProductSummaryDto
                {
                    ItemNumber = row.ItemNumber,
                    MatchStatus = row.MatchStatus,
                    ProductCode = row.ProductCode,
                    ProductName = row.ProductName,
                    LatestPurchaseDate = row.LatestPurchaseDate.HasValue
                        ? DateOnly.FromDateTime(row.LatestPurchaseDate.Value)
                        : null,
                    LatestPurchaseQuantity = row.LatestPurchaseQuantity,
                    SalesSinceLatestPurchase = row.SalesSinceLatestPurchase,
                    SalesStatisticLastUpdate = row.SalesStatisticLastUpdate,
                };
            })
            .ToList();
    }

    private async Task<DateOnly> ResolveStoreTodayAsync(string storeCode)
    {
        var store = await _db.Queryable<Store>()
            .Where(item => item.StoreCode == storeCode && item.IsActive && !item.IsDeleted)
            .FirstAsync();
        if (store == null)
        {
            throw new KeyNotFoundException("分店不存在或已停用。");
        }

        var timeZoneId =
            StoreTimeZonePolicy.TryNormalize(store.TimeZoneId, out var configuredTimeZone)
            && !string.IsNullOrWhiteSpace(configuredTimeZone)
                ? configuredTimeZone
                : InstallmentOrderStoreTimeZoneResolver.Resolve(store);
        var timeZone = FindTimeZone(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private async Task<List<string>> QueryEnabledPosStoreCodesAsync()
    {
        var storeCodes = await _posmDb.Queryable<POSM_设备注册信息表>()
            .Where(device =>
                device.设备状态 == 1
                && device.设备类型 == "POS"
                && device.分店代码 != null
                && device.分店代码 != ""
            )
            .Select(device => device.分店代码)
            .ToListAsync();

        var enabledPosStoreCodes = storeCodes
            .Select(code => code?.Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabledPosStoreCodes.Count == 0)
        {
            return new List<string>();
        }

        var activeStores = await _db.Queryable<Store>()
            .Where(store => store.IsActive && !store.IsDeleted)
            .Select(store => store.StoreCode)
            .ToListAsync();
        return activeStores
            .Select(code => code?.Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code) && enabledPosStoreCodes.Contains(code))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DateOnly ResolveRankingToday()
    {
        var timeZone = FindTimeZone(StoreTimeZonePolicy.Brisbane);
        var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private void EnsureSupplierEnabled(string supplierCode)
    {
        if (
            !GetSupplierProfiles()
                .Profiles.Any(profile =>
                    profile.SupplierCode.Equals(supplierCode, StringComparison.OrdinalIgnoreCase)
                )
        )
        {
            throw new KeyNotFoundException("供应商配置不存在或已停用。");
        }
    }

    private static string NormalizeCode(string value, string fieldName)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{fieldName}不能为空。", nameof(value));
        }

        if (normalized.Length > 50)
        {
            throw new ArgumentException($"{fieldName}最长为 50 个字符。", nameof(value));
        }

        return normalized;
    }

    private static TimeZoneInfo FindTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(StoreTimeZonePolicy.Brisbane);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(StoreTimeZonePolicy.Brisbane);
        }
    }

    private sealed class SummarySqlRow
    {
        public string ItemNumber { get; set; } = string.Empty;
        public string MatchStatus { get; set; } = BrowserExtensionMatchStatuses.Unmatched;
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public DateTime? LatestPurchaseDate { get; set; }
        public decimal? LatestPurchaseQuantity { get; set; }
        public decimal SalesSinceLatestPurchase { get; set; }
        public DateTime? SalesStatisticLastUpdate { get; set; }
    }

    private sealed class PurchaseSqlRow
    {
        public DateTime? PurchaseDate { get; set; }
        public string? InvoiceNumber { get; set; }
        public decimal Quantity { get; set; }
        public decimal? PurchasePrice { get; set; }
        public decimal? Amount { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
    }

    private sealed class SalesSqlRow
    {
        public DateTime? Date { get; set; }
        public string? ProductCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public DateTime? StatisticLastUpdate { get; set; }
    }
}

public static partial class BrowserExtensionProfileCatalog
{
    public const string ExtensionVersionHeader = "X-HB-Extension-Version";
    public const string SupplierProfilesMinimumClientVersion = "1.1.0";
    public const string ExtendedProfilesMinimumClientVersion = "1.2.0";

    private static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "attribute",
        "text",
    };

    private static readonly HashSet<string> AllowedTransforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "trim",
        "uppercase",
        "lowercase",
        "after-colon",
        "underscore-to-slash",
        "after-sku",
    };

    private static readonly HashSet<string> AllowedMountPositions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "beforebegin",
        "afterbegin",
        "beforeend",
        "afterend",
    };

    public static BrowserExtensionReleaseDto BuildRelease(BrowserExtensionOptions options) =>
        new()
        {
            LatestVersion = NormalizeVersion(options.LatestVersion, "1.0.0"),
            MinimumVersion = NormalizeVersion(options.MinimumVersion, "1.0.0"),
            ChromeStoreUrl = NormalizeHttpsUrl(options.ChromeStoreUrl),
            EdgeStoreUrl = NormalizeHttpsUrl(options.EdgeStoreUrl),
            ReleaseNotes = new BrowserExtensionReleaseNotesDto
            {
                Zh = options.ReleaseNotesZh?.Trim() ?? string.Empty,
                En = options.ReleaseNotesEn?.Trim() ?? string.Empty,
            },
        };

    public static BrowserExtensionSupplierProfilesDto BuildProfiles(BrowserExtensionOptions options)
    {
        var configuredProfiles = options.SupplierProfiles
            ?? new List<BrowserExtensionSupplierProfileOptions>();
        var configuredCodes = configuredProfiles
            .Select(profile => profile.SupplierCode?.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInProfiles = new List<BrowserExtensionSupplierProfileOptions>();
        if (options.UseBuiltInDatsProfile)
        {
            builtInProfiles.Add(BrowserExtensionSupplierProfileOptions.CreateDatsDefault());
        }
        if (options.UseBuiltInSupplierProfiles)
        {
            builtInProfiles.AddRange(BrowserExtensionSupplierProfileOptions.CreateSupplierDefaults());
        }

        // 同业务供应商代码的后台配置优先；配置为 disabled 可立即停用有问题的内置规则。
        var candidates = builtInProfiles
            .Where(profile => !configuredCodes.Contains(profile.SupplierCode))
            .Concat(configuredProfiles);

        return new BrowserExtensionSupplierProfilesDto
        {
            ConfigVersion = string.IsNullOrWhiteSpace(options.ConfigVersion)
                ? "1"
                : options.ConfigVersion.Trim(),
            Profiles = candidates
                .Where(profile => profile.Enabled)
                .Select(TryBuildProfile)
                .Where(profile => profile != null)
                .Cast<BrowserExtensionSupplierProfileDto>()
                .GroupBy(profile => profile.SupplierCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList(),
        };
    }

    public static BrowserExtensionSupplierProfilesDto FilterProfilesForClient(
        BrowserExtensionSupplierProfilesDto profiles,
        string? extensionVersion
    )
    {
        if (IsVersionAtLeast(extensionVersion, ExtendedProfilesMinimumClientVersion))
        {
            return profiles;
        }

        if (IsVersionAtLeast(extensionVersion, SupplierProfilesMinimumClientVersion))
        {
            // 1.1.x 不认识新增转换，也没有 TXK 的精确 HTTP host permission。
            return new BrowserExtensionSupplierProfilesDto
            {
                ConfigVersion = profiles.ConfigVersion,
                Profiles = profiles.Profiles
                    .Where(profile =>
                        profile.Origins.All(origin => origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        && profile.ItemNumber.Transforms.All(transform =>
                            !transform.Equals("underscore-to-slash", StringComparison.OrdinalIgnoreCase)
                            && !transform.Equals("after-sku", StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .ToList(),
            };
        }

        // 1.0.x 不认识 after-colon；仅返回旧客户端可安全校验的 DATS，避免整份目录失效。
        return new BrowserExtensionSupplierProfilesDto
        {
            ConfigVersion = profiles.ConfigVersion,
            Profiles = profiles.Profiles
                .Where(profile => profile.SupplierCode == "240")
                .ToList(),
        };
    }

    private static bool IsVersionAtLeast(string? value, string minimum)
    {
        return Version.TryParse(value?.Trim(), out var version)
            && Version.TryParse(minimum, out var minimumVersion)
            && version >= minimumVersion;
    }

    private static BrowserExtensionSupplierProfileDto? TryBuildProfile(
        BrowserExtensionSupplierProfileOptions options
    )
    {
        var supplierCode = options.SupplierCode?.Trim().ToUpperInvariant();
        var source = options.ItemNumberSource?.Trim().ToLowerInvariant();
        var origins = (options.Origins ?? new List<string>())
            .Select(value => value?.Trim())
            .Where(value => IsSafeMatchPattern(value, originOnly: true))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pagePatterns = (options.ListPagePatterns ?? new List<string>())
            .Select(value => value?.Trim())
            .Where(value => IsSafeMatchPattern(value, originOnly: false))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var transforms = (options.ItemNumberTransforms ?? new List<string>())
            .Select(value => value?.Trim().ToLowerInvariant())
            .Where(value => value != null && AllowedTransforms.Contains(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mountPosition = options.MountPosition?.Trim().ToLowerInvariant();

        if (
            string.IsNullOrWhiteSpace(supplierCode)
            || supplierCode.Length > 50
            || string.IsNullOrWhiteSpace(source)
            || !AllowedSources.Contains(source)
            || origins.Count == 0
            || pagePatterns.Count == 0
            || !IsSafeSelector(options.CardSelector)
            || !IsSafeSelector(options.MountSelector)
            || string.IsNullOrWhiteSpace(mountPosition)
            || !AllowedMountPositions.Contains(mountPosition)
            || (
                source.Equals("attribute", StringComparison.OrdinalIgnoreCase)
                && !IsSafeAttributeName(options.ItemNumberAttribute)
            )
            || !IsSafeSelector(options.ItemNumberSelector, allowEmpty: true)
        )
        {
            return null;
        }

        return new BrowserExtensionSupplierProfileDto
        {
            SupplierCode = supplierCode,
            DisplayName = string.IsNullOrWhiteSpace(options.DisplayName)
                ? supplierCode
                : options.DisplayName.Trim(),
            Enabled = true,
            Origins = origins,
            ListPagePatterns = pagePatterns,
            CardSelector = options.CardSelector.Trim(),
            ItemNumber = new BrowserExtensionItemNumberRuleDto
            {
                Source = source,
                Selector = string.IsNullOrWhiteSpace(options.ItemNumberSelector)
                    ? null
                    : options.ItemNumberSelector.Trim(),
                Attribute = string.IsNullOrWhiteSpace(options.ItemNumberAttribute)
                    ? null
                    : options.ItemNumberAttribute.Trim(),
                Transforms = transforms,
            },
            MountSelector = options.MountSelector.Trim(),
            MountPosition = mountPosition,
        };
    }

    private static bool IsSafeMatchPattern(string? value, bool originOnly)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 300)
        {
            return false;
        }

        var match = HttpsMatchPatternRegex().Match(value);
        if (!match.Success)
        {
            // 供应商目前只提供 HTTP；仅允许这个经过核验的精确主机，不开放 HTTP 通配。
            match = TxkHttpMatchPatternRegex().Match(value);
        }
        if (!match.Success)
        {
            return false;
        }

        return !originOnly || match.Groups["path"].Value == "/*";
    }

    private static bool IsSafeSelector(string? value, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return allowEmpty;
        }

        return value.Length <= 500 && !value.Contains('\0') && !value.Contains('\n');
    }

    private static bool IsSafeAttributeName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100
        && AttributeNameRegex().IsMatch(value);

    private static string NormalizeVersion(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return normalized != null && VersionRegex().IsMatch(normalized) ? normalized : fallback;
    }

    private static string NormalizeHttpsUrl(string? value)
    {
        var normalized = value?.Trim();
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : string.Empty;
    }

    [GeneratedRegex(@"^https://(?<host>(?:\*\.)?[A-Za-z0-9.-]+)(?::\d+)?(?<path>/[^\s]*)$")]
    private static partial Regex HttpsMatchPatternRegex();

    [GeneratedRegex(@"^http://txkorders\.inzantsales\.com(?<path>/[^\s]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TxkHttpMatchPatternRegex();

    [GeneratedRegex(@"^[A-Za-z_:][A-Za-z0-9_:.-]*$")]
    private static partial Regex AttributeNameRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionRegex();
}
