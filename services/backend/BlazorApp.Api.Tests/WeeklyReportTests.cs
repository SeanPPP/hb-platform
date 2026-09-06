using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Cache;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WeeklyReportTests : IDisposable
{
    private readonly SqliteConnection _localConnection = new("Data Source=:memory:");
    private readonly SqliteConnection _posmConnection = new("Data Source=:memory:");
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _posmDb;

    public WeeklyReportTests()
    {
        _localConnection.Open();
        _posmConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _posmDb = new SqlSugarClient(CreateConnectionConfig(_posmConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(
            typeof(StoreSalesStatistic),
            typeof(SalesStatisticRefreshState)
        );
        _posmDb.CodeFirst.InitTables(typeof(SalesOrder), typeof(POSM_设备注册信息表));
        CreateScheduledTaskLogTable(_localDb);
    }

    [Fact]
    public void 周层级接口要求报表查看权限()
    {
        var action = typeof(SalesDashboardController).GetMethod(
            nameof(SalesDashboardController.GetWeeklyPerformanceHierarchy)
        );

        var policy = Assert.Single(
            action!.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => !string.IsNullOrWhiteSpace(attribute.Policy)
        );
        Assert.Equal(Permissions.Reports.View, policy.Policy);
    }

    public static IEnumerable<object[]> 无效周层级日期参数()
    {
        yield return new object[]
        {
            new DateTime(2026, 1, 2),
            new DateTime(2026, 1, 1),
            null!,
            null!,
            CompareMode.ByDate,
        };
        yield return new object[]
        {
            new DateTime(2026, 1, 1),
            new DateTime(2027, 1, 2),
            null!,
            null!,
            CompareMode.ByDate,
        };
        yield return new object[]
        {
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            new DateTime(2025, 1, 1),
            null!,
            CompareMode.ByDate,
        };
        yield return new object[]
        {
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            null!,
            new DateTime(2025, 1, 7),
            CompareMode.ByDate,
        };
        yield return new object[]
        {
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            new DateTime(2025, 1, 7),
            new DateTime(2025, 1, 1),
            CompareMode.ByDate,
        };
        yield return new object[]
        {
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            new DateTime(2025, 1, 1),
            new DateTime(2025, 1, 8),
            CompareMode.ByDate,
        };
        yield return new object[]
        {
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            null!,
            null!,
            (CompareMode)999,
        };
    }

    [Theory]
    [MemberData(nameof(无效周层级日期参数))]
    public async Task 周层级接口拒绝无效日期参数并且不调用服务(
        DateTime startDate,
        DateTime endDate,
        DateTime? compareStartDate,
        DateTime? compareEndDate,
        CompareMode compareMode
    )
    {
        var service = new Mock<ISalesDashboardReactService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, CreateUserService("S1"));

        var action = await controller.GetWeeklyPerformanceHierarchy(
            startDate,
            endDate,
            compareStartDate,
            compareEndDate,
            compareMode,
            new List<string> { "S1" }
        );

        Assert.IsType<BadRequestObjectResult>(action);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 周层级接口接受366天且等长的成对比较范围()
    {
        var service = new Mock<ISalesDashboardReactService>();
        service
            .Setup(item => item.GetWeeklyPerformanceHierarchyAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ))
            .ReturnsAsync((
                new ExecutiveReportResultDto<WeeklyPerformanceHierarchyDto>(),
                "test-version"
            ));
        var controller = CreateController(service.Object, CreateUserService("S1"));

        var action = await controller.GetWeeklyPerformanceHierarchy(
            new DateTime(2026, 1, 1),
            new DateTime(2027, 1, 1),
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            CompareMode.ByDate,
            new List<string> { "S1" }
        );

        Assert.IsType<OkObjectResult>(action);
        service.Verify(item => item.GetWeeklyPerformanceHierarchyAsync(
            It.Is<DateRangeDto>(range =>
                range.StartDate == new DateTime(2026, 1, 1)
                && range.EndDate == new DateTime(2027, 1, 1)
                && range.CompareStartDate == new DateTime(2024, 1, 1)
                && range.CompareEndDate == new DateTime(2024, 12, 31)
            ),
            It.Is<List<string>?>(codes => codes != null && codes.SequenceEqual(new[] { "S1" }))
        ));
    }

    [Fact]
    public async Task 周层级接口只把用户授权分店交给服务()
    {
        var service = new Mock<ISalesDashboardReactService>();
        service
            .Setup(item => item.GetWeeklyPerformanceHierarchyAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ))
            .ReturnsAsync((
                new ExecutiveReportResultDto<WeeklyPerformanceHierarchyDto>(),
                "test-version"
            ));
        var controller = CreateController(service.Object, CreateUserService("S1"));

        await controller.GetWeeklyPerformanceHierarchy(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            branchCodes: new List<string> { "S1", "S2" }
        );

        service.Verify(item => item.GetWeeklyPerformanceHierarchyAsync(
            It.IsAny<DateRangeDto>(),
            It.Is<List<string>?>(codes => codes != null && codes.SequenceEqual(new[] { "S1" }))
        ));
    }

    [Fact]
    public async Task 周层级接口分店交集为空时返回类型化空数组且不查服务()
    {
        var service = new Mock<ISalesDashboardReactService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, CreateUserService("S1"));

        var action = await controller.GetWeeklyPerformanceHierarchy(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            branchCodes: new List<string> { "S2" }
        );

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Empty(ExtractAnonymousData<List<WeeklyPerformanceHierarchyDto>>(ok.Value));
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 周层级接口保持data数组并在根部返回完整性和缓存版本()
    {
        var service = new Mock<ISalesDashboardReactService>();
        service
            .Setup(item => item.GetWeeklyPerformanceHierarchyAsync(
                It.IsAny<DateRangeDto>(),
                It.IsAny<List<string>?>()
            ))
            .ReturnsAsync((
                new ExecutiveReportResultDto<WeeklyPerformanceHierarchyDto>
                {
                    Items = new List<WeeklyPerformanceHierarchyDto>
                    {
                        new() { Key = "w2026-01", Level = "week", Hierarchy = "2026-W01" },
                    },
                    StatisticsPending = true,
                    StatisticsExpectedItemCount = 2,
                    StatisticsSnapshotItemCount = 1,
                },
                "stats-v2"
            ));
        var controller = CreateController(service.Object, CreateUserService("S1"));

        var action = await controller.GetWeeklyPerformanceHierarchy(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 7),
            branchCodes: new List<string> { "S1" }
        );

        var value = Assert.IsType<OkObjectResult>(action).Value;
        Assert.Single(ExtractAnonymousData<List<WeeklyPerformanceHierarchyDto>>(value));
        Assert.True(ExtractAnonymousProperty<bool>(value, "statisticsPending"));
        Assert.Equal("Pending", ExtractAnonymousProperty<string>(value, "statisticStatus"));
        Assert.Equal("stats-v2", ExtractAnonymousProperty<string>(value, "cacheVersion"));
    }

    [Fact]
    public async Task 周层级按显式ByDate区间逐日偏移而不是自行寻找去年同星期()
    {
        await SeedAsync(new DateTime(2026, 1, 1), 100m, 5);
        await SeedAsync(new DateTime(2025, 1, 1), 40m, 2);
        await SeedAsync(new DateTime(2025, 1, 2), 999m, 9);
        var service = CreateService();

        var result = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 1),
                CompareStartDate = new DateTime(2025, 1, 1),
                CompareEndDate = new DateTime(2025, 1, 1),
                CompareMode = CompareMode.ByDate,
            },
            new List<string> { "S1" }
        );

        var date = Assert.Single(
            Assert.Single(Assert.Single(result.Report.Items).Children!).Children!
        );
        Assert.Equal(40m, date.RevenueLY);
        Assert.Equal(2, date.OrdersLY);
        Assert.False(result.Report.StatisticsPending);
        Assert.NotEmpty(result.CacheVersion);
    }

    [Fact]
    public async Task 周层级缓存按显式比较区间隔离()
    {
        await SeedAsync(new DateTime(2026, 7, 1), 100m, 5);
        await SeedAsync(new DateTime(2025, 7, 1), 10m, 1);
        await SeedAsync(new DateTime(2024, 7, 1), 20m, 2);
        var service = CreateService();

        var first = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2025, 7, 1),
                CompareEndDate = new DateTime(2025, 7, 1),
                CompareMode = CompareMode.ByDate,
            },
            new List<string> { "S1" }
        );
        var second = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 7, 1),
                EndDate = new DateTime(2026, 7, 1),
                CompareStartDate = new DateTime(2024, 7, 1),
                CompareEndDate = new DateTime(2024, 7, 1),
                CompareMode = CompareMode.ByDate,
            },
            new List<string> { "S1" }
        );

        Assert.Equal(10m, Assert.Single(first.Report.Items).RevenueLY);
        Assert.Equal(20m, Assert.Single(second.Report.Items).RevenueLY);
    }

    [Fact]
    public async Task 周层级跨ISO年边界仍按显式范围拆周并逐日配对()
    {
        await SeedAsync(new DateTime(2025, 12, 28), 100m, 5);
        await SeedAsync(new DateTime(2025, 12, 29), 200m, 8);
        await SeedAsync(new DateTime(2024, 12, 29), 40m, 2);
        await SeedAsync(new DateTime(2024, 12, 30), 80m, 4);
        var service = CreateService();

        var result = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2025, 12, 28),
                EndDate = new DateTime(2025, 12, 29),
                CompareStartDate = new DateTime(2024, 12, 29),
                CompareEndDate = new DateTime(2024, 12, 30),
                CompareMode = CompareMode.ByWeek,
            },
            new List<string> { "S1" }
        );

        Assert.Equal(2, result.Report.Count);
        Assert.Equal(40m, Assert.Single(result.Report, row => row.Hierarchy == "2025-W52").RevenueLY);
        Assert.Equal(80m, Assert.Single(result.Report, row => row.Hierarchy == "2026-W01").RevenueLY);
    }

    [Fact]
    public async Task 周层级同期独有日期和门店也映射回本期并补零()
    {
        await SeedAsync(new DateTime(2026, 1, 2), 50m, 2, "S1", "分店一");
        await SeedAsync(new DateTime(2025, 1, 1), 100m, 4, "S1", "分店一");
        await SeedAsync(new DateTime(2025, 1, 2), 80m, 3, "S2", "分店二");
        var service = CreateService();

        var result = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 2),
                CompareStartDate = new DateTime(2025, 1, 1),
                CompareEndDate = new DateTime(2025, 1, 2),
                CompareMode = CompareMode.ByDate,
            },
            new List<string> { "S1", "S2" }
        );

        var week = Assert.Single(result.Report.Items);
        Assert.Equal(50m, week.Revenue);
        Assert.Equal(180m, week.RevenueLY);
        var s1 = Assert.Single(week.Children!, row => row.Hierarchy == "分店一");
        var compareOnlyDate = Assert.Single(s1.Children!, row => row.Hierarchy == "2026-01-01");
        Assert.Equal(0m, compareOnlyDate.Revenue);
        Assert.Equal(100m, compareOnlyDate.RevenueLY);
        var s2 = Assert.Single(week.Children!, row => row.Hierarchy == "分店二");
        Assert.Equal(0m, s2.Revenue);
        Assert.Equal(80m, s2.RevenueLY);
    }

    [Fact]
    public async Task 周层级服务异常不得伪装成成功空数据()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = new DateTime(2026, 1, 2),
                EndDate = new DateTime(2026, 1, 1),
            },
            new List<string> { "S1" }
        ));
    }

    [Fact]
    public async Task 周层级缺失统计返回Pending而不是把空快照伪装Fresh()
    {
        var date = new DateTime(2026, 8, 1);
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = "weekly-pending",
            OrderTime = date.AddHours(10),
            BranchCode = "S1",
            DeviceCode = "D1",
            Status = 1,
        }).ExecuteCommandAsync();
        var service = CreateService();

        var result = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto { StartDate = date, EndDate = date },
            new List<string> { "S1" }
        );

        Assert.True(result.Report.StatisticsPending);
        Assert.True(
            result.Report.StatisticsExpectedItemCount
                > result.Report.StatisticsSnapshotItemCount
        );
    }

    [Fact]
    public async Task 周层级本期超过35天仍检查后续分段的统计缺口()
    {
        var startDate = new DateTime(2026, 6, 1);
        var missingDate = startDate.AddDays(35);
        await SeedSourceOrderAsync("weekly-current-segment", missingDate);
        var service = CreateService();

        var result = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto { StartDate = startDate, EndDate = missingDate },
            new List<string> { "S1" }
        );

        Assert.True(result.Report.StatisticsPending);
    }

    [Fact]
    public async Task 周层级同期超过35天仍检查后续分段的统计缺口()
    {
        var startDate = new DateTime(2026, 6, 1);
        var endDate = startDate.AddDays(35);
        var compareStartDate = new DateTime(2024, 6, 1);
        var compareEndDate = compareStartDate.AddDays(35);
        await SeedSourceOrderAsync("weekly-compare-segment", compareEndDate);
        var service = CreateService();

        var result = await service.GetWeeklyPerformanceHierarchyAsync(
            new DateRangeDto
            {
                StartDate = startDate,
                EndDate = endDate,
                CompareStartDate = compareStartDate,
                CompareEndDate = compareEndDate,
                CompareMode = CompareMode.ByDate,
            },
            new List<string> { "S1" }
        );

        Assert.True(result.Report.StatisticsPending);
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

    private async Task SeedAsync(
        DateTime date,
        decimal revenue,
        int orders,
        string branchCode = "S1",
        string branchName = "分店一"
    )
    {
        await _localDb.Insertable(new StoreSalesStatistic
        {
            Date = date,
            BranchCode = branchCode,
            BranchName = branchName,
            TotalAmount = revenue,
            TotalQuantity = orders,
            OrderCount = orders,
            CustomerCount = orders,
            AverageOrderValue = orders > 0 ? revenue / orders : 0,
        }).ExecuteCommandAsync();
        await _localDb.Storageable(new SalesStatisticRefreshState
        {
            StatisticType = SalesStatisticType.StoreSales,
            Date = date.Date,
            Status = SalesStatisticRefreshStatus.Fresh,
            CompletedAtUtc = DateTime.UtcNow,
            LastAggregatedAtUtc = DateTime.UtcNow,
        }).ExecuteCommandAsync();
    }

    private async Task SeedSourceOrderAsync(string orderGuid, DateTime date)
    {
        await _posmDb.Insertable(new SalesOrder
        {
            OrderGuid = orderGuid,
            OrderTime = date.AddHours(10),
            BranchCode = "S1",
            DeviceCode = "D1",
            Status = 1,
        }).ExecuteCommandAsync();
    }

    private static SalesDashboardController CreateController(
        ISalesDashboardReactService service,
        IUserService userService
    )
    {
        var controller = new SalesDashboardController(
            service,
            NullLogger<SalesDashboardController>.Instance,
            userService,
            Mock.Of<ISalesDashboardCacheWarmer>(),
            Mock.Of<IRoleService>()
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "user-1") },
                    "TestAuth"
                )),
            },
        };
        return controller;
    }

    private static IUserService CreateUserService(params string[] storeCodes)
    {
        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserByGuidAsync("user-1"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "user-1",
                Username = "tester",
                Stores = storeCodes.Select(code => new UserStoreDto { StoreCode = code }).ToList(),
            }));
        return userService.Object;
    }

    private static T ExtractAnonymousData<T>(object? value)
    {
        var property = value?.GetType().GetProperty(
            "data",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
        );
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(value));
    }

    private static T ExtractAnonymousProperty<T>(object? value, string name)
    {
        var property = value?.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
        );
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(value));
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

    private static void CreateScheduledTaskLogTable(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(
            """
            CREATE TABLE IF NOT EXISTS ScheduledTaskLog (
                Id TEXT PRIMARY KEY,
                TaskType TEXT NOT NULL,
                TaskParameters TEXT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                DurationMs INTEGER NULL,
                ErrorMessage TEXT NULL,
                RetryCount INTEGER NOT NULL,
                CanRetry INTEGER NOT NULL,
                ScheduledTime TEXT NOT NULL,
                TriggeredBy TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NULL
            );
            """
        );
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
        _localDb.Dispose();
        _posmDb.Dispose();
        _localConnection.Dispose();
        _posmConnection.Dispose();
    }
}
