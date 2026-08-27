using BlazorApp.Api.Services.Logging;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

namespace BlazorApp.Api.Data.SchemaMigrations;

internal enum SchemaDatabase
{
    Main,
    Posm,
}

/// <summary>
/// 将数据库 I/O 与迁移步骤隔离，协调器测试可精确验证跨库恢复和账本写入时机。
/// </summary>
internal interface ISchemaMigrationRuntime
{
    void EnsureSupportedProviders();

    Task<ISchemaMigrationSession> AcquireMigrationSessionAsync(
        SchemaDatabase database,
        CancellationToken cancellationToken
    );

    Task<bool> IsMigrationAppliedAsync(
        SchemaDatabase database,
        string migrationId,
        CancellationToken cancellationToken
    );

    Task ApplyBaselineAsync(SchemaDatabase database, CancellationToken cancellationToken);

    Task VerifyDeviceActivationSchemaAsync(CancellationToken cancellationToken);
}

internal interface ISchemaMigrationSession : IAsyncDisposable
{
    Task EnsureHistoryTableAsync(CancellationToken cancellationToken);

    Task<bool> IsAppliedAsync(string migrationId, CancellationToken cancellationToken);

    Task RecordAppliedAsync(string migrationId, CancellationToken cancellationToken);
}

internal sealed class SchemaProviderNotSupportedException : Exception;

internal sealed class DeviceActivationSchemaMismatchException : Exception;

internal sealed class SchemaBaselineSqlFailureException : Exception;

internal sealed class SqlServerSchemaMigrationRuntime : ISchemaMigrationRuntime
{
    private static readonly SemaphoreSlim SchemaConsoleRedirectLock = new(1, 1);

    internal const string MainHistoryTable = "HBWebSchemaMigrationHistory";
    internal const string PosmHistoryTable = "HBWebPosmSchemaMigrationHistory";

    private const string MainHistoryPrimaryKey = "PK_HBWebSchemaMigrationHistory";
    private const string PosmHistoryPrimaryKey = "PK_HBWebPosmSchemaMigrationHistory";
    private const string MainLockResource = "HBWeb:SchemaMigration:Main";
    private const string PosmLockResource = "HBWeb:SchemaMigration:POSM";

    private readonly SqlSugarContext _mainDbContext;
    private readonly POSMSqlSugarContext _posmDbContext;
    private readonly DatabaseDefinition _mainDatabase;
    private readonly DatabaseDefinition _posmDatabase;
    private readonly int _commandTimeoutSeconds;
    private readonly string _applicationVersion;

    public SqlServerSchemaMigrationRuntime(
        IConfiguration configuration,
        SqlSugarContext mainDbContext,
        POSMSqlSugarContext posmDbContext
    )
    {
        _mainDbContext = mainDbContext;
        _posmDbContext = posmDbContext;
        _mainDatabase = new DatabaseDefinition(
            configuration.GetConnectionString("DefaultConnection") ?? string.Empty,
            MainHistoryTable,
            MainHistoryPrimaryKey,
            MainLockResource
        );
        _posmDatabase = new DatabaseDefinition(
            configuration.GetConnectionString("HBPOSMConnection") ?? string.Empty,
            PosmHistoryTable,
            PosmHistoryPrimaryKey,
            PosmLockResource
        );
        _commandTimeoutSeconds = Math.Clamp(
            configuration.GetValue("Database:CommandTimeoutSeconds", 60),
            1,
            1800
        );
        _applicationVersion = GetApplicationVersion();
    }

    public void EnsureSupportedProviders()
    {
        if (
            string.IsNullOrWhiteSpace(_mainDatabase.ConnectionString)
            || string.IsNullOrWhiteSpace(_posmDatabase.ConnectionString)
            || _mainDbContext.Db.CurrentConnectionConfig.DbType != DbType.SqlServer
            || _posmDbContext.Db.CurrentConnectionConfig.DbType != DbType.SqlServer
        )
        {
            throw new SchemaProviderNotSupportedException();
        }
    }

    public async Task<ISchemaMigrationSession> AcquireMigrationSessionAsync(
        SchemaDatabase database,
        CancellationToken cancellationToken
    )
    {
        var definition = GetDefinition(database);
        var migrationLock = await SqlServerSchemaMigrationLock.AcquireAsync(
            definition.ConnectionString,
            definition.LockResource,
            _commandTimeoutSeconds,
            cancellationToken
        );
        return new SqlServerSchemaMigrationSession(
            migrationLock,
            definition,
            _applicationVersion,
            _commandTimeoutSeconds
        );
    }

    public Task<bool> IsMigrationAppliedAsync(
        SchemaDatabase database,
        string migrationId,
        CancellationToken cancellationToken
    )
    {
        var definition = GetDefinition(database);
        return SqlServerSchemaMigrationStore.IsAppliedAsync(
            definition.ConnectionString,
            definition.HistoryTable,
            migrationId,
            _commandTimeoutSeconds,
            cancellationToken
        );
    }

