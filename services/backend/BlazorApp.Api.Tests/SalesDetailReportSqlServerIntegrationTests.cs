using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDetailReportSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable = "HB_TEST_SQLSERVER_CONNECTION";

    public SalesDetailReportSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 销售明细验证。";
    }
}

[Trait("Category", "SQL")]
public sealed class SalesDetailReportSqlServerIntegrationTests
{
    private static readonly DateTime SeedDate = new(2026, 9, 9);
    private static readonly DateTime CompareDate = new(2026, 9, 8);
    private const string SqlServerTestConnectionEnvVar = "HB_TEST_SQLSERVER_CONNECTION";

    [SalesDetailReportSqlServerFact]
    public async Task 分店栏单商品精确筛选按授权门店范围且统计商品码去空格()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "授权店");
        await fixture.SeedStoreAsync("B2", "范围外店");
        await fixture.SeedChinaSupplierAsync("C1", "国内供应商");
        await fixture.SeedProductAsync("P-ONE", "商品一");
        await fixture.SeedProductAsync("P-TWO", "商品二");
        await fixture.SeedFactAsync(SeedDate, "B1", "C1", " P-ONE ", 1, 10m, "统计一");
        await fixture.SeedFactAsync(SeedDate, "B1", "C1", "P-TWO", 3, 30m, "统计二");
        await fixture.SeedFactAsync(SeedDate, "B2", "C1", "P-ONE", 9, 90m, "统计一");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.China, branchCodes: new() { "B1" },
            selectedProductCode: "P-ONE", sections: new[] { SalesDetailSection.Branches });

        Assert.Equal(SalesStatisticRefreshStatus.Fresh, result.StatisticStatus);
        var branch = Assert.Single(result.Data!.Branches!.Rows);
        Assert.Equal("B1", branch.Code);
        Assert.Equal(10m, branch.Revenue);
        Assert.Equal(1, branch.Quantity);
        Assert.DoesNotContain(result.Data.Branches.Rows, row => row.Code == "B2");
    }

    [SalesDetailReportSqlServerFact]
    public async Task 不同销售种类和所选供应商保持供应商归属口径()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedChinaSupplierAsync("C1", "国内一");
        await fixture.SeedLocalSupplierAsync("200", "HB仓库");
        await fixture.SeedProductAsync("P-CN", "国内商品");
        await fixture.SeedProductAsync("P-AU", "澳洲商品");
        await fixture.SeedMappingAsync("P-CN", "C1");
        await fixture.SeedFactAsync(SeedDate, "B1", "200", "P-CN", 2, 20m, "国内统计商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-AU", 5, 50m, "澳洲统计商品");

        var china = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.China, branchCodes: new() { "B1" },
            selectedSupplierCode: "C1", selectedProductCode: "P-CN",
            sections: new[] { SalesDetailSection.Branches });
        var australia = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedSupplierCode: "200", selectedProductCode: "P-CN",
            sections: new[] { SalesDetailSection.Branches });

        var chinaBranch = Assert.Single(china.Data!.Branches!.Rows);
        Assert.Equal("B1", chinaBranch.Code);
        Assert.Equal(20m, chinaBranch.Revenue);
        var australiaBranch = Assert.Single(australia.Data!.Branches!.Rows);
        Assert.Equal("B1", australiaBranch.Code);
        Assert.Equal(20m, australiaBranch.Revenue);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 关键词多个token可分别命中商品和国内供应商字段()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedChinaSupplierAsync("C2", "Beta 国内供应商");
        await fixture.SeedChinaSupplierAsync("C2", "Beta 国内供应商重复记录");
        await fixture.SeedProductAsync("P-MULTI", "普通商品", englishName: "Alpha 英文名", itemNumber: "货号");
        await fixture.SeedFactAsync(SeedDate, "B1", "C2", "P-MULTI", 4, 40m, "统计名称", "统计条码");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.China, branchCodes: new() { "B1" }, search: "Alpha Beta",
            sections: new[] { SalesDetailSection.Products });

        var product = Assert.Single(result.Data!.Products!.Rows);
        Assert.Equal("P-MULTI", product.Code);
        Assert.Equal(40m, product.Revenue);
        Assert.Equal(1, result.Data.Products.Total);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 重复ProductCode元信息同时命中搜索也不会放大汇总和商品销售事实()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedProductAsync("P-DUP", "商品主记录 Alpha", uuid: "product-dup-a");
        await fixture.SeedProductAsync("P-DUP", "商品重复记录 Alpha", uuid: "product-dup-b");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-DUP", 2, 100m, "统计商品");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            search: "Alpha", sections: new[] { SalesDetailSection.Summary, SalesDetailSection.Products });

        var product = Assert.Single(result.Data!.Products!.Rows);
        Assert.Equal("P-DUP", product.Code);
        Assert.Equal(100m, product.Revenue);
        Assert.Equal(2, product.Quantity);
        Assert.Equal(1, result.Data.Products.Total);
        Assert.Equal(100m, result.Data.Summary!.Summary!.Revenue);
        Assert.Equal(2, result.Data.Summary.Summary.Quantity);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 全量请求带selectedProduct时产品候选仍保留而分店栏按商品收窄()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedProductAsync("P-SELECT", "已选商品");
        await fixture.SeedProductAsync("P-OTHER", "其他商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-SELECT", 1, 10m, "已选统计商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-OTHER", 3, 30m, "其他统计商品");

        var result = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedProductCode: " P-SELECT ");

        var branch = Assert.Single(result.Data!.Branches!.Rows);
        Assert.Equal(10m, branch.Revenue);
        Assert.Equal(1, branch.Quantity);
        Assert.Equal(new[] { "P-OTHER", "P-SELECT" }, result.Data.Products!.Rows.Select(row => row.Code).OrderBy(code => code));
        Assert.Equal(2, result.Data.Products.Total);
    }

    [SalesDetailReportSqlServerFact]
    public async Task 分店栏本期同期单商品指标和缺成本语义与全量请求一致()
    {
        await using var fixture = await SalesDetailSqlServerFixture.CreateAsync();
        await fixture.SeedFreshStateAsync(SeedDate);
        await fixture.SeedFreshStateAsync(CompareDate);
        await fixture.SeedStoreAsync("B1", "测试店");
        await fixture.SeedLocalSupplierAsync("AUS1", "澳洲供应商");
        await fixture.SeedProductAsync("P-ROLL", "跨期商品");
        await fixture.SeedFactAsync(SeedDate, "B1", "AUS1", "P-ROLL", 3, 30m, "本期统计商品", totalCost: 12m, grossProfit: 18m);
        await fixture.SeedFactAsync(CompareDate, "B1", "AUS1", "P-ROLL", 2, 20m, "同期统计商品", totalCost: null, grossProfit: null, useAmountAsDefaultGrossProfit: false);

        var branchesOnly = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(SeedDate, CompareDate), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedProductCode: "P-ROLL", sections: new[] { SalesDetailSection.Branches });
        var full = await fixture.CreateService().GetSalesDetailReportAsync(
            Range(SeedDate, CompareDate), SalesDetailKind.Australia, branchCodes: new() { "B1" },
            selectedProductCode: "P-ROLL");

        var branch = Assert.Single(branchesOnly.Data!.Branches!.Rows);
        Assert.Equal(30m, branch.Revenue);
        Assert.Equal(20m, branch.CompareRevenue);
        Assert.Equal(3, branch.Quantity);
        Assert.Equal(2, branch.CompareQuantity);
        Assert.Equal(30m, branch.AverageTransaction);
        Assert.Equal(20m, branch.CompareAverageTransaction);
        Assert.Equal(10m, branch.AverageUnitPrice);
        Assert.Equal(10m, branch.CompareAverageUnitPrice);
        Assert.Equal(18m, branch.GrossProfit);
        Assert.Null(branch.CompareGrossProfit); // 同期缺成本时不能伪造毛利和毛利率。
        Assert.Equal(0.6m, branch.GrossMarginRate);
        Assert.Null(branch.CompareGrossMarginRate);

        var fullBranch = Assert.Single(full.Data!.Branches!.Rows);
        Assert.Equal(branch.Revenue, fullBranch.Revenue);
        Assert.Equal(branch.CompareRevenue, fullBranch.CompareRevenue);
        Assert.Equal(branch.Quantity, fullBranch.Quantity);
        Assert.Equal(branch.CompareQuantity, fullBranch.CompareQuantity);
        Assert.Equal(branch.AverageTransaction, fullBranch.AverageTransaction);
        Assert.Equal(branch.CompareAverageTransaction, fullBranch.CompareAverageTransaction);
        Assert.Equal(branch.AverageUnitPrice, fullBranch.AverageUnitPrice);
        Assert.Equal(branch.CompareAverageUnitPrice, fullBranch.CompareAverageUnitPrice);
        Assert.Equal(branch.GrossProfit, fullBranch.GrossProfit);
        Assert.Null(fullBranch.CompareGrossProfit);
        Assert.Equal(branch.GrossMarginRate, fullBranch.GrossMarginRate);
        Assert.Null(fullBranch.CompareGrossMarginRate);
    }

    private static DateRangeDto Range() => Range(SeedDate, null);

    private static DateRangeDto Range(DateTime currentDate, DateTime? compareDate)
        => new()
        {
            StartDate = currentDate, EndDate = currentDate,
            CompareStartDate = compareDate, CompareEndDate = compareDate,
        };

    private sealed class SalesDetailSqlServerFixture : IAsyncDisposable
    {
        private readonly string _masterConnectionString;
        private readonly string _databaseName;
        private readonly string _databaseConnectionString;
        private readonly SqlSugarClient _db;
        private readonly SqlSugarClient _posmDb;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        private SalesDetailSqlServerFixture(string masterConnectionString, string databaseName, string databaseConnectionString)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            _databaseConnectionString = databaseConnectionString;
            _db = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
            _posmDb = new SqlSugarClient(CreateConnectionConfig(databaseConnectionString));
        }

        public static async Task<SalesDetailSqlServerFixture> CreateAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerTestConnectionEnvVar);
            if (string.IsNullOrWhiteSpace(baseConnectionString))
                throw new InvalidOperationException($"未配置 {SqlServerTestConnectionEnvVar}。");
            EnsureLoopbackSqlServer(baseConnectionString);

            var databaseName = $"HbSalesDetail_{Guid.NewGuid():N}";
            var masterConnectionString = BuildConnectionString(baseConnectionString, "master");
            var databaseConnectionString = BuildConnectionString(baseConnectionString, databaseName);
            await ExecuteNonQueryAsync(masterConnectionString, $"CREATE DATABASE {QuoteSqlServerName(databaseName)};");
            try
            {
                await ExecuteNonQueryAsync(databaseConnectionString, "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;");
                await ExecuteNonQueryAsync(databaseConnectionString, SchemaSql);
                return new SalesDetailSqlServerFixture(masterConnectionString, databaseName, databaseConnectionString);
            }
            catch
            {
                await DropDatabaseAsync(masterConnectionString, databaseName);
                throw;
            }
        }

        public SalesDashboardReactService CreateService()
        {
            return new SalesDashboardReactService(
                CreateSqlSugarContext(_db), CreatePosmSqlSugarContext(_posmDb), Mock.Of<IMapper>(),
                NullLogger<SalesDashboardReactService>.Instance, _cache);
        }

        // 所有测试数据都使用参数写入，避免把测试输入拼接进 SQL。
        public Task SeedFreshStateAsync(DateTime date) => ExecuteNonQueryAsync(_databaseConnectionString, """
            INSERT INTO [dbo].[SalesStatisticRefreshState]
                ([StatisticType], [Date], [Status], [LastAggregatedAtUtc], [CompletedAtUtc])
            VALUES (N'ProductStoreDaily', @date, N'Fresh', SYSUTCDATETIME(), SYSUTCDATETIME());
            """, ("@date", date));

        public Task SeedStoreAsync(string code, string name) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[Store] ([StoreCode], [StoreName], [IsActive], [IsDeleted]) VALUES (@code, @name, 1, 0);",
            ("@code", code), ("@name", name));

        public Task SeedChinaSupplierAsync(string code, string name) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[ChinaSupplier] ([SupplierCode], [SupplierName]) VALUES (@code, @name);",
            ("@code", code), ("@name", name));

        public Task SeedLocalSupplierAsync(string code, string name) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[LocalSupplier] ([LocalSupplierCode], [Name], [IsDeleted]) VALUES (@code, @name, 0);",
            ("@code", code), ("@name", name));

        public Task SeedProductAsync(string code, string name, string? englishName = null, string? itemNumber = null, string? uuid = null)
            => ExecuteNonQueryAsync(_databaseConnectionString, """
                INSERT INTO [dbo].[Product]
                    ([UUID], [ProductCode], [ProductName], [EnglishName], [ItemNumber], [Barcode], [LocalSupplierCode], [ProductImage])
                VALUES (@uuid, @code, @name, @englishName, @itemNumber, NULL, NULL, NULL);
                """, ("@uuid", uuid ?? $"product-{Guid.NewGuid():N}"), ("@code", code), ("@name", name),
                ("@englishName", englishName), ("@itemNumber", itemNumber));

        public Task SeedMappingAsync(string productCode, string chinaSupplierCode) => ExecuteNonQueryAsync(_databaseConnectionString,
            "INSERT INTO [dbo].[posm_product_supplier_mapping] ([ProductCode], [LocalSupplierCode], [ChinaSupplierCode], [IsDeleted]) VALUES (@product, N'200', @china, 0);",
            ("@product", productCode), ("@china", chinaSupplierCode));

        public Task SeedFactAsync(DateTime date, string branch, string supplier, string product, int quantity, decimal amount,
            string? productName = null, string? barcode = null, decimal? totalCost = null, decimal? grossProfit = null,
            bool useAmountAsDefaultGrossProfit = true)
            => ExecuteNonQueryAsync(_databaseConnectionString, """
                INSERT INTO [dbo].[ProductStoreDailySalesStatistic]
                    ([Date], [BranchCode], [SupplierCode], [ProductCode], [ProductName], [Barcode], [TotalQuantity], [TotalAmount], [OrderCount], [TotalCost], [GrossProfit], [CostSource], [UpdateTime])
                VALUES (@date, @branch, @supplier, @product, @productName, @barcode, @quantity, @amount, 1, @totalCost, @grossProfit, N'Test', SYSUTCDATETIME());
                """, ("@date", date), ("@branch", branch), ("@supplier", supplier), ("@product", product),
                ("@productName", productName), ("@barcode", barcode), ("@quantity", quantity), ("@amount", amount),
                ("@totalCost", totalCost), ("@grossProfit", useAmountAsDefaultGrossProfit ? grossProfit ?? amount : grossProfit));

        public async ValueTask DisposeAsync()
        {
            _cache.Dispose();
            _db.Dispose();
            _posmDb.Dispose();
            await DropDatabaseAsync(_masterConnectionString, _databaseName);
        }

        private static readonly string SchemaSql = """
            SET NOCOUNT ON;
            CREATE TABLE [dbo].[Store] (
                [StoreCode] nvarchar(50) NOT NULL PRIMARY KEY,
                [StoreName] nvarchar(100) NOT NULL,
                [IsActive] bit NOT NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[LocalSupplier] (
                [LocalSupplierCode] nvarchar(64) NOT NULL PRIMARY KEY,
                [Name] nvarchar(128) NOT NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[ChinaSupplier] (
                [SupplierCode] nvarchar(50) NULL,
                [SupplierName] nvarchar(200) NULL
            );
            CREATE TABLE [dbo].[Product] (
                [UUID] nvarchar(50) NOT NULL PRIMARY KEY,
                [ProductCode] nvarchar(50) NULL,
                [ProductName] nvarchar(200) NULL,
                [EnglishName] nvarchar(200) NULL,
                [ItemNumber] nvarchar(50) NULL,
                [Barcode] nvarchar(50) NULL,
                [LocalSupplierCode] nvarchar(50) NULL,
                [ProductImage] nvarchar(200) NULL
            );
            CREATE TABLE [dbo].[posm_product_supplier_mapping] (
                [ProductCode] nvarchar(50) NOT NULL,
                [LocalSupplierCode] nvarchar(50) NOT NULL,
                [ChinaSupplierCode] nvarchar(50) NULL,
                [IsDeleted] bit NOT NULL
            );
            CREATE TABLE [dbo].[ProductStoreDailySalesStatistic] (
                [Date] datetime2(7) NOT NULL,
                [BranchCode] nvarchar(50) NOT NULL,
                [SupplierCode] nvarchar(50) NOT NULL,
                [ProductCode] nvarchar(50) NOT NULL,
                [ProductName] nvarchar(255) NULL,
                [Barcode] nvarchar(100) NULL,
                [TotalQuantity] int NOT NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [OrderCount] int NOT NULL,
                [TotalCost] decimal(18,2) NULL,
                [GrossProfit] decimal(18,2) NULL,
                [CostSource] nvarchar(50) NOT NULL,
                [UpdateTime] datetime2(7) NOT NULL,
                CONSTRAINT [PK_ProductStoreDailySalesStatistic] PRIMARY KEY ([Date], [BranchCode], [SupplierCode], [ProductCode])
            );
            CREATE TABLE [dbo].[SalesStatisticRefreshState] (
                [StatisticType] nvarchar(80) NOT NULL,
                [Date] datetime2(7) NOT NULL,
                [Status] nvarchar(20) NOT NULL,
                [LastAggregatedAtUtc] datetime2(7) NULL,
                [CompletedAtUtc] datetime2(7) NULL,
                [SourceProductVersion] nvarchar(64) NULL,
                CONSTRAINT [PK_SalesStatisticRefreshState] PRIMARY KEY ([StatisticType], [Date])
            );
            """;

        private static ConnectionConfig CreateConnectionConfig(string connectionString) => new()
        {
            ConnectionString = connectionString, DbType = DbType.SqlServer,
            IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
        };

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
            typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            return context;
        }

        private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
        {
            var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
            typeof(POSMSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
            return context;
        }

        private static async Task ExecuteNonQueryAsync(string connectionString, string sql, params (string Name, object? Value)[] parameters)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static string BuildConnectionString(string connectionString, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };
            return builder.ConnectionString;
        }

        private static void EnsureLoopbackSqlServer(string connectionString)
        {
            var dataSource = new SqlConnectionStringBuilder(connectionString).DataSource.Trim();
            if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) dataSource = dataSource[4..];
            var parts = dataSource.Split(',', 2, StringSplitOptions.TrimEntries);
            var host = parts[0].Trim().Trim('[', ']');
            if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                && !host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{SqlServerTestConnectionEnvVar} 必须指向 localhost 或 127.0.0.1。");
            if (parts.Length == 2 && (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535))
                throw new InvalidOperationException($"{SqlServerTestConnectionEnvVar} 的 SQL Server 端口无效。");
        }

        private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
        {
            var quotedName = QuoteSqlServerName(databaseName);
            await ExecuteNonQueryAsync(masterConnectionString, $"""
                IF DB_ID(N'{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE {quotedName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE {quotedName};
                END;
                """);
        }

        private static string QuoteSqlServerName(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
