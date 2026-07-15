using System.Collections.Concurrent;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    internal static class EmployeeProfileMediaLock
    {
        internal const string ResourcePrefix = "EmployeeProfileMedia";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new();

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
    }
}
