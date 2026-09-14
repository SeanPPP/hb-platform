using System.Data;
using BlazorApp.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 全日统计的 SQL Server 会话执行权。它必须与实际统计写入共用同一 SqlSugar 连接；
/// 一旦发现任意 SQL 错误或会话漂移，即永久失效，禁止旧 worker 透明重连后继续写入。
/// </summary>
internal sealed class SalesStatisticsDateExecutionGuard : IAsyncDisposable
{
    internal const string SessionLeaseTokenPrefix = "sqlsess1:";
    private const string LockPrefix = "HB:SalesStatistics:DateExecution:";
    private readonly ISqlSugarClient _db;
    private readonly ILogger _logger;
    private readonly SqlConnection? _connection;
    private readonly string? _resource;
    private readonly Guid _clientConnectionId;
    private readonly int _serverProcessId;
    private readonly string? _scopeKey;
    private readonly Action<SqlSugarException>? _previousOnError;
    private readonly Action<string, SugarParameter[]>? _previousOnLogExecuting;
    private readonly bool _acquired;
    private int _invalid;
    private bool _disposed;

    private SalesStatisticsDateExecutionGuard(ISqlSugarClient db, ILogger logger, bool acquired = true)
    {
        _db = db;
        _logger = logger;
        _acquired = acquired;
    }

    private SalesStatisticsDateExecutionGuard(
        ISqlSugarClient db,
        ILogger logger,
        SqlConnection connection,
        string resource,
        Guid clientConnectionId,
        int serverProcessId,
        Action<SqlSugarException>? previousOnError,
        Action<string, SugarParameter[]>? previousOnLogExecuting
    )
    {
        _db = db;
        _logger = logger;
        _connection = connection;
        _resource = resource;
        _clientConnectionId = clientConnectionId;
        _serverProcessId = serverProcessId;
        _scopeKey = resource[LockPrefix.Length..];
        _previousOnError = previousOnError;
        _previousOnLogExecuting = previousOnLogExecuting;
        _acquired = true;
    }

    internal bool Acquired => _acquired;
    internal bool IsSqlServerSessionGuarded => _connection != null;
    internal bool IsActive => Volatile.Read(ref _invalid) == 0 && !_disposed;
    internal int ServerProcessIdForTest => _serverProcessId;

    internal bool IsBoundTo(ISqlSugarClient db, string scopeKey) =>
        IsSqlServerSessionGuarded
        && ReferenceEquals(_db, db)
        && string.Equals(_scopeKey, scopeKey, StringComparison.Ordinal);

    /// <summary>唯一的全日统计 applock 资源构造入口，写 guard 与只读运行状态查询必须共用。</summary>
    internal static string GetLockResource(string scopeKey) => LockPrefix + scopeKey.Trim();

    internal static string GetLockResource(DateTime date) =>
        GetLockResource(date.Date.ToString("yyyy-MM-dd"));

