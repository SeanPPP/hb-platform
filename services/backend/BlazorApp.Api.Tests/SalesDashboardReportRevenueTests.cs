using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SalesDashboardReportRevenueTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _posmDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _posmConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public SalesDashboardReportRevenueTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _posmDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _posmConnection = new SqliteConnection($"Data Source={_posmDbPath}");
        _localConnection.Open();
        _posmConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(typeof(StoreSalesStatistic), typeof(HourlySalesStatistic));
    }

    [Fact]
    public async Task GetBranchDailyPerformanceAsync_使用统计表并按对比区间偏移配对()
    {
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 1), "S1", "分店一", 100m, 5);
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 2), "S1", "分店一", 150m, 7);
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 1), "S2", "分店二", 999m, 20);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 1), "S1", "分店一", 80m, 4);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 2), "S1", "分店一", 200m, 8);
        var service = CreateService();

        var result = await service.GetBranchDailyPerformanceAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 2),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 2),
            },
            new List<string> { "S1" }
        );

        Assert.Collection(
            result,
            row =>
            {
                Assert.Equal(new DateTime(2026, 7, 1), row.Date);
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal(100m, row.Revenue);
                Assert.Equal(80m, row.RevenueLY);
                Assert.Equal(5, row.OrderCount);
                Assert.Equal(4, row.OrderCountLY);
            },
            row =>
            {
                Assert.Equal(new DateTime(2026, 7, 2), row.Date);
                Assert.Equal("S1", row.BranchCode);
                Assert.Equal(150m, row.Revenue);
                Assert.Equal(200m, row.RevenueLY);
                Assert.Equal(7, row.OrderCount);
                Assert.Equal(8, row.OrderCountLY);
            }
        );
    }

    [Fact]
    public async Task GetExecutiveBranchPerformanceAsync_按分店代码聚合避免名称变化拆行()
    {
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 1), "S1", "Store A", 100m, 5);
        await SeedStoreSalesStatisticAsync(new DateTime(2026, 7, 2), "S1", "Store B", 150m, 5);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 1), "S1", "Store Old", 80m, 4);
        await SeedStoreSalesStatisticAsync(new DateTime(2025, 7, 2), "S1", "Store Old", 70m, 3);
        var service = CreateService();

        var result = await service.GetExecutiveBranchPerformanceAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 2),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 2),
            },
            branchCodes: new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("S1", row.BranchCode);
        Assert.Equal(250m, row.Revenue);
        Assert.Equal(150m, row.RevenueLY);
        Assert.Equal(10, row.OrderCount);
        Assert.Equal(7, row.OrderCountLY);
        Assert.Equal(25m, row.Aov);
        Assert.NotEmpty(row.BranchName);
    }

    [Fact]
    public async Task GetExecutiveHourlyTrafficAsync_使用小时统计表按分店和小时聚合()
    {
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 1), 9, "S1", "Store A", 100m, 4);
        await SeedHourlySalesStatisticAsync(new DateTime(2026, 7, 2), 9, "S1", "Store B", 50m, 2);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 1), 9, "S1", "Store Old", 80m, 3);
        await SeedHourlySalesStatisticAsync(new DateTime(2025, 7, 2), 9, "S1", "Store Old", 20m, 1);
        var service = CreateService();

        var result = await service.GetExecutiveHourlyTrafficAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 2),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 2),
            },
            new List<string> { "S1" }
        );

        var row = Assert.Single(result);
        Assert.Equal("09:00", row.Hour);
        Assert.Equal("S1", row.BranchCode);
        Assert.Equal(150m, row.Revenue);
        Assert.Equal(100m, row.RevenueLY);
        Assert.Equal(6, row.OrderCount);
        Assert.Equal(4, row.OrderCountLY);
        Assert.Equal(100, row.Percentage);
        Assert.True(row.IsPeak);
    }

    [Fact]
    public async Task GetBranchDailyPerformance_普通用户请求无权限分店返回空数组()
    {
        var serviceMock = new Mock<ISalesDashboardReactService>();
        var controller = CreateController(
            serviceMock.Object,
            CreateUserService(new[] { "S1" })
        );

        var response = await controller.GetBranchDailyPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7),
            branchCodes: new List<string> { "S2" }
        );

        var data = ExtractAnonymousData<List<object>>(AssertOk(response).Value);
        Assert.Empty(data);
        serviceMock.Verify(
            service => service.GetBranchDailyPerformanceAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task GetBranchDailyPerformance_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE StoreSalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetBranchDailyPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetExecutiveBranchPerformance_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE StoreSalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetExecutiveBranchPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetExecutiveHourlyTraffic_统计表查询失败时返回服务器错误()
    {
        await _localDb.Ado.ExecuteCommandAsync("DROP TABLE HourlySalesStatistic");
        var controller = CreateController(CreateService(), CreateUserService(new[] { "S1" }));

        var response = await controller.GetExecutiveHourlyTraffic(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            branchCodes: new List<string> { "S1" }
        );

        var objectResult = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetExecutiveBranchPerformance_普通用户未传分店时只查用户分店()
    {
        List<string>? capturedBranchCodes = null;
        int? capturedTopN = -1;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(service => service.GetExecutiveBranchPerformanceAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<int?>(),
                It.IsAny<List<string>?>()
            ))
            .Callback<DateRangeDto, int?, List<string>?>((_, topN, branchCodes) =>
            {
                capturedTopN = topN;
                capturedBranchCodes = branchCodes;
            })
            .ReturnsAsync(new List<ExecutiveBranchPerformanceDto>());
        var controller = CreateController(serviceMock.Object, CreateUserService(new[] { "S1", "S3" }));

        await controller.GetExecutiveBranchPerformance(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 7)
        );

        Assert.Equal(new[] { "S1", "S3" }, capturedBranchCodes);
        Assert.Null(capturedTopN);
    }

    [Fact]
    public async Task GetExecutiveHourlyTraffic_普通用户请求分店时取权限交集()
    {
        List<string>? capturedBranchCodes = null;
        var serviceMock = new Mock<ISalesDashboardReactService>();
        serviceMock
            .Setup(service => service.GetExecutiveHourlyTrafficAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ))
            .Callback<DateRangeDto, List<string>?>((_, branchCodes) =>
                capturedBranchCodes = branchCodes
            )
            .ReturnsAsync(new List<ExecutiveHourlyTrafficDto>());
        var controller = CreateController(serviceMock.Object, CreateUserService(new[] { "S1", "S3" }));

        await controller.GetExecutiveHourlyTraffic(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1),
            branchCodes: new List<string> { "S1", "S2" }
        );

        var branchCode = Assert.Single(capturedBranchCodes!);
        Assert.Equal("S1", branchCode);
    }

    private async Task SeedStoreSalesStatisticAsync(
        DateTime date,
        string branchCode,
        string branchName,
        decimal totalAmount,
        int orderCount
    )
    {
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date,
            BranchCode = branchCode,
            BranchName = branchName,
            TotalAmount = totalAmount,
            OrderCount = orderCount,
            AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0,
            TotalQuantity = orderCount,
            CustomerCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedHourlySalesStatisticAsync(
        DateTime date,
        int hour,
        string branchCode,
        string branchName,
        decimal totalAmount,
        int orderCount
    )
    {
        await _localDb.Insertable(new HourlySalesStatistic
        {
            Date = date,
            Hour = hour,
            BranchCode = branchCode,
            BranchName = branchName,
            TotalAmount = totalAmount,
            OrderCount = orderCount,
            AverageOrderValue = orderCount > 0 ? totalAmount / orderCount : 0,
            TotalQuantity = orderCount,
            CustomerCount = orderCount,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private SalesDashboardReactService CreateService()
    {
        return new SalesDashboardReactService(
            CreateSqlSugarContext(_localDb),
            CreatePosmSqlSugarContext(_posmDb),
            Mock.Of<IMapper>(),
            NullLogger<SalesDashboardReactService>.Instance,
            new MemoryCache(new MemoryCacheOptions())
        );
    }

    private static IUserService CreateUserService(IEnumerable<string> storeCodes)
    {
        var stores = storeCodes
            .Select(code => new UserStoreDto { StoreCode = code })
            .ToList();
        var userServiceMock = new Mock<IUserService>();
        userServiceMock
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = stores,
            }));
        return userServiceMock.Object;
    }

    private static SalesDashboardController CreateController(
        ISalesDashboardReactService service,
        IUserService userService
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
            },
            "TestAuth"
        ));

        var controller = new SalesDashboardController(
            service,
            NullLogger<SalesDashboardController>.Instance,
            userService,
            Mock.Of<ISalesDashboardCacheWarmer>()
        );
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static T ExtractAnonymousData<T>(object? value)
    {
        Assert.NotNull(value);
        var dataProperty = value!.GetType().GetProperty("data");
        Assert.NotNull(dataProperty);
        var data = dataProperty!.GetValue(value);
        return Assert.IsType<T>(data);
    }

    private static OkObjectResult AssertOk(IActionResult result)
    {
        return Assert.IsType<OkObjectResult>(result);
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString)
    {
        return new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    private static POSMSqlSugarContext CreatePosmSqlSugarContext(ISqlSugarClient db)
    {
        var context = (POSMSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(POSMSqlSugarContext));
        typeof(POSMSqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _localConnection.Dispose();
        _posmConnection.Dispose();
        if (File.Exists(_localDbPath)) SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        if (File.Exists(_posmDbPath)) SqliteTempFileCleanup.DeleteIfExists(_posmDbPath);
    }
}
