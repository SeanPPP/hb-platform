using System.Globalization;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 查询仓库商品在指定 Brisbane 自然日范围内最后一次零售价变更。
/// </summary>
public sealed class WarehouseRetailPriceChangeService : IWarehouseRetailPriceChangeService
{
    private const int NonSqlCandidateLimit = 10_000;
    private const int QueryBatchSize = 1_000;
    private static readonly TimeZoneInfo BrisbaneTimeZone = ResolveBrisbaneTimeZone();
    private readonly SqlSugarContext _context;
    private readonly TimeProvider _timeProvider;

    public WarehouseRetailPriceChangeService(SqlSugarContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WarehouseRetailPriceChangePage> GetAsync(
        WarehouseRetailPriceChangeQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = NormalizeQuery(query, _timeProvider.GetUtcNow());
        return _context.Db.CurrentConnectionConfig.DbType == DbType.SqlServer
            ? await GetSqlServerAsync(normalized, cancellationToken)
            : await GetNonSqlAsync(normalized, cancellationToken);
    }

    /// <summary>
    /// 将 Brisbane 的闭区间日期转换为 UTC 半开区间，默认覆盖完整当月而非截至当前日。
    /// </summary>
    public static WarehouseRetailPriceChangeNormalizedQuery NormalizeQuery(
        WarehouseRetailPriceChangeQuery query,
        DateTimeOffset nowUtc
    )
    {
        var brisbaneToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(nowUtc, BrisbaneTimeZone).DateTime
        );
        var firstDayOfMonth = new DateOnly(brisbaneToday.Year, brisbaneToday.Month, 1);
        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
        if (query.StartDate.HasValue != query.EndDate.HasValue)
            throw new ArgumentException("开始日期和结束日期必须同时提供。");
        if (query.PageNumber < 1)
            throw new ArgumentException("页码必须大于或等于 1。");
        if (query.PageSize is < 1 or > 100)
            throw new ArgumentException("每页数量必须在 1 到 100 之间。");

        var startDate = query.StartDate ?? firstDayOfMonth;
        var endDate = query.EndDate ?? lastDayOfMonth;
        if (endDate < startDate)
            throw new ArgumentException("结束日期不能早于开始日期。");
        if (endDate.DayNumber - startDate.DayNumber > 365)
            throw new ArgumentException("日期范围最多 366 天。");

        DateTime endExclusiveUtc;
        try
        {
            endExclusiveUtc = ToUtc(endDate.AddDays(1));
            _ = checked((query.PageNumber - 1) * query.PageSize);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException("结束日期超出可查询范围。", ex);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("分页参数超出可查询范围。", ex);
        }

        return new WarehouseRetailPriceChangeNormalizedQuery(
            startDate,
            endDate,
            query.OnlyWithLocation,
            string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim(),
            query.PageNumber,
            query.PageSize,
            ToUtc(startDate),
            endExclusiveUtc
        );
    }

    public static string BuildSqlServerPageSql() => SqlServerPageSql;

    public static string BuildSqlServerCountSql() => SqlServerCountSql;

    private async Task<WarehouseRetailPriceChangePage> GetSqlServerAsync(
        WarehouseRetailPriceChangeNormalizedQuery query,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _context.Db.Ado.SqlQueryAsync<SqlServerRow>(
            SqlServerPageSql,
            CreateSqlParameters(query, includePaging: true)
        );
        cancellationToken.ThrowIfCancellationRequested();
        var offset = checked((query.PageNumber - 1) * query.PageSize);
        var total = rows.Count == 0 && offset > 0
            ? await GetSqlServerTotalAsync(query, cancellationToken)
            : rows.Count == 0 ? 0 : rows[0].Total;

        return CreatePage(
            query,
            total,
            rows.Select(row => new WarehouseRetailPriceChangeItem
            {
                ProductCode = row.ProductCode,
                ProductImage = row.ProductImage,
                ItemNumber = row.ItemNumber,
                Barcode = row.Barcode,
                LatestRetailPrice = row.LatestRetailPrice,
                LastPriceChangedAtUtc = AsUtc(row.LastPriceChangedAtUtc),
            })
        );
    }

