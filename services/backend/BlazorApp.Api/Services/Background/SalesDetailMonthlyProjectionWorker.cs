using System.Data;
using BlazorApp.Api.Data;
using Microsoft.Data.SqlClient;

namespace BlazorApp.Api.Services.Background;

/// <summary>
/// 维护销售明细预聚合投影：先逐日重算身份或映射签名变化的日期（每日约 0.1 秒），再由日表汇总身份变化的月份。
/// 不挂进日统计写入事务，查询端按月身份、日身份逐级判定有效性，未覆盖的日期自动退回日事实；
/// 表未部署（迁移 20260922.001）时跳过。
/// 配置节 SalesDetailMonthlyProjection：Enabled（默认 true）、CheckIntervalMinutes（默认 2）、
/// MaxDaysPerPass（默认 200）、MaxMonthsPerPass（默认 12）。
/// </summary>
public sealed class SalesDetailMonthlyProjectionWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<SalesDetailMonthlyProjectionWorker> logger) : BackgroundService
{
    private const string LeaseTaskType = nameof(SalesDetailMonthlyProjectionWorker);
    private const string LeaseScope = "monthly-rollup";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RunBudget = TimeSpan.FromMinutes(6);
    // 单日约 1.2 万行日事实、整月由日表汇总都在秒级以内；给回填夜的锁等待留余量。
    private const int CommandTimeoutSeconds = 300;

    private bool _schemaMissingLogged;

