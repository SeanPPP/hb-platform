using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BlazorApp.Api.Tests;

public class LocalPurchaseDashboardTests
{
    [Fact]
    public void ResolvePeriod_ShouldReturnRollingTwelveCalendarMonths()
    {
        var period = LocalPurchaseDashboardSqlBuilder.ResolvePeriod("2026-07");

        Assert.Equal(new DateTime(2025, 8, 1), period.StartDate);
        Assert.Equal(new DateTime(2026, 8, 1), period.EndDateExclusive);
        Assert.Equal(
            new[]
            {
                "2025-08", "2025-09", "2025-10", "2025-11", "2025-12", "2026-01",
                "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07",
            },
            period.Months
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-7")]
    [InlineData("2026-13")]
    [InlineData("not-a-month")]
    public void ResolvePeriod_ShouldRejectInvalidEndMonth(string endMonth)
    {
        Assert.Throws<ArgumentException>(() =>
            LocalPurchaseDashboardSqlBuilder.ResolvePeriod(endMonth)
        );
    }

    [Fact]
    public void BuildDashboard_ShouldUseExGstAmountRulesDateFallbackAndStoreScope()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07",
            LocalPurchaseDashboardStoreScope.Restricted(new[] { "1001", "1002" })
        );

        Assert.Contains(
            "SUM(COALESCE(d.AllocQuantity, 0) * COALESCE(d.ImportPrice, 0))",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "SUM(COALESCE(h.TotalAmount, 0))",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("SalesMonthly AS", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM [StoreSalesStatistic] sales", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "sales.[Date] >= @StartDate",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "sales.[Date] < @EndDateExclusive",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "UPPER(LTRIM(RTRIM(sales.BranchCode))) <> N'ALL'",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "NULLIF(LTRIM(RTRIM(sales.BranchCode)), N'') IS NOT NULL",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "AND LTRIM(RTRIM(sales.BranchCode)) IN (@StoreCode0, @StoreCode1)",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("SELECT * FROM SalesMonthly", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CAST(SUM(source.SalesAmount) AS decimal(18, 2)) AS SalesAmount",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("1.1", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShippingFee", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "COALESCE(h.OutboundDate, h.OrderDate, h.CreatedAt)",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt)",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            "COALESCE(h.FlowStatus, -1) <> 0",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains("COALESCE(h.IsDeleted, 0) = 0", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(d.IsDeleted, 0) = 0", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN (@StoreCode0, @StoreCode1)", query.Sql, StringComparison.Ordinal);
        Assert.Contains(query.Parameters, item => item.ParameterName == "@StartDate");
        Assert.Contains(query.Parameters, item => item.ParameterName == "@EndDateExclusive");
        Assert.False(LocalPurchaseDashboardSqlBuilder.ContainsWriteKeyword(query.Sql));
        AssertUsesSargableDateFallback(query.Sql, "OutboundDate");
        AssertUsesSargableDateFallback(query.Sql, "InboundDate");
    }

    [Fact]
    public void BuildStoreSuppliers_ShouldParameterizeStoreAndPreserveUnassignedSupplier()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
            "1001",
            "2026-07",
            LocalPurchaseDashboardStoreScope.Restricted(new[] { "1001", "1002" })
        );

        Assert.Contains("WAREHOUSE_ORDER", query.Sql, StringComparison.Ordinal);
        Assert.Contains("UNASSIGNED", query.Sql, StringComparison.Ordinal);
        Assert.Contains("未匹配供应商", query.Sql, StringComparison.Ordinal);
        Assert.Contains("IsUnassigned", query.Sql, StringComparison.Ordinal);
        Assert.Contains(
            "SUM(COALESCE(h.TotalAmount, 0))",
            query.Sql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("1.1", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            query.Parameters,
            item => item.ParameterName == "@RequestedStoreCode" && (string)item.Value == "1001"
        );
        Assert.False(LocalPurchaseDashboardSqlBuilder.ContainsWriteKeyword(query.Sql));
        AssertUsesSargableDateFallback(query.Sql, "OutboundDate");
        AssertUsesSargableDateFallback(query.Sql, "InboundDate");
    }

    [Fact]
    public void BuildDashboard_ShouldPushSupplierFilterDownAndKeepSalesUnfiltered()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07",
            LocalPurchaseDashboardStoreScope.Restricted(new[] { "1001" }),
            supplierFilterMode: "include",
            supplierKeys: new[] { "LOCAL_SUPPLIER:false:ACME", "WAREHOUSE_ORDER:false:WAREHOUSE_ORDER" }
        );

        Assert.Contains("OPENJSON(@SupplierKeysJson)", query.Sql, StringComparison.Ordinal);
        Assert.Contains("N'LOCAL_SUPPLIER' + N':'", query.Sql, StringComparison.Ordinal);
        Assert.Contains("N'WAREHOUSE_ORDER' + N':'", query.Sql, StringComparison.Ordinal);
        Assert.Contains("OPENJSON(@SupplierKeysJson)", LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07", LocalPurchaseDashboardStoreScope.AllStores(), "include", Array.Empty<string>()).Sql);
        Assert.Contains("SELECT * FROM SalesMonthly", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOCAL_SUPPLIER:false:ACME", (string)query.Parameters.Single(p => p.ParameterName == "@SupplierKeysJson").Value);
    }

    [Fact]
    public void BuildDashboard_ShouldUseExcludeModeWithoutConcatenatingKeys()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07", LocalPurchaseDashboardStoreScope.AllStores(), "exclude",
            new[] { "LOCAL_SUPPLIER:false:ACME" });

        Assert.Contains("NOT EXISTS", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ACME", query.Sql, StringComparison.Ordinal);
        Assert.Contains("LOCAL_SUPPLIER:false:ACME", (string)query.Parameters.Single(p => p.ParameterName == "@SupplierKeysJson").Value);
    }

    [Fact]
    public void ValidateSupplierFilter_ShouldRejectInvalidModeSizeLengthAndUnknownKey()
    {
        var options = new[] { new LocalPurchaseDashboardSupplierOptionRow { SourceCode = "ACME", SourceType = "LOCAL_SUPPLIER" } };
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter("other", Array.Empty<string>(), options));
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter("include", Enumerable.Range(0, 2001).Select(i => i.ToString()).ToArray(), options));
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter("include", new[] { new string('x', 257) }, options));
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter("include", new[] { "LOCAL_SUPPLIER:false:MISSING" }, options));
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilter(null, new[] { "LOCAL_SUPPLIER:false:ACME" }, options));
    }

    [Fact]
    public void ValidateSupplierFilterShape_ShouldRejectRawInputBeforeOptionsQuery()
    {
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilterShape("include", Enumerable.Range(0, 2001).Select(i => i.ToString()).ToArray()));
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilterShape("include", new[] { " " + new string('x', 256) + " " }));
        var oversized = Enumerable.Repeat(new string('x', 40), 2000).ToArray();
        Assert.Throws<ArgumentException>(() => LocalPurchaseDashboardSqlBuilder.ValidateSupplierFilterShape("include", oversized));
    }

    [Fact]
    public void BuildStoreSuppliers_ShouldUseSourceIdentityThatPreservesColonCodesAndCollisions()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
            "1001", "2026-07", LocalPurchaseDashboardStoreScope.AllStores(), "include",
            new[] { "LOCAL_SUPPLIER:false:WAREHOUSE_ORDER", "LOCAL_SUPPLIER:false:ACME:WEST", "LOCAL_SUPPLIER:true:UNASSIGNED" }
        );

        Assert.Contains("CASE WHEN NULLIF(LTRIM(RTRIM(h.SupplierCode)), N'') IS NULL THEN N'true' ELSE N'false' END", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OPENJSON(@SupplierKeysJson)", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SPLIT", query.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDashboard_ShouldReturnCompleteSupplierOptionsQueryWithinStoreScope()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07", LocalPurchaseDashboardStoreScope.Restricted(new[] { "1001", "1002" }), "include",
            new[] { "LOCAL_SUPPLIER:false:ACME" }
        );

        Assert.Contains("SELECT SourceCode, SupplierCode, SupplierName, SourceType, IsUnassigned", query.OptionsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activeStore.IsActive", query.OptionsSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@SupplierKey", query.OptionsSql, StringComparison.Ordinal);
        Assert.Contains(query.OptionsParameters, p => p.ParameterName == "@StoreCode0");
    }

    [Theory]
    [InlineData(typeof(ILocalPurchaseDashboardService), nameof(ILocalPurchaseDashboardService.GetDashboardAsync))]
    [InlineData(typeof(ILocalPurchaseDashboardService), nameof(ILocalPurchaseDashboardService.GetStoreSuppliersAsync))]
    [InlineData(typeof(LocalPurchaseDashboardService), nameof(LocalPurchaseDashboardService.GetDashboardAsync))]
    [InlineData(typeof(LocalPurchaseDashboardService), nameof(LocalPurchaseDashboardService.GetStoreSuppliersAsync))]
    [InlineData(typeof(ReactLocalPurchaseDashboardController), nameof(ReactLocalPurchaseDashboardController.GetDashboard))]
    [InlineData(typeof(ReactLocalPurchaseDashboardController), nameof(ReactLocalPurchaseDashboardController.GetStoreSuppliers))]
    public void DashboardQueryPipeline_ShouldAcceptRequestCancellationToken(
        Type declaringType,
        string methodName
    )
    {
        var method = declaringType.GetMethod(methodName);

        Assert.NotNull(method);
        var cancellationToken = Assert.Single(
            method!.GetParameters(),
            parameter => parameter.ParameterType == typeof(CancellationToken)
        );
        Assert.Equal("cancellationToken", cancellationToken.Name);
    }

    [Fact]
    public void BuildStoreSuppliers_ShouldBlockStoreOutsideServiceScope()
    {
        var query = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
            "9999",
            "2026-07",
            LocalPurchaseDashboardStoreScope.Restricted(new[] { "1001" })
        );

        Assert.Contains("AND 1 = 0", query.Sql, StringComparison.Ordinal);
        Assert.False(query.StoreAllowed);
    }

    [Fact]
    public void SqlBuilder_ShouldRejectAmbiguousNullStoreScope()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LocalPurchaseDashboardSqlBuilder.BuildDashboard("2026-07", null!)
        );
        Assert.Throws<ArgumentNullException>(() =>
            LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers("1001", "2026-07", null!)
        );
    }

    [Fact]
    public void SqlBuilder_ShouldDistinguishAllStoresFromEmptyRestrictedScope()
    {
        var allStores = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07",
            LocalPurchaseDashboardStoreScope.AllStores()
        );
        var noStores = LocalPurchaseDashboardSqlBuilder.BuildDashboard(
            "2026-07",
            LocalPurchaseDashboardStoreScope.Restricted(Array.Empty<string>())
        );
        var allStoreSupplier = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
            "9999",
            "2026-07",
            LocalPurchaseDashboardStoreScope.AllStores()
        );
        var noStoreSupplier = LocalPurchaseDashboardSqlBuilder.BuildStoreSuppliers(
            "9999",
            "2026-07",
            LocalPurchaseDashboardStoreScope.Restricted(Array.Empty<string>())
        );

        Assert.DoesNotContain("@StoreCode0", allStores.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("AND 1 = 0", allStores.Sql, StringComparison.Ordinal);
        Assert.Contains("AND 1 = 0", noStores.Sql, StringComparison.Ordinal);
        Assert.True(allStoreSupplier.StoreAllowed);
        Assert.DoesNotContain("AND 1 = 0", allStoreSupplier.Sql, StringComparison.Ordinal);
        Assert.False(noStoreSupplier.StoreAllowed);
        Assert.Contains("AND 1 = 0", noStoreSupplier.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeDashboard_ShouldReconcileStoreAndSummaryTotalsWithoutGstConversion()
    {
        var period = LocalPurchaseDashboardSqlBuilder.ResolvePeriod("2026-07");
        var rows = new List<LocalPurchaseDashboardMonthlyRow>
        {
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                Month = "2026-06",
                WarehouseAmount = 110m,
                LocalSupplierAmount = 100m,
                SalesAmount = 500m,
            },
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                Month = "2026-07",
                WarehouseAmount = 20m,
                LocalSupplierAmount = 55.55m,
                SalesAmount = 600m,
            },
            new()
            {
                StoreCode = "9999",
                StoreName = "9999",
                Month = "2026-07",
                LocalSupplierAmount = 44.45m,
                SalesAmount = 40m,
            },
            new() { StoreCode = "1002", StoreName = "Empty Store" },
            new()
            {
                StoreCode = "2003",
                StoreName = "Sales Only Store",
                Month = "2026-07",
                SalesAmount = 70m,
            },
        };

        var result = LocalPurchaseDashboardComposer.ComposeDashboard(period, rows);

        Assert.Equal(130m, result.WarehouseTotal);
        Assert.Equal(200m, result.LocalSupplierTotal);
        Assert.Equal(330m, result.TotalAmount);
        Assert.Equal(4, result.Stores.Count);
        Assert.Equal(result.TotalAmount, result.Stores.Sum(store => store.TotalAmount));
        Assert.All(result.Stores, store => Assert.Equal(12, store.Months.Count));
        Assert.Equal(0m, result.Stores.Single(store => store.StoreCode == "1002").TotalAmount);
        var salesOnlyStore = result.Stores.Single(store => store.StoreCode == "2003");
        Assert.Equal(0m, salesOnlyStore.TotalAmount);
        Assert.Equal(
            70m,
            salesOnlyStore.Months.Single(month => month.Month == "2026-07").SalesAmount
        );
        var purchaseStore = result.Stores.Single(store => store.StoreCode == "1001");
        Assert.Equal(1100m, purchaseStore.Months.Sum(month => month.SalesAmount));
        Assert.Equal(285.55m, purchaseStore.TotalAmount);
    }

    [Fact]
    public void ComposeStoreSuppliers_ShouldPutWarehouseFirstSortSuppliersAndReconcile()
    {
        var period = LocalPurchaseDashboardSqlBuilder.ResolvePeriod("2026-07");
        var rows = new List<LocalPurchaseDashboardSupplierMonthlyRow>
        {
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "SUP-B",
                SupplierCode = "SUP-B",
                SupplierName = "Supplier B",
                SourceType = "LOCAL_SUPPLIER",
                Month = "2026-07",
                Amount = 20m,
            },
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "WAREHOUSE_ORDER",
                SupplierName = "仓库订单",
                SourceType = "WAREHOUSE_ORDER",
                Month = "2026-07",
                Amount = 80m,
            },
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "UNASSIGNED",
                SupplierCode = "UNASSIGNED",
                SupplierName = "未匹配供应商",
                SourceType = "LOCAL_SUPPLIER",
                IsUnassigned = true,
                Month = "2026-06",
                Amount = 30m,
            },
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "WAREHOUSE_ORDER",
                SupplierCode = "WAREHOUSE_ORDER",
                SupplierName = "同名本地供应商",
                SourceType = "LOCAL_SUPPLIER",
                Month = "2026-07",
                Amount = 5m,
            },
        };

        var result = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
            period,
            "1001",
            rows
        );

        Assert.Equal("WAREHOUSE_ORDER", result.Suppliers[0].SourceCode);
        Assert.Equal("WAREHOUSE_ORDER", result.Suppliers[0].SourceType);
        Assert.Equal("UNASSIGNED", result.Suppliers[1].SourceCode);
        Assert.Equal(80m, result.WarehouseTotal);
        Assert.Equal(55m, result.LocalSupplierTotal);
        Assert.Equal(135m, result.TotalAmount);
        Assert.Equal(
            2,
            result.Suppliers.Count(item => item.SourceCode == "WAREHOUSE_ORDER")
        );
        Assert.Equal(result.TotalAmount, result.Suppliers.Sum(item => item.TotalAmount));
        Assert.All(result.Suppliers, item => Assert.Equal(12, item.Months.Count));
    }

    [Fact]
    public void ComposeStoreSuppliers_ShouldKeepRealUnassignedCodeSeparateFromMissingSupplier()
    {
        var period = LocalPurchaseDashboardSqlBuilder.ResolvePeriod("2026-07");
        var rows = new List<LocalPurchaseDashboardSupplierMonthlyRow>
        {
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "UNASSIGNED",
                SupplierCode = null,
                SupplierName = "未匹配供应商",
                SourceType = "LOCAL_SUPPLIER",
                IsUnassigned = true,
                Month = "2026-07",
                Amount = 30m,
            },
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "UNASSIGNED",
                SupplierCode = "UNASSIGNED",
                SupplierName = "真实 UNASSIGNED 供应商",
                SourceType = "LOCAL_SUPPLIER",
                Month = "2026-07",
                Amount = 7m,
            },
        };

        var result = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
            period,
            "1001",
            rows
        );

        var duplicatedCodes = result.Suppliers
            .Where(item => item.SourceCode == "UNASSIGNED")
            .ToList();
        Assert.Equal(2, duplicatedCodes.Count);
        Assert.Contains(
            duplicatedCodes,
            item => item.IsUnassigned && item.SupplierName == "未匹配供应商" && item.TotalAmount == 30m
        );
        Assert.Contains(
            duplicatedCodes,
            item => !item.IsUnassigned && item.SupplierName == "真实 UNASSIGNED 供应商" && item.TotalAmount == 7m
        );
        Assert.Equal(37m, result.LocalSupplierTotal);
    }

    [Fact]
    public void ComposeStoreSuppliers_ShouldIncludeZeroWarehouseOnlyForEligibleSelectedStore()
    {
        var period = LocalPurchaseDashboardSqlBuilder.ResolvePeriod("2026-07");
        var rows = new List<LocalPurchaseDashboardSupplierMonthlyRow>
        {
            new()
            {
                StoreCode = "1001",
                StoreName = "Brisbane",
                SourceCode = "SUP-A",
                SupplierCode = "SUP-A",
                SupplierName = "Supplier A",
                SourceType = "LOCAL_SUPPLIER",
                Month = "2026-07",
                Amount = 25m,
            },
        };

        var result = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
            period,
            "1001",
            rows
        );

        var warehouse = Assert.Single(
            result.Suppliers,
            item => item.SourceType == "WAREHOUSE_ORDER"
        );
        Assert.Same(warehouse, result.Suppliers[0]);
        Assert.Equal("WAREHOUSE_ORDER", warehouse.SourceCode);
        Assert.Equal(0m, warehouse.TotalAmount);
        Assert.Equal(12, warehouse.Months.Count);
        Assert.All(warehouse.Months, month => Assert.Equal(0m, month.Amount));
        Assert.Equal(25m, result.TotalAmount);

        var emptyResult = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
            period,
            "1002",
            new[] { new LocalPurchaseDashboardSupplierMonthlyRow { StoreCode = "1002", StoreName = "Empty Store" } }
        );
        var emptyWarehouse = Assert.Single(emptyResult.Suppliers);
        Assert.Equal("WAREHOUSE_ORDER", emptyWarehouse.SourceType);
        Assert.Equal(0m, emptyResult.TotalAmount);
        Assert.Equal(12, emptyWarehouse.Months.Count);

        var missingStore = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(
            period, "inactive", Array.Empty<LocalPurchaseDashboardSupplierMonthlyRow>());
        Assert.Empty(missingStore.Suppliers);

        var localOnly = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(period, "1001", rows, includeWarehouse: false);
        Assert.Equal("SUP-A", Assert.Single(localOnly.Suppliers).SourceCode);
        Assert.Equal(25m, localOnly.TotalAmount);
        var none = LocalPurchaseDashboardComposer.ComposeStoreSuppliers(period, "1002",
            new[] { new LocalPurchaseDashboardSupplierMonthlyRow { StoreCode = "1002" } }, includeWarehouse: false);
        Assert.Empty(none.Suppliers);
    }

    [Theory]
    [InlineData(nameof(ReactLocalPurchaseDashboardController.GetDashboard))]
    [InlineData(nameof(ReactLocalPurchaseDashboardController.GetStoreSuppliers))]
    public void DashboardEndpoints_ShouldRequirePurchaseAmountViewPermission(string methodName)
    {
        var method = typeof(ReactLocalPurchaseDashboardController).GetMethod(methodName);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(Permissions.SalesDashboard.PurchaseAmountView, authorize!.Policy);
    }

    [Fact]
    public void DashboardRoleAliases_ShouldGrantAllStoreScopeOnlyToAdminAndWarehouseManager()
    {
        var globalRoleAliases = Permissions.SuperAdminRoleNames
            .Concat(Permissions.WarehouseManagerRoleNames)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in globalRoleAliases)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Role, roleName) },
                "Test"
            );

            Assert.True(
                ReactLocalPurchaseDashboardController.HasFullStoreScope(
                    new ClaimsPrincipal(identity)
                ),
                $"角色别名 {roleName} 应获得全店范围"
            );
        }

        var storeManager = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Role, "StoreManager") },
                "Test"
            )
        );
        Assert.False(ReactLocalPurchaseDashboardController.HasFullStoreScope(storeManager));
    }

    [Fact]
    public void Navigation_ShouldExposeDashboardForPurchaseAmountViewOnly()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("permission", Permissions.SalesDashboard.PurchaseAmountView) },
            "Test"
        );

        var menu = new NavigationService().BuildMenu(new ClaimsPrincipal(identity));

        var parent = Assert.Single(
            menu,
            item => item.Path == "/executive-sales-intelligence"
        );
        var dashboard = Assert.Single(
            parent.Children!,
            item => item.Path == "/executive-sales-intelligence/purchase-amount-dashboard"
        );
        Assert.Equal("menu.purchaseAmountDashboard", dashboard.TitleKey);
        Assert.Equal("DollarOutlined", dashboard.Icon);
        Assert.Equal(Permissions.SalesDashboard.PurchaseAmountView, dashboard.Permission);
    }

    private static void AssertUsesSargableDateFallback(string sql, string actualDateColumn)
    {
        // 范围过滤必须落在原始列上，同时严格保留“实际日期→订单日期→创建日期”的优先级。
        var normalizedSql = Regex.Replace(sql, @"\s+", " ");
        Assert.Contains(
            $"h.{actualDateColumn} >= @StartDate AND h.{actualDateColumn} < @EndDateExclusive",
            normalizedSql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            $"h.{actualDateColumn} IS NULL AND h.OrderDate >= @StartDate AND h.OrderDate < @EndDateExclusive",
            normalizedSql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            $"h.{actualDateColumn} IS NULL AND h.OrderDate IS NULL AND h.CreatedAt >= @StartDate AND h.CreatedAt < @EndDateExclusive",
            normalizedSql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            $"COALESCE(h.{actualDateColumn}, h.OrderDate, h.CreatedAt) >= @StartDate",
            normalizedSql,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            $"COALESCE(h.{actualDateColumn}, h.OrderDate, h.CreatedAt) < @EndDateExclusive",
            normalizedSql,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
