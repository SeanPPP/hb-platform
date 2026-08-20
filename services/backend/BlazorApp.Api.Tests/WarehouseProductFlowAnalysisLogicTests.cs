using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseProductFlowAnalysisLogicTests
{
    private static readonly DateTime BrisbaneNoonUtc = new(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidateDateRange_反向日期抛出参数错误()
    {
        var ex = Assert.Throws<WarehouseProductFlowAnalysisValidationException>(() =>
            WarehouseProductFlowAnalysisService.ValidateWarehouseProductFlowAnalysisDateRange(
                new DateTime(2026, 8, 18),
                new DateTime(2026, 8, 17),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("开始日期不能晚于结束日期", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_结束日期超过布里斯班昨天抛出参数错误()
    {
        var ex = Assert.Throws<WarehouseProductFlowAnalysisValidationException>(() =>
            WarehouseProductFlowAnalysisService.ValidateWarehouseProductFlowAnalysisDateRange(
                new DateTime(2026, 8, 18),
                new DateTime(2026, 8, 19),
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("结束日期不能晚于昨天", ex.Message);
    }

    [Fact]
    public void ValidateDateRange_超过366自然日抛出参数错误()
    {
        var ex = Assert.Throws<WarehouseProductFlowAnalysisValidationException>(() =>
            WarehouseProductFlowAnalysisService.ValidateWarehouseProductFlowAnalysisDateRange(
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
        var bounds = WarehouseProductFlowAnalysisService.ValidateWarehouseProductFlowAnalysisDateRange(
            new DateTime(2025, 8, 18),
            new DateTime(2026, 8, 18),
            BrisbaneNoonUtc
        );
        Assert.Equal(new DateTime(2025, 8, 18), bounds.StartDate);
        Assert.Equal(new DateTime(2026, 8, 18), bounds.EndDate);
    }

    [Fact]
    public void ValidatePeriods_每套日期均校验未来与超长范围()
    {
        var ex = Assert.Throws<WarehouseProductFlowAnalysisValidationException>(() =>
            WarehouseProductFlowAnalysisService.ValidateWarehouseProductFlowAnalysisPeriods(
                new WarehouseProductFlowPeriodsDto
                {
                    ContainerPeriod = new WarehouseProductFlowDatePeriodDto
                    {
                        StartDate = new DateTime(2026, 8, 18),
                        EndDate = new DateTime(2026, 8, 18),
                    },
                    OrderShipmentPeriod = new WarehouseProductFlowDatePeriodDto
                    {
                        StartDate = new DateTime(2026, 8, 18),
                        EndDate = new DateTime(2026, 8, 18),
                    },
                    SalesPeriod = new WarehouseProductFlowDatePeriodDto
                    {
                        StartDate = new DateTime(2026, 8, 18),
                        EndDate = new DateTime(2026, 8, 19),
                    },
                },
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("salesPeriod", ex.Message);
        Assert.Contains("结束日期不能晚于昨天", ex.Message);
    }

    [Fact]
    public void ValidatePeriods_三套日期均可为空校验()
    {
        var ex = Assert.Throws<WarehouseProductFlowAnalysisValidationException>(() =>
            WarehouseProductFlowAnalysisService.ValidateWarehouseProductFlowAnalysisPeriods(
                new WarehouseProductFlowPeriodsDto
                {
                    ContainerPeriod = new WarehouseProductFlowDatePeriodDto
                    {
                        StartDate = new DateTime(2026, 8, 18),
                        EndDate = new DateTime(2026, 8, 18),
                    },
                    OrderShipmentPeriod = new WarehouseProductFlowDatePeriodDto
                    {
                        StartDate = new DateTime(2026, 8, 18),
                        EndDate = new DateTime(2026, 8, 18),
                    },
                },
                BrisbaneNoonUtc
            )
        );
        Assert.Contains("salesPeriod", ex.Message);
        Assert.Contains("不能为空", ex.Message);
    }

    [Theory]
    [InlineData(10, 25.0, 2.5)]
    [InlineData(-5, 10.0, -2.0)]
    [InlineData(-5, -10.0, 2.0)]
    public void CalculateAverageUnitPrice_按净额计算(int quantity, decimal salesAmount, decimal expected)
    {
        Assert.Equal(
            expected,
            WarehouseProductFlowAnalysisService.CalculateAverageUnitPrice(quantity, salesAmount)
        );
    }

    [Fact]
    public void CalculateAverageUnitPrice_数量为零返回null()
    {
        Assert.Null(WarehouseProductFlowAnalysisService.CalculateAverageUnitPrice(0, 10m));
    }

    [Fact]
    public void BatchCodes_显式商品按500个分批并去重()
    {
        var codes = Enumerable.Range(0, 1_201)
            .Select(index => $"P{index:D4}")
            .Concat(new[] { "P0001", " p0002 ", string.Empty });

        var batches = WarehouseProductFlowAnalysisService.BatchCodes(codes);

        Assert.Equal(new[] { 500, 500, 201 }, batches.Select(batch => batch.Count));
        Assert.Equal(1, batches.SelectMany(batch => batch).Count(code => code == "P0001"));
        Assert.Equal(1, batches.SelectMany(batch => batch).Count(code => code == "P0002"));
    }

    [Fact]
    public void ApplySelection_included只保留圈定商品()
    {
        var result = WarehouseProductFlowAnalysisService.ApplySelection(
            new[] { "A", "B", "C" },
            new WarehouseProductFlowAnalysisSelectionDto
            {
                Mode = "included",
                IncludedProductCodes = new List<string> { "A", "C" },
            }
        );
        Assert.Equal(new[] { "A", "C" }, result);
    }

    [Fact]
    public void ApplySelection_allFiltered移除排除商品()
    {
        var result = WarehouseProductFlowAnalysisService.ApplySelection(
            new[] { "A", "B", "C" },
            new WarehouseProductFlowAnalysisSelectionDto
            {
                Mode = "allFiltered",
                ExcludedProductCodes = new List<string> { "B" },
            }
        );
        Assert.Equal(new[] { "A", "C" }, result);
    }

    [Fact]
    public void BuildDailySeries_补齐自然日并保留负销量()
    {
        var series = WarehouseProductFlowAnalysisService.BuildDailySeries(
            new[]
            {
                new WarehouseProductFlowAnalysisDailyAggregateRow
                {
                    Date = new DateTime(2026, 8, 17),
                    NetSalesQuantity = -2,
                    NetSalesAmount = -6m,
                },
            },
            new DateTime(2026, 8, 16),
            new DateTime(2026, 8, 18)
        );

        Assert.Equal(3, series.Count);
        Assert.Equal(0, series[0].NetSalesQuantity);
        Assert.Equal(0, series[0].OrderedQuantity);
        Assert.Null(series[0].AverageUnitPrice);
        Assert.Equal(-2, series[1].NetSalesQuantity);
        Assert.Equal(3m, series[1].AverageUnitPrice);
    }

    [Fact]
    public void SortAndPageCandidates_缺货号排后且默认货号升序()
    {
        var rows = new List<WarehouseProductFlowCandidateDto>
        {
            new() { ProductCode = "P3" },
            new() { ProductCode = "P1", ItemNumber = "B" },
            new() { ProductCode = "P2", ItemNumber = "A" },
        };

        var page = WarehouseProductFlowAnalysisService.SortAndPageCandidates(
            rows,
            pageNumber: 1,
            pageSize: 3,
            sortBy: null,
            sortDirection: null
        );

        Assert.Equal(new[] { "P2", "P1", "P3" }, page.Items.Select(row => row.ProductCode));
    }

    [Fact]
    public void SortAndPageProducts_默认净销量降序且商品代码稳定打破平局()
    {
        var rows = new List<WarehouseProductFlowProductDto>
        {
            new() { ProductCode = "B", Metrics = new WarehouseProductFlowMetricsDto { NetSalesQuantity = 10 } },
            new() { ProductCode = "A", Metrics = new WarehouseProductFlowMetricsDto { NetSalesQuantity = 10 } },
            new() { ProductCode = "C", Metrics = new WarehouseProductFlowMetricsDto { NetSalesQuantity = 5 } },
        };

        var page = WarehouseProductFlowAnalysisService.SortAndPageProducts(
            rows,
            pageNumber: 1,
            pageSize: 3,
            sortBy: null,
            sortDirection: null
        );

        Assert.Equal(new[] { "A", "B", "C" }, page.Items.Select(row => row.ProductCode));
    }

    [Fact]
    public void SortAndPageProducts_货号升序且缺货号排后()
    {
        var rows = new List<WarehouseProductFlowProductDto>
        {
            new() { ProductCode = "P1", ItemNumber = "B" },
            new() { ProductCode = "P2", ItemNumber = "A" },
            new() { ProductCode = "P3" },
        };

        var page = WarehouseProductFlowAnalysisService.SortAndPageProducts(rows, 1, 3, "itemNumber", "asc");

        Assert.Equal(new[] { "P2", "P1", "P3" }, page.Items.Select(row => row.ProductCode));
    }

    [Fact]
    public void ResolveCurrentProductCode_缺少商品代码抛出参数错误()
    {
        var ex = Assert.Throws<WarehouseProductFlowAnalysisValidationException>(() =>
            WarehouseProductFlowAnalysisService.ResolveCurrentProductCode(
                new WarehouseProductFlowAnalysisRequest()
            )
        );
        Assert.Contains("currentProductCode", ex.Message);
    }

    [Fact]
    public async Task Controller_ReportsView别名不能绕过精确权限()
    {
        var service = new Mock<IWarehouseProductFlowAnalysisService>();
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(x => x.UserHasExactPermissionAsync(
                "user-1",
                Permissions.Reports.ProductMovementView
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(false));

        var controller = CreateController(service.Object, roleService.Object, "user-1");
        var result = await controller.GetSummary(
            new WarehouseProductFlowAnalysisRequest
            {
                Periods = CreatePeriods(new DateTime(2026, 8, 1), new DateTime(2026, 8, 18)),
            }
        );

        Assert.IsType<ForbidResult>(result);
        service.Verify(
            x => x.GetSummaryAsync(
                It.IsAny<WarehouseProductFlowAnalysisRequest>(),
                It.IsAny<List<string>?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Controller_超级管理员别名按全分店解析()
    {
        List<string>? capturedBranchCodes = null;
        var service = new Mock<IWarehouseProductFlowAnalysisService>();
        service
            .Setup(x => x.GetSummaryAsync(
                It.IsAny<WarehouseProductFlowAnalysisRequest>(),
                It.IsAny<List<string>?>()
            ))
            .Callback<WarehouseProductFlowAnalysisRequest, List<string>?>((_, codes) => capturedBranchCodes = codes)
            .ReturnsAsync(ApiResponse<WarehouseProductFlowAnalysisSummaryDto>.OK(
                new WarehouseProductFlowAnalysisSummaryDto()
            ));

        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(x => x.UserHasExactPermissionAsync("user-1", Permissions.Reports.ProductMovementView))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(x => x.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(
                new UserPermissionSnapshotDto
                {
                    UserGuid = "user-1",
                    RoleNames = new List<string> { "超级管理员" },
                    PermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                    ExactPermissionCodes = new List<string> { Permissions.Reports.ProductMovementView },
                }
            ));

        var controller = CreateController(service.Object, roleService.Object, "user-1", "User");
        var result = await controller.GetSummary(
            new WarehouseProductFlowAnalysisRequest
            {
                Periods = CreatePeriods(new DateTime(2026, 8, 1), new DateTime(2026, 8, 18)),
            }
        );

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(capturedBranchCodes);
    }

    [Fact]
    public async Task Controller_普通用户严格限授权分店()
    {
        List<string>? capturedBranchCodes = null;
        var service = new Mock<IWarehouseProductFlowAnalysisService>();
        service
            .Setup(x => x.GetSummaryAsync(
                It.IsAny<WarehouseProductFlowAnalysisRequest>(),
                It.IsAny<List<string>?>()
            ))
            .Callback<WarehouseProductFlowAnalysisRequest, List<string>?>((_, codes) => capturedBranchCodes = codes)
            .ReturnsAsync(ApiResponse<WarehouseProductFlowAnalysisSummaryDto>.OK(
                new WarehouseProductFlowAnalysisSummaryDto()
            ));

        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(x => x.UserHasExactPermissionAsync("user-1", Permissions.Reports.ProductMovementView))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(x => x.GetUserPermissionSnapshotAsync("user-1"))
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
            .Setup(x => x.GetUserByGuidAsync("user-1"))
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

        var controller = CreateController(service.Object, roleService.Object, "user-1", "User", userService.Object);
        var result = await controller.GetSummary(
            new WarehouseProductFlowAnalysisRequest
            {
                Periods = CreatePeriods(new DateTime(2026, 8, 1), new DateTime(2026, 8, 18)),
            }
        );

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new[] { "B1", "B2" }, capturedBranchCodes!.OrderBy(code => code));
    }

    private static WarehouseProductFlowAnalysisController CreateController(
        IWarehouseProductFlowAnalysisService service,
        IRoleService roleService,
        string userGuid,
        string? roleClaim = null,
        IUserService? userService = null
    )
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userGuid) };
        if (!string.IsNullOrWhiteSpace(roleClaim))
            claims.Add(new Claim(ClaimTypes.Role, roleClaim!));

        return new WarehouseProductFlowAnalysisController(
            service,
            NullLogger<WarehouseProductFlowAnalysisController>.Instance,
            userService ?? Mock.Of<IUserService>(),
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

    private static WarehouseProductFlowPeriodsDto CreatePeriods(DateTime startDate, DateTime endDate)
    {
        return new WarehouseProductFlowPeriodsDto
        {
            ContainerPeriod = new WarehouseProductFlowDatePeriodDto
            {
                StartDate = startDate,
                EndDate = endDate,
            },
            OrderShipmentPeriod = new WarehouseProductFlowDatePeriodDto
            {
                StartDate = startDate,
                EndDate = endDate,
            },
            SalesPeriod = new WarehouseProductFlowDatePeriodDto
            {
                StartDate = startDate,
                EndDate = endDate,
            },
        };
    }
}
