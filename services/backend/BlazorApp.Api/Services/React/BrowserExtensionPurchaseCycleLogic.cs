using System.Text.RegularExpressions;
using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class BrowserExtensionPurchaseLine
{
    public DateOnly PurchaseDate { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? Amount { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
}

public sealed class BrowserExtensionSalesLine
{
    public DateOnly Date { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public string? ProductCode { get; set; }
    public DateTime? StatisticLastUpdate { get; set; }
}

public static class BrowserExtensionPurchaseCycleCalculator
{
    public const int MaximumCycles = 6;

    public static List<BrowserExtensionPurchaseCycleDto> Build(
        IEnumerable<BrowserExtensionPurchaseLine> purchaseLines,
        IEnumerable<BrowserExtensionSalesLine> salesLines,
        DateOnly today
    )
    {
        var cutoff = today.AddMonths(-12);
        var events = purchaseLines
            .Where(line => line.PurchaseDate >= cutoff && line.PurchaseDate <= today)
            .GroupBy(line => line.PurchaseDate)
            .Select(group => BuildPurchaseEvent(group.Key, group.ToList()))
            .OrderByDescending(item => item.PurchaseDate)
            .Take(MaximumCycles)
            .ToList();

        var sales = salesLines
            .Where(line => line.Date >= cutoff && line.Date <= today)
            .ToList();
        var result = new List<BrowserExtensionPurchaseCycleDto>(events.Count);

        for (var index = 0; index < events.Count; index++)
        {
            var purchase = events[index];
            var salesEnd = index == 0 ? today : events[index - 1].PurchaseDate.AddDays(-1);
            var cycleSales = sales
                .Where(line => line.Date >= purchase.PurchaseDate && line.Date <= salesEnd)
                .ToList();
            var salesQuantity = cycleSales.Sum(line => line.Quantity);
            var salesAmount = cycleSales.Sum(line => line.Amount);

            result.Add(
                new BrowserExtensionPurchaseCycleDto
                {
                    PurchaseDate = purchase.PurchaseDate,
                    InvoiceNumbers = purchase.InvoiceNumbers,
                    PurchaseQuantity = purchase.PurchaseQuantity,
                    AveragePurchasePrice = purchase.AveragePurchasePrice,
                    SalesStartDate = purchase.PurchaseDate,
                    SalesEndDate = salesEnd,
                    SalesQuantity = salesQuantity,
                    AverageSalePrice = salesQuantity == 0m ? null : salesAmount / salesQuantity,
                }
            );
        }

        return result;
    }

    private static PurchaseEvent BuildPurchaseEvent(
        DateOnly purchaseDate,
        IReadOnlyCollection<BrowserExtensionPurchaseLine> lines
    )
    {
        var quantity = lines.Sum(line => line.Quantity);
        var hasPrice = lines.Any(line => line.Amount.HasValue || line.PurchasePrice.HasValue);
        var amount = lines.Sum(line =>
            line.Amount ?? (line.PurchasePrice.HasValue ? line.Quantity * line.PurchasePrice.Value : 0m)
        );

        return new PurchaseEvent
        {
            PurchaseDate = purchaseDate,
            InvoiceNumbers = lines
                .Select(line => line.InvoiceNumber?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PurchaseQuantity = quantity,
            AveragePurchasePrice = hasPrice && quantity != 0m ? amount / quantity : null,
        };
    }

    private sealed class PurchaseEvent
    {
        public DateOnly PurchaseDate { get; init; }
        public List<string> InvoiceNumbers { get; init; } = new();
        public decimal PurchaseQuantity { get; init; }
        public decimal? AveragePurchasePrice { get; init; }
    }
}

public sealed class BrowserExtensionSqlQuery
{
    public string Sql { get; init; } = string.Empty;
    public List<SugarParameter> Parameters { get; init; } = new();
}

public static partial class BrowserExtensionPurchaseCycleSqlBuilder
{
    public const int MaximumBatchSize = 100;

    public static IReadOnlyList<string> NormalizeItemNumbers(IEnumerable<string>? itemNumbers)
    {
        var normalized = (itemNumbers ?? Array.Empty<string>())
            .Select(value => value?.Trim().ToUpperInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            throw new ArgumentException("商品货号不能为空。", nameof(itemNumbers));
        }

        if (normalized.Count > MaximumBatchSize)
        {
            throw new ArgumentException($"一次最多查询 {MaximumBatchSize} 个商品货号。", nameof(itemNumbers));
        }

        if (normalized.Any(value => value.Length > 50))
        {
            throw new ArgumentException("商品货号最长为 50 个字符。", nameof(itemNumbers));
        }

        return normalized;
    }

    public static BrowserExtensionSqlQuery BuildSummary(
        string storeCode,
        string supplierCode,
        IEnumerable<string> itemNumbers,
        DateOnly today
    )
    {
        var normalizedStoreCode = NormalizeCode(storeCode, "分店代码");
        var normalizedSupplierCode = NormalizeCode(supplierCode, "供应商代码");
        var normalizedItems = NormalizeItemNumbers(itemNumbers);
        var valuesSql = string.Join(
            ",\n        ",
            normalizedItems.Select((_, index) => $"(@Item{index})")
        );
        var parameters = new List<SugarParameter>
        {
            new("@StoreCode", normalizedStoreCode),
            new("@SupplierCode", normalizedSupplierCode),
            new("@Today", today.ToDateTime(TimeOnly.MinValue)),
        };
        parameters.AddRange(
            normalizedItems.Select((value, index) => new SugarParameter($"@Item{index}", value))
        );

        var sql = $$"""
            WITH RequestedItems (ItemNumber) AS (
                SELECT source.ItemNumber
                FROM (VALUES
                    {{valuesSql}}
                ) source(ItemNumber)
            ),
            ProductMatches AS (
                SELECT
                    requested.ItemNumber,
                    MAX(CASE WHEN p.UUID IS NULL THEN 0 ELSE 1 END) AS HasProduct,
                    MAX(NULLIF(p.ProductCode, N'')) AS ProductCode,
                    MAX(NULLIF(p.ProductName, N'')) AS ProductName
                FROM RequestedItems requested
                LEFT JOIN [Product] p
                    ON UPPER(LTRIM(RTRIM(p.ItemNumber))) = requested.ItemNumber
                    AND UPPER(LTRIM(RTRIM(COALESCE(p.LocalSupplierCode, N'')))) = @SupplierCode
                    AND COALESCE(p.IsDeleted, 0) = 0
                GROUP BY requested.ItemNumber
            ),
            -- 列表按钮需要显示“全历史最后一次订货至今销量”；12 个月窗口只限制侧栏周期明细。
            PurchaseLines AS (
                SELECT
                    requested.ItemNumber,
                    CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) AS PurchaseDate,
                    COALESCE(NULLIF(d.ProductCode, N''), NULLIF(p.ProductCode, N''), NULLIF(pm.ProductCode, N'')) AS ProductCode,
                    COALESCE(NULLIF(d.ProductName, N''), NULLIF(p.ProductName, N''), NULLIF(pm.ProductName, N'')) AS ProductName,
                    COALESCE(d.Quantity, 0) AS PurchaseQuantity
                FROM [StoreLocalSupplierInvoiceDetails] d
                INNER JOIN [StoreLocalSupplierInvoice] h
                    ON h.InvoiceGUID = d.InvoiceGUID
                    AND COALESCE(h.IsDeleted, 0) = 0
                LEFT JOIN [Product] p
                    ON p.ProductCode = NULLIF(d.ProductCode, N'')
                    AND COALESCE(p.IsDeleted, 0) = 0
                INNER JOIN RequestedItems requested
                    ON UPPER(LTRIM(RTRIM(COALESCE(d.ItemNumber, p.ItemNumber)))) = requested.ItemNumber
                LEFT JOIN ProductMatches pm
                    ON pm.ItemNumber = requested.ItemNumber
                WHERE
                    COALESCE(d.IsDeleted, 0) = 0
                    AND UPPER(LTRIM(RTRIM(COALESCE(h.StoreCode, N'')))) = @StoreCode
                    AND UPPER(LTRIM(RTRIM(COALESCE(h.SupplierCode, N'')))) = @SupplierCode
                    AND COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) IS NOT NULL
                    AND CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) <= @Today
            ),
            PurchaseEvents AS (
                SELECT
                    ItemNumber,
                    PurchaseDate,
                    MAX(ProductCode) AS ProductCode,
                    MAX(ProductName) AS ProductName,
                    SUM(PurchaseQuantity) AS PurchaseQuantity
                FROM PurchaseLines
                GROUP BY ItemNumber, PurchaseDate
            ),
            RankedPurchaseEvents AS (
                SELECT
                    events.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY events.ItemNumber
                        ORDER BY events.PurchaseDate DESC
                    ) AS PurchaseRank
                FROM PurchaseEvents events
            ),
            LatestPurchase AS (
                SELECT *
                FROM RankedPurchaseEvents
                WHERE PurchaseRank = 1
            ),
            PurchaseProducts AS (
                SELECT DISTINCT ItemNumber, ProductCode
                FROM PurchaseLines
                WHERE NULLIF(ProductCode, N'') IS NOT NULL
                UNION
                SELECT latest.ItemNumber, matches.ProductCode
                FROM LatestPurchase latest
                INNER JOIN ProductMatches matches
                    ON matches.ItemNumber = latest.ItemNumber
                WHERE NULLIF(matches.ProductCode, N'') IS NOT NULL
            ),
            SalesMetrics AS (
                SELECT
                    latest.ItemNumber,
                    SUM(COALESCE(s.TotalQuantity, 0)) AS SalesSinceLatestPurchase,
                    MAX(s.UpdateTime) AS SalesStatisticLastUpdate
                FROM LatestPurchase latest
                LEFT JOIN PurchaseProducts products
                    ON products.ItemNumber = latest.ItemNumber
                LEFT JOIN [ProductStoreDailySalesStatistic] s
                    ON s.BranchCode = @StoreCode
                    AND s.SupplierCode = @SupplierCode
                    AND s.ProductCode = products.ProductCode
                    AND s.Date >= latest.PurchaseDate
                    AND s.Date < DATEADD(day, 1, @Today)
                GROUP BY latest.ItemNumber
            )
            SELECT
                requested.ItemNumber AS ItemNumber,
                CASE
                    WHEN latest.PurchaseDate IS NOT NULL THEN N'matched'
                    WHEN matches.HasProduct = 1 THEN N'no-purchase'
                    ELSE N'unmatched'
                END AS MatchStatus,
                COALESCE(NULLIF(latest.ProductCode, N''), NULLIF(matches.ProductCode, N'')) AS ProductCode,
                COALESCE(NULLIF(latest.ProductName, N''), NULLIF(matches.ProductName, N'')) AS ProductName,
                latest.PurchaseDate AS LatestPurchaseDate,
                latest.PurchaseQuantity AS LatestPurchaseQuantity,
                COALESCE(sales.SalesSinceLatestPurchase, 0) AS SalesSinceLatestPurchase,
                sales.SalesStatisticLastUpdate AS SalesStatisticLastUpdate
            FROM RequestedItems requested
            LEFT JOIN ProductMatches matches
                ON matches.ItemNumber = requested.ItemNumber
            LEFT JOIN LatestPurchase latest
                ON latest.ItemNumber = requested.ItemNumber
            LEFT JOIN SalesMetrics sales
                ON sales.ItemNumber = requested.ItemNumber
            ORDER BY requested.ItemNumber;
            """;

        return new BrowserExtensionSqlQuery { Sql = sql, Parameters = parameters };
    }

    public static BrowserExtensionSqlQuery BuildPurchaseLines(
        string storeCode,
        string supplierCode,
        string itemNumber,
        DateOnly cutoff,
        DateOnly today
    )
    {
        var normalizedStoreCode = NormalizeCode(storeCode, "分店代码");
        var normalizedSupplierCode = NormalizeCode(supplierCode, "供应商代码");
        var normalizedItemNumber = NormalizeItemNumbers(new[] { itemNumber })[0];
        var sql = """
            WITH ProductMatch AS (
                SELECT TOP (1)
                    UPPER(LTRIM(RTRIM(p.ItemNumber))) AS ItemNumber,
                    NULLIF(p.ProductCode, N'') AS ProductCode,
                    NULLIF(p.ProductName, N'') AS ProductName
                FROM [Product] p
                WHERE
                    UPPER(LTRIM(RTRIM(p.ItemNumber))) = @ItemNumber
                    AND UPPER(LTRIM(RTRIM(COALESCE(p.LocalSupplierCode, N'')))) = @SupplierCode
                    AND COALESCE(p.IsDeleted, 0) = 0
                ORDER BY COALESCE(p.UpdatedAt, p.CreatedAt) DESC, p.UUID DESC
            )
            SELECT
                CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) AS PurchaseDate,
                NULLIF(h.InvoiceNo, N'') AS InvoiceNumber,
                COALESCE(d.Quantity, 0) AS Quantity,
                d.PurchasePrice AS PurchasePrice,
                COALESCE(d.Amount, COALESCE(d.Quantity, 0) * d.PurchasePrice) AS Amount,
                COALESCE(NULLIF(d.ProductCode, N''), NULLIF(p.ProductCode, N''), NULLIF(pm.ProductCode, N'')) AS ProductCode,
                COALESCE(NULLIF(d.ProductName, N''), NULLIF(p.ProductName, N''), NULLIF(pm.ProductName, N'')) AS ProductName
            FROM [StoreLocalSupplierInvoiceDetails] d
            INNER JOIN [StoreLocalSupplierInvoice] h
                ON h.InvoiceGUID = d.InvoiceGUID
                AND COALESCE(h.IsDeleted, 0) = 0
            LEFT JOIN [Product] p
                ON p.ProductCode = NULLIF(d.ProductCode, N'')
                AND COALESCE(p.IsDeleted, 0) = 0
            LEFT JOIN ProductMatch pm
                ON pm.ItemNumber = UPPER(LTRIM(RTRIM(COALESCE(d.ItemNumber, p.ItemNumber))))
            WHERE
                COALESCE(d.IsDeleted, 0) = 0
                AND UPPER(LTRIM(RTRIM(COALESCE(h.StoreCode, N'')))) = @StoreCode
                AND UPPER(LTRIM(RTRIM(COALESCE(h.SupplierCode, N'')))) = @SupplierCode
                AND UPPER(LTRIM(RTRIM(COALESCE(d.ItemNumber, p.ItemNumber)))) = @ItemNumber
                AND COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) IS NOT NULL
                AND CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) >= @Cutoff
                AND CAST(COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt) AS date) < DATEADD(day, 1, @Today)
            ORDER BY PurchaseDate DESC, InvoiceNumber ASC;
            """;

        return new BrowserExtensionSqlQuery
        {
            Sql = sql,
            Parameters = new List<SugarParameter>
            {
                new("@StoreCode", normalizedStoreCode),
                new("@SupplierCode", normalizedSupplierCode),
                new("@ItemNumber", normalizedItemNumber),
                new("@Cutoff", cutoff.ToDateTime(TimeOnly.MinValue)),
                new("@Today", today.ToDateTime(TimeOnly.MinValue)),
            },
        };
    }

    public static BrowserExtensionSqlQuery BuildSales(
        string storeCode,
        string supplierCode,
        IEnumerable<string> productCodes,
        DateOnly startDate,
        DateOnly today
    )
    {
        var normalizedStoreCode = NormalizeCode(storeCode, "分店代码");
        var normalizedSupplierCode = NormalizeCode(supplierCode, "供应商代码");
        var normalizedProducts = productCodes
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedProducts.Count == 0)
        {
            throw new ArgumentException("商品编码不能为空。", nameof(productCodes));
        }
        if (normalizedProducts.Count > MaximumBatchSize)
        {
            throw new ArgumentException(
                $"一次最多查询 {MaximumBatchSize} 个商品编码。",
                nameof(productCodes)
            );
        }

        var productParameters = string.Join(
            ", ",
            normalizedProducts.Select((_, index) => $"@ProductCode{index}")
        );
        var parameters = new List<SugarParameter>
        {
            new("@StoreCode", normalizedStoreCode),
            new("@SupplierCode", normalizedSupplierCode),
            new("@StartDate", startDate.ToDateTime(TimeOnly.MinValue)),
            new("@Today", today.ToDateTime(TimeOnly.MinValue)),
        };
        parameters.AddRange(
            normalizedProducts.Select(
                (value, index) => new SugarParameter($"@ProductCode{index}", value)
            )
        );

        return new BrowserExtensionSqlQuery
        {
            Sql = $"""
                SELECT
                    CAST(s.Date AS date) AS [Date],
                    s.ProductCode AS ProductCode,
                    SUM(CAST(COALESCE(s.TotalQuantity, 0) AS decimal(18, 4))) AS Quantity,
                    SUM(COALESCE(s.TotalAmount, 0)) AS Amount,
                    MAX(s.UpdateTime) AS StatisticLastUpdate
                FROM [ProductStoreDailySalesStatistic] s
                WHERE
                    s.BranchCode = @StoreCode
                    AND s.SupplierCode = @SupplierCode
                    AND s.ProductCode IN ({productParameters})
                    AND s.Date >= @StartDate
                    AND s.Date < DATEADD(day, 1, @Today)
                GROUP BY CAST(s.Date AS date), s.ProductCode
                ORDER BY [Date] ASC, s.ProductCode ASC;
                """,
            Parameters = parameters,
        };
    }

    public static bool ContainsWriteKeyword(string sql) => WriteKeywordRegex().IsMatch(sql);

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

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex WriteKeywordRegex();
}
