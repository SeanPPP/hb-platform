using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductReportSortingSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    public ProductReportSortingSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 商品明细排序验证。";
        }
    }
}

/// <summary>
/// 商品报告商品明细排序在真实 SQL Server 上的验证：路径 A（供应商汇总开启，临时表排名分页）
/// 与路径 B（SqlSugar 快速路径）对每种排序都必须给出同一顺序，并且跨页无重复、无遗漏。
/// </summary>
[Trait("Category", "SQL")]
public sealed class ProductReportSortingSqlServerIntegrationTests
{
    private const string SqlServerTestConnectionEnvVar = "HB_TEST_SQLSERVER_CONNECTION";
    private const string SupplierCode = "AUS-SORT";
    private static readonly DateTime CurrentDate = new(2026, 7, 1);
    private static readonly DateTime CompareDate = new(2025, 7, 1);

    // 期望顺序按“本期值 → 同期值 → 商品编码升序”手工推导，字母对应 P-SORT-{字母}；
    // 未知字段（含 itemNumber）和未知方向回退为金额降序。
    private static readonly (string? SortField, string? SortOrder, string ExpectedOrder)[] SortCases =
    {
        (null, null, "ABCFDEHIG"),
        ("amount", "desc", "ABCFDEHIG"),
        ("amount", "asc", "GHIEDFCBA"),
        ("quantity", "desc", "BACFDEHIG"),
        ("quantity", "asc", "GHIEDFCAB"),
        ("unitPrice", "desc", "ACFDEHIBG"),
        ("unitPrice", "asc", "GBHIEDFCA"),
        ("itemNumber", "sideways", "ABCFDEHIG"),
    };

    /// <summary>两店合计后的本期金额/数量与同期金额/数量，与 SeedAsync 的明细行一一对应。</summary>
    private static readonly IReadOnlyDictionary<char, (decimal Amount, int Quantity, decimal AmountLY, int QuantityLY)>
        ExpectedValues = new Dictionary<char, (decimal, int, decimal, int)>
        {
            ['A'] = (100m, 10, 50m, 5),
            ['B'] = (90m, 30, 0m, 0),
            ['C'] = (47m, 6, 20m, 2),
            ['D'] = (39m, 5, 36m, 3),
            ['E'] = (39m, 5, 0m, 0),
            ['F'] = (47m, 6, 0m, 0),
            ['G'] = (0m, 0, 80m, 4),
            ['H'] = (10m, 2, 0m, 0),
            ['I'] = (10m, 2, 0m, 0),
        };

    [ProductReportSortingSqlServerFact]
    public async Task GetEnhancedSalesProductDetailsAsync_Sort_数据库分页与快速路径按排序稳定翻页()
    {
        await using var fixture = await ProductSortSqlServerFixture.CreateAsync();

        foreach (var useSupplierRollups in new[] { true, false })
        {
            foreach (var (sortField, sortOrder, expectedOrder) in SortCases)
            {
                var caseName = $"rollups={useSupplierRollups}, sort={sortField ?? "null"}/{sortOrder ?? "null"}";
                // 每个用例独立缓存，保证每一页都真实查询数据库；缓存隔离由单独用例覆盖。
                using var cache = new MemoryCache(new MemoryCacheOptions());
                var service = fixture.CreateService(useSupplierRollups, cache);
                fixture.ExecutedSql.Clear();

                var pages = new List<PagedSalesProductDetailWithDiscountDto>();
                for (var pageIndex = 1; pageIndex <= 4; pageIndex++)
                {
                    pages.Add(await service.GetEnhancedSalesProductDetailsAsync(
                        CreateDateRange(),
                        localSupplierCodes: new List<string> { SupplierCode },
                        pageIndex: pageIndex,
                        pageSize: 4,
                        sortField: sortField,
                        sortOrder: sortOrder
                    ));
                }

                // 第 4 页越界：数据为空但 Total 仍准确。
                var rows = pages.SelectMany(page => page.Data).ToList();
                AssertCase(
                    caseName,
                    $"order={expectedOrder} | sizes=4,4,1,0 | totals=9,9,9,9",
                    $"order={ToLetters(rows)}"
                        + $" | sizes={string.Join(",", pages.Select(page => page.Data.Count))}"
                        + $" | totals={string.Join(",", pages.Select(page => page.Total))}"
                );
                AssertCase(caseName, DescribeExpectedValues(expectedOrder), DescribeValues(rows));

                var sort = ProductReportSort.Parse(sortField, sortOrder);
                var rankingOrderBy = "ORDER BY " + SalesDashboardReactService.BuildProductReportRankingOrderBy(sort);
                if (useSupplierRollups)
                {
                    // 路径 A：临时表排名 SQL 真实执行，且排名顺序来自同一白名单片段。
                    Assert.True(
                        fixture.ExecutedSql.Any(sql =>
                            sql.Contains("#ProductReportAggregates", StringComparison.Ordinal)
                            && sql.Contains(rankingOrderBy, StringComparison.Ordinal)),
                        $"{caseName}: 未执行带排序的数据库排名 SQL。"
                    );
                }
                else
                {
                    Assert.False(
                        fixture.ExecutedSql.Any(sql => sql.Contains("#ProductReportAggregates", StringComparison.Ordinal)),
                        $"{caseName}: 关闭供应商汇总时不应走数据库临时表分页。"
                    );
                    if (sort.Field == ProductReportSortField.UnitPrice)
                    {
                        var unitPriceOrderBy = SalesDashboardReactService.BuildFastPathUnitPriceOrderBy(sort.Ascending);
                        Assert.True(
                            fixture.ExecutedSql.Any(sql => sql.Contains(unitPriceOrderBy, StringComparison.Ordinal)),
                            $"{caseName}: 快速路径未使用固定的均价排序片段。"
                        );
                    }
                }
            }
        }
    }

