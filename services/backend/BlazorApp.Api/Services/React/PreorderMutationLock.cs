using System.Diagnostics;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

internal static class PreorderMutationLock
{
    private static readonly object ProcessLockGate = new();
    private static readonly Dictionary<string, LockEntry> ProcessLocks = new(StringComparer.Ordinal);

    internal static int ProcessLockCount
    {
        get
        {
            lock (ProcessLockGate)
            {
                return ProcessLocks.Count;
            }
        }
    }

    internal static async ValueTask<IAsyncDisposable> AcquireProcessAsync(string resource)
    {
        var normalized = NormalizeResource(resource);
        LockEntry entry;
        lock (ProcessLockGate)
        {
            if (!ProcessLocks.TryGetValue(normalized, out entry!))
            {
                entry = new LockEntry();
                ProcessLocks.Add(normalized, entry);
            }
            entry.ReferenceCount++;
        }
        try
        {
            await entry.Semaphore.WaitAsync();
            return new Releaser(normalized, entry);
        }
        catch
        {
            ReleaseReference(normalized, entry, releaseSemaphore: false);
            throw;
        }
    }

    internal static async ValueTask<IAsyncDisposable> AcquireProcessesAsync(
        IEnumerable<string> resources
    )
    {
        var ordered = NormalizeAndOrderResources(resources);
        var acquired = new List<IAsyncDisposable>(ordered.Count);
        try
        {
            foreach (var resource in ordered)
            {
                acquired.Add(await AcquireProcessAsync(resource));
            }
            return new CompositeReleaser(acquired);
        }
        catch
        {
            for (var index = acquired.Count - 1; index >= 0; index--)
            {
                await acquired[index].DisposeAsync();
            }
            throw;
        }
    }

    internal static List<string> NormalizeAndOrderResources(IEnumerable<string> resources)
    {
        // 多资源的进程锁和数据库锁必须共用此规范化顺序，避免大小写不同导致跨实例反向等待。
        return resources
            .Select(NormalizeResource)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
    }

    internal static async Task<int> AcquireDatabaseAsync(
        ISqlSugarClient db,
        string resource,
        string? canonicalStoreGuid = null,
        bool requireStoreIdentity = true
    )
    {
        var normalized = NormalizeResource(resource);
        EnsureSupportedProvider(db.CurrentConnectionConfig.DbType);
        var separator = normalized.IndexOf(':');
        var kind = normalized[..separator];
        var key = normalized[(separator + 1)..];
        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            const string sql = """
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = @resource,
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 10000;
IF @lockResult < 0 THROW 51016, '获取 Preorder 写锁失败', 1;
""";
            await db.Ado.ExecuteCommandAsync(sql, new SugarParameter("@resource", normalized));
            return 1 + await AcquireStoreIdentityRowLockIfNeededAsync(
                db,
                kind,
                key,
                canonicalStoreGuid,
                requireStoreIdentity
            );
        }

