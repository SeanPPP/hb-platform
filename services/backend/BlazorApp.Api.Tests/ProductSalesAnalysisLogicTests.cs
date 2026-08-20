using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("SalesDashboardCache")]
public sealed class ProductSalesAnalysisLogicTests
{
    private static readonly DateTime BrisbaneNoonUtc = new(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidateDateRange_反向日期抛出参数错误()
    {
        var ex = Assert.Throws<ProductSalesAnalysisValidationException>(() =>
            SalesDashboardReactService.ValidateProductSalesAnalysisDateRange(
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
        var ex = Assert.Throws<ProductSalesAnalysisValidationException>(() =>
            SalesDashboardReactService.ValidateProductSalesAnalysisDateRange(
                DateTime.MinValue,
                DateTime.MinValue,
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("日期", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_结束日期超过布里斯班今天抛出参数错误()
    {
        var ex = Assert.Throws<ProductSalesAnalysisValidationException>(() =>
            SalesDashboardReactService.ValidateProductSalesAnalysisDateRange(
                new DateTime(2026, 8, 18),
                new DateTime(2026, 8, 19),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("结束日期不能晚于今天", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_超过366自然日抛出参数错误()
    {
        var ex = Assert.Throws<ProductSalesAnalysisValidationException>(() =>
            SalesDashboardReactService.ValidateProductSalesAnalysisDateRange(
                new DateTime(2025, 8, 17),
                new DateTime(2026, 8, 18),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("366", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_366自然日包含首尾且通过()
    {
        var bounds = SalesDashboardReactService.ValidateProductSalesAnalysisDateRange(
            new DateTime(2025, 8, 18),
            new DateTime(2026, 8, 18),
            BrisbaneNoonUtc
        );

        Assert.Equal(new DateTime(2025, 8, 18), bounds.StartDate);
        Assert.Equal(new DateTime(2026, 8, 18), bounds.EndDate);
    }

    [Theory]
    [InlineData(10, 25.0, 2.5)]
    [InlineData(-5, 10.0, -2.0)]
    [InlineData(-5, -10.0, 2.0)]
    public void CalculateAverageUnitPrice_按净额计算(int quantity, decimal salesAmount, decimal expected)
    {
        Assert.Equal(expected, SalesDashboardReactService.CalculateAverageUnitPrice(quantity, salesAmount));
    }

    [Fact]
    public void CalculateAverageUnitPrice_数量为零返回null()
    {
        Assert.Null(SalesDashboardReactService.CalculateAverageUnitPrice(0, 10m));
    }

    [Fact]
    public void ApplySelection_allFiltered默认并移除排除商品()
    {
        var rows = new List<ProductSalesAggregateRow>
        {
            new() { ProductCode = "A", Quantity = 1 },
            new() { ProductCode = "B", Quantity = 2 },
            new() { ProductCode = "C", Quantity = 3 },
        };

        var result = SalesDashboardReactService.ApplyProductSalesSelection(
            rows,
            null,
            null,
            new[] { "B" }
        );

        Assert.Equal(new[] { "A", "C" }, result.Select(x => x.ProductCode).OrderBy(x => x));
    }

    [Fact]
    public void ApplySelection_included只保留圈定商品()
    {
        var rows = new List<ProductSalesAggregateRow>
        {
            new() { ProductCode = "A", Quantity = 1 },
            new() { ProductCode = "B", Quantity = 2 },
            new() { ProductCode = "C", Quantity = 3 },
        };

        var result = SalesDashboardReactService.ApplyProductSalesSelection(
            rows,
            "included",
            new[] { "A", "C" },
            null
        );

        Assert.Equal(new[] { "A", "C" }, result.Select(x => x.ProductCode).OrderBy(x => x));
    }

    [Fact]
    public void SortAndPage_默认销量降序并用商品代码稳定打破平局()
    {
        var rows = new List<ProductSalesAggregateRow>
        {
            new() { ProductCode = "B", Quantity = 10 },
            new() { ProductCode = "A", Quantity = 10 },
            new() { ProductCode = "C", Quantity = 5 },
            new() { ProductCode = "D", Quantity = 15 },
        };

        var page = SalesDashboardReactService.SortAndPageProductSalesAggregates(
            rows,
            pageNumber: 1,
            pageSize: 3,
            sortBy: null,
            sortDirection: null
        );

        Assert.Equal(4, page.Total);
        Assert.Equal(new[] { "D", "A", "B" }, page.Items.Select(x => x.ProductCode));
    }

    [Fact]
    public void SortAndPage_金额升序分页稳定()
    {
        var rows = new List<ProductSalesAggregateRow>
        {
            new() { ProductCode = "A", SalesAmount = 30 },
            new() { ProductCode = "B", SalesAmount = 10 },
            new() { ProductCode = "C", SalesAmount = 20 },
        };

        var page = SalesDashboardReactService.SortAndPageProductSalesAggregates(
            rows,
            pageNumber: 2,
            pageSize: 2,
            sortBy: "salesAmount",
            sortDirection: "asc"
        );

        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { "A" }, page.Items.Select(x => x.ProductCode));
    }

    [Fact]
    public void BuildProductDailySeries_补齐无销售自然日()
    {
        var rows = new List<ProductSalesDailyRow>
        {
            new() { Date = new DateTime(2026, 8, 16), Quantity = 5, SalesAmount = 10m },
        };

        var series = SalesDashboardReactService.BuildProductDailySeries(
            rows,
            new DateTime(2026, 8, 15),
            new DateTime(2026, 8, 17)
        );

        Assert.Equal(3, series.Count);
        Assert.Equal(new DateTime(2026, 8, 15), series[0].Date);
        Assert.Equal(0, series[0].Metrics.Quantity);
        Assert.Equal(0m, series[0].Metrics.SalesAmount);
        Assert.Null(series[0].Metrics.AverageUnitPrice);
        Assert.Equal(5, series[1].Metrics.Quantity);
        Assert.Equal(2m, series[1].Metrics.AverageUnitPrice);
        Assert.Equal(0, series[2].Metrics.Quantity);
    }

    [Fact]
    public void BuildBranchDailySeries_补齐无销售自然日且允许负销量()
    {
        var rows = new List<ProductSalesBranchDailyRow>
        {
            new() { Date = new DateTime(2026, 8, 17), Quantity = -2, SalesAmount = -6m },
        };

        var series = SalesDashboardReactService.BuildBranchDailySeries(
            rows,
            new DateTime(2026, 8, 16),
            new DateTime(2026, 8, 18)
        );

        Assert.Equal(3, series.Count);
        Assert.Equal(-2, series[1].Metrics.Quantity);
        Assert.Equal(-6m, series[1].Metrics.SalesAmount);
        Assert.Equal(3m, series[1].Metrics.AverageUnitPrice);
    }

    [Fact]
    public void ResolveScopeProductCodes_currentProduct需要商品代码()
    {
        var ex = Assert.Throws<ProductSalesAnalysisValidationException>(() =>
            SalesDashboardReactService.ResolveScopeProductCodes(
                new ProductSalesAnalysisRequest
                {
                    Scope = new ProductSalesAnalysisScopeDto { Mode = "currentProduct" },
                }
            )
        );
        Assert.Contains("productCode", ex.Message);
    }

    [Fact]
    public void ResolveScopeProductCodes_selectedProducts返回圈定商品()
    {
        var codes = SalesDashboardReactService.ResolveScopeProductCodes(
            new ProductSalesAnalysisRequest
            {
                Scope = new ProductSalesAnalysisScopeDto { Mode = "selectedProducts" },
                Selection = new ProductSalesAnalysisSelectionDto
                {
                    Mode = "included",
                    IncludedProductCodes = new List<string> { "A", " B ", "a" },
                },
            }
        );

        Assert.Equal(new[] { "A", "B" }, codes.OrderBy(x => x));
    }

    [Fact]
    public void ResolveScopeProductCodes_非法模式抛出参数错误()
    {
        var ex = Assert.Throws<ProductSalesAnalysisValidationException>(() =>
            SalesDashboardReactService.ResolveScopeProductCodes(
                new ProductSalesAnalysisRequest
                {
                    Scope = new ProductSalesAnalysisScopeDto { Mode = "unknown" },
                }
            )
        );
        Assert.Contains("scope.mode", ex.Message);
    }

    [Fact]
    public void RequestJson_按filterSelectionScope嵌套契约绑定()
    {
        const string json = """
            {
              "filter": {
                "startDate": "2026-08-01",
                "endDate": "2026-08-18",
                "keyword": "HB001",
                "australianSupplierCodes": ["A1"],
                "chinaSupplierCodes": ["C1"]
              },
              "selection": {
                "mode": "allFiltered",
                "includedProductCodes": [],
                "excludedProductCodes": ["P2"]
              },
              "scope": {
                "mode": "currentProduct",
                "productCode": "P1"
              }
            }
            """;

        var request = JsonSerializer.Deserialize<ProductSalesAnalysisRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        Assert.NotNull(request);
        Assert.Equal(new DateTime(2026, 8, 1), request!.Filter.StartDate);
        Assert.Equal("HB001", request.Filter.Keyword);
        Assert.Equal(new[] { "A1" }, request.Filter.AustralianSupplierCodes);
        Assert.Equal("allFiltered", request.Selection.Mode);
        Assert.Equal(new[] { "P2" }, request.Selection.ExcludedProductCodes);
        Assert.Equal("currentProduct", request.Scope?.Mode);
        Assert.Equal("P1", request.Scope?.ProductCode);
    }

    [Fact]
    public void ResolveSelectedProductCodes_allFiltered按筛选全集排除商品()
    {
        var codes = SalesDashboardReactService.ResolveSelectedProductCodes(
            new ProductSalesAnalysisSelectionDto
            {
                Mode = "allFiltered",
                ExcludedProductCodes = new List<string> { "B" },
            },
            new[] { "A", "B", "C" }
        );

        Assert.Equal(new[] { "A", "C" }, codes);
    }

    [Fact]
    public void BuildSupplierQueryPlan_大量旧200映射仍保留精确商品集合()
    {
        var context = new ProductSalesAnalysisQueryContext
        {
            AustralianSupplierCodes = new List<string> { "AU1" },
            ChinaSupplierCodes = new HashSet<string>(new[] { "CN1" }, StringComparer.OrdinalIgnoreCase),
            AllChinaSupplierCodes = new HashSet<string>(new[] { "CN1", "CN2" }, StringComparer.OrdinalIgnoreCase),
            ChinaProductMap = Enumerable.Range(1, 2_501)
                .ToDictionary(index => $"P{index:0000}", _ => "CN1", StringComparer.OrdinalIgnoreCase),
        };

        var plan = SalesDashboardReactService.BuildProductSalesSupplierQueryPlan(context);

        Assert.False(plan.IsUnfiltered);
        Assert.Equal(new[] { "AU1", "CN1" }, plan.DirectSupplierCodes.OrderBy(code => code));
        Assert.Equal(2_501, plan.LegacyProductCodes.Count);
        Assert.DoesNotContain("200", plan.DirectSupplierCodes);
    }

    [Fact]
    public void BuildSupplierQueryPlan_澳洲200表示全部国内供应商且不重复旧映射()
    {
        var context = new ProductSalesAnalysisQueryContext
        {
            AustralianSupplierCodes = new List<string> { "200" },
            ChinaSupplierCodes = new HashSet<string>(new[] { "CN1" }, StringComparer.OrdinalIgnoreCase),
            AllChinaSupplierCodes = new HashSet<string>(new[] { "CN1", "CN2" }, StringComparer.OrdinalIgnoreCase),
            ChinaProductMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["P1"] = "CN1",
            },
        };

        var plan = SalesDashboardReactService.BuildProductSalesSupplierQueryPlan(context);

        Assert.False(plan.IsUnfiltered);
        Assert.Equal(new[] { "200", "CN1", "CN2" }, plan.DirectSupplierCodes.OrderBy(code => code));
        Assert.Empty(plan.LegacyProductCodes);
    }

    [Fact]
    public void BatchProductSalesCodes_显式商品按500个分批并保持稳定去重()
    {
        var productCodes = Enumerable.Range(0, 1_201)
            .Select(index => $"P{index:D4}")
            .Concat(new[] { "P0001", " p0002 ", string.Empty });

        var batches = SalesDashboardReactService.BatchProductSalesCodes(productCodes);

        Assert.Equal(new[] { 500, 500, 201 }, batches.Select(batch => batch.Count));
        Assert.Equal("P0000", batches[0][0]);
        Assert.Equal("P1200", batches[2][200]);
        Assert.Equal(1, batches.SelectMany(batch => batch).Count(code => code == "P0001"));
        Assert.Equal(1, batches.SelectMany(batch => batch).Count(code => code == "P0002"));
    }

    [Fact]
    public async Task ProductSalesAnalysisOptions_ReportsView别名不能绕过精确权限()
    {
        var dashboardService = new Mock<ISalesDashboardReactService>();
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        var controller = new SalesDashboardController(
            dashboardService.Object,
            NullLogger<SalesDashboardController>.Instance,
            Mock.Of<IUserService>(),
            Mock.Of<ISalesDashboardCacheWarmer>(),
            roleService.Object
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "user-1") },
                        "TestAuth"
                    )),
                },
            },
        };

        var result = await controller.GetProductSalesAnalysisOptions(new ProductSalesAnalysisFilterDto
        {
            StartDate = new DateTime(2026, 8, 1),
            EndDate = new DateTime(2026, 8, 18),
        });

        Assert.IsType<ForbidResult>(result);
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisOptionsAsync(
                It.IsAny<ProductSalesAnalysisFilterDto>(),
                It.IsAny<List<string>?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public void ProductSalesAnalysisCacheKeys_生成键不登记_活动登记按引用计数释放()
    {
        SalesDashboardCacheKeys.ClearActiveKeys();
        var request = new ProductSalesAnalysisRequest
        {
            Filter = new ProductSalesAnalysisFilterDto
            {
                StartDate = new DateTime(2026, 8, 1),
                EndDate = new DateTime(2026, 8, 18),
            },
        };

        var firstKey = SalesDashboardCacheKeys.ProductSalesAnalysisCandidates(
            request,
            new List<string> { "B1" },
            "v1"
        );

        Assert.Empty(SalesDashboardCacheKeys.ActiveKeys);

        SalesDashboardCacheKeys.RegisterProductSalesAnalysisKey(firstKey);
        Assert.Contains(firstKey, SalesDashboardCacheKeys.ActiveKeys);

        SalesDashboardCacheKeys.RegisterProductSalesAnalysisKey(firstKey);
        Assert.Contains(firstKey, SalesDashboardCacheKeys.ActiveKeys);

        SalesDashboardCacheKeys.UnregisterProductSalesAnalysisKey(firstKey);
        Assert.Contains(firstKey, SalesDashboardCacheKeys.ActiveKeys);

        SalesDashboardCacheKeys.UnregisterProductSalesAnalysisKey(firstKey);
        Assert.DoesNotContain(firstKey, SalesDashboardCacheKeys.ActiveKeys);
        SalesDashboardCacheKeys.ClearActiveKeys();
    }

    [Fact]
    public void ProductSalesAnalysisCacheKeys_同键并发登记与释放不会丢登记()
    {
        SalesDashboardCacheKeys.ClearActiveKeys();
        var key = SalesDashboardCacheKeys.ProductSalesAnalysisCandidates(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            },
            new List<string> { "B1" },
            "v1"
        );

        Parallel.For(0, 1000, _ =>
            SalesDashboardCacheKeys.RegisterProductSalesAnalysisKey(key));
        Assert.Contains(key, SalesDashboardCacheKeys.ActiveKeys);

        Parallel.For(0, 999, _ =>
            SalesDashboardCacheKeys.UnregisterProductSalesAnalysisKey(key));
        Assert.Contains(key, SalesDashboardCacheKeys.ActiveKeys);

        SalesDashboardCacheKeys.UnregisterProductSalesAnalysisKey(key);
        Assert.DoesNotContain(key, SalesDashboardCacheKeys.ActiveKeys);
        SalesDashboardCacheKeys.ClearActiveKeys();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("管理员")]
    [InlineData("超级管理员")]
    [InlineData("SuperAdmin")]
    [InlineData("WarehouseManager")]
    [InlineData("仓库经理")]
    [InlineData("仓库管理员")]
    [InlineData("Warehouse")]
    [InlineData("WarehouseAdmin")]
    public async Task ProductSalesAnalysis_管理员中英文别名按全分店解析(string role)
    {
        List<string>? capturedBranchCodes = null;
        var dashboardService = new Mock<ISalesDashboardReactService>();
        dashboardService
            .Setup(service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ))
            .Callback<ProductSalesAnalysisRequest, List<string>?, bool>((_, codes, _) => capturedBranchCodes = codes)
            .ReturnsAsync(
                new ProductSalesAnalysisResponse<ProductSalesAnalysisPagedDto<ProductSalesProductRowDto>>
                {
                    StatisticStatus = "Fresh",
                    Data = new ProductSalesAnalysisPagedDto<ProductSalesProductRowDto>(),
                }
            );

        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(
                new UserPermissionSnapshotDto
                {
                    UserGuid = "user-1",
                    RoleNames = new List<string> { role },
                    PermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                    ExactPermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                }
            ));

        var userService = new Mock<IUserService>();
        var controller = CreateController(
            dashboardService.Object,
            roleService.Object,
            "user-1",
            // JWT 固定为普通/旧角色，全分店身份只能来自实时快照中的管理员/仓库别名，防止回退到 JWT 判定。
            "User",
            userService.Object
        );

        var result = await controller.GetProductSalesAnalysisSummary(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            }
        );

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(capturedBranchCodes);
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ),
            Times.Once
        );
        userService.Verify(
            service => service.GetUserByGuidAsync(It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ProductSalesAnalysis_StaleWarehouseManagerClaim_UsesAuthorizedStoresNotAllStores()
    {
        List<string>? capturedBranchCodes = null;
        var dashboardService = new Mock<ISalesDashboardReactService>();
        dashboardService
            .Setup(service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ))
            .Callback<ProductSalesAnalysisRequest, List<string>?, bool>((_, codes, _) => capturedBranchCodes = codes)
            .ReturnsAsync(
                new ProductSalesAnalysisResponse<ProductSalesAnalysisPagedDto<ProductSalesProductRowDto>>
                {
                    StatisticStatus = "Fresh",
                    Data = new ProductSalesAnalysisPagedDto<ProductSalesProductRowDto>(),
                }
            );

        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(
                new UserPermissionSnapshotDto
                {
                    UserGuid = "user-1",
                    RoleNames = new List<string>(),
                    PermissionCodes = new List<string>(),
                    ExactPermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                }
            ));

        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(
                new UserDetailDto
                {
                    UserGUID = "user-1",
                    Stores = new List<UserStoreDto>
                    {
                        new() { StoreCode = "B1" },
                        new() { StoreCode = "B2" },
                    },
                }
            ));

        var controller = CreateController(
            dashboardService.Object,
            roleService.Object,
            "user-1",
            "WarehouseManager",
            userService.Object
        );

        var result = await controller.GetProductSalesAnalysisSummary(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            }
        );

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedBranchCodes);
        Assert.Equal(new[] { "B1", "B2" }, capturedBranchCodes!.OrderBy(code => code));
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task ProductSalesAnalysis_零分店视为授权空范围并原样返回Fresh空信封()
    {
        List<string>? capturedBranchCodes = null;
        var freshResponse = new ProductSalesAnalysisResponse<
            ProductSalesAnalysisPagedDto<ProductSalesProductRowDto>
        >
        {
            StatisticStatus = "Fresh",
            StatisticMessage = null,
            StatisticUpdatedAt = new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc),
            CacheVersion = "v1",
            Data = new ProductSalesAnalysisPagedDto<ProductSalesProductRowDto>(),
        };

        var dashboardService = new Mock<ISalesDashboardReactService>();
        dashboardService
            .Setup(service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ))
            .Callback<ProductSalesAnalysisRequest, List<string>?, bool>((_, codes, _) => capturedBranchCodes = codes)
            .ReturnsAsync(freshResponse);

        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(
                new UserPermissionSnapshotDto
                {
                    UserGuid = "user-1",
                    RoleNames = new List<string>(),
                    PermissionCodes = new List<string>(),
                    ExactPermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                }
            ));

        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(
                new UserDetailDto
                {
                    UserGUID = "user-1",
                    Stores = new List<UserStoreDto>(),
                }
            ));

        var controller = CreateController(
            dashboardService.Object,
            roleService.Object,
            "user-1",
            "User",
            userService.Object
        );

        var result = await controller.GetProductSalesAnalysisSummary(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            }
        );

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Same(freshResponse, okResult.Value);
        Assert.NotNull(capturedBranchCodes);
        Assert.Empty(capturedBranchCodes!);
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ),
            Times.Once
        );
        userService.Verify(
            service => service.GetUserByGuidAsync("user-1"),
            Times.Once
        );
    }

    [Fact]
    public async Task ProductSalesAnalysis_快照失败时failClosed且不调用service()
    {
        var dashboardService = new Mock<ISalesDashboardReactService>();
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.Error("快照失败"));

        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(
                new UserDetailDto
                {
                    UserGUID = "user-1",
                    Stores = new List<UserStoreDto> { new() { StoreCode = "B1" } },
                }
            ));

        var controller = CreateController(
            dashboardService.Object,
            roleService.Object,
            "user-1",
            "User",
            userService.Object
        );

        var result = await controller.GetProductSalesAnalysisSummary(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            }
        );

        Assert.IsType<OkObjectResult>(result);
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ),
            Times.Never
        );
        userService.Verify(
            service => service.GetUserByGuidAsync(It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ProductSalesAnalysis_用户读取失败时failClosed且不调用service()
    {
        var dashboardService = new Mock<ISalesDashboardReactService>();
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(
                new UserPermissionSnapshotDto
                {
                    UserGuid = "user-1",
                    RoleNames = new List<string>(),
                    PermissionCodes = new List<string>(),
                    ExactPermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                }
            ));

        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.Error("用户读取失败"));

        var controller = CreateController(
            dashboardService.Object,
            roleService.Object,
            "user-1",
            "User",
            userService.Object
        );

        var result = await controller.GetProductSalesAnalysisSummary(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            }
        );

        Assert.IsType<OkObjectResult>(result);
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ),
            Times.Never
        );
        userService.Verify(
            service => service.GetUserByGuidAsync("user-1"),
            Times.Once
        );
    }

    [Fact]
    public async Task ProductSalesAnalysis_缺用户ID时拒绝访问且不读取快照()
    {
        var dashboardService = new Mock<ISalesDashboardReactService>();
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasExactPermissionAsync(
                It.IsAny<string>(),
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));

        var controller = new SalesDashboardController(
            dashboardService.Object,
            NullLogger<SalesDashboardController>.Instance,
            Mock.Of<IUserService>(),
            Mock.Of<ISalesDashboardCacheWarmer>(),
            roleService.Object
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "TestAuth")),
                },
            },
        };

        var result = await controller.GetProductSalesAnalysisSummary(
            new ProductSalesAnalysisRequest
            {
                Filter = new ProductSalesAnalysisFilterDto
                {
                    StartDate = new DateTime(2026, 8, 1),
                    EndDate = new DateTime(2026, 8, 18),
                },
            }
        );

        Assert.IsType<ForbidResult>(result);
        roleService.Verify(
            service => service.GetUserPermissionSnapshotAsync(It.IsAny<string>()),
            Times.Never
        );
        dashboardService.Verify(
            service => service.GetProductSalesAnalysisSummaryAsync(
                It.IsAny<ProductSalesAnalysisRequest>(),
                It.IsAny<List<string>?>(),
                false
            ),
            Times.Never
        );
    }

    [Fact]
    public void ProductSalesAnalysisOptionsCacheKey_只包含日期分店与统计水位()
    {
        var key = SalesDashboardCacheKeys.ProductSalesAnalysisOptions(
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 18),
            new List<string> { "B1" },
            "v1"
        );
        var sameKey = SalesDashboardCacheKeys.ProductSalesAnalysisOptions(
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 18),
            new List<string> { "B1" },
            "v1"
        );
        var otherBranch = SalesDashboardCacheKeys.ProductSalesAnalysisOptions(
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 18),
            new List<string> { "B2" },
            "v1"
        );
        var otherVersion = SalesDashboardCacheKeys.ProductSalesAnalysisOptions(
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 18),
            new List<string> { "B1" },
            "v2"
        );

        Assert.Equal(key, sameKey);
        Assert.NotEqual(key, otherBranch);
        Assert.NotEqual(key, otherVersion);
        SalesDashboardCacheKeys.ClearActiveKeys();
    }

    private static SalesDashboardController CreateController(
        ISalesDashboardReactService dashboardService,
        IRoleService roleService,
        string userGuid,
        string? roleClaim = null,
        IUserService? userService = null
    )
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userGuid) };
        if (!string.IsNullOrWhiteSpace(roleClaim))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleClaim!));
        }

        return new SalesDashboardController(
            dashboardService,
            NullLogger<SalesDashboardController>.Instance,
            userService ?? Mock.Of<IUserService>(),
            Mock.Of<ISalesDashboardCacheWarmer>(),
            roleService
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
    }
}
