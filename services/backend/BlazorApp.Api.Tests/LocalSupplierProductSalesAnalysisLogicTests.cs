using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public class LocalSupplierProductSalesAnalysisLogicTests
{
    private static readonly DateTime BrisbaneNoonUtc = new(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidateDateRange_反向日期抛出参数错误()
    {
        var ex = Assert.Throws<LocalSupplierProductSalesAnalysisValidationException>(() =>
            LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                new DateTime(2026, 8, 18),
                new DateTime(2026, 8, 17),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("开始日期不能晚于结束日期", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_缺少日期抛出参数错误()
    {
        var ex = Assert.Throws<LocalSupplierProductSalesAnalysisValidationException>(() =>
            LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                DateTime.MinValue,
                DateTime.MinValue,
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("日期不能为空", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_结束日期超过布里斯班昨天抛出参数错误()
    {
        // BrisbaneNoonUtc => 布里斯班 2026-08-18 12:00，昨天为 2026-08-17。
        var ex = Assert.Throws<LocalSupplierProductSalesAnalysisValidationException>(() =>
            LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                new DateTime(2026, 8, 1),
                new DateTime(2026, 8, 18),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("结束日期不能晚于昨天", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_结束日期等于昨天且366自然日通过()
    {
        var bounds = LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
            new DateTime(2025, 8, 18),
            new DateTime(2026, 8, 17),
            BrisbaneNoonUtc
        );
        Assert.Equal(new DateTime(2025, 8, 18), bounds.StartDate);
        Assert.Equal(new DateTime(2026, 8, 17), bounds.EndDate);
    }

    [Fact]
    public void ValidateDateRange_超过366自然日抛出参数错误()
    {
        var ex = Assert.Throws<LocalSupplierProductSalesAnalysisValidationException>(() =>
            LocalSupplierProductSalesAnalysisLogic.ValidateDateRange(
                new DateTime(2025, 8, 16),
                new DateTime(2026, 8, 17),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("366", ex.Message);
    }

    [Fact]
    public void ResolvePurchaseDate_入库日优先于订单日与创建日()
    {
        var date = LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseDate(
            new DateTime(2026, 8, 3),
            new DateTime(2026, 8, 2),
            new DateTime(2026, 8, 1)
        );
        Assert.Equal(new DateTime(2026, 8, 3), date);
    }

    [Fact]
    public void ResolvePurchaseDate_无入库日回退订单日再回退创建日()
    {
        Assert.Equal(
            new DateTime(2026, 8, 2),
            LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseDate(
                null,
                new DateTime(2026, 8, 2),
                new DateTime(2026, 8, 1)
            )
        );
        Assert.Equal(
            new DateTime(2026, 8, 1),
            LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseDate(
                null,
                null,
                new DateTime(2026, 8, 1)
            )
        );
    }

    [Fact]
    public void ResolveSupplierCode_明细优先回退表头()
    {
        Assert.Equal(
            "SUP-D",
            LocalSupplierProductSalesAnalysisLogic.ResolveSupplierCode(" SUP-D ", "SUP-H")
        );
        Assert.Equal(
            "SUP-H",
            LocalSupplierProductSalesAnalysisLogic.ResolveSupplierCode(null, "SUP-H")
        );
        Assert.Null(
            LocalSupplierProductSalesAnalysisLogic.ResolveSupplierCode("  ", "  ")
        );
    }

    [Fact]
    public void ResolvePurchaseAmount_金额优先否则数量乘进货价()
    {
        Assert.Equal(20m, LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseAmount(20m, 3m, 2m));
        Assert.Equal(6m, LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseAmount(null, 3m, 2m));
        Assert.Equal(0m, LocalSupplierProductSalesAnalysisLogic.ResolvePurchaseAmount(null, null, null));
    }

    [Fact]
    public void CalculateSellThroughRate_返回百分比且无进货返回null()
    {
        Assert.Equal(20m, LocalSupplierProductSalesAnalysisLogic.CalculateSellThroughRate(10m, 2m));
        Assert.Null(LocalSupplierProductSalesAnalysisLogic.CalculateSellThroughRate(0m, 2m));
    }

    [Fact]
    public void CalculateAverageUnitPrice_负销量保留且零销量返回null()
    {
        Assert.Equal(10m, LocalSupplierProductSalesAnalysisLogic.CalculateAverageUnitPrice(-2m, -20m));
        Assert.Equal(-2m, LocalSupplierProductSalesAnalysisLogic.CalculateAverageUnitPrice(-5m, 10m));
        Assert.Null(LocalSupplierProductSalesAnalysisLogic.CalculateAverageUnitPrice(0m, 10m));
    }

    [Fact]
    public void ExpandCategoryGuids_父分类包含子孙分类()
    {
        var categories = new List<WarehouseCategory>
        {
            new() { CategoryGUID = "root", ParentGUID = null, CategoryName = "Root" },
            new() { CategoryGUID = "child", ParentGUID = "root", CategoryName = "Child" },
            new() { CategoryGUID = "grand", ParentGUID = "child", CategoryName = "Grand" },
            new() { CategoryGUID = "other", ParentGUID = null, CategoryName = "Other" },
        };

        var expanded = LocalSupplierProductSalesAnalysisLogic.ExpandCategoryGuids(
            categories,
            new[] { "root" }
        );

        Assert.Equal(new[] { "child", "grand", "root" }, expanded.OrderBy(x => x));
    }

    [Fact]
    public void ApplySelection_默认allFiltered排除商品()
    {
        var codes = new[] { "A", "B", "C" };
        var result = LocalSupplierProductSalesAnalysisLogic.ApplySelection(
            codes,
            new LocalSupplierProductSalesSelectionDto
            {
                Mode = "allFiltered",
                ExcludedProductCodes = new List<string> { "B" },
            }
        );
        Assert.Equal(new[] { "A", "C" }, result.OrderBy(x => x));
    }

    [Fact]
    public void ApplySelection_included只保留圈定商品()
    {
        var result = LocalSupplierProductSalesAnalysisLogic.ApplySelection(
            new[] { "A", "B", "C" },
            new LocalSupplierProductSalesSelectionDto
            {
                Mode = "included",
                IncludedProductCodes = new List<string> { "A", " C " },
            }
        );
        Assert.Equal(new[] { "A", "C" }, result.OrderBy(x => x));
    }

    [Fact]
    public void BatchCodes_每500分批并去重去空白()
    {
        var codes = Enumerable.Range(0, 1250).Select(i => $"P{i % 1249}").ToList();
        codes.Add("  ");
        codes.Add(" p0 ");

        var batches = LocalSupplierProductSalesAnalysisLogic.BatchCodes(codes);

        Assert.Equal(3, batches.Count);
        Assert.Equal(500, batches[0].Count);
        Assert.Equal(500, batches[1].Count);
        Assert.DoesNotContain("p0", batches[2]);
    }

    [Fact]
    public void ResolveCategoryGuids_单值与列表合并()
    {
        var filter = new LocalSupplierProductSalesAnalysisFilterDto
        {
            CategoryGuid = "cat-1",
            WarehouseCategoryGuids = new List<string> { "cat-2", "cat-1" },
        };
        Assert.Equal(
            new[] { "cat-1", "cat-2" },
            LocalSupplierProductSalesAnalysisLogic.ResolveCategoryGuids(filter).OrderBy(x => x)
        );
    }

    [Fact]
    public void BuildBranchDailySeries_补齐无销售自然日并允许负销量()
    {
        var rows = new List<LocalSupplierProductSalesBranchDailyDto>
        {
            new()
            {
                Date = new DateTime(2026, 8, 17),
                NetSalesQuantity = -2m,
                NetSalesAmount = -6m,
            },
        };

        var series = LocalSupplierProductSalesAnalysisLogic.BuildBranchDailySeries(
            rows,
            new DateTime(2026, 8, 16),
            new DateTime(2026, 8, 18)
        );

        Assert.Equal(3, series.Count);
        Assert.Equal(0m, series[0].NetSalesQuantity);
        Assert.Null(series[0].AverageUnitPrice);
        Assert.Equal(-2m, series[1].NetSalesQuantity);
        Assert.Equal(3m, series[1].AverageUnitPrice);
        Assert.Equal(0m, series[2].NetSalesQuantity);
    }

    [Fact]
    public void BuildProductDailySeries_补齐进货与销量日期()
    {
        var rows = new List<LocalSupplierProductSalesDailyDto>
        {
            new()
            {
                Date = new DateTime(2026, 8, 16),
                PurchaseQuantity = 5m,
                PurchaseAmount = 20m,
                NetSalesQuantity = 2m,
                NetSalesAmount = 10m,
            },
        };

        var series = LocalSupplierProductSalesAnalysisLogic.BuildProductDailySeries(
            rows,
            new DateTime(2026, 8, 15),
            new DateTime(2026, 8, 17)
        );

        Assert.Equal(3, series.Count);
        Assert.Equal(0m, series[0].PurchaseQuantity);
        Assert.Equal(5m, series[1].PurchaseQuantity);
        Assert.Equal(5m, series[1].AverageUnitPrice);
    }

    [Fact]
    public void SortSummaryRows_默认按净销量降序并用商品代码稳定排序()
    {
        var rows = new List<LocalSupplierProductSalesSummaryRowDto>
        {
            new() { ProductCode = "B", NetSalesQuantity = 10m },
            new() { ProductCode = "A", NetSalesQuantity = 10m },
            new() { ProductCode = "C", NetSalesQuantity = 5m },
        };

        var sorted = LocalSupplierProductSalesAnalysisLogic.SortSummaryRows(rows, null, null);

        Assert.Equal(new[] { "A", "B", "C" }, sorted.Select(x => x.ProductCode));
    }

    [Fact]
    public async Task Controller_过期管理员Claim不能绕过实时普通用户分店范围()
    {
        IReadOnlyList<string>? capturedStores = null;
        var service = new Mock<ILocalSupplierProductSalesAnalysisService>();
        service.Setup(x => x.GetOptionsAsync(It.IsAny<IReadOnlyList<string>?>()))
            .Callback<IReadOnlyList<string>?>(codes => capturedStores = codes)
            .ReturnsAsync(ApiResponse<LocalSupplierProductSalesOptionsDto>.OK(new()));
        var roleService = new Mock<IRoleService>();
        roleService.Setup(x => x.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new UserPermissionSnapshotDto
            {
                UserGuid = "user-1",
                RoleNames = new List<string> { "User" },
                PermissionCodes = new List<string> { Permissions.LocalPurchase.View },
            }));
        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Stores = new List<UserStoreDto> { new() { StoreCode = "B1" } },
            }));

        var result = await CreateController(service.Object, userService.Object, roleService.Object, "WarehouseManager").GetOptions();

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new[] { "B1" }, capturedStores);
    }

    [Fact]
    public async Task Controller_实时仓库管理员别名使用全分店范围()
    {
        IReadOnlyList<string>? capturedStores = new List<string> { "unexpected" };
        var service = new Mock<ILocalSupplierProductSalesAnalysisService>();
        service.Setup(x => x.GetOptionsAsync(It.IsAny<IReadOnlyList<string>?>()))
            .Callback<IReadOnlyList<string>?>(codes => capturedStores = codes)
            .ReturnsAsync(ApiResponse<LocalSupplierProductSalesOptionsDto>.OK(new()));
        var roleService = new Mock<IRoleService>();
        roleService.Setup(x => x.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new UserPermissionSnapshotDto
            {
                UserGuid = "user-1",
                RoleNames = new List<string> { "仓库管理员" },
                PermissionCodes = new List<string> { Permissions.LocalPurchase.View },
            }));

        var result = await CreateController(service.Object, Mock.Of<IUserService>(), roleService.Object, "User").GetOptions();

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(capturedStores);
    }

    [Fact]
    public async Task Controller_零授权分店返回空范围而非Forbid()
    {
        IReadOnlyList<string>? capturedStores = new List<string> { "unexpected" };
        var service = new Mock<ILocalSupplierProductSalesAnalysisService>();
        service.Setup(x => x.GetOptionsAsync(It.IsAny<IReadOnlyList<string>?>()))
            .Callback<IReadOnlyList<string>?>(codes => capturedStores = codes)
            .ReturnsAsync(ApiResponse<LocalSupplierProductSalesOptionsDto>.OK(new()));
        var roleService = new Mock<IRoleService>();
        roleService.Setup(x => x.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new UserPermissionSnapshotDto
            {
                UserGuid = "user-1",
                RoleNames = new List<string> { "User" },
                PermissionCodes = new List<string> { Permissions.LocalPurchase.View },
            }));
        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Stores = new List<UserStoreDto>(),
            }));

        var result = await CreateController(service.Object, userService.Object, roleService.Object, "User").GetOptions();

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedStores);
        Assert.Empty(capturedStores);
    }

    [Fact]
    public void CleanSelection_included裁剪无效商品()
    {
        var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B" };
        var effective = LocalSupplierProductSalesAnalysisLogic.CleanSelection(
            new LocalSupplierProductSalesSelectionDto
            {
                Mode = "included",
                IncludedProductCodes = new List<string> { "a", " C ", "B" },
            },
            valid
        );

        Assert.Equal("included", effective.Mode);
        Assert.Equal(2, effective.IncludedProductCodes!.Count);
        Assert.Contains(
            effective.IncludedProductCodes,
            code => code.Equals("A", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            effective.IncludedProductCodes,
            code => code.Equals("B", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void CleanSelection_allFiltered保留模式并清理无效excluded()
    {
        var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B" };
        var effective = LocalSupplierProductSalesAnalysisLogic.CleanSelection(
            new LocalSupplierProductSalesSelectionDto
            {
                Mode = "allFiltered",
                ExcludedProductCodes = new List<string> { "B", "ZZZ" },
            },
            valid
        );

        Assert.Equal("allFiltered", effective.Mode);
        Assert.Equal(new[] { "B" }, effective.ExcludedProductCodes);
    }

    [Fact]
    public void ResolveSelection_autoSelectFirst为true且current失效改首个候选()
    {
        var codes = new List<string> { "B", "A" };
        var outcome = LocalSupplierProductSalesAnalysisLogic.ResolveSelection(
            codes,
            null,
            "ZZZ",
            autoSelectFirst: true
        );

        Assert.Equal("B", outcome.CurrentProductCode);
        Assert.Equal(new[] { "B", "A" }, outcome.SelectedCodes);
    }

    [Fact]
    public void ResolveSelection_current大小写不敏感匹配()
    {
        var codes = new List<string> { "B", "A" };
        var outcome = LocalSupplierProductSalesAnalysisLogic.ResolveSelection(
            codes,
            null,
            "a",
            autoSelectFirst: false
        );

        Assert.Equal("A", outcome.CurrentProductCode);
    }

    [Fact]
    public void ResolveSelection_autoSelectFirst为false但current失效迁移到首个仍选中候选()
    {
        var codes = new List<string> { "B", "A" };
        var outcome = LocalSupplierProductSalesAnalysisLogic.ResolveSelection(
            codes,
            null,
            "ZZZ",
            autoSelectFirst: false
        );

        Assert.Equal("B", outcome.CurrentProductCode);
    }

    [Fact]
    public void ResolveSelection_current已被选择排除时迁移到首个仍选中候选()
    {
        var outcome = LocalSupplierProductSalesAnalysisLogic.ResolveSelection(
            new[] { "A", "B", "C" },
            new LocalSupplierProductSalesSelectionDto
            {
                Mode = "allFiltered",
                ExcludedProductCodes = new List<string> { "B" },
            },
            "B",
            autoSelectFirst: false
        );

        Assert.Equal("A", outcome.CurrentProductCode);
        Assert.Equal(new[] { "A", "C" }, outcome.SelectedCodes);
    }

    [Fact]
    public void ResolveSelection_选择仅作用于已过滤商品()
    {
        var filtered = new List<string> { "A" };
        var outcome = LocalSupplierProductSalesAnalysisLogic.ResolveSelection(
            filtered,
            new LocalSupplierProductSalesSelectionDto { Mode = "allFiltered" },
            null,
            autoSelectFirst: true
        );

        Assert.Equal(new[] { "A" }, outcome.SelectedCodes);
        Assert.Equal("A", outcome.CurrentProductCode);
    }

    private static LocalSupplierProductSalesAnalysisController CreateController(
        ILocalSupplierProductSalesAnalysisService service,
        IUserService userService,
        IRoleService roleService,
        string roleClaim
    )
    {
        return new LocalSupplierProductSalesAnalysisController(
            service,
            userService,
            roleService,
            NullLogger<LocalSupplierProductSalesAnalysisController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "user-1"),
                        new Claim(ClaimTypes.Role, roleClaim),
                    }, "TestAuth")),
                },
            },
        };
    }
}