    public async Task ApplyBaselineAsync(
        SchemaDatabase database,
        CancellationToken cancellationToken
    )
    {
        if (database == SchemaDatabase.Main)
        {
            await RunStrictBaselineAsync(
                _mainDbContext.Db,
                async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _mainDbContext.CreateTable();
                    cancellationToken.ThrowIfCancellationRequested();
                    await StartupSchemaMigrator.EnsureAsync(
                        _mainDbContext.Db,
                        NullLogger.Instance
                    );
                    cancellationToken.ThrowIfCancellationRequested();
                    await ApplicationLogSchemaMigrator.EnsureAsync(
                        _mainDbContext.Db,
                        NullLogger.Instance
                    );
                },
                cancellationToken
            );
            return;
        }

        await RunStrictBaselineAsync(
            _posmDbContext.Db,
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StartupSchemaMigrator.EnsurePosmAsync(
                    _posmDbContext.Db,
                    NullLogger.Instance
                );
                cancellationToken.ThrowIfCancellationRequested();
                await PaymentTerminalSettingsSchemaMigrator.EnsureAsync(
                    _posmDbContext.Db,
                    NullLogger.Instance
                );
                cancellationToken.ThrowIfCancellationRequested();
                await DeviceRuntimeStatusSchemaMigrator.EnsureAsync(
                    _posmDbContext.Db,
                    NullLogger.Instance
                );
                cancellationToken.ThrowIfCancellationRequested();
                await EmergencyLoginGrantSchemaMigrator.EnsureAsync(
                    _posmDbContext.Db,
                    NullLogger.Instance
                );
                cancellationToken.ThrowIfCancellationRequested();
                await EmergencyLoginKeySchemaMigrator.EnsureAsync(
                    _posmDbContext.Db,
                    NullLogger.Instance
                );
            },
            cancellationToken
        );
    }

    public async Task VerifyDeviceActivationSchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SqlServerSchemaMigrationStore.ExecuteReadOnlyBatchAsync(
                _posmDatabase.ConnectionString,
                DeviceActivationCodeSchema.VerifySql,
                _commandTimeoutSeconds,
                cancellationToken
            );
        }
        catch (SqlException exception) when (exception.Number is >= 51100 and <= 51199)
        {
            throw new DeviceActivationSchemaMismatchException();
        }
    }

    private DatabaseDefinition GetDefinition(SchemaDatabase database) =>
        database == SchemaDatabase.Main ? _mainDatabase : _posmDatabase;

    internal static async Task RunStrictBaselineAsync(
        ISqlSugarClient database,
        Func<Task> baseline,
        CancellationToken cancellationToken
    )
    {
        await SchemaConsoleRedirectLock.WaitAsync(cancellationToken);
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var sqlFailureDetected = 0;

        try
        {
            // 旧迁移器会吞掉部分 SQL 异常；显式迁移必须把这类失败提升为 baseline 失败。
            database.Aop.OnError = _ => Interlocked.Exchange(ref sqlFailureDetected, 1);

            // 旧迁移器会直接输出原始异常。一次性 schema 进程中统一静音，外层只记安全诊断码。
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            await baseline();
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref sqlFailureDetected) != 0)
            {
                throw new SchemaBaselineSqlFailureException();
            }
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            // 当前两个上下文都没有常驻 OnError；显式迁移结束后恢复为空，避免影响后续只读检查。
            database.Aop.OnError = null;
            SchemaConsoleRedirectLock.Release();
        }
    }

    private static string GetApplicationVersion()
    {
        var version =
            typeof(SchemaMigrationCoordinator).Assembly.GetName().Version?.ToString() ?? "unknown";
        return version.Length <= 64 ? version : version[..64];
    }

    private sealed record DatabaseDefinition(
        string ConnectionString,
        string HistoryTable,
        string HistoryPrimaryKey,
        string LockResource
    );

    private sealed class SqlServerSchemaMigrationSession(
        SqlServerSchemaMigrationLock migrationLock,
        DatabaseDefinition definition,
        string applicationVersion,
        int commandTimeoutSeconds
    ) : ISchemaMigrationSession
    {
        public Task EnsureHistoryTableAsync(CancellationToken cancellationToken) =>
            SqlServerSchemaMigrationStore.EnsureHistoryTableAsync(
                migrationLock.Connection,
                definition.HistoryTable,
                definition.HistoryPrimaryKey,
                commandTimeoutSeconds,
                cancellationToken
            );

        public Task<bool> IsAppliedAsync(
            string migrationId,
            CancellationToken cancellationToken
        ) =>
            SqlServerSchemaMigrationStore.IsAppliedAsync(
                migrationLock.Connection,
                definition.HistoryTable,
                migrationId,
                commandTimeoutSeconds,
                cancellationToken
            );

        public Task RecordAppliedAsync(
            string migrationId,
            CancellationToken cancellationToken
        ) =>
            SqlServerSchemaMigrationStore.RecordAppliedAsync(
                migrationLock.Connection,
                definition.HistoryTable,
                migrationId,
                applicationVersion,
                commandTimeoutSeconds,
                cancellationToken
            );

        public ValueTask DisposeAsync() => migrationLock.DisposeAsync();
    }
}
