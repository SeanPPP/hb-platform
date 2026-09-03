using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// HQ 商品写入的跨路径互斥。推送和窄投影必须共用该锁，防止两个独立事务同时作出
/// update-if-zero-insert 判定。SQL Server 使用独立 session applock；其他方言以进程内
/// 按商品键的信号量保证测试与单进程运行时的相同行为。
/// </summary>
public sealed class ProductHqMutationExecutionLock : IAsyncDisposable
{
    private const string ResourcePrefix = "ProductHqMutation:";
    private const string SqlServerAcquireSql = """
        DECLARE @Result int;
        EXEC @Result = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Session',
            @LockTimeout = 0;
        SELECT @Result;
        """;
    private const string SqlServerReleaseSql = """
        DECLARE @Result int;
        EXEC @Result = sys.sp_releaseapplock
            @Resource = @Resource,
            @LockOwner = N'Session';
        SELECT @Result;
        """;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> NonSqlServerLocks = new(
        StringComparer.Ordinal
    );

    private readonly SqlConnection? _connection;
    private readonly IReadOnlyList<string> _resources;
    private readonly IReadOnlyList<SemaphoreSlim> _semaphores;
    private int _disposed;

    private ProductHqMutationExecutionLock(
        SqlConnection? connection,
        IReadOnlyList<string> resources,
        IReadOnlyList<SemaphoreSlim> semaphores
    )
    {
        _connection = connection;
        _resources = resources;
        _semaphores = semaphores;
    }

    /// <summary>返回 null 表示同商品已有 HQ 写入正在执行；数据库异常仍由调用方按暂态异常处理。</summary>
    public static async Task<ProductHqMutationExecutionLock?> AcquireAsync(
        ISqlSugarClient hqDb,
        IEnumerable<string?> productCodes,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(hqDb);
        ArgumentNullException.ThrowIfNull(productCodes);

        var normalizedCodes = NormalizeProductCodes(productCodes);
        var resources = normalizedCodes.Select(GetResourceKey).ToArray();
        if (hqDb.CurrentConnectionConfig.DbType != SqlSugar.DbType.SqlServer)
        {
            return await AcquireNonSqlServerAsync(resources, cancellationToken);
        }

        return await AcquireSqlServerAsync(hqDb, resources, cancellationToken);
    }

    public static string GetResourceKey(string productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new ArgumentException("HQ 商品写入锁的商品编码不能为空", nameof(productCode));
        }

        var normalized = productCode.Trim().ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..32];
        return ResourcePrefix + hash;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_connection == null)
        {
            for (var index = _semaphores.Count - 1; index >= 0; index--)
            {
                _semaphores[index].Release();
            }
            return;
        }

        var mustClearPool = false;
        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                foreach (var resource in _resources.Reverse())
                {
                    await using var command = CreateCommand(_connection, SqlServerReleaseSql, resource, 5);
                    var result = await ReadLockResultAsync(command, CancellationToken.None);
                    if (result < 0)
                    {
                        // 未确认释放的 session lock 不能随连接池交给下一个请求。
                        mustClearPool = true;
                        break;
                    }
                }
            }
            else
            {
                mustClearPool = true;
            }
        }
        catch
        {
            mustClearPool = true;
        }
        finally
        {
            if (mustClearPool)
            {
                SqlConnection.ClearPool(_connection);
            }
            await _connection.DisposeAsync();
        }
    }

    private static async Task<ProductHqMutationExecutionLock?> AcquireNonSqlServerAsync(
        IReadOnlyList<string> resources,
        CancellationToken cancellationToken
    )
    {
        var semaphores = new List<SemaphoreSlim>(resources.Count);
        try
        {
            foreach (var resource in resources)
            {
                var semaphore = NonSqlServerLocks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));
                if (!await semaphore.WaitAsync(0, cancellationToken))
                {
                    ReleaseSemaphores(semaphores);
                    return null;
                }
                semaphores.Add(semaphore);
            }

            return new ProductHqMutationExecutionLock(null, resources, semaphores);
        }
        catch
        {
            ReleaseSemaphores(semaphores);
            throw;
        }
    }

    private static async Task<ProductHqMutationExecutionLock?> AcquireSqlServerAsync(
        ISqlSugarClient hqDb,
        IReadOnlyList<string> resources,
        CancellationToken cancellationToken
    )
    {
        var connection = new SqlConnection(hqDb.CurrentConnectionConfig.ConnectionString);
        var acquiredResources = new List<string>(resources.Count);
        var mustClearPool = false;
        try
        {
            await connection.OpenAsync(cancellationToken);
            foreach (var resource in resources)
            {
                await using var command = CreateCommand(
                    connection,
                    SqlServerAcquireSql,
                    resource,
                    Math.Clamp(hqDb.Ado.CommandTimeOut, 1, 30)
                );
                var result = await ReadLockResultAsync(command, cancellationToken);
                if (result < 0)
                {
                    // 即使部分已获得锁，也通过断开该 session 立即全部释放。
                    mustClearPool = true;
                    mustClearPool |= await ReleaseSqlResourcesAsync(connection, acquiredResources);
                    if (mustClearPool)
                    {
                        SqlConnection.ClearPool(connection);
                    }
                    await connection.DisposeAsync();
                    return null;
                }
                acquiredResources.Add(resource);
            }

            return new ProductHqMutationExecutionLock(connection, resources, Array.Empty<SemaphoreSlim>());
        }
        catch
        {
            // 获取异常时无法证明每个 session lock 的状态，隔离物理连接以确保不泄漏锁。
            SqlConnection.ClearPool(connection);
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> ReleaseSqlResourcesAsync(
        SqlConnection connection,
        IEnumerable<string> resources
    )
    {
        try
        {
            foreach (var resource in resources.Reverse())
            {
                await using var command = CreateCommand(connection, SqlServerReleaseSql, resource, 5);
                if (await ReadLockResultAsync(command, CancellationToken.None) < 0)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<int> ReadLockResultAsync(
        SqlCommand command,
        CancellationToken cancellationToken
    )
    {
        var resultValue = await command.ExecuteScalarAsync(cancellationToken);
        return resultValue is null or DBNull
            ? -999
            : Convert.ToInt32(resultValue, CultureInfo.InvariantCulture);
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        string sql,
        string resource,
        int commandTimeoutSeconds
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = resource;
        return command;
    }

    private static List<string> NormalizeProductCodes(IEnumerable<string?> productCodes) =>
        productCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

    private static void ReleaseSemaphores(IReadOnlyList<SemaphoreSlim> semaphores)
    {
        for (var index = semaphores.Count - 1; index >= 0; index--)
        {
            semaphores[index].Release();
        }
    }
}
