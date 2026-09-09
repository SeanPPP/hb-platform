using System.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Data.SqlClient;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalSupplierPurchaseSalesSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "PURCHASE_SALES_SQLSERVER_TEST_CONNECTION";

    public LocalSupplierPurchaseSalesSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过只读 SQL Server 进货销量验证。";
        }
    }
}

[Trait("Category", "SQL")]
public sealed class LocalSupplierPurchaseSalesSqlServerTests
{
    private const string SqlServerTestConnectionEnvVar =
        "PURCHASE_SALES_SQLSERVER_TEST_CONNECTION";

    // 关键位置：七个同名 CTE 只用 VALUES 构造数据，确保真实 SQL Server 验证不读写业务表。
    private const string FixturePrefix =
        """
WITH StoreLocalSupplierInvoice
    (InvoiceGUID, StoreCode, OrderDate, InboundDate, CreatedAt, IsDeleted) AS (
    SELECT * FROM (VALUES
        (CAST(N'H-P1-OLD' AS nvarchar(50)), CAST(N'1004' AS nvarchar(50)), CAST('2026-03-30' AS datetime2), CAST('2026-03-20' AS datetime2), CAST('2026-03-20' AS datetime2), CAST(NULL AS bit)),
        (N'H-P1-NEW', N'1004', CAST('2026-03-21' AS datetime2), CAST('2026-04-01' AS datetime2), CAST('2026-04-01' AS datetime2), NULL),
        (N'H-P2', N'1004', CAST('2026-04-02' AS datetime2), CAST('2026-04-02' AS datetime2), CAST('2026-04-02' AS datetime2), NULL),
        (N'H-P3', N'1004', CAST('2026-04-03' AS datetime2), CAST('2026-04-03' AS datetime2), CAST('2026-04-03' AS datetime2), NULL),
        (N'H-P4', N'1004', CAST('2026-04-04' AS datetime2), CAST('2026-04-04' AS datetime2), CAST('2026-04-04' AS datetime2), NULL),
        (N'H-ORDER-OUTSIDE', N'1004', CAST('2026-03-12' AS datetime2), CAST('2026-05-01' AS datetime2), CAST('2026-03-12' AS datetime2), NULL)
    ) value(InvoiceGUID, StoreCode, OrderDate, InboundDate, CreatedAt, IsDeleted)
),
StoreLocalSupplierInvoiceDetails
    (InvoiceGUID, StoreProductCode, ProductCode, ItemNumber, Barcode, ProductName, Quantity, IsDeleted) AS (
    SELECT * FROM (VALUES
        (CAST(N'H-P1-OLD' AS nvarchar(50)), CAST(N'SRP-P1' AS nvarchar(50)), CAST(N'P1' AS nvarchar(50)), CAST(N'ITEM-P1' AS nvarchar(50)), CAST(N'BAR-P1' AS nvarchar(50)), CAST(N'Product 1' AS nvarchar(100)), CAST(5 AS decimal(18, 2)), CAST(NULL AS bit)),
        (N'H-P1-NEW', N'SRP-P1', N'P1', N'ITEM-P1', N'BAR-P1', N'Product 1', CAST(10 AS decimal(18, 2)), NULL),
        (N'H-P2', N'SRP-P2', N'P2', N'ITEM-P2', N'BAR-P2', N'Product 2', CAST(4 AS decimal(18, 2)), NULL),
        (N'H-P3', N'SRP-P3', N'P3', N'ITEM-P3', N'BAR-P3', N'Product 3', CAST(8 AS decimal(18, 2)), NULL),
        (N'H-P4', N'SRP-P4', N'P4', N'ITEM-P4', N'BAR-P4', N'Product 4', CAST(6 AS decimal(18, 2)), NULL),
        (N'H-ORDER-OUTSIDE', N'SRP-P1', N'P1', N'ITEM-P1', N'BAR-P1', N'Product 1', CAST(999 AS decimal(18, 2)), NULL)
    ) value(InvoiceGUID, StoreProductCode, ProductCode, ItemNumber, Barcode, ProductName, Quantity, IsDeleted)
),
StoreRetailPrice (UUID, ProductCode, SupplierCode, IsDeleted) AS (
    SELECT * FROM (VALUES
        (CAST(N'SRP-P1' AS nvarchar(50)), CAST(N'P1' AS nvarchar(50)), CAST(N'243' AS nvarchar(50)), CAST(NULL AS bit)),
        (N'SRP-P2', N'P2', N'243', NULL),
        (N'SRP-P3', N'P3', N'243', NULL),
        (N'SRP-P4', N'P4', N'243', NULL)
    ) value(UUID, ProductCode, SupplierCode, IsDeleted)
),
Product
    (ProductCode, ItemNumber, Barcode, ProductName, ProductImage, LocalSupplierCode, IsDeleted) AS (
    SELECT * FROM (VALUES
        (CAST(N'P1' AS nvarchar(50)), CAST(N'ITEM-P1' AS nvarchar(50)), CAST(N'BAR-P1' AS nvarchar(50)), CAST(N'Product 1' AS nvarchar(100)), CAST(N'p1.jpg' AS nvarchar(200)), CAST(N'243' AS nvarchar(50)), CAST(NULL AS bit)),
        (N'P2', N'ITEM-P2', N'BAR-P2', N'Product 2', N'p2.jpg', N'243', NULL),
        (N'P3', N'ITEM-P3', N'BAR-P3', N'Product 3', N'p3.jpg', N'OTHER', NULL),
        (N'P4', N'ITEM-P4', N'BAR-P4', N'Product 4', N'p4.jpg', NULL, NULL)
    ) value(ProductCode, ItemNumber, Barcode, ProductName, ProductImage, LocalSupplierCode, IsDeleted)
),
Store (StoreCode, StoreName, IsDeleted) AS (
    SELECT CAST(N'1004' AS nvarchar(50)), CAST(N'Campbelltown' AS nvarchar(100)), CAST(NULL AS bit)
),
LocalSupplier (LocalSupplierCode, Name, IsDeleted) AS (
    SELECT * FROM (VALUES
        (CAST(N'243' AS nvarchar(50)), CAST(N'Brazco' AS nvarchar(100)), CAST(NULL AS bit)),
        (N'OTHER', N'Other Supplier', NULL)
    ) value(LocalSupplierCode, Name, IsDeleted)
),
ProductStoreDailySalesStatistic (BranchCode, ProductCode, Date, TotalQuantity, UpdateTime) AS (
    SELECT * FROM (VALUES
        (CAST(N'1004' AS nvarchar(50)), CAST(N'P1' AS nvarchar(50)), CAST('2026-03-20' AS date), CAST(2 AS int), CAST('2026-07-01T01:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-03-31' AS date), -1, CAST('2026-07-01T02:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-04-01' AS date), 3, CAST('2026-07-01T03:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-04-30' AS date), 4, CAST('2026-07-01T04:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-05-01' AS date), 5, CAST('2026-07-01T05:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-05-30' AS date), 6, CAST('2026-07-01T06:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-05-31' AS date), 7, CAST('2026-07-01T07:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-06-29' AS date), 8, CAST('2026-07-01T08:00:00' AS datetime2)),
        (N'1004', N'P1', CAST('2026-06-30' AS date), 999, CAST('2026-07-01T09:00:00' AS datetime2))
    ) value(BranchCode, ProductCode, Date, TotalQuantity, UpdateTime)
)
""";

