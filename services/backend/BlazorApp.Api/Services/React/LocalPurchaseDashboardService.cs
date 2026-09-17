using System.Globalization;
using System.Text.Json;
using System.Text;
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
            CancellationToken cancellationToken,
            string? supplierFilterMode = null,
            IReadOnlyCollection<string>? supplierKeys = null
        )
        {
            try
            {
                LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilterShape(supplierFilterMode, supplierKeys);
                var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
                    endMonth,
                    storeScope,
                    supplierFilterMode,
                    supplierKeys
                );
                var optionRows = await _db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierOptionRow>(
                    query.OptionsSql,
                    query.OptionsParameters.ToArray(),
                    cancellationToken
                );
                LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter(supplierFilterMode, supplierKeys, optionRows);
                var rows = await _db.Ado.SqlQueryAsync<LocalPurchaseDashboardMonthlyRow>(
                    query.Sql,
                    query.Parameters.ToArray(),
                    cancellationToken
                );

                return ApiResponse<LocalPurchaseDashboardResponseDto>.OK(
                    LocalPurchaseDashboardComposer.ComposeDashboard(query.Period, rows, optionRows)
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
            CancellationToken cancellationToken,
            string? supplierFilterMode = null,
            IReadOnlyCollection<string>? supplierKeys = null
        )
        {
            try
            {
                LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilterShape(supplierFilterMode, supplierKeys);
                var query = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
                    storeCode,
                    endMonth,
                    storeScope,
                    supplierFilterMode,
                    supplierKeys
                );

                // controller 已做一次权限校验；service 再收口，避免未来被其他入口复用时越权。
                if (!query.StoreAllowed)
                {
                    return ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.Error(
                        "无权查看该分店的进货金额",
                        "FORBIDDEN"
                    );
                }

                var optionRows = await _db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierOptionRow>(
                    query.OptionsSql,
                    query.OptionsParameters.ToArray(),
                    cancellationToken
                );
                LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter(supplierFilterMode, supplierKeys, optionRows);
                var warehouseKey = "WAREHOUSE_ORDER:false:WAREHOUSE_ORDER";
                var warehouseSelected = (supplierKeys ?? Array.Empty<string>()).Any(key => string.Equals(key?.Trim(), warehouseKey, StringComparison.OrdinalIgnoreCase));
                var includeWarehouse = supplierFilterMode == null
                    || (string.Equals(supplierFilterMode, "include", StringComparison.OrdinalIgnoreCase) && warehouseSelected)
                    || (string.Equals(supplierFilterMode, "exclude", StringComparison.OrdinalIgnoreCase) && !warehouseSelected);
                var rows = await _db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierMonthlyRow>(
                    query.Sql,
                    query.Parameters.ToArray(),
                    cancellationToken
                );
                return ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.OK(
                    LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
                        query.Period,
                        storeCode.Trim(),
                        rows,
                        includeWarehouse
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
            LocalPurchaseDashboardStoreScope storeScope,
            string? supplierFilterMode = null,
            IReadOnlyCollection<string>? supplierKeys = null
        )
        {
            ArgumentNullException.ThrowIfNull(storeScope);
            var period = ResolvePeriod(endMonth);
            var parameters = BuildPeriodParameters(period);
            var storeParameterNames = AddStoreParameters(parameters, storeScope);
            var warehouseStoreFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);
            var localStoreFilter = BuildStoreFilter("h.StoreCode", storeParameterNames);
            var supplierParameters = AddSupplierParameters(parameters, supplierFilterMode, supplierKeys);
            var warehouseSupplierFilter = BuildSupplierFilter("N'WAREHOUSE_ORDER' + N':' + N'false' + N':' + N'WAREHOUSE_ORDER'", supplierFilterMode, supplierParameters);
            var localSupplierKeyExpression = "N'LOCAL_SUPPLIER' + N':' + CASE WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN N'true' ELSE N'false' END + N':' + COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED')";
            var localSupplierFilter = BuildSupplierFilter(localSupplierKeyExpression, supplierFilterMode, supplierParameters);
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
        AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(h.StoreCode)))
{{WarehouseDateRangeFilter}}{{warehouseStoreFilter}}{{warehouseSupplierFilter}}
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
        AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(h.StoreCode)))
{{LocalSupplierDateRangeFilter}}{{localStoreFilter}}{{localSupplierFilter}}
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
        AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(sales.BranchCode)))
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
        AND COALESCE(s.IsActive, 0) = 1
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

            var options = BuildSupplierOptions(null, period, storeScope);
            return new LocalPurchaseDashboardSqlBuildResult(sql, parameters, period, true, options.Sql, options.Parameters);
        }

        public static LocalPurchaseDashboardSqlBuildResult BuildStoreSuppliers(
            string storeCode,
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope,
            string? supplierFilterMode = null,
            IReadOnlyCollection<string>? supplierKeys = null
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
            var supplierParameters = AddSupplierParameters(parameters, supplierFilterMode, supplierKeys);
            var warehouseSupplierFilter = BuildSupplierFilter("N'WAREHOUSE_ORDER' + N':' + N'false' + N':' + N'WAREHOUSE_ORDER'", supplierFilterMode, supplierParameters);
            var localSupplierKeyExpression = "N'LOCAL_SUPPLIER' + N':' + CASE WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN N'true' ELSE N'false' END + N':' + COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED')";
            var localSupplierFilter = BuildSupplierFilter(localSupplierKeyExpression, supplierFilterMode, supplierParameters);

            var sql = $$"""
WITH StoreIdentity AS (
    SELECT
        @RequestedStoreCode AS StoreCode,
        COALESCE(NULLIF(st.StoreName, N''), @RequestedStoreCode) AS StoreName
    FROM [Store] st
    WHERE st.StoreCode = @RequestedStoreCode
        AND COALESCE(st.IsDeleted, 0) = 0
        AND COALESCE(st.IsActive, 0) = 1
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
        AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(h.StoreCode)))
{{WarehouseDateRangeFilter}}{{scopeGuard}}{{warehouseSupplierFilter}}
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
        AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(h.StoreCode)))
{{LocalSupplierDateRangeFilter}}{{scopeGuard}}{{localSupplierFilter}}
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

            // 合法键必须来自当前权限范围和期间的全局选项；明细金额本身仍按请求分店过滤。
            var options = BuildSupplierOptions(null, period, storeScope);
            return new LocalPurchaseDashboardSqlBuildResult(sql, parameters, period, storeAllowed, options.Sql, options.Parameters);
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

        private static List<SugarParameter> AddSupplierParameters(
            List<SugarParameter> parameters,
            string? filterMode,
            IReadOnlyCollection<string>? supplierKeys
        )
        {
            var names = new List<SugarParameter>();
            if (string.IsNullOrWhiteSpace(filterMode)) return names;
            var keys = (supplierKeys ?? Array.Empty<string>()).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var parameter = new SugarParameter("@SupplierKeysJson", JsonSerializer.Serialize(keys.Select(key => key.Trim())));
            parameters.Add(parameter);
            names.Add(parameter);
            return names;
        }

        private static string BuildSupplierFilter(string expression, string? filterMode, IReadOnlyList<SugarParameter> parameters)
        {
            if (string.IsNullOrWhiteSpace(filterMode)) return string.Empty;
            if (parameters.Count == 0)
                return string.Equals(filterMode, "include", StringComparison.OrdinalIgnoreCase) ? "\n        AND 1 = 0" : string.Empty;
            var exists = "EXISTS";
            if (string.Equals(filterMode, "exclude", StringComparison.OrdinalIgnoreCase)) exists = "NOT EXISTS";
            return "\n        AND " + exists + " (SELECT 1 FROM OPENJSON(@SupplierKeysJson) filterKey WHERE filterKey.value = " + expression + ")";
        }

        public static void ValidateSupplierFilter(
            string? filterMode,
            IReadOnlyCollection<string>? supplierKeys,
            IReadOnlyList<LocalPurchaseDashboardSupplierOptionRow> optionRows
        )
        {
            if (filterMode == null && supplierKeys is { Count: > 0 })
                throw new ArgumentException("供应商筛选模式不能为空。", nameof(filterMode));
            if (filterMode != null && !string.Equals(filterMode, "include", StringComparison.OrdinalIgnoreCase) && !string.Equals(filterMode, "exclude", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("供应商筛选模式必须是 include 或 exclude。", nameof(filterMode));
            var keys = (supplierKeys ?? Array.Empty<string>()).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (keys.Count > 2000) throw new ArgumentException("供应商筛选项过多。", nameof(supplierKeys));
            if (keys.Any(key => key.Length > 256)) throw new ArgumentException("供应商筛选项过长。", nameof(supplierKeys));
            if (JsonSerializer.Serialize(keys).Length > 65536) throw new ArgumentException("供应商筛选请求过大。", nameof(supplierKeys));
            if (filterMode == null) return;
            var validKeys = optionRows.Select(row =>
                (row.SourceType ?? string.Empty).Trim() + ":" + (row.IsUnassigned ? "true" : "false") + ":" + (row.SourceCode ?? string.Empty).Trim()
            ).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (keys.Any(key => !validKeys.Contains(key))) throw new ArgumentException("供应商筛选项无效。", nameof(supplierKeys));
        }

        public static void ValidateSupplierFilterShape(string? filterMode, IReadOnlyCollection<string>? supplierKeys)
        {
            if (filterMode == null && supplierKeys is { Count: > 0 })
                throw new ArgumentException("供应商筛选模式不能为空。", nameof(filterMode));
            if (filterMode != null && !string.Equals(filterMode, "include", StringComparison.OrdinalIgnoreCase) && !string.Equals(filterMode, "exclude", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("供应商筛选模式必须是 include 或 exclude。", nameof(filterMode));
            if (supplierKeys == null) return;
            if (supplierKeys.Count > 2000) throw new ArgumentException("供应商筛选项过多。", nameof(supplierKeys));
            if (supplierKeys.Any(key => key == null || key.Length > 256)) throw new ArgumentException("供应商筛选项过长。", nameof(supplierKeys));
            var json = JsonSerializer.Serialize(supplierKeys);
            if (Encoding.UTF8.GetByteCount(json) > 65536) throw new ArgumentException("供应商筛选请求过大。", nameof(supplierKeys));
        }

        private static LocalPurchaseDashboardOptionsSqlBuildResult BuildSupplierOptions(
            string? requestedStoreCode,
            LocalPurchaseDashboardPeriod period,
            LocalPurchaseDashboardStoreScope storeScope
        )
        {
            var parameters = BuildPeriodParameters(period);
            var storeNames = AddStoreParameters(parameters, storeScope);
            var storeFilter = BuildStoreFilter("h.StoreCode", storeNames);
            var requestedFilter = requestedStoreCode == null ? string.Empty : "\n        AND h.StoreCode = @RequestedStoreCode";
            if (requestedStoreCode != null) parameters.Add(new SugarParameter("@RequestedStoreCode", requestedStoreCode));
            var sql = $$"""
SELECT SourceCode, SupplierCode, SupplierName, SourceType, IsUnassigned
FROM (
    SELECT N'WAREHOUSE_ORDER' AS SourceCode, CAST(NULL AS nvarchar(50)) AS SupplierCode, N'仓库订单' AS SupplierName, N'WAREHOUSE_ORDER' AS SourceType, CAST(0 AS bit) AS IsUnassigned
    FROM [WareHouseOrder] h
    INNER JOIN [WareHouseOrderDetails] d ON d.OrderGUID = h.OrderGUID AND COALESCE(d.IsDeleted, 0) = 0
    WHERE COALESCE(h.IsDeleted, 0) = 0 AND COALESCE(h.FlowStatus, -1) <> 0
      AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(h.StoreCode)))
{{WarehouseDateRangeFilter}}{{storeFilter}}{{requestedFilter}}
    GROUP BY h.StoreCode
    UNION
    SELECT COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED'), COALESCE(NULLIF(LTRIM(RTRIM(h.SupplierCode)), N''), N'UNASSIGNED'),
      CASE WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN N'未匹配供应商' ELSE COALESCE(NULLIF(supplier.Name, N''), LTRIM(RTRIM(h.SupplierCode))) END,
      N'LOCAL_SUPPLIER', CASE WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
    FROM [StoreLocalSupplierInvoice] h
    LEFT JOIN [LocalSupplier] supplier ON supplier.LocalSupplierCode = NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') AND COALESCE(supplier.IsDeleted, 0) = 0
    WHERE COALESCE(h.IsDeleted, 0) = 0
      AND EXISTS (SELECT 1 FROM [Store] activeStore WHERE COALESCE(activeStore.IsDeleted, 0) = 0 AND COALESCE(activeStore.IsActive, 0) = 1 AND activeStore.StoreCode = LTRIM(RTRIM(h.StoreCode)))
{{LocalSupplierDateRangeFilter}}{{storeFilter}}{{requestedFilter}}
) options
ORDER BY CASE WHEN SourceType = N'WAREHOUSE_ORDER' THEN 0 ELSE 1 END, SupplierName, SourceCode
""";
            return new LocalPurchaseDashboardOptionsSqlBuildResult(sql, parameters);
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
            IReadOnlyList<LocalPurchaseDashboardMonthlyRow> rows,
            IReadOnlyList<LocalPurchaseDashboardSupplierOptionRow>? optionRows = null
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
                SupplierOptions = ComposeSupplierOptions(optionRows),
            };
        }

        public static LocalPurchaseDashboardStoreSuppliersDto ComposeStoreSuppliers(
            LocalPurchaseDashboardPeriod period,
            string storeCode,
            IReadOnlyList<LocalPurchaseDashboardSupplierMonthlyRow> rows,
            bool includeWarehouse = true
        )
        {
            var monthSet = period.Months.ToHashSet(StringComparer.Ordinal);
            // 仅为已启用且选中仓库来源的分店补零行；无门店身份或排除仓库时不得补回。
            var hasStoreIdentity = rows.Any(row =>
                string.Equals(row.StoreCode?.Trim(), storeCode, StringComparison.OrdinalIgnoreCase)
            );
            var sourceRows = includeWarehouse && hasStoreIdentity
                ? rows.Append(new LocalPurchaseDashboardSupplierMonthlyRow
                {
                    StoreCode = storeCode,
                    SourceCode = "WAREHOUSE_ORDER",
                    SupplierName = "仓库订单",
                    SourceType = "WAREHOUSE_ORDER",
                })
                : rows;
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

        private static List<LocalPurchaseDashboardSupplierOptionDto> ComposeSupplierOptions(
            IReadOnlyList<LocalPurchaseDashboardSupplierOptionRow>? rows
        ) => (rows ?? Array.Empty<LocalPurchaseDashboardSupplierOptionRow>())
            .Where(row => !string.IsNullOrWhiteSpace(row.SourceCode) && !string.IsNullOrWhiteSpace(row.SourceType))
            .GroupBy(row => row.SourceType!.Trim() + "\u001f" + row.IsUnassigned + "\u001f" + row.SourceCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var sourceType = first.SourceType!.Trim();
                var sourceCode = first.SourceCode!.Trim();
                var warehouse = sourceType.Equals("WAREHOUSE_ORDER", StringComparison.OrdinalIgnoreCase);
                return new LocalPurchaseDashboardSupplierOptionDto
                {
                    SourceCode = sourceCode,
                    SupplierCode = warehouse ? null : (first.SupplierCode ?? sourceCode).Trim(),
                    SupplierName = string.IsNullOrWhiteSpace(first.SupplierName) ? sourceCode : first.SupplierName.Trim(),
                    SourceType = warehouse ? "WAREHOUSE_ORDER" : "LOCAL_SUPPLIER",
                    IsUnassigned = !warehouse && first.IsUnassigned,
                };
            })
            .OrderBy(option => option.SourceType.Equals("WAREHOUSE_ORDER", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(option => option.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        bool StoreAllowed,
        string OptionsSql,
        IReadOnlyList<SugarParameter> OptionsParameters
    );

    internal sealed record LocalPurchaseDashboardOptionsSqlBuildResult(string Sql, IReadOnlyList<SugarParameter> Parameters);

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

    internal sealed class LocalPurchaseDashboardSupplierOptionRow
    {
        public string? SourceCode { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? SourceType { get; set; }
        public bool IsUnassigned { get; set; }
    }
}
