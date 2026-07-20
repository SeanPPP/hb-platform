using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using Microsoft.Data.SqlClient;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LocalPurchaseDashboardSqlServerFactAttribute : FactAttribute
{
    private const string ConnectionEnvironmentVariable =
        "LOCAL_PURCHASE_DASHBOARD_SQLSERVER_TEST_CONNECTION";

    public LocalPurchaseDashboardSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)))
        {
            Skip = $"未配置 {ConnectionEnvironmentVariable}，跳过真实 SQL Server 集成验证。";
        }
    }
}

public sealed class LocalPurchaseDashboardSqlServerIntegrationTests
{
    private const string SqlServerTestConnectionEnvVar =
        "LOCAL_PURCHASE_DASHBOARD_SQLSERVER_TEST_CONNECTION";

    [LocalPurchaseDashboardSqlServerFact]
    public async Task DashboardSql_真实执行并保持主表与供应商抽屉金额一致()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerTestConnectionEnvVar);
        // 关键位置：自定义 Fact 已在发现阶段处理缺失配置；此断言防止运行阶段环境被意外清空。
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));

        var databaseName = $"HbLocalPurchaseDashboard_{Guid.NewGuid():N}";
        var masterConnectionString = BuildConnectionString(baseConnectionString!, "master");
        var databaseConnectionString = BuildConnectionString(baseConnectionString!, databaseName);
        await ExecuteNonQueryAsync(
            masterConnectionString,
            $"CREATE DATABASE {QuoteSqlServerName(databaseName)};"
        );

        try
        {
            await CreateSchemaAndSeedAsync(databaseConnectionString);
            using var db = new SqlSugarClient(CreateSqlServerConnectionConfig(databaseConnectionString));

            var dashboardQuery = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
                "2026-07",
                LocalPurchaseDashboardStoreScope.AllStores()
            );
            var dashboardRows = await db.Ado.SqlQueryAsync<LocalPurchaseDashboardMonthlyRow>(
                dashboardQuery.Sql,
                dashboardQuery.Parameters.ToArray()
            );
            var dashboard = LocalPurchaseDashboardComposer.ComposeDashboard(
                dashboardQuery.Period,
                dashboardRows
            );

            var supplierQuery = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
                "1001",
                "2026-07",
                LocalPurchaseDashboardStoreScope.AllStores()
            );
            var supplierRows = await db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierMonthlyRow>(
                supplierQuery.Sql,
                supplierQuery.Parameters.ToArray()
            );
            var supplierDrawer = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
                supplierQuery.Period,
                "1001",
                supplierRows
            );

            Assert.Equal(200m, dashboard.WarehouseTotal);
            Assert.Equal(160m, dashboard.LocalSupplierTotal);
            Assert.Equal(360m, dashboard.TotalAmount);
            Assert.Equal(dashboard.TotalAmount, dashboard.Stores.Sum(store => store.TotalAmount));

            var store = Assert.Single(dashboard.Stores, item => item.StoreCode == "1001");
            Assert.Equal(200m, store.WarehouseTotal);
            Assert.Equal(150m, store.LocalSupplierTotal);
            Assert.Equal(350m, store.TotalAmount);
            Assert.Equal(40m, store.Months.Single(month => month.Month == "2026-05").WarehouseAmount);
            Assert.Equal(60m, store.Months.Single(month => month.Month == "2026-06").WarehouseAmount);
            Assert.Equal(100m, store.Months.Single(month => month.Month == "2026-07").WarehouseAmount);
            Assert.Equal(30m, store.Months.Single(month => month.Month == "2026-05").LocalSupplierAmount);
            Assert.Equal(44.45m, store.Months.Single(month => month.Month == "2026-06").LocalSupplierAmount);
            Assert.Equal(75.55m, store.Months.Single(month => month.Month == "2026-07").LocalSupplierAmount);

            Assert.Equal(store.WarehouseTotal, supplierDrawer.WarehouseTotal);
            Assert.Equal(store.LocalSupplierTotal, supplierDrawer.LocalSupplierTotal);
            Assert.Equal(store.TotalAmount, supplierDrawer.TotalAmount);
            Assert.Equal(
                supplierDrawer.TotalAmount,
                supplierDrawer.Suppliers.Sum(supplier => supplier.TotalAmount)
            );
            Assert.Equal(
                200m,
                supplierDrawer.Suppliers.Single(item => item.SourceType == "WAREHOUSE_ORDER").TotalAmount
            );
            Assert.Equal(
                100m,
                supplierDrawer.Suppliers.Single(item => item.SourceCode == "SUP-A").TotalAmount
            );
            var unassigned = Assert.Single(
                supplierDrawer.Suppliers,
                item => item.SourceCode == "UNASSIGNED" && item.IsUnassigned
            );
            Assert.Equal(30m, unassigned.TotalAmount);
            var unknownSupplier = Assert.Single(
                supplierDrawer.Suppliers,
                item => item.SourceCode == "SUP-X"
            );
            Assert.Equal("SUP-X", unknownSupplier.SupplierName);
            Assert.Equal(20m, unknownSupplier.TotalAmount);

            Assert.Equal(0m, dashboard.Stores.Single(item => item.StoreCode == "1002").TotalAmount);
            Assert.Equal(10m, dashboard.Stores.Single(item => item.StoreCode == "9999").TotalAmount);
            Assert.Equal(
                1500m,
                store.Months.Sum(month => month.SalesAmount)
            );
            Assert.Equal(
                500m,
                store.Months.Single(month => month.Month == "2026-06").SalesAmount
            );
            Assert.Equal(
                1000m,
                store.Months.Single(month => month.Month == "2026-07").SalesAmount
            );
            var salesOnlyStore = Assert.Single(
                dashboard.Stores,
                item => item.StoreCode == "2003"
            );
            Assert.Equal(0m, salesOnlyStore.TotalAmount);
            Assert.Equal(
                70m,
                salesOnlyStore.Months.Single(month => month.Month == "2026-05").SalesAmount
            );
        }
        finally
        {
            await DropDatabaseAsync(masterConnectionString, databaseName);
        }
    }

    private static async Task CreateSchemaAndSeedAsync(string connectionString)
    {
        // 关键位置：只创建看板 SQL 实际引用的列，确保测试验证的是生产查询语法与金额口径。
        const string sql = """
CREATE TABLE [Store] (
    [StoreCode] nvarchar(50) NOT NULL,
    [StoreName] nvarchar(200) NULL,
    [IsDeleted] bit NULL
);

CREATE TABLE [WareHouseOrder] (
    [OrderGUID] nvarchar(50) NOT NULL,
    [StoreCode] nvarchar(50) NULL,
    [OutboundDate] datetime2 NULL,
    [OrderDate] datetime2 NULL,
    [CreatedAt] datetime2 NULL,
    [IsDeleted] bit NULL,
    [FlowStatus] int NULL
);

CREATE TABLE [WareHouseOrderDetails] (
    [OrderGUID] nvarchar(50) NOT NULL,
    [AllocQuantity] decimal(18, 4) NULL,
    [ImportPrice] decimal(18, 4) NULL,
    [IsDeleted] bit NULL
);

CREATE TABLE [StoreLocalSupplierInvoice] (
    [StoreCode] nvarchar(50) NULL,
    [SupplierCode] nvarchar(50) NULL,
    [TotalAmount] decimal(18, 2) NULL,
    [InboundDate] datetime2 NULL,
    [OrderDate] datetime2 NULL,
    [CreatedAt] datetime2 NULL,
    [IsDeleted] bit NULL
);

CREATE TABLE [LocalSupplier] (
    [LocalSupplierCode] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NULL,
    [IsDeleted] bit NULL
);

CREATE TABLE [StoreSalesStatistic] (
    [Date] datetime2 NOT NULL,
    [BranchCode] nvarchar(50) NOT NULL,
    [TotalAmount] decimal(18, 2) NULL
);

INSERT INTO [Store] ([StoreCode], [StoreName], [IsDeleted]) VALUES
    (N'1001', N'Brisbane', 0),
    (N'1002', N'Empty Store', 0),
    (N'DELETED', N'Deleted Store', 1);

INSERT INTO [LocalSupplier] ([LocalSupplierCode], [Name], [IsDeleted]) VALUES
    (N'SUP-A', N'Supplier A', 0),
    (N'SUP-X', N'Deleted Supplier Name', 1);

-- 出库日优先，其次订单日，最后创建日；草稿、软删除表头和软删除明细均不得计入。
INSERT INTO [WareHouseOrder]
    ([OrderGUID], [StoreCode], [OutboundDate], [OrderDate], [CreatedAt], [IsDeleted], [FlowStatus]) VALUES
    (N'WH-OUTBOUND', N'1001', '2026-07-12', '2024-01-01', '2024-01-01', 0, 1),
    (N'WH-ORDER', N'1001', NULL, '2026-06-12', '2024-01-01', 0, 1),
    (N'WH-CREATED', N'1001', NULL, NULL, '2026-05-12', 0, 1),
    (N'WH-DRAFT', N'1001', '2026-07-15', NULL, '2026-07-15', 0, 0),
    (N'WH-DELETED', N'1001', '2026-07-16', NULL, '2026-07-16', 1, 1),
    (N'WH-DELETED-DETAIL', N'1001', '2026-07-17', NULL, '2026-07-17', 0, 1);

INSERT INTO [WareHouseOrderDetails] ([OrderGUID], [AllocQuantity], [ImportPrice], [IsDeleted]) VALUES
    (N'WH-OUTBOUND', 2, 50, 0),
    (N'WH-ORDER', 3, 20, 0),
    (N'WH-CREATED', 4, 10, 0),
    (N'WH-DRAFT', 9, 99, 0),
    (N'WH-DELETED', 9, 99, 0),
    (N'WH-DELETED-DETAIL', 9, 99, 1);

-- 本地供应商 TotalAmount 已是不含 GST 金额，按原值计入，并覆盖三层日期回退与供应商名称回退。
INSERT INTO [StoreLocalSupplierInvoice]
    ([StoreCode], [SupplierCode], [TotalAmount], [InboundDate], [OrderDate], [CreatedAt], [IsDeleted]) VALUES
    (N'1001', N'SUP-A', 55.55, '2026-07-08', '2024-01-01', '2024-01-01', 0),
    (N'1001', N'SUP-A', 44.45, NULL, '2026-06-08', '2024-01-01', 0),
    (N'1001', NULL, 30.00, NULL, NULL, '2026-05-08', 0),
    (N'1001', N'SUP-X', 20.00, '2026-07-09', NULL, '2026-07-09', 0),
    (N'1001', N'SUP-A', 999.00, '2026-07-10', NULL, '2026-07-10', 1),
    (N'9999', N'SUP-A', 10.00, '2026-07-11', NULL, '2026-07-11', 0);

-- 营业额严格按统计日期和 trim 后分店编码聚合；边界外、空编码与 ALL 均不得计入。
INSERT INTO [StoreSalesStatistic] ([Date], [BranchCode], [TotalAmount]) VALUES
    ('2026-07-05', N' 1001 ', 1000.00),
    ('2026-06-05', N'1001', 500.00),
    ('2026-05-05', N'2003', 70.00),
    ('2025-07-31', N'1001', 999.00),
    ('2026-08-01', N'1001', 888.00),
    ('2026-07-06', N' ALL ', 777.00),
    ('2026-07-07', N'   ', 666.00);
""";

        await ExecuteNonQueryAsync(connectionString, sql);
    }

    private static ConnectionConfig CreateSqlServerConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        };
    }

    private static string BuildConnectionString(string connectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 60,
        };
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
"""
        );
    }

    private static string QuoteSqlServerName(string name)
    {
        return $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