    [LocalSupplierPurchaseSalesSqlServerFact]
    public async Task 查询遵守供应商优先订单日期最近两次与Null删除标记语义()
    {
        var sql = BuildQuery();
        var rows = await QueryAsync(sql.PagedSql, sql.Parameters);

        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, row => Text(row, "ProductCode") == "P3");

        var p1 = Assert.Single(rows, row => Text(row, "ProductCode") == "P1");
        Assert.Equal("243", Text(p1, "SupplierCode"));
        Assert.Equal(new DateTime(2026, 4, 1), Date(p1, "LatestPurchaseDate"));
        Assert.Equal(10m, Decimal(p1, "LatestPurchaseQty"));
        Assert.Equal(new DateTime(2026, 3, 20), Date(p1, "PreviousPurchaseDate"));
        Assert.Equal(5m, Decimal(p1, "PreviousPurchaseQty"));
        Assert.Equal(12, Integer(p1, "PurchaseIntervalDays"));

        var p4 = Assert.Single(rows, row => Text(row, "ProductCode") == "P4");
        Assert.Equal("243", Text(p4, "SupplierCode"));
    }

    [LocalSupplierPurchaseSalesSqlServerFact]
    public async Task 销量窗口包含退货并保持左闭右开及空数据语义()
    {
        var sql = BuildQuery();
        var rows = await QueryAsync(sql.PagedSql, sql.Parameters);

        var p1 = Assert.Single(rows, row => Text(row, "ProductCode") == "P1");
        Assert.Equal(1, Integer(p1, "SalesBetweenPurchases"));
        Assert.Equal(7, Integer(p1, "SalesQty30"));
        Assert.Equal(18, Integer(p1, "SalesQty60"));
        Assert.Equal(33, Integer(p1, "SalesQty90"));

        var p2 = Assert.Single(rows, row => Text(row, "ProductCode") == "P2");
        Assert.Null(p2["PreviousPurchaseDate"]);
        Assert.Null(p2["SalesBetweenPurchases"]);
        Assert.Equal(0, Integer(p2, "SalesQty30"));
        Assert.Equal(0, Integer(p2, "SalesQty60"));
        Assert.Equal(0, Integer(p2, "SalesQty90"));
    }

    [LocalSupplierPurchaseSalesSqlServerFact]
    public async Task Scope交集分页总数与全局销量排序在边界页保持一致()
    {
        var firstPage = BuildQuery(page: 1, sortBy: "salesQty90");
        var firstRows = await QueryAsync(firstPage.PagedSql, firstPage.Parameters);
        Assert.Equal("P1", Text(firstRows[0], "ProductCode"));

        var beyondLastPage = BuildQuery(page: 2, sortBy: "salesQty90");
        var beyondRows = await QueryAsync(beyondLastPage.PagedSql, beyondLastPage.Parameters);
        var beyondSummary = Assert.Single(
            await QueryAsync(beyondLastPage.SummarySql, beyondLastPage.Parameters)
        );
        Assert.Empty(beyondRows);
        Assert.Equal(3, Integer(beyondSummary, "TotalCount"));

        var disjoint = BuildQuery(scopedStoreCodes: ["9999"]);
        Assert.Empty(await QueryAsync(disjoint.PagedSql, disjoint.Parameters));
        var disjointSummary = Assert.Single(
            await QueryAsync(disjoint.SummarySql, disjoint.Parameters)
        );
        Assert.Equal(0, Integer(disjointSummary, "TotalCount"));
    }

    private static LocalSupplierPurchaseSalesAnalysisSqlBuildResult BuildQuery(
        int page = 1,
        string sortBy = "latestPurchaseDate",
        IReadOnlyList<string>? scopedStoreCodes = null
    )
    {
        var sql = LocalSupplierInvoiceSalesAnalysisSqlBuilder.BuildPurchaseSalesAnalysis(
            new LocalSupplierPurchaseSalesAnalysisQueryDto
            {
                StoreCode = "1004",
                SupplierCode = "243",
                OrderDateStart = new DateTime(2026, 3, 13),
                OrderDateEnd = new DateTime(2026, 9, 9),
                SortBy = sortBy,
                SortOrder = "desc",
                Page = page,
                PageSize = 100,
            },
            scopedStoreCodes
        );

        return new LocalSupplierPurchaseSalesAnalysisSqlBuildResult
        {
            PagedSql = AddFixture(sql.PagedSql),
            SummarySql = AddFixture(sql.SummarySql),
            Parameters = sql.Parameters,
        };
    }

    private static string AddFixture(string builderSql)
    {
        const string marker = "WITH FilteredInvoices AS (";
        Assert.StartsWith(marker, builderSql, StringComparison.Ordinal);
        return FixturePrefix + ",\nFilteredInvoices AS (" + builderSql[marker.Length..];
    }

    private static async Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql,
        IReadOnlyList<SugarParameter> parameters
    )
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerTestConnectionEnvVar);
        // 自定义 Fact 在发现阶段 Skip；这里防止测试运行期间环境被意外清空。
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 60;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string Text(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToString(row[key]) ?? string.Empty;

    private static DateTime Date(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToDateTime(row[key]);

    private static decimal Decimal(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToDecimal(row[key]);

    private static int Integer(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt32(row[key]);
}
