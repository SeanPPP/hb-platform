using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 仅用于本机隔离 SQL Server 的 90 天页面容量测量和临时 HTTP 调试。
/// 固定测试用户明确拥有 S01-S58；不使用管理员的“全部门店”捷径。
/// </summary>
public sealed partial class BatchProductSalesAnalysisSqlServerIntegrationTests
{
    private static readonly DateTime HarnessStartDate = new(2026, 6, 10);
    private static readonly DateTime HarnessEndDate = new(2026, 9, 7);
    private static readonly JsonSerializerOptions HarnessJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [BatchSalesOverviewBenchmarkFact]
    public async Task Overview_Performance_SQLServer_100商品_58店_90天()
    {
        var fixture = await Seed90Day100ProductFixtureAsync();
        var service = CreateAnalysisService();
        var query = new BatchProductSalesQueryRequestDto
        {
            ItemNumbers = fixture.Products.ToList(),
            StoreCodes = fixture.Stores.ToList(),
            StartDate = HarnessStartDate,
            EndDate = HarnessEndDate,
        };

        var summary = (await service.QueryAsync(query, fixture.Stores)).Data!;
        Assert.Equal(100 * 58 * 90 * 2m, summary.Overview.Metrics!.Quantity);
        Assert.Equal(100 * 58 * 90 * 9m, summary.Overview.Metrics.SalesAmount);
        Assert.Equal(58, summary.Overview.Branches.Count);

        var branch = new BatchProductSalesBranchOverviewRequestDto
        {
            ProductCodes = fixture.Products.ToList(),
            StoreCodes = fixture.Stores.ToList(),
            StartDate = HarnessStartDate,
            EndDate = HarnessEndDate,
            BranchCode = fixture.Stores[0],
            CoverageVersion = summary.Coverage.Version,
            ReadyDates = summary.Coverage.ReadyDates,
        };

        var queryMeasurement = await Measure90DayAsync("query", 20,
            () => service.QueryAsync(query, fixture.Stores));
        var branchMeasurement = await Measure90DayAsync("overview/branch", 20,
            () => service.GetBranchOverviewAsync(branch, fixture.Stores));

        var output = Environment.GetEnvironmentVariable("BATCH_SALES_BENCHMARK_OUTPUT_DIR")
            ?? Path.Combine(Path.GetTempPath(), $"hb-batch-90day-benchmark-{_id}");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "90day-100product-measurements.json"), JsonSerializer.Serialize(new
        {
            environment = "local isolated SQL Server; service calls only; excludes HTTP, browser and fixture seeding",
            authorization = "fixed test user with explicit S01-S58 scope",
            range = new { startDate = HarnessStartDate.ToString("yyyy-MM-dd"), endDate = HarnessEndDate.ToString("yyyy-MM-dd"), days = 90 },
            products = fixture.Products.Count,
            stores = fixture.Stores.Count,
            statisticRows = 100 * 58 * 90,
            snapshotRows = 100 * 90,
            targetsMs = new { queryFirstScreen = 5000, branchClick = 3000 },
            measurements = new[] { queryMeasurement, branchMeasurement },
        }, new JsonSerializerOptions(HarnessJson) { WriteIndented = true }));
    }

    [BatchSalesHttpHarnessFact]
    public async Task BatchProductSalesHttpHarness_90天100商品58店()
    {
        var fixture = await Seed90Day100ProductFixtureAsync();
        var port = GetHarnessPort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        MapHarnessEndpoints(app, fixture.Stores);
        app.MapGet("/healthz", () => Results.Ok(new
        {
            status = "ready",
            scope = fixture.Stores.Count,
            products = fixture.Products.Count,
            startDate = HarnessStartDate.ToString("yyyy-MM-dd"),
            endDate = HarnessEndDate.ToString("yyyy-MM-dd"),
        }));
        // 仅测试 harness 使用：让父任务主动结束时仍走 xUnit 的正常 fixture 清理，而不是杀死进程。
        app.MapPost("/shutdown", () =>
        {
            shutdown.TrySetResult();
            return Results.NoContent();
        });

        await app.StartAsync();
        try
        {
            // 父任务可据此等待后再用浏览器访问；不含连接字符串或其他凭据。
            Console.WriteLine($"BATCH_SALES_HTTP_HARNESS_READY http://127.0.0.1:{port}/healthz databases={CatalogName},{PosmName},{HbsName}");
            var seconds = GetHarnessDurationSeconds();
            await Task.WhenAny(shutdown.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private async Task<BatchSales90DayFixture> Seed90Day100ProductFixtureAsync()
    {
        await PrepareAnalysisSchemaAsync();
        var dates = Enumerable.Range(0, 90).Select(offset => HarnessStartDate.AddDays(offset)).ToArray();
        Assert.Equal(HarnessEndDate, dates[^1]);
        var products = Enumerable.Range(1, 100).Select(number => $"P{number:D3}").ToArray();
        var stores = Enumerable.Range(1, 58).Select(number => $"S{number:D2}").ToArray();

        await SeedProductsAsync(products.Select(code => (code, code, (string?)null)).ToArray());
        await _catalog!.Insertable(stores.Select(code => new Store
        {
            StoreCode = code,
            StoreName = $"Harness {code}",
            IsDeleted = false,
        }).ToList()).ExecuteCommandAsync();
        await _catalog.Insertable(dates.Select(day => State(day, "Fresh", $"harness-{day:yyyyMMdd}")).ToList()).ExecuteCommandAsync();
        await _catalog.Insertable(dates.Select(day => new BatchProductSalesDiscountRefreshState
        {
            Date = day,
            Status = "Fresh",
            RuleVersion = 1,
            StatisticsVersion = "harness-statistics",
            SourceVersion = "harness-source",
            RequestedAtUtc = day,
            NextAttemptAtUtc = day,
            CompletedAtUtc = day.AddHours(1),
            SnapshotCount = products.Length,
        }).ToList()).ExecuteCommandAsync();

        foreach (var batch in products.Chunk(20))
        {
            var statistics = new List<ProductStoreDailySalesStatistic>(batch.Length * dates.Length * stores.Length);
            var snapshots = new List<BatchProductSalesDiscountSnapshot>(batch.Length * dates.Length);
            foreach (var product in batch)
            foreach (var day in dates)
            {
                var rows = stores.Select(store => new BatchProductSalesAggregateRow
                {
                    ProductCode = product,
                    BranchCode = store,
                    Date = day,
                    Quantity = 2,
                    RegularQuantity = 1,
                    DiscountQuantity = 1,
                    SalesAmount = 9,
                    OriginalPriceMin = 5,
                    OriginalPriceMax = 5,
                    DiscountPriceMin = 4,
                    DiscountPriceMax = 4,
                }).ToList();
                statistics.AddRange(stores.Select(store => new ProductStoreDailySalesStatistic
                {
                    ProductCode = product,
                    BranchCode = store,
                    SupplierCode = "HARNESS",
                    Date = day,
                    TotalQuantity = 2,
                    TotalAmount = 9,
                    UpdateTime = day,
                }));
                snapshots.Add(new BatchProductSalesDiscountSnapshot
                {
                    Id = $"harness-{product}-{day:yyyyMMdd}",
                    ProductCode = product,
                    StartDate = day,
                    EndDate = day,
                    SnapshotFormat = 2,
                    SourceVersion = "harness-source",
                    Status = "Fresh",
                    RequestedAtUtc = day,
                    NextAttemptAtUtc = day,
                    CompletedAtUtc = day.AddHours(1),
                    StoreCodesJson = JsonSerializer.Serialize(stores),
                    PayloadJson = JsonSerializer.Serialize(rows),
                });
            }
            await _catalog.Fastest<ProductStoreDailySalesStatistic>().BulkCopyAsync(statistics);
            await _catalog.Fastest<BatchProductSalesDiscountSnapshot>().BulkCopyAsync(snapshots);
        }

        return new(products, stores);
    }

    private async Task<object> Measure90DayAsync<T>(string endpoint, int samples, Func<Task<T>> read)
    {
        await read(); // 预热不计入样本，避免将 JIT 与连接初始化混入页面指标。
        var walls = new List<double>();
        var sqlCounts = new List<int>();
        var sqlMilliseconds = new List<double>();
        var bytes = 0;
        for (var sample = 0; sample < samples; sample++)
        {
            var count = 0;
            var sqlMillisecondsThisSample = 0d;
            var stopwatch = new Stopwatch();
            _catalog!.Aop.OnLogExecuting = (_, _) => stopwatch.Restart();
            _catalog.Aop.OnLogExecuted = (_, _) =>
            {
                count++;
                sqlMillisecondsThisSample += stopwatch.Elapsed.TotalMilliseconds;
            };
            try
            {
                var wall = Stopwatch.StartNew();
                var result = await read();
                wall.Stop();
                walls.Add(wall.Elapsed.TotalMilliseconds);
                sqlCounts.Add(count);
                sqlMilliseconds.Add(sqlMillisecondsThisSample);
                bytes = JsonSerializer.SerializeToUtf8Bytes(result, HarnessJson).Length;
            }
            finally
            {
                _catalog.Aop.OnLogExecuting = null;
                _catalog.Aop.OnLogExecuted = null;
            }
        }
        var rawWalls = walls.ToArray();
        var rawSqlCounts = sqlCounts.ToArray();
        var rawSqlMilliseconds = sqlMilliseconds.ToArray();
        walls.Sort();
        sqlCounts.Sort();
        sqlMilliseconds.Sort();
        return new
        {
            endpoint,
            samples,
            medianWallMs = walls[samples / 2],
            p95WallMs = walls[(int)Math.Ceiling(samples * .95) - 1],
            medianSqlMs = sqlMilliseconds[samples / 2],
            medianSqlCount = sqlCounts[samples / 2],
            responseBytes = bytes,
            rawWallMs = rawWalls,
            rawSqlCounts,
            rawSqlMilliseconds,
        };
    }

    private void MapHarnessEndpoints(WebApplication app, IReadOnlyList<string> scope)
    {
        app.MapGet("/api/react/v1/dashboard/batch-product-sales-analysis/options",
            (HttpContext context) => ExecuteHarnessRequestAsync(context, scope, controller => controller.GetOptions()));
        app.MapPost("/api/react/v1/dashboard/batch-product-sales-analysis/query",
            (HttpContext context, BatchProductSalesQueryRequestDto request) => ExecuteHarnessRequestAsync(context, scope, controller => controller.Query(request)));
        app.MapPost("/api/react/v1/dashboard/batch-product-sales-analysis/detail",
            (HttpContext context, BatchProductSalesDetailRequestDto request) => ExecuteHarnessRequestAsync(context, scope, controller => controller.Detail(request)));
        app.MapPost("/api/react/v1/dashboard/batch-product-sales-analysis/overview/branch",
            (HttpContext context, BatchProductSalesBranchOverviewRequestDto request) => ExecuteHarnessRequestAsync(context, scope, controller => controller.BranchOverview(request)));
        app.MapPost("/api/react/v1/dashboard/batch-product-sales-analysis/overview/discounts",
            (HttpContext context, BatchProductSalesBranchOverviewRequestDto request) => ExecuteHarnessRequestAsync(context, scope, controller => controller.DiscountOverview(request)));
        app.MapPost("/api/react/v1/dashboard/batch-product-sales-analysis/export/detail",
            (HttpContext context, BatchProductSalesFollowupRequestDto request) => ExecuteHarnessRequestAsync(context, scope, controller => controller.ExportDetail(request)));
    }

    private async Task ExecuteHarnessRequestAsync(HttpContext context, IReadOnlyList<string> scope,
        Func<BatchProductSalesAnalysisController, Task<IActionResult>> action)
    {
        // 与生产请求 scope 一样，每个 HTTP 请求拥有独立 SqlSugarClient，不能并发共享 fixture 的连接状态。
        using var db = _catalog!.CopyNew();
        var queue = new Mock<IProductStoreDailyStatisticQueueService>();
        queue.Setup(value => value.EnqueueAsync(It.IsAny<IEnumerable<DateTime>>(), "batch-product-sales-analysis", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductStoreDailyRecalculationSubmitResult());
        queue.Setup(value => value.EnqueueYearBackfillAsync(It.IsAny<IEnumerable<DateTime>>(), "batch-product-sales-analysis", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductStoreDailyRecalculationSubmitResult());
        var service = new BatchProductSalesAnalysisService(db, queue.Object, NullLogger<BatchProductSalesAnalysisService>.Instance);
        var actionTimer = Stopwatch.StartNew();
        var responseWriteTimer = new Stopwatch();
        try
        {
            var result = await action(HarnessController(service, scope, context));
            actionTimer.Stop();
            responseWriteTimer.Start();
            await WriteActionAsync(context, Task.FromResult(result));
            responseWriteTimer.Stop();
        }
        finally
        {
            if (actionTimer.IsRunning) actionTimer.Stop();
            if (responseWriteTimer.IsRunning) responseWriteTimer.Stop();
            // 不记录请求体或凭据；此处只区分业务计算、响应写入以及客户端取消是否真正到达 Kestrel。
            Console.WriteLine($"BATCH_SALES_HTTP_REQUEST path={context.Request.Path} actionMs={actionTimer.Elapsed.TotalMilliseconds:F1} responseWriteMs={responseWriteTimer.Elapsed.TotalMilliseconds:F1} requestAborted={context.RequestAborted.IsCancellationRequested} status={context.Response.StatusCode}");
        }
    }

    private static BatchProductSalesAnalysisController HarnessController(BatchProductSalesAnalysisService service, IReadOnlyList<string> scope, HttpContext context)
    {
        var roles = new Mock<IRoleService>();
        roles.Setup(value => value.GetUserPermissionSnapshotAsync("batch-sales-harness-user"))
            .ReturnsAsync(ApiResponse<UserPermissionSnapshotDto>.OK(new UserPermissionSnapshotDto
            {
                UserGuid = "batch-sales-harness-user",
                RoleNames = ["User"],
                ExactPermissionCodes = [Permissions.SalesDashboard.BatchProductSalesView],
                PermissionCodes = [Permissions.SalesDashboard.BatchProductSalesView],
            }));
        var users = new Mock<IUserService>();
        users.Setup(value => value.GetUserByGuidAsync("batch-sales-harness-user"))
            .ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
            {
                UserGUID = "batch-sales-harness-user",
                Stores = scope.Select(store => new UserStoreDto { StoreCode = store }).ToList(),
            }));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "batch-sales-harness-user")], "harness"));
        return new BatchProductSalesAnalysisController(service, users.Object, roles.Object,
            NullLogger<BatchProductSalesAnalysisController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private static async Task WriteActionAsync(HttpContext context, Task<IActionResult> action)
    {
        var result = await action;
        switch (result)
        {
            case ObjectResult objectResult:
                context.Response.StatusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";
                await JsonSerializer.SerializeAsync(context.Response.Body, objectResult.Value, objectResult.Value?.GetType() ?? typeof(object), HarnessJson, context.RequestAborted);
                return;
            case FileContentResult file:
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = file.ContentType;
                if (!string.IsNullOrWhiteSpace(file.FileDownloadName))
                    context.Response.Headers.ContentDisposition = $"attachment; filename=\"{file.FileDownloadName}\"";
                await context.Response.Body.WriteAsync(file.FileContents, context.RequestAborted);
                return;
            case ForbidResult:
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            case EmptyResult:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            default:
                throw new InvalidOperationException($"Harness does not support {result.GetType().Name}.");
        }
    }

    private static int GetHarnessPort() => int.TryParse(Environment.GetEnvironmentVariable("BATCH_SALES_HTTP_HARNESS_PORT"), out var port)
        && port is >= 1024 and <= 65535 ? port : 5189;

    private static int GetHarnessDurationSeconds() => int.TryParse(Environment.GetEnvironmentVariable("BATCH_SALES_HTTP_HARNESS_SECONDS"), out var seconds)
        && seconds is >= 30 and <= 3600 ? seconds : 900;

    private sealed record BatchSales90DayFixture(IReadOnlyList<string> Products, IReadOnlyList<string> Stores);
}

internal sealed class BatchSalesHttpHarnessFactAttribute : FactAttribute
{
    public BatchSalesHttpHarnessFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BATCH_SALES_HTTP_HARNESS") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BATCH_SALES_SQLSERVER_TEST_CONNECTION")))
            Skip = "需显式设置 BATCH_SALES_HTTP_HARNESS=1 及本机隔离 SQL Server 连接。";
    }
}
