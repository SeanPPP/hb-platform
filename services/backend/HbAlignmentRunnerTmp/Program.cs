using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../BlazorApp.Api"));
var configuration = new ConfigurationBuilder()
    .SetBasePath(apiRoot)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});
services.Configure<ScheduledTaskOptions>(configuration.GetSection("ScheduledTasks"));
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUserService, CurrentUserService>();
services.AddScoped<SqlSugarContext>();
services.AddScoped<POSMSqlSugarContext>();
services.AddScoped<ScheduledTaskLeaseService>();
services.AddScoped<SalesStatisticsJobService>();
services.AddScoped<SalesStatisticsAlignmentService>();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

if (args.Length > 0 && args[0] == "clear-leases")
{
    var dates = args.Skip(1).Select(x => DateTime.Parse(x).Date.ToString("yyyy-MM-dd")).ToArray();
    var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
    var now = DateTime.UtcNow;
    var updated = await context.Db.Updateable<ScheduledTaskLease>()
        .SetColumns(x => x.Status == ScheduledTaskLeaseStatus.Failed)
        .SetColumns(x => x.LeaseUntilUtc == null)
        .SetColumns(x => x.CompletedAtUtc == now)
        .SetColumns(x => x.LastError == "本地补算中断后释放租约")
        .SetColumns(x => x.UpdatedAtUtc == now)
        .Where(x =>
            x.TaskType == SalesStatisticsAlignmentService.DailyFullRefreshLeaseTaskType
            && dates.Contains(x.ScopeKey)
            && x.Status == ScheduledTaskLeaseStatus.Running
        )
        .ExecuteCommandAsync();
    Console.WriteLine($"CLEAR_LEASES updated={updated} dates={string.Join(",", dates)}");
    return;
}

var offset = args.Length > 0 && args[0] == "alignment-only" ? 1 : 0;
var start = args.Length > offset ? DateTime.Parse(args[offset]).Date : new DateTime(2026, 6, 9);
var end = args.Length > offset + 1 ? DateTime.Parse(args[offset + 1]).Date : new DateTime(2026, 7, 8);
var concurrency = args.Length > offset + 2 ? int.Parse(args[offset + 2]) : 5;

var refreshSucceeded = true;
if (args.Length == 0 || args[0] != "alignment-only")
{
    var jobService = scope.ServiceProvider.GetRequiredService<SalesStatisticsJobService>();
    Console.WriteLine($"RUN_FULL_REFRESH {start:yyyy-MM-dd} {end:yyyy-MM-dd} concurrency={concurrency}");
    var result = await jobService.BatchFullRefreshConcurrent(start, end, concurrency);
    refreshSucceeded = result.Success;
    Console.WriteLine(
        $"FULL_REFRESH_RESULT success={result.Success} processed={result.ProcessedDays}/{result.TotalDays} skipped={result.SkippedDates.Count} failed={result.FailedDates.Count} message={result.Message}"
    );
    if (result.SkippedDates.Any())
    {
        Console.WriteLine($"SKIPPED_DATES {string.Join(",", result.SkippedDates)}");
    }
    if (result.FailedDates.Any())
    {
        Console.WriteLine($"FAILED_DATES {string.Join(",", result.FailedDates)}");
    }
}

var alignmentService = scope.ServiceProvider.GetRequiredService<SalesStatisticsAlignmentService>();
var alignment = await alignmentService.GetDailyAlignmentAsync(start, end);
Console.WriteLine(
    $"ALIGNMENT_OVERVIEW aligned={alignment.Overview.AlignedDays} abnormal={alignment.Overview.AbnormalDays} missing={alignment.Overview.MissingTableCount} maxAmountDiff={alignment.Overview.MaxAmountDifference} latestSourceWatermark={alignment.Overview.LatestSourceWatermark:yyyy-MM-dd HH:mm:ss}"
);
foreach (var row in alignment.Rows.Where(row => row.OverallStatus != "Aligned"))
{
    Console.WriteLine(
        $"ALIGNMENT_ROW date={row.Date:yyyy-MM-dd} status={row.OverallStatus} abnormal={string.Join("|", row.AbnormalTables)} reason={row.Reason}"
    );
    foreach (var detail in row.Details.Where(detail => detail.Status != "Aligned"))
    {
        Console.WriteLine(
            $"ALIGNMENT_DETAIL date={row.Date:yyyy-MM-dd} table={detail.DisplayName} status={detail.Status} amount={detail.TotalAmount} amountDiff={detail.AmountDifference} quantity={detail.TotalQuantity} quantityDiff={detail.QuantityDifference} orders={detail.OrderCount} orderDiff={detail.OrderCountDifference}"
        );
    }
}

Environment.Exit(refreshSucceeded && alignment.Overview.AbnormalDays == 0 ? 0 : 2);