    [ProductReportSortingSqlServerFact]
    public async Task GetEnhancedSalesProductDetailsAsync_Sort_数据库分页缓存按排序隔离()
    {
        await using var fixture = await ProductSortSqlServerFixture.CreateAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = fixture.CreateService(useSupplierRollups: true, cache);
        fixture.ExecutedSql.Clear();

        async Task<string> FirstPageAsync(string? sortField, string? sortOrder)
        {
            var page = await service.GetEnhancedSalesProductDetailsAsync(
                CreateDateRange(),
                localSupplierCodes: new List<string> { SupplierCode },
                pageIndex: 1,
                pageSize: 4,
                sortField: sortField,
                sortOrder: sortOrder
            );
            Assert.Equal(9, page.Total);
            return ToLetters(page.Data);
        }

        int RankingQueryCount() =>
            fixture.ExecutedSql.Count(sql => sql.Contains("#ProductReportAggregates", StringComparison.Ordinal));

        // 同一服务实例、同一页码：排序若不参与完整快照缓存键，后两次会直接拿到默认排序的缓存页。
        Assert.Equal("ABCF", await FirstPageAsync(null, null));
        Assert.Equal("ACFD", await FirstPageAsync("unitPrice", "desc"));
        Assert.Equal("GHIE", await FirstPageAsync("quantity", "asc"));
        Assert.Equal(3, RankingQueryCount());
        // 默认排序再次请求命中自己的缓存键，不被其它排序覆盖，也不再执行排名 SQL。
        Assert.Equal("ABCF", await FirstPageAsync(null, null));
        Assert.Equal(3, RankingQueryCount());
    }