    internal static async Task<SalesStatisticsDateExecutionGuard> TryAcquireAsync(
        SqlSugarContext context,
        DateTime date,
        ILogger logger
    )
    {
        var db = context.Db;
        if (db.CurrentConnectionConfig.DbType != SqlSugar.DbType.SqlServer)
        {
            // SQLite 单元测试及其他非 SQL Server 环境沿用数据库 TTL 租约。
            return new SalesStatisticsDateExecutionGuard(db, logger);
        }

        db.CurrentConnectionConfig.IsAutoCloseConnection = false;
        var connection = db.Ado.Connection as SqlConnection
            ?? throw new InvalidOperationException("全日统计会话锁要求 Microsoft.Data.SqlClient 连接");
        if (connection.State != ConnectionState.Closed)
        {
            throw new InvalidOperationException("全日统计会话锁必须在固定写连接打开前建立");
        }

        var builder = new SqlConnectionStringBuilder(connection.ConnectionString)
        {
            ConnectRetryCount = 0,
            // 失联会话绝不回到连接池；guard 已失效，后续必须由新执行者重新取得 applock。
            Pooling = false,
        };
        connection.ConnectionString = builder.ConnectionString;
        db.CurrentConnectionConfig.ConnectionString = builder.ConnectionString;
        try
        {
            await connection.OpenAsync();
            var resource = GetLockResource(date);
            var acquireResult = await ExecuteAppLockAsync(connection, resource, acquire: true);
            if (acquireResult == -1)
            {
                await connection.CloseAsync();
                return new SalesStatisticsDateExecutionGuard(db, logger, acquired: false);
            }
            if (acquireResult < 0)
            {
                throw new InvalidOperationException(
                    $"无法获取全日统计 SQL 会话锁: {date:yyyy-MM-dd}; result={acquireResult}"
                );
            }

            var serverProcessId = await QueryServerProcessIdAsync(connection, null);
            var guard = new SalesStatisticsDateExecutionGuard(
                db,
                logger,
                connection,
                resource,
                connection.ClientConnectionId,
                serverProcessId,
                db.CurrentConnectionConfig.AopEvents?.OnError,
                db.CurrentConnectionConfig.AopEvents?.OnLogExecuting
            );
            guard.InstallSqlSugarFailClosedHooks();
            logger.LogInformation(
                "已取得全日统计 SQL 会话锁: Date={Date}, ClientConnectionId={ClientConnectionId}, Spid={Spid}",
                date.ToString("yyyy-MM-dd"),
                guard._clientConnectionId,
                guard._serverProcessId
            );
            return guard;
        }
        catch
        {
            // 应用锁、SPID 查询或钩子安装失败都不能遗留打开的 Session 锁连接。
            try
            {
                if (connection.State != ConnectionState.Closed)
                    await connection.CloseAsync();
            }
            catch (Exception closeException)
            {
                logger.LogWarning(closeException, "获取全日统计 SQL 会话锁失败后的连接关闭失败");
            }
            throw;
        }
    }

    internal async Task EnsureActiveAsync(string stepName)
    {
        AssertActiveForSqlSugarOperation();
        await Task.CompletedTask;
    }

    internal void RecordException(Exception exception)
    {
        if (!IsSqlServerSessionGuarded)
            return;
        // BulkCopy、提交和回滚在不同驱动版本不一定直接抛出 SqlException。进入受
        // guard 保护的写步骤后，任何异常都不能再安全地使用这个连接发布结果。
        MarkInvalid(exception, "统计步骤异常");
    }

    private void InstallSqlSugarFailClosedHooks()
    {
        if (!IsSqlServerSessionGuarded)
            return;

        // FastestProvider 在 BulkCopy Begin 中会临时关闭 IsEnableLogEvent；若回调抛出，
        // 旧版本不会进入 End 恢复。先确保开启，并在回调首行再次恢复，避免之后的
        // Lease/状态 SQL 绕过 OnLogExecuting 而透明重连。
        _db.Ado.IsEnableLogEvent = true;
        _db.Aop.OnError = exception =>
        {
            MarkInvalid(exception, "SqlSugar 执行异常");
            _previousOnError?.Invoke(exception);
        };
        _db.Aop.OnLogExecuting = (sql, parameters) =>
        {
            _db.Ado.IsEnableLogEvent = true;
            // 此回调发生在每条 SqlSugar SQL 前；用被捕获的原生连接探测，绝不经由
            // SqlSugar 再开连接。BulkCopy 后的内部状态写入也会在这里被拦住。
            AssertActiveForSqlSugarOperation();
            _previousOnLogExecuting?.Invoke(sql, parameters);
        };
    }

