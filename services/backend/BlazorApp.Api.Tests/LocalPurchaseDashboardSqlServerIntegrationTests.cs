using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
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

[Trait("Category", "SQL")]
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

            async Task<LocalPurchaseDashboardResponseDto> ReadDashboard(
                LocalPurchaseDashboardStoreScope scope, string[]? keys = null)
            {
                var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard("2026-07", scope, keys == null ? null : "include", keys);
                return LocalPurchaseDashboardComposer.ComposeDashboard(query.Period,
                    await db.Ado.SqlQueryAsync<LocalPurchaseDashboardMonthlyRow>(query.Sql, query.Parameters.ToArray()),
                    await db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierOptionRow>(query.OptionsSql, query.OptionsParameters.ToArray()));
            }

            async Task<LocalPurchaseDashboardStoreSuppliersDto> ReadSuppliers(string storeCode, string[]? keys = null)
            {
                var query = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
                    storeCode, "2026-07", LocalPurchaseDashboardStoreScope.AllStores(), keys == null ? null : "include", keys);
                var rows = await db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierMonthlyRow>(query.Sql, query.Parameters.ToArray());
                return LocalPurchaseDashboardComposer.ComposeStoreSuppliers(query.Period, storeCode, rows,
                    keys == null || keys.Contains("WAREHOUSE_ORDER:false:WAREHOUSE_ORDER"));
            }

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
                dashboardRows,
                await db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierOptionRow>(
                    dashboardQuery.OptionsSql, dashboardQuery.OptionsParameters.ToArray())
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

            Assert.Equal(4, dashboard.SupplierOptions.Count);
            Assert.Contains(dashboard.SupplierOptions, item => item.SourceType == "WAREHOUSE_ORDER");
            Assert.Contains(dashboard.SupplierOptions, item => item.SourceCode == "UNASSIGNED" && item.IsUnassigned);
            Assert.Contains(dashboard.SupplierOptions, item => item.SourceCode == "SUP-X");

            var selectedQuery = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
                "2026-07", LocalPurchaseDashboardStoreScope.AllStores(), "include",
                new[] { "WAREHOUSE_ORDER:false:WAREHOUSE_ORDER", "LOCAL_SUPPLIER:false:SUP-A" }
            );
            var selectedRows = await db.Ado.SqlQueryAsync<LocalPurchaseDashboardMonthlyRow>(
                selectedQuery.Sql, selectedQuery.Parameters.ToArray()
            );
            var selected = LocalPurchaseDashboardComposer.ComposeDashboard(selectedQuery.Period, selectedRows);
            Assert.Equal(310m, selected.TotalAmount);
            Assert.Equal(1570m, selected.Stores.Sum(store => store.Months.Sum(month => month.SalesAmount)));

            var emptyQuery = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
                "2026-07", LocalPurchaseDashboardStoreScope.AllStores(), "include", Array.Empty<string>()
            );
            var emptyRows = await db.Ado.SqlQueryAsync<LocalPurchaseDashboardMonthlyRow>(
                emptyQuery.Sql, emptyQuery.Parameters.ToArray()
            );
            var empty = LocalPurchaseDashboardComposer.ComposeDashboard(emptyQuery.Period, emptyRows);
            Assert.Equal(0m, empty.TotalAmount);
            Assert.Equal(1570m, empty.Stores.Sum(store => store.Months.Sum(month => month.SalesAmount)));

            var inactiveQuery = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
                "3003", "2026-07", LocalPurchaseDashboardStoreScope.AllStores()
            );
            var inactiveRows = await db.Ado.SqlQueryAsync<LocalPurchaseDashboardSupplierMonthlyRow>(
                inactiveQuery.Sql, inactiveQuery.Parameters.ToArray()
            );
            var inactive = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
                inactiveQuery.Period, "3003", inactiveRows
            );
            Assert.Empty(inactive.Suppliers);
            Assert.DoesNotContain(dashboard.Stores, item => item.StoreCode == "3003" || item.StoreCode == "DELETED");

            // 真正执行两条金额查询及选项查询，验证过滤后仍可反选且主表与抽屉对账。
            var scenarios = new[]
            {
                (Keys: Array.Empty<string>(), Warehouse: 0m, Local: 0m, StoreLocal: 0m, Sources: 0),
                (Keys: new[] { "LOCAL_SUPPLIER:false:SUP-A" }, Warehouse: 0m, Local: 110m, StoreLocal: 100m, Sources: 1),
                (Keys: new[] { "WAREHOUSE_ORDER:false:WAREHOUSE_ORDER" }, Warehouse: 200m, Local: 0m, StoreLocal: 0m, Sources: 1),
                (Keys: new[] { "LOCAL_SUPPLIER:true:UNASSIGNED" }, Warehouse: 0m, Local: 30m, StoreLocal: 30m, Sources: 1),
                (Keys: new[] { "LOCAL_SUPPLIER:false:SUP-A", "LOCAL_SUPPLIER:false:SUP-X" }, Warehouse: 0m, Local: 130m, StoreLocal: 120m, Sources: 2),
            };
            foreach (var scenario in scenarios)
            {
                var filtered = await ReadDashboard(LocalPurchaseDashboardStoreScope.AllStores(), scenario.Keys);
                var drawer = await ReadSuppliers("1001", scenario.Keys);
                Assert.Equal(scenario.Warehouse, filtered.WarehouseTotal);
                Assert.Equal(scenario.Local, filtered.LocalSupplierTotal);
                Assert.Equal(scenario.Sources, drawer.Suppliers.Count);
                Assert.Equal(scenario.StoreLocal, drawer.LocalSupplierTotal);
                Assert.Equal(filtered.Stores.Single(s => s.StoreCode == "1001").TotalAmount, drawer.TotalAmount);
                Assert.Equal(1570m, filtered.Stores.Sum(s => s.Months.Sum(m => m.SalesAmount)));
                Assert.Equal(4, filtered.SupplierOptions.Count);
            }

            var restricted = await ReadDashboard(LocalPurchaseDashboardStoreScope.Restricted(new[] { "1001", "3003" }));
            Assert.Equal("1001", Assert.Single(restricted.Stores).StoreCode);
            Assert.Equal(350m, restricted.TotalAmount);
            var noScope = await ReadDashboard(LocalPurchaseDashboardStoreScope.Restricted(Array.Empty<string>()));
            Assert.Empty(noScope.Stores);
            Assert.Empty(noScope.SupplierOptions);
            var inactiveScope = await ReadDashboard(LocalPurchaseDashboardStoreScope.Restricted(new[] { "3003" }));
            Assert.Empty(inactiveScope.Stores);
            Assert.Empty(inactiveScope.SupplierOptions);
            Assert.Empty((await ReadSuppliers("UNKNOWN")).Suppliers);
            Assert.Single((await ReadSuppliers("1002")).Suppliers);

            // 真编码与虚拟来源同名、含冒号时仍精确筛选，不把业务编码拼成 SQL。
            await ExecuteNonQueryAsync(databaseConnectionString, """
INSERT INTO [StoreLocalSupplierInvoice] ([StoreCode], [SupplierCode], [TotalAmount], [InboundDate], [IsDeleted]) VALUES
    (N'1001', N'UNASSIGNED', 7, '2026-07-12', 0),
    (N'1001', N'WAREHOUSE_ORDER', 8, '2026-07-12', 0),
    (N'1001', N'ACME:WEST', 9, '2026-07-12', 0);
""");
            foreach (var (code, amount) in new[] { ("UNASSIGNED", 7m), ("WAREHOUSE_ORDER", 8m), ("ACME:WEST", 9m) })
            {
                var keys = new[] { "LOCAL_SUPPLIER:false:" + code };
                var filtered = await ReadDashboard(LocalPurchaseDashboardStoreScope.AllStores(), keys);
                var drawer = await ReadSuppliers("1001", keys);
                Assert.Equal(amount, filtered.TotalAmount);
                var supplier = Assert.Single(drawer.Suppliers);
                Assert.Equal(code, supplier.SupplierCode);
                Assert.False(supplier.IsUnassigned);
                Assert.Equal(amount, drawer.TotalAmount);
            }
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
    [IsDeleted] bit NULL,
    [IsActive] bit NULL
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