    private static void AssertCase(string caseName, string expected, string actual)
    {
        // 一个用例循环覆盖多种组合；xUnit 的字符串差异输出会截断前缀，失败信息需显式带上完整组合名。
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Assert.Fail($"{caseName}{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual:   {actual}");
        }
    }

    private static DateRangeDto CreateDateRange() => new()
    {
        StartDate = CurrentDate,
        EndDate = CurrentDate,
        CompareStartDate = CompareDate,
        CompareEndDate = CompareDate,
    };

    private static string ToLetters(IEnumerable<SalesProductDetailWithDiscountDto> rows) =>
        string.Concat(rows.Select(row => row.ProductCode.StartsWith("P-SORT-", StringComparison.Ordinal)
            ? row.ProductCode["P-SORT-".Length..]
            : $"[{row.ProductCode}]"));

    private static string DescribeExpectedValues(string order) =>
        string.Join(" ", order.Select(letter =>
        {
            var expected = ExpectedValues[letter];
            return Describe(letter.ToString(), $"HB-SORT-{letter}", expected.Amount, expected.Quantity, expected.AmountLY, expected.QuantityLY);
        }));

    private static string DescribeValues(IEnumerable<SalesProductDetailWithDiscountDto> rows) =>
        string.Join(" ", rows.Select(row => Describe(
            row.ProductCode.StartsWith("P-SORT-", StringComparison.Ordinal) ? row.ProductCode["P-SORT-".Length..] : row.ProductCode,
            row.ItemNumber,
            row.SalesAmount,
            row.Quantity,
            row.SalesAmountLY,
            row.QuantityLY)));

    private static string Describe(string letter, string? itemNumber, decimal amount, int quantity, decimal amountLY, int quantityLY) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{letter}:{itemNumber}={amount:0.####}/{quantity},LY={amountLY:0.####}/{quantityLY}"
        );

    private sealed class ProductSortSqlServerFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly string _databaseConnectionString;
        private readonly SqlSugarClient _db;

        private ProductSortSqlServerFixture(
            string masterConnectionString,
            string databaseName,
            string databaseConnectionString)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _databaseConnectionString = databaseConnectionString;
            _db = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
            // 记录真实执行的 SQL，用来区分路径 A（#ProductReportAggregates 临时表排名）与路径 B（SqlSugar 分页）。
            _db.Aop.OnLogExecuting = (sql, _) => ExecutedSql.Add(sql);
        }

        public List<string> ExecutedSql { get; } = new();

        public static async Task<ProductSortSqlServerFixture> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerTestConnectionEnvVar);
            if (string.IsNullOrWhiteSpace(baseConnectionString))
                throw new InvalidOperationException($"未配置 {SqlServerTestConnectionEnvVar}。");
            EnsureLoopbackSqlServer(baseConnectionString);

            var databaseName = $"HbProductSort_{Guid.NewGuid():N}";
            var masterConnectionString = BuildConnectionString(baseConnectionString, "master");
            var databaseConnectionString = BuildConnectionString(baseConnectionString, databaseName);
            await ExecuteNonQueryAsync(
                masterConnectionString,
                $"CREATE DATABASE {QuoteSqlServerName(databaseName)};");

            ProductSortSqlServerFixture? fixture = null;
            try
            {
                // 生产库已启用快照隔离，路径 A 会在快照事务里创建临时表并排名；测试库保持同一条件。
                await ExecuteNonQueryAsync(
                    databaseConnectionString,
                    "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;");
                fixture = new ProductSortSqlServerFixture(
                    masterConnectionString,
                    databaseName,
                    databaseConnectionString);
                await fixture.SeedAsync();
                fixture.ExecutedSql.Clear();
                return fixture;
            }
            catch
            {
                if (fixture is not null)
                    await fixture.DisposeAsync();
                else
                    await DropDatabaseAsync(masterConnectionString, databaseName);
                throw;
            }
        }

        public SalesDashboardReactService CreateService(bool useSupplierRollups, IMemoryCache cache)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Reports:UseSupplierRollups"] = useSupplierRollups ? "true" : "false",
                })
                .Build();
            // 报表库与 POSM 库共用同一个隔离测试库；本用例不涉及中国供应商映射。
            return new SalesDashboardReactService(
                InjectContext<SqlSugarContext>(_db),
                InjectContext<POSMSqlSugarContext>(_db),
                Mock.Of<IMapper>(),
                NullLogger<SalesDashboardReactService>.Instance,
                cache,
                configuration: configuration);
        }

        public async ValueTask DisposeAsync()
        {
            _db.Dispose();
            using (var connection = new SqlConnection(_databaseConnectionString))
            {
                SqlConnection.ClearPool(connection);
            }
            await DropDatabaseAsync(_masterConnectionString, _databaseName);
        }

        private async Task SeedAsync()
        {
            _db.CodeFirst.InitTables(
                typeof(Product),
                typeof(ProductStoreDailySalesStatistic),
                typeof(SalesStatisticRefreshState),
                typeof(ChinaSupplier)
            );

            await _db.Insertable("ABCDEFGHI".Select(letter => new Product
            {
                UUID = $"P-SORT-{letter}",
                ProductCode = $"P-SORT-{letter}",
                ItemNumber = $"HB-SORT-{letter}",
                Barcode = $"BAR-SORT-{letter}",
                ProductName = $"排序商品{letter}",
                LocalSupplierCode = SupplierCode,
            }).ToList()).ExecuteCommandAsync();

            // 与 SalesDashboardReportRevenueTests.SeedProductSortFixtureAsync 相同的数据集：
            // A、C 本期和 G 同期拆在两店覆盖 SUM；金额全部为整数，整数除法会让均价顺序出错。
            await _db.Insertable(new List<ProductStoreDailySalesStatistic>
            {
                Statistic(CurrentDate, "S1", SupplierCode, "P-SORT-A", 60m, 6),
                Statistic(CurrentDate, "S2", SupplierCode, "P-SORT-A", 40m, 4),
                Statistic(CurrentDate, "S1", SupplierCode, "P-SORT-B", 90m, 30),
                Statistic(CurrentDate, "S1", SupplierCode, "P-SORT-C", 20m, 3),
                Statistic(CurrentDate, "S2", SupplierCode, "P-SORT-C", 27m, 3),
                Statistic(CurrentDate, "S1", SupplierCode, "P-SORT-D", 39m, 5),
                Statistic(CurrentDate, "S2", SupplierCode, "P-SORT-E", 39m, 5),
                Statistic(CurrentDate, "S1", SupplierCode, "P-SORT-F", 47m, 6),
                Statistic(CurrentDate, "S1", SupplierCode, "P-SORT-H", 10m, 2),
                Statistic(CurrentDate, "S2", SupplierCode, "P-SORT-I", 10m, 2),
                Statistic(CompareDate, "S1", SupplierCode, "P-SORT-A", 50m, 5),
                Statistic(CompareDate, "S2", SupplierCode, "P-SORT-C", 20m, 2),
                Statistic(CompareDate, "S1", SupplierCode, "P-SORT-D", 36m, 3),
                Statistic(CompareDate, "S1", SupplierCode, "P-SORT-G", 50m, 2),
                Statistic(CompareDate, "S2", SupplierCode, "P-SORT-G", 30m, 2),
                // 其它供应商的高额干扰商品：供应商筛选失效时会排到金额降序首位并把 Total 变成 10。
                Statistic(CurrentDate, "S1", "OTHER-SORT", "P-SORT-NOISE", 999m, 1),
                Statistic(CompareDate, "S1", "OTHER-SORT", "P-SORT-NOISE", 999m, 1),
            }).ExecuteCommandAsync();

            // 路径 A 要求三类统计同版本 Fresh；路径 B 只读取商品日统计状态。
            var publishedAt = DateTime.UtcNow;
            foreach (var date in new[] { CurrentDate, CompareDate })
            {
                foreach (var type in new[]
                {
                    SalesStatisticType.ProductStoreDaily,
                    SalesStatisticType.AustralianSupplierStoreSales,
                    SalesStatisticType.ChinaSupplierStoreSales,
                })
                {
                    await _db.Insertable(new SalesStatisticRefreshState
                    {
                        Date = date,
                        StatisticType = type,
                        Status = SalesStatisticRefreshStatus.Fresh,
                        SourceProductVersion = $"sort-{date:yyyyMMdd}",
                        LastAggregatedAtUtc = publishedAt,
                        CompletedAtUtc = publishedAt,
                    }).ExecuteCommandAsync();
                }
            }
        }

        private static ProductStoreDailySalesStatistic Statistic(
            DateTime date,
            string branchCode,
            string supplierCode,
            string productCode,
            decimal totalAmount,
            int totalQuantity) => new()
        {
            Date = date,
            BranchCode = branchCode,
            SupplierCode = supplierCode,
            ProductCode = productCode,
            ProductName = productCode,
            Barcode = productCode.Replace("P-SORT-", "BAR-SORT-", StringComparison.Ordinal),
            TotalAmount = totalAmount,
            TotalQuantity = totalQuantity,
            OrderCount = 1,
            CostSource = "Test",
            UpdateTime = DateTime.UtcNow,
        };

        private static ConnectionConfig CreateConnectionConfig(string connectionString) => new()
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
            MoreSettings = new ConnMoreSettings { SqlServerCodeFirstNvarchar = true },
        };

        private static T InjectContext<T>(ISqlSugarClient db)
        {
            var context = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(context, db);
            return context;
        }

        private static string BuildConnectionString(string connectionString, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }

        private static void EnsureLoopbackSqlServer(string connectionString)
        {
            // 该测试会创建并删除数据库，只允许指向本机环回地址的隔离 SQL Server。
            var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource.Trim();
            if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
                dataSource = dataSource[4..];
            var parts = dataSource.Split(',', 2, StringSplitOptions.TrimEntries);
            var host = parts[0].Trim().Trim('[', ']');
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{SqlServerTestConnectionEnvVar} 必须指向 localhost 或 127.0.0.1，当前 DataSource 不符合环回测试约束。");
            }
            if (parts.Length == 2
                && (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535))
            {
                throw new InvalidOperationException(
                    $"{SqlServerTestConnectionEnvVar} 的 SQL Server 端口无效，当前测试仅允许本地环回连接。");
            }
        }

        private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }

        private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
        {
            var quotedName = QuoteSqlServerName(databaseName);
            await ExecuteNonQueryAsync(
                masterConnectionString,
                $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quotedName};
                END;
                """);
        }

        private static string QuoteSqlServerName(string name) =>
            $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