    private void AssertActiveForSqlSugarOperation()
    {
        if (!IsSqlServerSessionGuarded)
            return;
        if (!IsActive)
            throw new SalesStatisticsDateExecutionGuardLostException("全日统计 SQL 会话执行权已失效");

        try
        {
            var connection = _connection!;
            // SqlSugar 可能在错误后替换内部连接对象；即使旧会话仍显示持锁，也绝不能
            // 让 ORM 通过另一条连接继续写入。
            if (!ReferenceEquals(_db.Ado.Connection, connection))
            {
                throw new InvalidOperationException("SqlSugar 当前写连接已不再是持有日期锁的连接");
            }
            if (connection.State != ConnectionState.Open
                || connection.ClientConnectionId != _clientConnectionId)
            {
                throw new InvalidOperationException("全日统计写连接已关闭或发生重连");
            }

            var transaction = _db.Ado.Transaction as SqlTransaction;
            if (transaction?.Connection != null && !ReferenceEquals(transaction.Connection, connection))
            {
                throw new InvalidOperationException("全日统计事务未绑定会话锁连接");
            }

            var observedSpid = QueryServerProcessIdAsync(connection, transaction).GetAwaiter().GetResult();
            if (observedSpid != _serverProcessId)
                throw new InvalidOperationException("全日统计 SQL Server 会话标识已改变");

            var lockMode = QueryLockModeAsync(connection, transaction, _resource!).GetAwaiter().GetResult();
            if (!string.Equals(lockMode, "Exclusive", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("全日统计 SQL 会话锁不再由当前连接持有");
        }
        catch (Exception exception)
        {
            MarkInvalid(exception, "写入前会话锁校验失败");
            throw new SalesStatisticsDateExecutionGuardLostException(
                "全日统计 SQL 会话执行权已失效，禁止继续写入",
                exception
            );
        }
    }

    private void MarkInvalid(Exception exception, string reason)
    {
        if (IsSqlServerSessionGuarded)
        {
            // 兜底恢复 FastestProvider 遗留的日志开关，使任何后续 ORM SQL 都必须经过
            // OnLogExecuting 的 fail-closed 校验。
            _db.Ado.IsEnableLogEvent = true;
        }
        if (Interlocked.Exchange(ref _invalid, 1) == 0)
        {
            _logger.LogError(
                exception,
                "{Reason}; ClientConnectionId={ClientConnectionId}, Spid={Spid}",
                reason,
                _clientConnectionId,
                _serverProcessId
            );
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (!IsSqlServerSessionGuarded)
            return;

        // 正常结束时还原已有回调。失效时保留拒绝钩子，避免 finally 之后同一
        // SqlSugar context 透明重连；批量入口会为下一个日期建立新 scope。
        if (Volatile.Read(ref _invalid) == 0)
        {
            _db.Aop.OnError = _previousOnError;
            _db.Aop.OnLogExecuting = _previousOnLogExecuting;
        }

        try
        {
            if (Volatile.Read(ref _invalid) == 0 && _connection!.State == ConnectionState.Open)
                await ExecuteAppLockAsync(_connection, _resource!, acquire: false);
        }
        catch (Exception cleanupException)
        {
            // 写会话已被 KILL 时，释放和 SqlSugar Dispose 都可能再次失败；不得覆盖主异常。
            _logger.LogWarning(cleanupException, "释放全日统计 SQL 会话锁失败");
        }
        finally
        {
            if (Volatile.Read(ref _invalid) != 0)
            {
                BestEffortClearBrokenTransaction();
            }
            try
            {
                if (_connection!.State != ConnectionState.Closed)
                    await _connection.CloseAsync();
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(cleanupException, "关闭全日统计 SQL 写连接失败");
            }
        }
    }

    private void BestEffortClearBrokenTransaction()
    {
        // KILL 后 SqlSugar Dispose 可能尝试回滚僵尸事务并再次抛错。直接处理已捕获
        // 的原生事务，再清掉 ADO 引用；所有清理异常只记录，不能掩盖原始执行失败。
        var transaction = _db.Ado.Transaction as SqlTransaction;
        if (transaction != null)
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception cleanupException)
            {
                _logger.LogDebug(cleanupException, "失效全日统计事务回滚未完成");
            }
            try
            {
                transaction.Dispose();
            }
            catch (Exception cleanupException)
            {
                _logger.LogDebug(cleanupException, "失效全日统计事务释放未完成");
            }
        }
        try
        {
            _db.Ado.Transaction = null;
        }
        catch (Exception cleanupException)
        {
            _logger.LogDebug(cleanupException, "清理失效全日统计事务引用失败");
        }
    }

    private static async Task<int> ExecuteAppLockAsync(SqlConnection connection, string resource, bool acquire)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = acquire
            ? "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=0; SELECT @result;"
            : "DECLARE @result int; EXEC @result = sys.sp_releaseapplock @Resource=@resource, @LockOwner=N'Session'; SELECT @result;";
        command.Parameters.AddWithValue("@resource", resource);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> QueryServerProcessIdAsync(
        SqlConnection connection,
        SqlTransaction? transaction
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT @@SPID;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> QueryLockModeAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string resource
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT APPLOCK_MODE(N'public', @resource, N'Session');";
        command.Parameters.AddWithValue("@resource", resource);
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

}

internal sealed class SalesStatisticsDateExecutionGuardLostException : InvalidOperationException
{
    internal SalesStatisticsDateExecutionGuardLostException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