INSERT INTO [Store] ([StoreCode], [StoreName], [IsDeleted], [IsActive]) VALUES
    (N'1001', N'Brisbane', 0, 1),
    (N'1002', N'Empty Store', 0, 1),
    (N'9999', N'Active Other Store', 0, 1),
    (N'2003', N'Active Sales Store', 0, 1),
    (N'3003', N'Inactive Store', 0, 0),
    (N'DELETED', N'Deleted Store', 1, 1);

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

INSERT INTO [WareHouseOrder]
    ([OrderGUID], [StoreCode], [OutboundDate], [OrderDate], [CreatedAt], [IsDeleted], [FlowStatus]) VALUES
    (N'WH-INACTIVE', N'3003', '2026-07-12', NULL, NULL, 0, 1);

INSERT INTO [WareHouseOrderDetails] ([OrderGUID], [AllocQuantity], [ImportPrice], [IsDeleted]) VALUES
    (N'WH-OUTBOUND', 2, 50, 0),
    (N'WH-ORDER', 3, 20, 0),
    (N'WH-CREATED', 4, 10, 0),
    (N'WH-DRAFT', 9, 99, 0),
    (N'WH-DELETED', 9, 99, 0),
    (N'WH-DELETED-DETAIL', 9, 99, 1);
INSERT INTO [WareHouseOrderDetails] ([OrderGUID], [AllocQuantity], [ImportPrice], [IsDeleted]) VALUES
    (N'WH-INACTIVE', 7, 10, 0);