    private bool Enabled => configuration.GetValue("SalesDetailMonthlyProjection:Enabled", true);
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(
        Math.Max(1, configuration.GetValue("SalesDetailMonthlyProjection:CheckIntervalMinutes", 2)));
    private int MaxDaysPerPass => Math.Max(1, configuration.GetValue("SalesDetailMonthlyProjection:MaxDaysPerPass", 200));
    private int MaxMonthsPerPass => Math.Max(1, configuration.GetValue("SalesDetailMonthlyProjection:MaxMonthsPerPass", 12));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "销售明细预聚合投影后台任务暂不可用"); }

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

        var context = services.GetRequiredService<SqlSugarContext>();
        var posmContext = services.GetRequiredService<POSMSqlSugarContext>();
        if (context.Db.CurrentConnectionConfig.DbType != SqlSugar.DbType.SqlServer
            || posmContext.Db.CurrentConnectionConfig.DbType != SqlSugar.DbType.SqlServer
            || !TryGetSameServerPosmDatabase(context, posmContext, out var posmDatabase))
            return;

        var connectionString = context.Db.CurrentConnectionConfig.ConnectionString;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(stoppingToken);
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = SalesDetailQueryMonthlyProjection.TablesExistSql;
            if (Convert.ToInt32(await probe.ExecuteScalarAsync(stoppingToken)) != 1)
            {
                if (!_schemaMissingLogged)
                {
                    logger.LogInformation("销售明细预聚合投影表尚未部署，后台任务跳过；请执行 --schema=migrate");
                    _schemaMissingLogged = true;
                }
                return;
            }
        }
        _schemaMissingLogged = false;

        var staleDays = await ReadStaleDaysAsync(connection, posmDatabase, MaxDaysPerPass, stoppingToken);
        var staleMonths = staleDays.Count == 0
            ? await ReadStaleMonthsAsync(connection, posmDatabase, stoppingToken)
            : new List<DateTime>();
        if (staleDays.Count == 0 && staleMonths.Count == 0)
            return;

        var leases = services.GetRequiredService<ScheduledTaskLeaseService>();
        var lease = await leases.TryAcquireAsync(LeaseTaskType, LeaseScope, LeaseDuration);
        if (!lease.Acquired || string.IsNullOrWhiteSpace(lease.Lease?.LeaseToken))
            return;

        var leaseToken = lease.Lease.LeaseToken;
        var success = false;
        var deadline = DateTime.UtcNow.Add(RunBudget);
        var refreshedDays = 0;
        var refreshedMonths = 0;
        var skippedMonths = 0;
        try
        {
            foreach (var day in staleDays)
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                    break;
                await leases.EnsureActiveAsync(LeaseTaskType, LeaseScope, leaseToken, LeaseDuration, "销售明细预聚合投影");
                await RefreshDayAsync(connection, posmDatabase, day, stoppingToken);
                refreshedDays++;
            }
            // 日表追上后再看哪些月份可以汇总；本轮没处理完的日期所在月份会自动留到下一轮。
            if (staleDays.Count > 0 && DateTime.UtcNow < deadline)
                staleMonths = await ReadStaleMonthsAsync(connection, posmDatabase, stoppingToken);
            foreach (var month in staleMonths.Take(MaxMonthsPerPass))
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                    break;
                await leases.EnsureActiveAsync(LeaseTaskType, LeaseScope, leaseToken, LeaseDuration, "销售明细预聚合投影");
                try
                {
                    await RefreshMonthAsync(connection, posmDatabase, month, stoppingToken);
                    refreshedMonths++;
                }
                catch (SqlException ex) when (ex.Number == SalesDetailQueryMonthlyProjection.DaysNotReadyErrorNumber)
                {
                    // 读取待办与汇总之间该月又有日期重新发布，等下一轮日表追上再汇总。
                    skippedMonths++;
                }
            }
            success = true;
        }
        finally
        {
            await leases.CompleteAsync(LeaseTaskType, LeaseScope, leaseToken, success);
        }
        if (refreshedDays > 0 || refreshedMonths > 0 || skippedMonths > 0)
            logger.LogInformation("销售明细预聚合投影本轮重算 {Days} 天、汇总 {Months} 个月，跳过 {Skipped} 个月，待处理 {StaleDays} 天",
                refreshedDays, refreshedMonths, skippedMonths, staleDays.Count - refreshedDays);
    }

    internal static async Task<List<DateTime>> ReadStaleDaysAsync(
        SqlConnection connection, string posmDatabase, int maxDays, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SalesDetailQueryMonthlyProjection.BuildStaleDaysSql(posmDatabase);
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Parameters.Add("@sdmMaxDays", SqlDbType.Int).Value = Math.Max(1, maxDays);
        var days = new List<DateTime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            days.Add(reader.GetDateTime(0).Date);
        return days;
    }

    internal static async Task<List<DateTime>> ReadStaleMonthsAsync(
        SqlConnection connection, string posmDatabase, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SalesDetailQueryMonthlyProjection.BuildStaleMonthsSql(posmDatabase);
        command.CommandTimeout = CommandTimeoutSeconds;
        var months = new List<DateTime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            months.Add(reader.GetDateTime(0).Date);
        return months;
    }

    /// <summary>一天在一个 SNAPSHOT 事务里重算：身份、事实与状态取自同一快照，不会把两版数据拼在一起。</summary>
    internal static Task RefreshDayAsync(
        SqlConnection connection, string posmDatabase, DateTime day, CancellationToken cancellationToken)
        => ExecuteInSnapshotAsync(connection, SalesDetailQueryMonthlyProjection.BuildRefreshDaySql(posmDatabase),
            "@sdmDay", day.Date, cancellationToken);

    /// <summary>整月在一个 SNAPSHOT 事务里由日表汇总；日表未就绪时抛 51015，由调用方决定重试。</summary>
    internal static Task RefreshMonthAsync(
        SqlConnection connection, string posmDatabase, DateTime month, CancellationToken cancellationToken)
        => ExecuteInSnapshotAsync(connection, SalesDetailQueryMonthlyProjection.BuildRefreshMonthSql(posmDatabase),
            "@sdmMonth", month.Date, cancellationToken);

    private static async Task ExecuteInSnapshotAsync(
        SqlConnection connection, string sql, string dateParameter, DateTime date, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.CommandTimeout = CommandTimeoutSeconds;
            command.Parameters.Add(dateParameter, SqlDbType.Date).Value = date;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { /* 连接已断时无需再回滚 */ }
            throw;
        }
    }

    private static bool TryGetSameServerPosmDatabase(SqlSugarContext context, POSMSqlSugarContext posmContext, out string database)
    {
        database = string.Empty;
        try
        {
            var main = new SqlConnectionStringBuilder(context.Db.CurrentConnectionConfig.ConnectionString);
            var posm = new SqlConnectionStringBuilder(posmContext.Db.CurrentConnectionConfig.ConnectionString);
            if (!string.Equals(main.DataSource, posm.DataSource, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(posm.InitialCatalog))
                return false;
            database = posm.InitialCatalog;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
