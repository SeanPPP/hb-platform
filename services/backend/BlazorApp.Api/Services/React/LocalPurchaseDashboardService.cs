using System.Globalization;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocalPurchaseDashboardService : ILocalPurchaseDashboardService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<LocalPurchaseDashboardService> _logger;

        public LocalPurchaseDashboardService(
            SqlSugarContext context,
            ILogger<LocalPurchaseDashboardService> logger
        )
        {
            _db = context.Db;
            _logger = logger;
        }

        public async Task<ApiResponse<LocalPurchaseDashboardResponseDto>> GetDashboardAsync(
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
                    endMonth,
                    storeScope
                );
                var rows = await _db.Ado.SqlQueryAsync<LocalPurchaseDashboardMonthlyRow>(
                    query.Sql,
                    query.Parameters.ToArray(),
                    cancellationToken
                );

                return ApiResponse<LocalPurchaseDashboardResponseDto>.OK(
                    LocalPurchaseDashboardComposer.ComposeDashboard(query.Period, rows)
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // SqlSugar 5.1.4.198 原生支持带 CancellationToken 的查询重载。
                throw;
            }
            catch (ArgumentException ex)
            {
                return ApiResponse<LocalPurchaseDashboardResponseDto>.Error(
                    ex.Message,
                    "VALIDATION_ERROR"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "进货金额看板查询失败 EndMonth={EndMonth}, StoreScope={StoreScope}",
                    endMonth,
                    storeScope?.IncludesAllStores == true
                        ? "ALL"
                        : string.Join(",", storeScope?.StoreCodes ?? Array.Empty<string>())
                );
                return ApiResponse<LocalPurchaseDashboardResponseDto>.Error(
                    "进货金额看板查询失败",
                    "QUERY_ERROR"
                );
            }
        }

        public async Task<ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>> GetStoreSuppliersAsync(
            string storeCode,
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var query = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
                    storeCode,
                    endMonth,
                    storeScope
                );

                // controller 已做一次权限校验；service 再收口，避免未来被其他入口复用时越权。
                if (!query.StoreAllowed)
                {
                    return ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.Error(
                        "无权查看该分店的进货金额",
                        "FORBIDDEN"
                    );
                }

                var rows = await _db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierMonthlyRow>(
                    query.Sql,
                    query.Parameters.ToArray(),
                    cancellationToken
                );

                return ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.OK(
                    LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
                        query.Period,
                        storeCode.Trim(),
                        rows
                    )
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                return ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.Error(
                    ex.Message,
                    "VALIDATION_ERROR"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "分店供应商进货金额查询失败 StoreCode={StoreCode}, EndMonth={EndMonth}",
                    storeCode,
                    endMonth
                );
                return ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.Error(
                    "分店供应商进货金额查询失败",
                    "QUERY_ERROR"
                );
            }
        }
    }

    internal static class LocalPurchaseDashboardSqlBuilder
    {
        // 仅在范围过滤中拆开日期回退分支，使 SQL Server 可使用各日期列索引；月份分组仍使用 COALESCE。
        private const string WarehouseDateRangeFilter = """
        AND (
            (h.OutboundDate >= @StartDate AND h.OutboundDate < @EndDateExclusive)
            OR (
                h.OutboundDate IS NULL
                AND h.OrderDate >= @StartDate AND h.OrderDate < @EndDateExclusive
            )
            OR (
                h.OutboundDate IS NULL
                AND h.OrderDate IS NULL
                AND h.CreatedAt >= @StartDate AND h.CreatedAt < @EndDateExclusive
            )
        )
""";

        private const string LocalSupplierDateRangeFilter = """
        AND (
            (h.InboundDate >= @StartDate AND h.InboundDate < @EndDateExclusive)
            OR (
                h.InboundDate IS NULL
                AND h.OrderDate >= @StartDate AND h.OrderDate < @EndDateExclusive
            )
            OR (
                h.InboundDate IS NULL
                AND h.OrderDate IS NULL
                AND h.CreatedAt >= @StartDate AND h.CreatedAt < @EndDateExclusive
            )
        )
""";

        public static LocalPurchaseDashboardPeriod ResolvePeriod(string endMonth)
        {
            if (
                string.IsNullOrWhiteSpace(endMonth)
                || endMonth.Length != 7
                || !DateTime.TryParseExact(
                    endMonth,
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedEndMonth
                )
            )
            {
                throw new ArgumentException("结束月份必须是合法的 YYYY-MM。", nameof(endMonth));
            }

            var endDateExclusive = new DateTime(parsedEndMonth.Year, parsedEndMonth.Month, 1)
                .AddMonths(1);
            var startDate = endDateExclusive.AddMonths(-12);
            var months = Enumerable
                .Range(0, 12)
                .Select(index => startDate.AddMonths(index).ToString("yyyy-MM", CultureInfo.InvariantCulture))
                .ToList();

            return new LocalPurchaseDashboardPeriod(startDate, endDateExclusive, months);
        }

        public static LocalPurchaseDashboardSqlBuildResult BuildDashboard(
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope
        )
        {
            ArgumentNullException.ThrowIfNull(storeScope);
            var period = ResolvePeriod(endMonth);
            var parameters = BuildPeriodParameters(period);
            var storeParameterNames = AddStoreParameters(parameters, storeScope);
            var warehouseStoreFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);
            var localStoreFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);
            var salesStoreFilter = BuildStoreFilter(
                "LTRIM(RTRIM(sales.BranchCode))",
                storeParameterNames
            );
            var masterStoreFilter = BuildStoreFilter("s.StoreCode", storeParameterNames);

            // 采购与营业额先在数据库内按分店和月份聚合，避免把日统计或单据明细拉回应用层。
            var sql = $$"""
WITH WarehouseMonthly AS (
    SELECT
        LTRIM(RTRIM(h.StoreCode)) AS StoreCode,
        CONVERT(char(7), COALESCE(h.OutboundDate, h.OrderDate, h.CreatedAt), 120) AS Month,
        CAST(SUM(COALESCE(d.AllocQuantity, 0) * COALESCE(d.ImportPrice, 0)) AS decimal(18, 2)) AS WarehouseAmount,
        CAST(0 AS decimal(18, 2)) AS LocalSupplierAmount,
        CAST(0 AS decimal(18, 2)) AS SalesAmount
    FROM [WareHouseOrder] h
    INNER JOIN [WareHouseOrderDetails] d
        ON d.OrderGUID = h.OrderGUID
        AND COALESCE(d.IsDeleted, 0) = 0
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND COALESCE(h.FlowStatus, -1) <> 0
        AND NULLIF(LTRIM(RTRIM(h.StoreCode)), N'') IS NOT NULL
{{WarehouseDateRangeFilter}}{{warehouseStoreFilter}}
    GROUP BY
        LTRIM(RTRIM(h.StoreCode)),
        CONVERT(char(7), COALESCE(h.OutboundDate, h.OrderDate, h.CreatedAt), 120)
),
LocalSupplierMonthly AS (
    SELECT
        LTRIM(RTRIM(h.StoreCode)) AS StoreCode,
        CONVERT(char(7), COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt), 120) AS Month,
        CAST(0 AS decimal(18, 2)) AS WarehouseAmount,
        CAST(SUM(COALESCE(h.TotalAmount, 0)) AS decimal(18, 2)) AS LocalSupplierAmount,
        CAST(0 AS decimal(18, 2)) AS SalesAmount
    FROM [StoreLocalSupplierInvoice] h
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND NULLIF(LTRIM(RTRIM(h.StoreCode)), N'') IS NOT NULL
{{LocalSupplierDateRangeFilter}}{{localStoreFilter}}
    GROUP BY
        LTRIM(RTRIM(h.StoreCode)),
        CONVERT(char(7), COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt), 120)
),
SalesMonthly AS (
    SELECT
        LTRIM(RTRIM(sales.BranchCode)) AS StoreCode,
        CONVERT(char(7), sales.[Date], 120) AS Month,
        CAST(0 AS decimal(18, 2)) AS WarehouseAmount,
        CAST(0 AS decimal(18, 2)) AS LocalSupplierAmount,
        CAST(SUM(COALESCE(sales.TotalAmount, 0)) AS decimal(18, 2)) AS SalesAmount
    FROM [StoreSalesStatistic] sales
    WHERE
        sales.[Date] >= @StartDate
        AND sales.[Date] < @EndDateExclusive
        AND NULLIF(LTRIM(RTRIM(sales.BranchCode)), N'') IS NOT NULL
        AND UPPER(LTRIM(RTRIM(sales.BranchCode))) <> N'ALL'{{salesStoreFilter}}
    GROUP BY
        LTRIM(RTRIM(sales.BranchCode)),
        CONVERT(char(7), sales.[Date], 120)
),
MonthlyAmounts AS (
    SELECT
        source.StoreCode,
        source.Month,
        CAST(SUM(source.WarehouseAmount) AS decimal(18, 2)) AS WarehouseAmount,
        CAST(SUM(source.LocalSupplierAmount) AS decimal(18, 2)) AS LocalSupplierAmount,
        CAST(SUM(source.SalesAmount) AS decimal(18, 2)) AS SalesAmount
    FROM (
        SELECT * FROM WarehouseMonthly
        UNION ALL
        SELECT * FROM LocalSupplierMonthly
        UNION ALL
        SELECT * FROM SalesMonthly
    ) source
    GROUP BY source.StoreCode, source.Month
),
AllStores AS (
    SELECT
        LTRIM(RTRIM(s.StoreCode)) AS StoreCode,
        COALESCE(NULLIF(s.StoreName, N''), LTRIM(RTRIM(s.StoreCode))) AS StoreName
    FROM [Store] s
    WHERE
        COALESCE(s.IsDeleted, 0) = 0
        AND NULLIF(LTRIM(RTRIM(s.StoreCode)), N'') IS NOT NULL{{masterStoreFilter}}

    UNION

    SELECT
        monthly.StoreCode,
        COALESCE(NULLIF(s.StoreName, N''), monthly.StoreCode) AS StoreName
    FROM MonthlyAmounts monthly
    LEFT JOIN [Store] s
        ON s.StoreCode = monthly.StoreCode
        AND COALESCE(s.IsDeleted, 0) = 0
)
SELECT
    stores.StoreCode,
    stores.StoreName,
    monthly.Month,
    COALESCE(monthly.WarehouseAmount, 0) AS WarehouseAmount,
    COALESCE(monthly.LocalSupplierAmount, 0) AS LocalSupplierAmount,
    COALESCE(monthly.SalesAmount, 0) AS SalesAmount
FROM AllStores stores
LEFT JOIN MonthlyAmounts monthly
    ON monthly.StoreCode = stores.StoreCode
ORDER BY stores.StoreCode, monthly.Month
""";

            return new LocalPurchaseDashboardSqlBuildResult(sql, parameters, period, true);
        }

        public static LocalPurchaseDashboardSqlBuildResult BuildStoreSuppliers(
            string storeCode,
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope
        )
        {
            ArgumentNullException.ThrowIfNull(storeScope);
            var normalizedStoreCode = NormalizeStoreCode(storeCode);
            if (normalizedStoreCode == null)
            {
                throw new ArgumentException("分店编码不能为空。", nameof(storeCode));
            }

            var period = ResolvePeriod(endMonth);
            var normalizedScope = storeScope.IncludesAllStores
                ? null
                : NormalizeStoreScope(storeScope.StoreCodes);
            var storeAllowed =
                normalizedScope == null
                || normalizedScope.Contains(normalizedStoreCode, StringComparer.OrdinalIgnoreCase);
            var scopeGuard = storeAllowed ? string.Empty : "\n        AND 1 = 0";
            var parameters = BuildPeriodParameters(period);
            parameters.Add(new SugarParameter("@RequestedStoreCode", normalizedStoreCode));

            var sql = $$"""
WITH StoreIdentity AS (
    SELECT
        @RequestedStoreCode AS StoreCode,
        COALESCE(NULLIF(st.StoreName, N''), @RequestedStoreCode) AS StoreName
    FROM (SELECT 1 AS Seed) seed
    LEFT JOIN [Store] st
        ON st.StoreCode = @RequestedStoreCode
        AND COALESCE(st.IsDeleted, 0) = 0
),
WarehouseMonthly AS (
    SELECT
        LTRIM(RTRIM(h.StoreCode)) AS StoreCode,
        N'WAREHOUSE_ORDER' AS SourceCode,
        CAST(NULL AS nvarchar(50)) AS SupplierCode,
        N'仓库订单' AS SupplierName,
        N'WAREHOUSE_ORDER' AS SourceType,
        CAST(0 AS bit) AS IsUnassigned,
        CONVERT(char(7), COALESCE(h.OutboundDate, h.OrderDate, h.CreatedAt), 120) AS Month,
        CAST(SUM(COALESCE(d.AllocQuantity, 0) * COALESCE(d.ImportPrice, 0)) AS decimal(18, 2)) AS Amount
    FROM [WareHouseOrder] h
    INNER JOIN [WareHouseOrderDetails] d
        ON d.OrderGUID = h.OrderGUID
        AND COALESCE(d.IsDeleted, 0) = 0
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND COALESCE(h.FlowStatus, -1) <> 0
        AND h.StoreCode = @RequestedStoreCode
{{WarehouseDateRangeFilter}}{{scopeGuard}}
    GROUP BY
        LTRIM(RTRIM(h.StoreCode)),
        CONVERT(char(7), COALESCE(h.OutboundDate, h.OrderDate, h.CreatedAt), 120)
),
LocalSupplierMonthly AS (
    SELECT
        LTRIM(RTRIM(h.StoreCode)) AS StoreCode,
        COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED') AS SourceCode,
        COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED') AS SupplierCode,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN N'未匹配供应商'
            ELSE COALESCE(NULLIF(supplier.Name, N''), LTRIM(RTRIM(h.SupplierCode)))
        END AS SupplierName,
        N'LOCAL_SUPPLIER' AS SourceType,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END AS IsUnassigned,
        CONVERT(char(7), COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt), 120) AS Month,
        CAST(SUM(COALESCE(h.TotalAmount, 0)) AS decimal(18, 2)) AS Amount
    FROM [StoreLocalSupplierInvoice] h
    LEFT JOIN [LocalSupplier] supplier
        ON supplier.LocalSupplierCode = NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'')
        AND COALESCE(supplier.IsDeleted, 0) = 0
    WHERE
        COALESCE(h.IsDeleted, 0) = 0
        AND h.StoreCode = @RequestedStoreCode
{{LocalSupplierDateRangeFilter}}{{scopeGuard}}
    GROUP BY
        LTRIM(RTRIM(h.StoreCode)),
        COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED'),
        CASE
            WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN N'未匹配供应商'
            ELSE COALESCE(NULLIF(supplier.Name, N''), LTRIM(RTRIM(h.SupplierCode)))
        END,
        CASE
            WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END,
        CONVERT(char(7), COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt), 120)
),
SourceRows AS (
    SELECT * FROM WarehouseMonthly
    UNION ALL
    SELECT * FROM LocalSupplierMonthly
)
SELECT
    identityRow.StoreCode,
    identityRow.StoreName,
    source.SourceCode,
    source.SupplierCode,
    source.SupplierName,
    source.SourceType,
    COALESCE(source.IsUnassigned, CAST(0 AS bit)) AS IsUnassigned,
    source.Month,
    COALESCE(source.Amount, 0) AS Amount
FROM StoreIdentity identityRow
LEFT JOIN SourceRows source
    ON source.StoreCode = identityRow.StoreCode
ORDER BY
    CASE WHEN source.SourceCode = N'WAREHOUSE_ORDER' THEN 0 ELSE 1 END,
    source.SourceCode,
    source.Month
""";

            return new LocalPurchaseDashboardSqlBuildResult(
                sql,
                parameters,
                period,
                storeAllowed
            );
        }

        public static bool ContainsWriteKeyword(string sql)
        {
            var upper = " " + sql.ToUpperInvariant() + " ";
            return new[]
            {
                " INSERT ", " UPDATE ", " DELETE ", " MERGE ", " CREATE ", " ALTER ",
                " DROP ", " TRUNCATE ", " EXEC ",
            }.Any(upper.Contains);
        }

        private static List<SugarParameter> BuildPeriodParameters(
            LocalPurchaseDashboardPeriod period
        )
        {
            return new List<SugarParameter>
            {
                new("@StartDate", period.StartDate),
                new("@EndDateExclusive", period.EndDateExclusive),
            };
        }

        private static IReadOnlyList<string>? AddStoreParameters(
            List<SugarParameter> parameters,
            LocalPurchaseDashboardStoreScope storeScope
        )
        {
            if (storeScope.IncludesAllStores)
            {
                return null;
            }

            var normalized = NormalizeStoreScope(storeScope.StoreCodes);

            var names = new List<string>(normalized.Count);
            for (var index = 0; index < normalized.Count; index++)
            {
                var name = "@StoreCode" + index;
                names.Add(name);
                parameters.Add(new SugarParameter(name, normalized[index]));
            }

            return names;
        }

        private static string BuildStoreFilter(
            string column,
            IReadOnlyList<string>? storeParameterNames
        )
        {
            if (storeParameterNames == null)
            {
                return string.Empty;
            }

            if (storeParameterNames.Count == 0)
            {
                return "\n        AND 1 = 0";
            }

            return "\n        AND " + column + " IN (" + string.Join(", ", storeParameterNames) + ")";
        }

        private static IReadOnlyList<string> NormalizeStoreScope(
            IReadOnlyList<string> scopedStoreCodes
        )
        {
            return scopedStoreCodes
                .Select(NormalizeStoreCode)
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? NormalizeStoreCode(string? storeCode)
        {
            return string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim();
        }
    }

    internal static class LocalPurchaseDashboardComposer
    {
        public static LocalPurchaseDashboardResponseDto ComposeDashboard(
            LocalPurchaseDashboardPeriod period,
            IReadOnlyList<LocalPurchaseDashboardMonthlyRow> rows
        )
        {
            var monthSet = period.Months.ToHashSet(StringComparer.Ordinal);
            var stores = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.StoreCode))
                .GroupBy(row => row.StoreCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var monthRows = group
                        .Where(row => row.Month != null && monthSet.Contains(row.Month))
                        .GroupBy(row => row.Month!, StringComparer.Ordinal)
                        .ToDictionary(
                            monthGroup => monthGroup.Key,
                            monthGroup => new
                            {
                                Warehouse = monthGroup.Sum(row => row.WarehouseAmount),
                                LocalSupplier = monthGroup.Sum(row => row.LocalSupplierAmount),
                                Sales = monthGroup.Sum(row => row.SalesAmount),
                            },
                            StringComparer.Ordinal
                        );
                    var months = period.Months
                        .Select(month =>
                        {
                            monthRows.TryGetValue(month, out var amount);
                            var warehouse = RoundMoney(amount?.Warehouse ?? 0m);
                            var localSupplier = RoundMoney(amount?.LocalSupplier ?? 0m);
                            var sales = RoundMoney(amount?.Sales ?? 0m);
                            return new LocalPurchaseDashboardStoreMonthDto
                            {
                                Month = month,
                                WarehouseAmount = warehouse,
                                LocalSupplierAmount = localSupplier,
                                TotalAmount = RoundMoney(warehouse + localSupplier),
                                SalesAmount = sales,
                            };
                        })
                        .ToList();
                    var warehouseTotal = RoundMoney(months.Sum(month => month.WarehouseAmount));
                    var localSupplierTotal = RoundMoney(months.Sum(month => month.LocalSupplierAmount));
                    var storeName = group
                        .Select(row => row.StoreName?.Trim())
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key;

                    return new LocalPurchaseDashboardStoreDto
                    {
                        StoreCode = group.Key,
                        StoreName = storeName,
                        WarehouseTotal = warehouseTotal,
                        LocalSupplierTotal = localSupplierTotal,
                        TotalAmount = RoundMoney(warehouseTotal + localSupplierTotal),
                        Months = months,
                    };
                })
                .OrderBy(store => store.StoreCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var warehouseTotal = RoundMoney(stores.Sum(store => store.WarehouseTotal));
            var localSupplierTotal = RoundMoney(stores.Sum(store => store.LocalSupplierTotal));

            return new LocalPurchaseDashboardResponseDto
            {
                Months = period.Months.ToList(),
                WarehouseTotal = warehouseTotal,
                LocalSupplierTotal = localSupplierTotal,
                TotalAmount = RoundMoney(warehouseTotal + localSupplierTotal),
                Stores = stores,
            };
        }

        public static LocalPurchaseDashboardStoreSuppliersDto ComposeStoreSuppliers(
            LocalPurchaseDashboardPeriod period,
            string storeCode,
            IReadOnlyList<LocalPurchaseDashboardSupplierMonthlyRow> rows
        )
        {
            var monthSet = period.Months.ToHashSet(StringComparer.Ordinal);
            // 仓库来源是固定的虚拟行；即使期间没有仓库订单，也要返回完整十二个月零金额。
            var sourceRows = rows.Append(new LocalPurchaseDashboardSupplierMonthlyRow
            {
                StoreCode = storeCode,
                SourceCode = "WAREHOUSE_ORDER",
                SupplierName = "仓库订单",
                SourceType = "WAREHOUSE_ORDER",
            });
            var suppliers = sourceRows
                .Where(row => !string.IsNullOrWhiteSpace(row.SourceCode))
                // 虚拟身份参与分组，避免真实业务编码 UNASSIGNED 或 WAREHOUSE_ORDER 与系统行碰撞。
                .GroupBy(
                    row =>
                        (row.SourceType ?? string.Empty).Trim()
                        + "\u001f"
                        + (row.IsUnassigned ? "UNASSIGNED_VIRTUAL" : "BUSINESS_CODE")
                        + "\u001f"
                        + row.SourceCode!.Trim(),
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(group =>
                {
                    var monthAmounts = group
                        .Where(row => row.Month != null && monthSet.Contains(row.Month))
                        .GroupBy(row => row.Month!, StringComparer.Ordinal)
                        .ToDictionary(
                            monthGroup => monthGroup.Key,
                            monthGroup => monthGroup.Sum(row => row.Amount),
                            StringComparer.Ordinal
                        );
                    var months = period.Months
                        .Select(month => new LocalPurchaseDashboardSupplierMonthDto
                        {
                            Month = month,
                            Amount = RoundMoney(
                                monthAmounts.TryGetValue(month, out var amount) ? amount : 0m
                            ),
                        })
                        .ToList();
                    var first = group.First();
                    var sourceCode = first.SourceCode!.Trim();
                    var isWarehouse = string.Equals(
                        first.SourceType,
                        "WAREHOUSE_ORDER",
                        StringComparison.OrdinalIgnoreCase
                    );

                    return new LocalPurchaseDashboardSupplierDto
                    {
                        SourceCode = sourceCode,
                        SupplierCode = isWarehouse ? null : first.SupplierCode ?? sourceCode,
                        SupplierName = string.IsNullOrWhiteSpace(first.SupplierName)
                            ? sourceCode
                            : first.SupplierName.Trim(),
                        SourceType = isWarehouse ? "WAREHOUSE_ORDER" : "LOCAL_SUPPLIER",
                        IsUnassigned = !isWarehouse && first.IsUnassigned,
                        TotalAmount = RoundMoney(months.Sum(month => month.Amount)),
                        Months = months,
                    };
                })
                .OrderBy(item =>
                    item.SourceType.Equals("WAREHOUSE_ORDER", StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : 1
                )
                .ThenByDescending(item => item.TotalAmount)
                .ThenBy(item => item.SupplierName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var warehouseTotal = RoundMoney(
                suppliers
                    .Where(item => item.SourceType.Equals("WAREHOUSE_ORDER", StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.TotalAmount)
            );
            var localSupplierTotal = RoundMoney(
                suppliers
                    .Where(item => !item.SourceType.Equals("WAREHOUSE_ORDER", StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.TotalAmount)
            );
            var storeName = rows
                .Select(row => row.StoreName?.Trim())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? storeCode;

            return new LocalPurchaseDashboardStoreSuppliersDto
            {
                StoreCode = storeCode,
                StoreName = storeName,
                Months = period.Months.ToList(),
                WarehouseTotal = warehouseTotal,
                LocalSupplierTotal = localSupplierTotal,
                TotalAmount = RoundMoney(warehouseTotal + localSupplierTotal),
                Suppliers = suppliers,
            };
        }

        private static decimal RoundMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    internal sealed record LocalPurchaseDashboardPeriod(
        DateTime StartDate,
        DateTime EndDateExclusive,
        IReadOnlyList<string> Months
    );

    internal sealed record LocalPurchaseDashboardSqlBuildResult(
        string Sql,
        IReadOnlyList<SugarParameter> Parameters,
        LocalPurchaseDashboardPeriod Period,
        bool StoreAllowed
    );

    internal sealed class LocalPurchaseDashboardMonthlyRow
    {
        public string StoreCode { get; set; } = string.Empty;
        public string? StoreName { get; set; }
        public string? Month { get; set; }
        public decimal WarehouseAmount { get; set; }
        public decimal LocalSupplierAmount { get; set; }
        public decimal SalesAmount { get; set; }
    }

    internal sealed class LocalPurchaseDashboardSupplierMonthlyRow
    {
        public string StoreCode { get; set; } = string.Empty;
        public string? StoreName { get; set; }
        public string? SourceCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? SourceType { get; set; }
        public bool IsUnassigned { get; set; }
        public string? Month { get; set; }
        public decimal Amount { get; set; }
    }
}