    private async Task<int> GetSqlServerTotalAsync(
        WarehouseRetailPriceChangeNormalizedQuery query,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await _context.Db.Ado.SqlQueryAsync<SqlServerCountRow>(
            SqlServerCountSql,
            CreateSqlParameters(query, includePaging: false)
        );
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Single().Total;
    }

    private static SugarParameter[] CreateSqlParameters(
        WarehouseRetailPriceChangeNormalizedQuery query,
        bool includePaging
    )
    {
        var parameters = new List<SugarParameter>
        {
            new("@StartUtc", query.StartUtc),
            new("@EndExclusiveUtc", query.EndExclusiveUtc),
            new("@OnlyWithLocation", query.OnlyWithLocation),
            new("@Keyword", (object?)query.Keyword ?? DBNull.Value),
        };
        if (includePaging)
        {
            parameters.Add(new SugarParameter("@Offset", checked((query.PageNumber - 1) * query.PageSize)));
            parameters.Add(new SugarParameter("@PageSize", query.PageSize));
        }
        return parameters.ToArray();
    }

    private async Task<WarehouseRetailPriceChangePage> GetNonSqlAsync(
        WarehouseRetailPriceChangeNormalizedQuery query,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var histories = await _context.Db.Queryable<WarehouseProductChangeHistory>()
            .Where(item => item.Action != "Create")
            .Where(item => item.OccurredAtUtc >= query.StartUtc && item.OccurredAtUtc < query.EndExclusiveUtc)
            .Where(item => item.ChangesJson.Contains("retailPrice"))
            .Take(NonSqlCandidateLimit + 1)
            .ToListAsync();
        if (histories.Count > NonSqlCandidateLimit)
            throw new ArgumentException("查询范围内候选记录过多，请缩小日期范围。");

        var latestByCode = histories
            .Select(ToRetailPriceChange)
            .Where(item => item != null)
            .Cast<RetailPriceChangeCandidate>()
            .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id).First())
            .ToList();
        if (latestByCode.Count == 0)
            return CreatePage(query, 0, []);

        var productCodes = latestByCode.Select(item => item.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var warehouseProducts = await QueryWarehouseProductsAsync(productCodes, cancellationToken);
        var currentWarehouseCodes = warehouseProducts
            .Where(item => !item.IsDeleted)
            .Select(item => item.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eligibleChanges = latestByCode
            .Where(item => currentWarehouseCodes.Contains(item.ProductCode))
            .ToList();
        if (eligibleChanges.Count == 0)
            return CreatePage(query, 0, []);

        var eligibleCodes = eligibleChanges.Select(item => item.ProductCode).ToList();
        var products = await QueryProductsAsync(eligibleCodes, cancellationToken);
        var domesticProducts = await QueryDomesticProductsAsync(eligibleCodes, cancellationToken);
        var productByCode = SelectCurrentProducts(products);
        var domesticByCode = SelectCurrentDomesticProducts(domesticProducts);
        var codesWithLocation = query.OnlyWithLocation
            ? await QueryCodesWithLocationAsync(eligibleCodes, cancellationToken)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rows = eligibleChanges
            .Where(item => !query.OnlyWithLocation || codesWithLocation.Contains(item.ProductCode))
            .Select(item => CreateNonSqlRow(item, productByCode, domesticByCode))
            .Where(item => KeywordMatches(item, query.Keyword))
            .OrderByDescending(item => item.LastPriceChangedAtUtc)
            .ThenBy(item => string.IsNullOrWhiteSpace(item.ItemNumber) ? 1 : 0)
            .ThenBy(item => item.ItemNumber, StringComparer.Ordinal)
            .ThenBy(item => item.ProductCode, StringComparer.Ordinal)
            .ToList();

        var pageItems = rows
            .Skip(checked((query.PageNumber - 1) * query.PageSize))
            .Take(query.PageSize)
            .ToList();
        return CreatePage(query, rows.Count, pageItems);
    }

    private async Task<List<WarehouseProduct>> QueryWarehouseProductsAsync(
        IReadOnlyCollection<string> productCodes,
        CancellationToken cancellationToken
    )
    {
        var result = new List<WarehouseProduct>();
        foreach (var codes in productCodes.Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = codes.ToList();
            result.AddRange(await _context.Db.Queryable<WarehouseProduct>()
                .Where(item => batch.Contains(item.ProductCode))
                .Where(item => !item.IsDeleted)
                .ToListAsync());
        }
        return result;
    }

    private async Task<List<Product>> QueryProductsAsync(
        IReadOnlyCollection<string> productCodes,
        CancellationToken cancellationToken
    )
    {
        var result = new List<Product>();
        foreach (var codes in productCodes.Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = codes.ToList();
            result.AddRange(await _context.Db.Queryable<Product>()
                .Where(item => item.ProductCode != null && batch.Contains(item.ProductCode))
                .Where(item => !item.IsDeleted)
                .ToListAsync());
        }
        return result;
    }

    private async Task<List<DomesticProduct>> QueryDomesticProductsAsync(
        IReadOnlyCollection<string> productCodes,
        CancellationToken cancellationToken
    )
    {
        var result = new List<DomesticProduct>();
        foreach (var codes in productCodes.Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = codes.ToList();
            result.AddRange(await _context.Db.Queryable<DomesticProduct>()
                .Where(item => batch.Contains(item.ProductCode))
                .Where(item => !item.IsDeleted)
                .ToListAsync());
        }
        return result;
    }

    private async Task<HashSet<string>> QueryCodesWithLocationAsync(
        IReadOnlyCollection<string> productCodes,
        CancellationToken cancellationToken
    )
    {
        var mappings = new List<ProductLocation>();
        foreach (var codes in productCodes.Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = codes.ToList();
            mappings.AddRange(await _context.Db.Queryable<ProductLocation>()
                .Where(item => item.ProductCode != null && batch.Contains(item.ProductCode))
                .Where(item => !item.IsDeleted)
                .ToListAsync());
        }
        var locationGuids = mappings
            .Select(item => item.LocationGuid)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var locations = new List<Location>();
        foreach (var guids in locationGuids.Chunk(QueryBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = guids.ToList();
            locations.AddRange(await _context.Db.Queryable<Location>()
                .Where(item => batch.Contains(item.LocationGuid))
                .Where(item => !item.IsDeleted)
                .ToListAsync());
        }
        var currentLocationGuids = locations.Select(item => item.LocationGuid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return mappings
            .Where(item => item.ProductCode != null && item.LocationGuid != null
                && currentLocationGuids.Contains(item.LocationGuid))
            .Select(item => item.ProductCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Product> SelectCurrentProducts(IEnumerable<Product> products) => products
        .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
        .GroupBy(item => item.ProductCode!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .ThenBy(item => item.UUID, StringComparer.Ordinal).First(),
            StringComparer.OrdinalIgnoreCase
        );

    private static Dictionary<string, DomesticProduct> SelectCurrentDomesticProducts(
        IEnumerable<DomesticProduct> products
    ) => products
        .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
        .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .ThenBy(item => item.HBProductNo, StringComparer.Ordinal).First(),
            StringComparer.OrdinalIgnoreCase
        );

    private static WarehouseRetailPriceChangeItem CreateNonSqlRow(
        RetailPriceChangeCandidate change,
        IReadOnlyDictionary<string, Product> products,
        IReadOnlyDictionary<string, DomesticProduct> domesticProducts
    )
    {
        // Product 当前行存在时整行取 Product；仅整行缺失才回退 Domestic，防止跨主档拼接元数据。
        if (products.TryGetValue(change.ProductCode, out var product))
        {
            return new WarehouseRetailPriceChangeItem
            {
                ProductCode = change.ProductCode,
                ProductImage = product.ProductImage,
                ItemNumber = product.ItemNumber,
                Barcode = product.Barcode,
                LatestRetailPrice = change.LatestRetailPrice,
                LastPriceChangedAtUtc = AsUtc(change.OccurredAtUtc),
            };
        }
        if (domesticProducts.TryGetValue(change.ProductCode, out var domesticProduct))
        {
            return new WarehouseRetailPriceChangeItem
            {
                ProductCode = change.ProductCode,
                ProductImage = domesticProduct.ProductImage,
                ItemNumber = domesticProduct.HBProductNo,
                Barcode = domesticProduct.Barcode,
                LatestRetailPrice = change.LatestRetailPrice,
                LastPriceChangedAtUtc = AsUtc(change.OccurredAtUtc),
            };
        }
        return new WarehouseRetailPriceChangeItem
        {
            ProductCode = change.ProductCode,
            LatestRetailPrice = change.LatestRetailPrice,
            LastPriceChangedAtUtc = AsUtc(change.OccurredAtUtc),
        };
    }

    private static RetailPriceChangeCandidate? ToRetailPriceChange(WarehouseProductChangeHistory history)
    {
        try
        {
            using var document = JsonDocument.Parse(history.ChangesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("fieldKey", out var fieldKey)
                    || !string.Equals(fieldKey.GetString(), "retailPrice", StringComparison.Ordinal))
                    continue;
                return new RetailPriceChangeCandidate(
                    history.Id,
                    history.ProductCode,
                    history.OccurredAtUtc,
                    ReadDecimalOrNull(entry, "afterValue")
                );
            }
        }
        catch (JsonException)
        {
            // 非 SQL Server 需要和 ISJSON 一致地忽略损坏的历史 JSON，而不是令整页失败。
        }
        return null;
    }

    private static decimal? ReadDecimalOrNull(JsonElement entry, string propertyName)
    {
        if (!entry.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed
            ) => parsed,
            _ => null,
        };
    }

    private static bool KeywordMatches(WarehouseRetailPriceChangeItem item, string? keyword) =>
        string.IsNullOrEmpty(keyword)
        || ContainsIgnoreCase(item.ProductCode, keyword)
        || ContainsIgnoreCase(item.ItemNumber, keyword)
        || ContainsIgnoreCase(item.Barcode, keyword);

    private static bool ContainsIgnoreCase(string? value, string keyword) => value?.Contains(
        keyword, StringComparison.OrdinalIgnoreCase
    ) == true;

    private static WarehouseRetailPriceChangePage CreatePage(
        WarehouseRetailPriceChangeNormalizedQuery query,
        int total,
        IEnumerable<WarehouseRetailPriceChangeItem> items
    ) => new()
    {
        StartDate = query.StartDate,
        EndDate = query.EndDate,
        OnlyWithLocation = query.OnlyWithLocation,
        PageNumber = query.PageNumber,
        PageSize = query.PageSize,
        Total = total,
        Items = items.ToList(),
    };

    private static DateTime ToUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
        BrisbaneTimeZone
    );

    private static TimeZoneInfo ResolveBrisbaneTimeZone()
    {
        foreach (var timeZoneId in new[] { "Australia/Brisbane", "E. Australia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // 继续尝试另一平台的时区 ID。
            }
            catch (InvalidTimeZoneException)
            {
                // 系统时区数据损坏时仍使用固定 Brisbane 标准时的安全回退。
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone(
            "Australia/Brisbane-Fallback",
            TimeSpan.FromHours(10),
            "Australia/Brisbane",
            "Australia/Brisbane"
        );
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record RetailPriceChangeCandidate(
        long Id,
        string ProductCode,
        DateTime OccurredAtUtc,
        decimal? LatestRetailPrice
    );

    private sealed class SqlServerRow
    {
        public string ProductCode { get; set; } = string.Empty;
        public string? ProductImage { get; set; }
        public string? ItemNumber { get; set; }
        public string? Barcode { get; set; }
        public decimal? LatestRetailPrice { get; set; }
        public DateTime LastPriceChangedAtUtc { get; set; }
        public int Total { get; set; }
    }

    private sealed class SqlServerCountRow
    {
        public int Total { get; set; }
    }

    private const string SqlServerFilteredCte = """
WITH PriceChanges AS (
    SELECT
        [h].[Id], [h].[ProductCode], [h].[OccurredAtUtc],
        JSON_VALUE([change].[value], '$.afterValue') AS [LatestRetailPriceText],
        ROW_NUMBER() OVER (
            PARTITION BY [h].[ProductCode]
            ORDER BY [h].[OccurredAtUtc] DESC, [h].[Id] DESC
        ) AS [LatestRank]
    FROM [dbo].[WarehouseProductChangeHistory] AS [h]
    -- 先将无效 JSON 归为空数组，避免 SQL 优化器调整 WHERE 求值顺序时 OPENJSON 抛错。
    CROSS APPLY OPENJSON(CASE WHEN ISJSON([h].[ChangesJson]) = 1 THEN [h].[ChangesJson] ELSE N'[]' END) AS [change]
    WHERE [h].[Action] <> N'Create'
      AND [h].[OccurredAtUtc] >= @StartUtc
      AND [h].[OccurredAtUtc] < @EndExclusiveUtc
      AND ISJSON([h].[ChangesJson]) = 1
      AND JSON_VALUE([change].[value], '$.fieldKey') = N'retailPrice'
), Metadata AS (
    SELECT
        [c].[ProductCode],
        CASE WHEN [p].[UUID] IS NULL THEN [d].[ProductImage] ELSE [p].[ProductImage] END AS [ProductImage],
        CASE WHEN [p].[UUID] IS NULL THEN [d].[HBProductNo] ELSE [p].[ItemNumber] END AS [ItemNumber],
        CASE WHEN [p].[UUID] IS NULL THEN [d].[Barcode] ELSE [p].[Barcode] END AS [Barcode],
        TRY_CONVERT(decimal(18, 4), [c].[LatestRetailPriceText]) AS [LatestRetailPrice],
        [c].[OccurredAtUtc] AS [LastPriceChangedAtUtc]
    FROM PriceChanges AS [c]
    INNER JOIN [dbo].[WarehouseProduct] AS [w]
        ON [w].[ProductCode] = [c].[ProductCode] AND ISNULL([w].[IsDeleted], 0) = 0
    OUTER APPLY (
        SELECT TOP (1) [candidate].*
        FROM [dbo].[Product] AS [candidate]
        WHERE [candidate].[ProductCode] = [c].[ProductCode]
          AND ISNULL([candidate].[IsDeleted], 0) = 0
        ORDER BY ISNULL([candidate].[UpdatedAt], [candidate].[CreatedAt]) DESC, [candidate].[UUID] ASC
    ) AS [p]
    OUTER APPLY (
        SELECT TOP (1) [candidate].*
        FROM [dbo].[DomesticProduct] AS [candidate]
        WHERE [candidate].[ProductCode] = [c].[ProductCode]
          AND ISNULL([candidate].[IsDeleted], 0) = 0
        ORDER BY ISNULL([candidate].[UpdatedAt], [candidate].[CreatedAt]) DESC, [candidate].[HBProductNo] ASC
    ) AS [d]
    WHERE [c].[LatestRank] = 1
      AND (
        @OnlyWithLocation = 0 OR EXISTS (
            SELECT 1
            FROM [dbo].[ProductLocation] AS [pl]
            INNER JOIN [dbo].[Location] AS [location]
                ON [location].[LocationGuid] = [pl].[LocationGuid]
               AND ISNULL([location].[IsDeleted], 0) = 0
            WHERE [pl].[ProductCode] = [c].[ProductCode]
              AND ISNULL([pl].[IsDeleted], 0) = 0
        )
      )
), Filtered AS (
    SELECT *
    FROM Metadata
    WHERE @Keyword IS NULL
       OR CHARINDEX(@Keyword, [ProductCode]) > 0
       OR CHARINDEX(@Keyword, [ItemNumber]) > 0
       OR CHARINDEX(@Keyword, [Barcode]) > 0
)
""";

    private const string SqlServerPageSql = SqlServerFilteredCte + """
SELECT
    [ProductCode], [ProductImage], [ItemNumber], [Barcode], [LatestRetailPrice], [LastPriceChangedAtUtc],
    COUNT(1) OVER () AS [Total]
FROM Filtered
ORDER BY [LastPriceChangedAtUtc] DESC,
    CASE WHEN [ItemNumber] IS NULL OR LTRIM(RTRIM([ItemNumber])) = N'' THEN 1 ELSE 0 END ASC,
    [ItemNumber] ASC, [ProductCode] ASC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
""";

    private const string SqlServerCountSql = SqlServerFilteredCte + """
SELECT COUNT(1) AS [Total]
FROM Filtered;
""";
}

public sealed record WarehouseRetailPriceChangeNormalizedQuery(
    DateOnly StartDate,
    DateOnly EndDate,
    bool OnlyWithLocation,
    string? Keyword,
    int PageNumber,
    int PageSize,
    DateTime StartUtc,
    DateTime EndExclusiveUtc
);
