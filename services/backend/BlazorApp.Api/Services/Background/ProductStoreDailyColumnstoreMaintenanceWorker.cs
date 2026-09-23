using System.Diagnostics;
using BlazorApp.Api.Data;
using SqlSugar;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 按行组健康度在线整理商品分店日统计的列存索引，让销售明细长区间查询持续走批处理聚合。
/// 索引未部署（见 SqlScripts/ProductStoreDailySalesStatisticColumnstore.sql）或非 SQL Server 时直接跳过；
/// 由分布式租约保证多实例下只有一个在整理。配置节 ProductStoreDailyColumnstoreMaintenance：
/// Enabled（默认 true）、CheckIntervalMinutes（默认 60）、MinimumIntervalHours（默认 4）。
/// </summary>
public sealed class ProductStoreDailyColumnstoreMaintenanceWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ProductStoreDailyColumnstoreMaintenanceWorker> logger) : BackgroundService
{
    private const string LeaseTaskType = nameof(ProductStoreDailyColumnstoreMaintenanceWorker);
    private const string LeaseScope = "columnstore-reorganize";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    // 7.5M 行全量整理单线程约 1–3 分钟；给回填日成倍的行组留足余量，并保证能在租约内结束。
    private const int ReorganizeCommandTimeoutSeconds = 1800;

    private bool _indexMissingLogged;
    private DateTime? _lastReorganizedAtUtc;

    private bool Enabled => configuration.GetValue("ProductStoreDailyColumnstoreMaintenance:Enabled", true);
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(
        Math.Max(5, configuration.GetValue("ProductStoreDailyColumnstoreMaintenance:CheckIntervalMinutes", 60)));
    private TimeSpan MinimumInterval => TimeSpan.FromHours(
        Math.Max(1, configuration.GetValue("ProductStoreDailyColumnstoreMaintenance:MinimumIntervalHours", 4)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "商品日统计列存索引整理任务暂不可用"); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
            return;
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        if (!await services.GetRequiredService<ScheduledTaskRuntimeControlService>().IsLeaseManagedWorkerEnabledAsync())
            return;

        var db = services.GetRequiredService<SqlSugarContext>().Db;
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            return;
        if (await db.Ado.GetIntAsync(ProductStoreDailyColumnstoreMaintenancePolicy.IndexExistsSql) == 0)
        {
            if (!_indexMissingLogged)
            {
                logger.LogInformation(
                    "{Index} 尚未部署，列存整理任务跳过；请先执行 SqlScripts/ProductStoreDailySalesStatisticColumnstore.sql",
                    ProductStoreDailyColumnstoreMaintenancePolicy.IndexName);
                _indexMissingLogged = true;
            }
            return;
        }
        _indexMissingLogged = false;

        var health = await ReadHealthAsync(db);
        if (!ProductStoreDailyColumnstoreMaintenancePolicy.ShouldReorganize(health, _lastReorganizedAtUtc, DateTime.UtcNow, MinimumInterval))
            return;

        var leases = services.GetRequiredService<ScheduledTaskLeaseService>();
        var lease = await leases.TryAcquireAsync(LeaseTaskType, LeaseScope, LeaseDuration);
        if (!lease.Acquired || string.IsNullOrWhiteSpace(lease.Lease?.LeaseToken))
            return;

        var leaseToken = lease.Lease.LeaseToken;
        var success = false;
        var previousTimeout = db.Ado.CommandTimeOut;
        var elapsed = Stopwatch.StartNew();
        try
        {
            stoppingToken.ThrowIfCancellationRequested();
            db.Ado.CommandTimeOut = ReorganizeCommandTimeoutSeconds;
            logger.LogInformation(
                "开始整理 {Index}：行组 {RowGroups}（小行组 {SmallRowGroups}），已删除行 {DeletedRows}/{TotalRows}",
                ProductStoreDailyColumnstoreMaintenancePolicy.IndexName,
                health.RowGroups, health.SmallRowGroups, health.DeletedRows, health.TotalRows);
            await db.Ado.ExecuteCommandAsync(ProductStoreDailyColumnstoreMaintenancePolicy.ReorganizeSql);
            _lastReorganizedAtUtc = DateTime.UtcNow;
            var after = await ReadHealthAsync(db);
            logger.LogInformation(
                "{Index} 整理完成，耗时 {ElapsedMs}ms：行组 {RowGroups}（小行组 {SmallRowGroups}），已删除行 {DeletedRows}/{TotalRows}",
                ProductStoreDailyColumnstoreMaintenancePolicy.IndexName, elapsed.ElapsedMilliseconds,
                after.RowGroups, after.SmallRowGroups, after.DeletedRows, after.TotalRows);
            success = true;
        }
        finally
        {
            db.Ado.CommandTimeOut = previousTimeout;
            await leases.CompleteAsync(LeaseTaskType, LeaseScope, leaseToken, success);
        }
    }

    private static async Task<ProductStoreDailyColumnstoreHealth> ReadHealthAsync(ISqlSugarClient db)
    {
        var row = await db.Ado.GetDataTableAsync(ProductStoreDailyColumnstoreMaintenancePolicy.HealthSql);
        if (row.Rows.Count == 0)
            return new ProductStoreDailyColumnstoreHealth(0, 0, 0, 0);
        var first = row.Rows[0];
        static long Read(object value) => value is DBNull or null ? 0L : Convert.ToInt64(value);
        return new ProductStoreDailyColumnstoreHealth(
            Read(first["RowGroups"]), Read(first["SmallRowGroups"]), Read(first["TotalRows"]), Read(first["DeletedRows"]));
    }
}
