using System.Reflection;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Models;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BrowserExtensionAccessServiceTests
{
    [Fact]
    public async Task WarehouseStaff_RequiresExplicitOrdersCreate_AndBypassesStoreScope()
    {
        var scope = new Mock<ICurrentUserManageableStoreScopeService>(MockBehavior.Strict);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        var service = new BrowserExtensionAccessService(
            CreateAuthorizationService(Permissions.Orders.Create),
            scope.Object,
            userService.Object
        );

        var allowed = await service.CanAccessAsync(
            CreateUser("user-1", "WarehouseStaff"),
            "1004"
        );

        Assert.True(allowed);
        scope.VerifyNoOtherCalls();
        userService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WarehouseStaff_WithoutOrdersCreate_IsDeniedEvenWhenOrderFrontViewExists()
    {
        var service = new BrowserExtensionAccessService(
            CreateAuthorizationService(Permissions.OrderFront.View),
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<IUserService>()
        );

        var allowed = await service.CanAccessAsync(
            CreateUser("user-1", "仓库员工"),
            "1004"
        );

        Assert.False(allowed);
    }

    [Fact]
    public async Task OrderFrontUser_MustHaveSelectedStoreScope()
    {
        var scope = new Mock<ICurrentUserManageableStoreScopeService>(MockBehavior.Strict);
        scope.Setup(item => item.CanAccessStoreCodeAsync(It.IsAny<string>())).ReturnsAsync(false);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(item => item.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new() { StoreCode = "1004", StoreName = "Kawana", IsActive = true },
                    }
                )
            );
        var service = new BrowserExtensionAccessService(
            CreateAuthorizationService(Permissions.OrderFront.View),
            scope.Object,
            userService.Object
        );

        Assert.True(await service.CanAccessAsync(CreateUser("user-1", "Order"), "1004"));
        Assert.False(await service.CanAccessAsync(CreateUser("user-1", "Order"), "1006"));
    }

    [Fact]
    public async Task OrdersCreateAlone_DoesNotGrantOrdinaryUserAccess()
    {
        var service = new BrowserExtensionAccessService(
            CreateAuthorizationService(Permissions.Orders.Create),
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            Mock.Of<IUserService>()
        );

        Assert.False(await service.CanAccessAsync(CreateUser("user-1", "Order"), null));
    }

    [Fact]
    public async Task Admin_CanAccessBaseAndAnySelectedStoreWithoutAdditionalPolicy()
    {
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        var scope = new Mock<ICurrentUserManageableStoreScopeService>(MockBehavior.Strict);
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        var service = new BrowserExtensionAccessService(
            authorization.Object,
            scope.Object,
            userService.Object
        );
        var user = CreateUser("admin-1", "SuperAdmin");

        Assert.True(await service.CanAccessAsync(user));
        Assert.True(await service.CanAccessAsync(user, "9999"));
        authorization.VerifyNoOtherCalls();
        scope.VerifyNoOtherCalls();
        userService.VerifyNoOtherCalls();
    }

    private static ClaimsPrincipal CreateUser(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new("userId", userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static IAuthorizationService CreateAuthorizationService(params string[] allowedPolicies)
    {
        var allowed = allowedPolicies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var service = new Mock<IAuthorizationService>(MockBehavior.Strict);
        service
            .Setup(item =>
                item.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync(
                (ClaimsPrincipal _, object? _, string policy) =>
                    allowed.Contains(policy)
                        ? AuthorizationResult.Success()
                        : AuthorizationResult.Failed()
            );
        return service.Object;
    }
}

public sealed class BrowserExtensionPurchaseCycleTests
{
    [Fact]
    public void Build_GroupsSameDayInvoices_UsesWeightedPrices_AndSplitsSalesAtNextPurchase()
    {
        var today = new DateOnly(2026, 8, 19);
        var purchases = new[]
        {
            Purchase(new DateOnly(2026, 8, 1), "INV-1", 4m, 2m, "P1"),
            Purchase(new DateOnly(2026, 8, 1), "INV-2", 6m, 3m, "P1"),
            Purchase(new DateOnly(2026, 8, 10), "INV-3", 5m, 4m, "P1"),
        };
        var sales = new[]
        {
            Sale(new DateOnly(2026, 8, 1), 2m, 6m, "P1"),
            Sale(new DateOnly(2026, 8, 9), 3m, 12m, "P1"),
            Sale(new DateOnly(2026, 8, 10), 4m, 20m, "P1"),
            Sale(today, 1m, 7m, "P1"),
        };

        var cycles = BrowserExtensionPurchaseCycleCalculator.Build(purchases, sales, today);

        Assert.Equal(2, cycles.Count);
        var latest = cycles[0];
        Assert.Equal(new DateOnly(2026, 8, 10), latest.PurchaseDate);
        Assert.Equal(today, latest.SalesEndDate);
        Assert.Equal(5m, latest.SalesQuantity);
        Assert.Equal(5.4m, latest.AverageSalePrice);

        var previous = cycles[1];
        Assert.Equal(new[] { "INV-1", "INV-2" }, previous.InvoiceNumbers);
        Assert.Equal(10m, previous.PurchaseQuantity);
        Assert.Equal(2.6m, previous.AveragePurchasePrice);
        Assert.Equal(new DateOnly(2026, 8, 9), previous.SalesEndDate);
        Assert.Equal(5m, previous.SalesQuantity);
    }

    [Fact]
    public void Build_ReturnsAtMostSixEventsWithinPreviousTwelveMonths()
    {
        var today = new DateOnly(2026, 8, 19);
        var purchases = Enumerable.Range(0, 8)
            .Select(index =>
                Purchase(today.AddMonths(-index * 2), $"INV-{index}", 1m, 1m, "P1")
            )
            .ToList();

        var cycles = BrowserExtensionPurchaseCycleCalculator.Build(purchases, [], today);

        Assert.Equal(6, cycles.Count);
        Assert.All(cycles, cycle => Assert.True(cycle.PurchaseDate >= today.AddMonths(-12)));
        Assert.Equal(today, cycles[0].PurchaseDate);
    }

    private static BrowserExtensionPurchaseLine Purchase(
        DateOnly date,
        string invoiceNumber,
        decimal quantity,
        decimal price,
        string productCode
    ) =>
        new()
        {
            PurchaseDate = date,
            InvoiceNumber = invoiceNumber,
            Quantity = quantity,
            PurchasePrice = price,
            Amount = quantity * price,
            ProductCode = productCode,
        };

    private static BrowserExtensionSalesLine Sale(
        DateOnly date,
        decimal quantity,
        decimal amount,
        string productCode
    ) =>
        new()
        {
            Date = date,
            Quantity = quantity,
            Amount = amount,
            ProductCode = productCode,
        };
}

public sealed class BrowserExtensionSqlBuilderTests
{
    [Fact]
    public void SummaryQuery_IsReadOnlyParameterized_AndUsesExpectedPurchaseDateFallback()
    {
        var query = BrowserExtensionPurchaseCycleSqlBuilder.BuildSummary(
            "1004",
            "DATS",
            new[] { "ABC-1", "X'; DROP TABLE Product;--" },
            new DateOnly(2026, 8, 19)
        );

        Assert.Contains("COALESCE(h.InboundDate, h.OrderDate, h.CreatedAt)", query.Sql);
        Assert.DoesNotContain("@Cutoff", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(d.ItemNumber, p.ItemNumber)", query.Sql);
        Assert.Contains("COALESCE(h.IsDeleted, 0) = 0", query.Sql);
        Assert.Contains("COALESCE(d.IsDeleted, 0) = 0", query.Sql);
        Assert.DoesNotContain("DROP TABLE", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(query.Parameters, parameter => Equals(parameter.Value, "ABC-1"));
        Assert.Contains("s.SupplierCode = @SupplierCode", query.Sql);
        Assert.Contains(
            query.Parameters,
            parameter => Equals(parameter.Value, "X'; DROP TABLE PRODUCT;--")
        );
        Assert.False(BrowserExtensionPurchaseCycleSqlBuilder.ContainsWriteKeyword(query.Sql));
    }

    [Fact]
    public void SalesQuery_FiltersBySupplierStoreAndProductCodes()
    {
        var query = BrowserExtensionPurchaseCycleSqlBuilder.BuildSales(
            "1004",
            "DATS",
            new[] { "P-1" },
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 19)
        );

        Assert.Contains("s.BranchCode = @StoreCode", query.Sql);
        Assert.Contains("s.SupplierCode = @SupplierCode", query.Sql);
        Assert.Contains(query.Parameters, parameter => Equals(parameter.Value, "DATS"));
        Assert.False(BrowserExtensionPurchaseCycleSqlBuilder.ContainsWriteKeyword(query.Sql));
    }

    [Fact]
    public void SalesQuery_RejectsMoreThanMaximumProductCodesInsteadOfSilentlyTruncating()
    {
        var productCodes = Enumerable.Range(0, 101).Select(index => $"P-{index}");

        Assert.Throws<ArgumentException>(() =>
            BrowserExtensionPurchaseCycleSqlBuilder.BuildSales(
                "1004",
                "DATS",
                productCodes,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 19)
            )
        );
    }

    [Fact]
    public void NormalizeItemNumbers_DeduplicatesAndLimitsBatchSize()
    {
        var normalized = BrowserExtensionPurchaseCycleSqlBuilder.NormalizeItemNumbers(
            new[] { " abc ", "ABC", "xyz" }
        );

        Assert.Equal(new[] { "ABC", "XYZ" }, normalized);
        Assert.Throws<ArgumentException>(() =>
            BrowserExtensionPurchaseCycleSqlBuilder.NormalizeItemNumbers(
                Enumerable.Range(0, 101).Select(index => $"ITEM-{index}")
            )
        );
    }
}

public sealed class BrowserExtensionControllerContractTests
{
    [Fact]
    public void Controller_UsesAuthenticatedBrowserExtensionRoute()
    {
        var type = typeof(ReactBrowserExtensionController);
        var route = type.GetCustomAttribute<RouteAttribute>();

        Assert.Equal("api/react/v1/browser-extension", route?.Template);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(type.GetMethod(nameof(ReactBrowserExtensionController.GetRelease)));
        Assert.NotNull(type.GetMethod(nameof(ReactBrowserExtensionController.GetSupplierProfiles)));
        Assert.NotNull(type.GetMethod(nameof(ReactBrowserExtensionController.GetProductSummaries)));
        Assert.NotNull(type.GetMethod(nameof(ReactBrowserExtensionController.GetPurchaseCycles)));
    }
}

public sealed class BrowserExtensionProfileCatalogTests
{
    [Fact]
    public void Profiles_CanDisableBuiltInDatsWithoutPublishingExtension()
    {
        var result = BrowserExtensionProfileCatalog.BuildProfiles(
            new BrowserExtensionOptions
            {
                UseBuiltInDatsProfile = false,
                SupplierProfiles = new List<BrowserExtensionSupplierProfileOptions>(),
            }
        );

        Assert.Empty(result.Profiles);
    }

    [Fact]
    public void Profiles_OnlyReturnEnabledDeclarativeHttpsConfiguration()
    {
        var options = new BrowserExtensionOptions
        {
            SupplierProfiles = new List<BrowserExtensionSupplierProfileOptions>
            {
                BrowserExtensionSupplierProfileOptions.CreateDatsDefault(),
                new()
                {
                    SupplierCode = "UNSAFE",
                    DisplayName = "Unsafe",
                    Origins = new List<string> { "javascript:alert(1)" },
                    ListPagePatterns = new List<string> { "https://example.com/*" },
                    CardSelector = ".product",
                    ItemNumberSource = "attribute",
                    ItemNumberAttribute = "data-code",
                    MountSelector = ".code",
                },
                new()
                {
                    SupplierCode = "OFF",
                    Enabled = false,
                    Origins = new List<string> { "https://example.com/*" },
                    ListPagePatterns = new List<string> { "https://example.com/*" },
                    CardSelector = ".product",
                    ItemNumberSource = "text",
                    MountSelector = ".code",
                },
            },
        };

        var result = BrowserExtensionProfileCatalog.BuildProfiles(options);

        var profile = Assert.Single(result.Profiles);
        Assert.Equal("DATS", profile.SupplierCode);
        Assert.Equal(new[] { "trim", "uppercase" }, profile.ItemNumber.Transforms);
    }

    [Fact]
    public void Release_StripsNonHttpsStoreLinksAndInvalidVersions()
    {
        var release = BrowserExtensionProfileCatalog.BuildRelease(
            new BrowserExtensionOptions
            {
                LatestVersion = "not-a-version",
                MinimumVersion = "1.0.0",
                ChromeStoreUrl = "javascript:alert(1)",
                EdgeStoreUrl = "https://microsoftedge.microsoft.com/addons/detail/example",
            }
        );

        Assert.Equal("1.0.0", release.LatestVersion);
        Assert.Empty(release.ChromeStoreUrl);
        Assert.StartsWith("https://", release.EdgeStoreUrl, StringComparison.Ordinal);
    }
}
