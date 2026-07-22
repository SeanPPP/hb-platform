using System.Collections.Concurrent;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    internal static class EmployeeProfileMediaLock
    {
        internal const string ResourcePrefix = "EmployeeProfileMedia";
        internal const string ProfileLifecycleKind = "profile-lifecycle";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new();

        internal static Task<IAsyncDisposable> AcquireProfileLifecycleAsync(
            ISqlSugarClient db,
            string userGuid,
            ILogger? logger = null,
            CancellationToken cancellationToken = default
        ) => AcquireAsync(db, userGuid, ProfileLifecycleKind, logger, cancellationToken);

        internal static async Task<IAsyncDisposable> AcquireProfileLifecycleManyAsync(
            ISqlSugarClient db,
            IEnumerable<string> userGuids,
            ILogger? logger = null,
            CancellationToken cancellationToken = default
        ) => await AcquireManyAsync(
            db,
            userGuids,
            ProfileLifecycleKind,
            logger,
            cancellationToken
        );

        internal static async Task<IAsyncDisposable> AcquireManyAsync(
            ISqlSugarClient db,
            IEnumerable<string> userGuids,
            string kind,
            ILogger? logger = null,
            CancellationToken cancellationToken = default
        )
        {
            var orderedUserGuids = userGuids
                .Where(userGuid => !string.IsNullOrWhiteSpace(userGuid))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (orderedUserGuids.Length == 0)
            {
                return new BatchHandle([], [], null, logger);
            }

            var processLocks = new List<(string Resource, SemaphoreSlim Semaphore)>(orderedUserGuids.Length);
            var databaseResources = new List<string>(orderedUserGuids.Length);
            ISqlSugarClient? databaseLock = null;
            try
            {
                foreach (var userGuid in orderedUserGuids)
                {
                    var resource = $"{ResourcePrefix}:{userGuid}:{kind}";
                    var processLock = ProcessLocks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));
                    await processLock.WaitAsync(cancellationToken);
                    processLocks.Add((resource, processLock));
                }

                var config = db.CurrentConnectionConfig;
                if (config.DbType == DbType.SqlServer)
                {
                    // 关键逻辑：整批数据库锁复用同一个 Session，避免大批量操作耗尽连接池后自我等待。
                    databaseLock = new SqlSugarClient(new ConnectionConfig
                    {
                        ConnectionString = config.ConnectionString,
                        DbType = config.DbType,
                        IsAutoCloseConnection = false,
                        InitKeyType = InitKeyType.Attribute,
                    });
                    foreach (var (resource, _) in processLocks)
                    {
                        var result = await databaseLock.Ado.SqlQuerySingleAsync<int>(
                            """
                            DECLARE @Result int;
                            EXEC @Result = sys.sp_getapplock
                                @Resource = @Resource,
                                @LockMode = N'Exclusive',
                                @LockOwner = N'Session',
                                @LockTimeout = 30000;
                            SELECT @Result;
                            """,
                            new SugarParameter("@Resource", resource)
                        );
                        if (result < 0)
                        {
                            throw new InvalidOperationException("获取员工图片操作锁失败");
                        }

                        databaseResources.Add(resource);
                    }
                }

                return new BatchHandle(processLocks, databaseResources, databaseLock, logger);
            }
            catch
            {
                for (var index = databaseResources.Count - 1; index >= 0; index--)
                {
                    var resource = databaseResources[index];
                    try
                    {
                        await databaseLock!.Ado.ExecuteCommandAsync(
                            """
                            EXEC sys.sp_releaseapplock
                                @Resource = @Resource,
                                @LockOwner = N'Session';
                            """,
                            new SugarParameter("@Resource", resource)
                        );
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "释放员工图片操作锁失败。Resource: {Resource}", resource);
                    }
                }

                try
                {
                    databaseLock?.Dispose();
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "释放员工图片批量操作锁 Session 失败");
                }

                for (var index = processLocks.Count - 1; index >= 0; index--)
                {
                    var (resource, processLock) = processLocks[index];
                    try
                    {
                        processLock.Release();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "释放员工图片进程锁失败。Resource: {Resource}", resource);
                    }
                }

                throw;
            }
        }

        internal static async Task<IAsyncDisposable> AcquireAsync(
            ISqlSugarClient db,
            string userGuid,
            string kind,
            ILogger? logger = null,
            CancellationToken cancellationToken = default
        )
        {
            var resource = $"{ResourcePrefix}:{userGuid}:{kind}";
            var processLock = ProcessLocks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));
            await processLock.WaitAsync(cancellationToken);
            ISqlSugarClient? databaseLock = null;
            try
            {
                var config = db.CurrentConnectionConfig;
                if (config.DbType == DbType.SqlServer)
                {
                    databaseLock = new SqlSugarClient(new ConnectionConfig
                    {
                        ConnectionString = config.ConnectionString,
                        DbType = config.DbType,
                        IsAutoCloseConnection = false,
                        InitKeyType = InitKeyType.Attribute,
                    });
                    var result = await databaseLock.Ado.SqlQuerySingleAsync<int>(
                        """
                        DECLARE @Result int;
                        EXEC @Result = sys.sp_getapplock
                            @Resource = @Resource,
                            @LockMode = N'Exclusive',
                            @LockOwner = N'Session',
                            @LockTimeout = 30000;
                        SELECT @Result;
                        """,
                        new SugarParameter("@Resource", resource)
                    );
                    if (result < 0)
                    {
                        throw new InvalidOperationException("获取员工图片操作锁失败");
                    }
                }
                return new Handle(resource, processLock, databaseLock, logger);
            }
            catch
            {
                databaseLock?.Dispose();
                processLock.Release();
                throw;
            }
        }

        private sealed class Handle(
            string resource,
            SemaphoreSlim processLock,
            ISqlSugarClient? databaseLock,
            ILogger? logger
        ) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (databaseLock is not null)
                    {
                        await databaseLock.Ado.ExecuteCommandAsync(
                            """
                            EXEC sys.sp_releaseapplock
                                @Resource = @Resource,
                                @LockOwner = N'Session';
                            """,
                            new SugarParameter("@Resource", resource)
                        );
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "释放员工图片操作锁失败。Resource: {Resource}", resource);
                }
                finally
                {
                    databaseLock?.Dispose();
                    processLock.Release();
                }
            }
        }

        private sealed class BatchHandle(
            IReadOnlyList<(string Resource, SemaphoreSlim Semaphore)> processLocks,
            IReadOnlyList<string> databaseResources,
            ISqlSugarClient? databaseLock,
            ILogger? logger
        ) : IAsyncDisposable
        {
            private int _disposed;

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                try
                {
                    for (var index = databaseResources.Count - 1; index >= 0; index--)
                    {
                        var resource = databaseResources[index];
                        try
                        {
                            await databaseLock!.Ado.ExecuteCommandAsync(
                                """
                                EXEC sys.sp_releaseapplock
                                    @Resource = @Resource,
                                    @LockOwner = N'Session';
                                """,
                                new SugarParameter("@Resource", resource)
                            );
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "释放员工图片操作锁失败。Resource: {Resource}", resource);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        databaseLock?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "释放员工图片批量操作锁 Session 失败");
                    }

                    // 多用户锁按全序获取、逆序释放，避免批量操作互相等待形成死锁。
                    for (var index = processLocks.Count - 1; index >= 0; index--)
                    {
                        var (resource, processLock) = processLocks[index];
                        try
                        {
                            processLock.Release();
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "释放员工图片进程锁失败。Resource: {Resource}", resource);
                        }
                    }
                }
            }
        }
    }
}
