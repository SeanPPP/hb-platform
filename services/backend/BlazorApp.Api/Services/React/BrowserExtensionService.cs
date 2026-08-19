using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Models;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class BrowserExtensionService : IBrowserExtensionService
{
    private readonly ISqlSugarClient _db;
    private readonly IOptionsSnapshot<BrowserExtensionOptions> _options;
    private readonly ILogger<BrowserExtensionService> _logger;
    private readonly TimeProvider _timeProvider;

    public BrowserExtensionService(
        SqlSugarContext context,
        IOptionsSnapshot<BrowserExtensionOptions> options,
        ILogger<BrowserExtensionService> logger,
        TimeProvider? timeProvider = null
    )
    {
        _db = context.Db;
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
        IEnumerable<BrowserExtensionSupplierProfileOptions> candidates = configuredProfiles;
        if (
            options.UseBuiltInDatsProfile
            && !configuredProfiles.Any(profile =>
                string.Equals(profile.SupplierCode, "DATS", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            candidates = new[] { BrowserExtensionSupplierProfileOptions.CreateDatsDefault() }
                .Concat(configuredProfiles);
        }

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

    [GeneratedRegex(@"^[A-Za-z_:][A-Za-z0-9_:.-]*$")]
    private static partial Regex AttributeNameRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionRegex();
}