-- 本地供应商 TotalAmount 已是不含 GST 金额，按原值计入，并覆盖三层日期回退与供应商名称回退。
INSERT INTO [StoreLocalSupplierInvoice]
    ([StoreCode], [SupplierCode], [TotalAmount], [InboundDate], [OrderDate], [CreatedAt], [IsDeleted]) VALUES
    (N'1001', N'SUP-A', 55.55, '2026-07-08', '2024-01-01', '2024-01-01', 0),
    (N'1001', N'SUP-A', 44.45, NULL, '2026-06-08', '2024-01-01', 0),
    (N'1001', NULL, 30.00, NULL, NULL, '2026-05-08', 0),
    (N'1001', N'SUP-X', 20.00, '2026-07-09', NULL, '2026-07-09', 0),
    (N'1001', N'SUP-A', 999.00, '2026-07-10', NULL, '2026-07-10', 1),
    (N'9999', N'SUP-A', 10.00, '2026-07-11', NULL, '2026-07-11', 0);
INSERT INTO [StoreLocalSupplierInvoice]
    ([StoreCode], [SupplierCode], [TotalAmount], [InboundDate], [OrderDate], [CreatedAt], [IsDeleted]) VALUES
    (N'3003', N'SUP-A', 11.00, '2026-07-11', NULL, NULL, 0);

-- 营业额严格按统计日期和 trim 后分店编码聚合；边界外、空编码与 ALL 均不得计入。
INSERT INTO [StoreSalesStatistic] ([Date], [BranchCode], [TotalAmount]) VALUES
    ('2026-07-05', N' 1001 ', 1000.00),
    ('2026-06-05', N'1001', 500.00),
    ('2026-05-05', N'2003', 70.00),
    ('2025-07-31', N'1001', 999.00),
    ('2026-08-01', N'1001', 888.00),
    ('2026-07-06', N' ALL ', 777.00),
    ('2026-07-07', N'   ', 666.00);
INSERT INTO [StoreSalesStatistic] ([Date], [BranchCode], [TotalAmount]) VALUES
    ('2026-07-07', N'3003', 13.00);
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