        if (db.CurrentConnectionConfig.DbType == DbType.PostgreSQL)
        {
            // PostgreSQL 使用事务级 advisory lock，跨实例保持与 SQL Server applock 相同的互斥语义。
            await db.Ado.ExecuteCommandAsync(
                "SELECT pg_advisory_xact_lock(hashtext(@resource))",
                new SugarParameter("@resource", normalized)
            );
            return 1 + await AcquireStoreIdentityRowLockIfNeededAsync(
                db,
                kind,
                key,
                canonicalStoreGuid,
                requireStoreIdentity
            );
        }

        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            // SQLite 的事务默认可延迟到首次写入；先执行 no-op UPDATE 获取跨进程数据库写锁，
            // 再由调用方重读并校验，避免 Close/Submit 或 Update/Activate 穿透进程内 keyed lock。
            var sql = kind switch
            {
                "preordertemplate" => "UPDATE \"PreorderTemplate\" SET \"TemplateGuid\" = \"TemplateGuid\" WHERE lower(\"TemplateGuid\") = @key",
                "preorderactivation" => "UPDATE \"PreorderActivation\" SET \"ActivationGuid\" = \"ActivationGuid\" WHERE lower(\"ActivationGuid\") = @key",
                "preorderstoregate" => GetStoreIdentityRowLockSql(DbType.Sqlite),
                _ => throw new InvalidOperationException($"不支持的 Preorder SQLite 锁资源: {kind}"),
            };
            var affected = await db.Ado.ExecuteCommandAsync(sql, new SugarParameter("@key", key));
            if (kind == "preorderstoregate"
                && (affected > 1 || (requireStoreIdentity && affected != 1)))
            {
                throw new InvalidOperationException("Preorder Store 身份行不存在或不唯一");
            }
            return 1;
        }

        throw new UnreachableException();
    }

    internal static void EnsureSupportedProvider(DbType dbType)
    {
        if (dbType is not (DbType.SqlServer or DbType.PostgreSQL or DbType.Sqlite))
        {
            // 未适配 provider 绝不能静默跳过数据库锁，否则多实例会 fail-open。
            throw new InvalidOperationException(
                $"不支持的 Preorder 数据库锁 Provider: {dbType}"
            );
        }
    }

    internal static string GetStoreIdentityRowLockSql(DbType dbType) => dbType switch
    {
        // SQL Server 的 StoreGUID 已有可索引身份列，禁止在条件左侧套 LOWER 导致逐行计算。
        DbType.SqlServer => "UPDATE [Store] WITH (UPDLOCK, HOLDLOCK) SET [StoreGUID] = [StoreGUID] WHERE [StoreGUID] = @key",
        DbType.PostgreSQL => "UPDATE \"Store\" SET \"StoreGUID\" = \"StoreGUID\" WHERE lower(\"StoreGUID\") = @key",
        DbType.Sqlite => "UPDATE \"Store\" SET \"StoreGUID\" = \"StoreGUID\" WHERE lower(\"StoreGUID\") = @key",
        _ => throw new InvalidOperationException($"不支持的 Preorder StoreGate 数据库类型: {dbType}"),
    };

    internal static string ResolveStoreIdentityRowLockKey(
        DbType dbType,
        string normalizedKey,
        string? canonicalStoreGuid
    )
    {
        if (dbType != DbType.SqlServer)
        {
            return normalizedKey;
        }
        if (string.IsNullOrWhiteSpace(canonicalStoreGuid)
            || !string.Equals(
                normalizedKey,
                canonicalStoreGuid.Trim(),
                StringComparison.OrdinalIgnoreCase
            ))
        {
            // SQL Server 可能使用大小写敏感排序规则，必须携带数据库读取出的原始 StoreGUID。
            throw new InvalidOperationException("Preorder StoreGuid 与 StoreGate 锁资源不匹配");
        }
        return canonicalStoreGuid;
    }

    private static async Task<int> AcquireStoreIdentityRowLockIfNeededAsync(
        ISqlSugarClient db,
        string resourceKind,
        string key,
        string? canonicalStoreGuid,
        bool requireStoreIdentity
    )
    {
        if (resourceKind != "preorderstoregate")
        {
            return 0;
        }

        // 应用锁只协调 Preorder 写入；行更新锁同时阻止 StoreService 改名、停用或删除身份行。
        var affected = await db.Ado.ExecuteCommandAsync(
            GetStoreIdentityRowLockSql(db.CurrentConnectionConfig.DbType),
            new SugarParameter(
                "@key",
                ResolveStoreIdentityRowLockKey(
                    db.CurrentConnectionConfig.DbType,
                    key,
                    canonicalStoreGuid
                )
            )
        );
        if (affected > 1 || (requireStoreIdentity && affected != 1))
        {
            throw new InvalidOperationException("Preorder Store 身份行不存在或不唯一");
        }
        return 1;
    }

    private static string NormalizeResource(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource) || !resource.Contains(':'))
        {
            throw new ArgumentException("Preorder 锁资源格式无效", nameof(resource));
        }
        return resource.Trim().ToLowerInvariant();
    }

    private static void ReleaseReference(string resource, LockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }
        lock (ProcessLockGate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0
                && ProcessLocks.TryGetValue(resource, out var current)
                && ReferenceEquals(current, entry))
            {
                ProcessLocks.Remove(resource);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class LockEntry
    {
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);
        internal int ReferenceCount { get; set; }
    }

    private sealed class Releaser(string resource, LockEntry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseReference(resource, entry, releaseSemaphore: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompositeReleaser(IReadOnlyList<IAsyncDisposable> releasers)
        : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            for (var index = releasers.Count - 1; index >= 0; index--)
            {
                await releasers[index].DisposeAsync();
            }
        }
    }
}
