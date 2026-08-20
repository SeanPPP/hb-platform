using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BrowserExtensionRankingTests
{
    [Fact]
    public void Controller_ExposesStoreAndSupplierRankingRoutes()
    {
        var type = typeof(ReactBrowserExtensionController);

        Assert.NotNull(type.GetMethod(nameof(ReactBrowserExtensionController.GetEnabledStores)));
        Assert.NotNull(type.GetMethod(nameof(ReactBrowserExtensionController.GetSupplierTopSales)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(100, 10)]
    public void CalculateTopItemCount_UsesCeilingTenPercent(int total, int expected)
    {
        Assert.Equal(expected, BrowserExtensionRankingLogic.CalculateTopItemCount(total));
    }

    [Fact]
    public void RankTopDecile_SortsBySalesThenProductCode()
    {
        var rows = Enumerable.Range(1, 11)
            .Select(index => new BrowserExtensionSupplierSalesAggregate
            {
                ProductCode = $"P-{index:00}",
                SalesQuantity = index == 10 || index == 11 ? 50m : index,
            })
            .ToList();

        var ranked = BrowserExtensionRankingLogic.RankTopDecile(rows);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(new[] { "P-10", "P-11" }, ranked.Select(item => item.ProductCode));
        Assert.Equal(new[] { 1, 2 }, ranked.Select(item => item.Rank));
    }

    [Fact]
    public void RankTopDecile_ComputesAverageSellingPriceFromNetSalesAndQuantity()
    {
        var rows = Enumerable.Range(1, 11)
            .Select(index => new BrowserExtensionSupplierSalesAggregate
            {
                ProductCode = $"P-{index:00}",
                SalesQuantity = index == 10 || index == 11 ? 50m : index,
                SalesAmount = index == 10 ? 100m : index == 11 ? 250m : index * 10m,
            })
            .ToList();

        var ranked = BrowserExtensionRankingLogic.RankTopDecile(rows);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(2m, ranked[0].AverageSellingPrice);
        Assert.Equal(5m, ranked[1].AverageSellingPrice);
    }

    [Fact]
    public void CalculateAverageSellingPrice_ZeroQuantityReturnsNull()
    {
        Assert.Null(BrowserExtensionRankingLogic.CalculateAverageSellingPrice(100m, 0m));
    }

    [Fact]
    public void CalculateAverageSellingPrice_DividesNetSalesByQuantity()
    {
        Assert.Equal(5m, BrowserExtensionRankingLogic.CalculateAverageSellingPrice(100m, 20m));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(90)]
    public void NormalizeDays_AcceptsSupportedWindow(int days)
    {
        Assert.Equal(days, BrowserExtensionRankingLogic.NormalizeDays(days));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void NormalizeDays_RejectsOutOfRange(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BrowserExtensionRankingLogic.NormalizeDays(days)
        );
    }

    [Fact]
    public void ResolveStartDate_UsesInclusiveSixtyDayWindow()
    {
        var today = new DateOnly(2026, 8, 20);

        Assert.Equal(new DateOnly(2026, 6, 22), BrowserExtensionRankingLogic.ResolveStartDate(today, 60));
    }
}

public sealed class BrowserExtensionStoreSelectionTests
{
    [Fact]
    public void NormalizeRelatedStoreCodes_OnlyKeepsActiveRelatedStores()
    {
        var result = BrowserExtensionStoreSelection.NormalizeRelatedStoreCodes(
            new[]
            {
                new UserStoreDto { StoreCode = " 1014 ", IsActive = true },
                new UserStoreDto { StoreCode = "1014", IsActive = true },
                new UserStoreDto { StoreCode = "1013", IsActive = false },
                new UserStoreDto { StoreCode = "", IsActive = true },
            }
        );

        Assert.Equal(new[] { "1014" }, result);
    }

    [Fact]
    public async Task GetRelatedStoreCodesAsync_UsesCurrentUsersActiveStores()
    {
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        userService
            .Setup(service => service.GetUserStoresAsync("user-1"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new() { StoreCode = "1014", IsActive = true },
                        new() { StoreCode = "1013", IsActive = false },
                    }
                )
            );
        var service = new BrowserExtensionAccessService(
            Mock.Of<IAuthorizationService>(),
            Mock.Of<ICurrentUserManageableStoreScopeService>(),
            userService.Object
        );
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("userId", "user-1") }, "test")
        );

        var result = await service.GetRelatedStoreCodesAsync(user);

        Assert.Equal(new[] { "1014" }, result);
        userService.VerifyAll();
    }
}
