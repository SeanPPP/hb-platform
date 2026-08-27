using System.Diagnostics;

namespace BlazorApp.Api.Data.SchemaMigrations;

internal sealed class SchemaMigrationCoordinator
{
    internal const string MainMigrationId = "20260827.001-hbweb-baseline";
    internal const string PosmMigrationId = "20260827.001-hbweb-posm-baseline";

    private const string MainScope = "Main";
    private const string PosmScope = "POSM";
    private const string DeviceActivationSignatureId = "device-activation-schema-signature";

    private readonly ISchemaMigrationRuntime _runtime;
    private readonly ILogger<SchemaMigrationCoordinator> _logger;

    public SchemaMigrationCoordinator(
        IConfiguration configuration,
        SqlSugarContext mainDbContext,
        POSMSqlSugarContext posmDbContext,
        ILogger<SchemaMigrationCoordinator> logger
    )
        : this(
            new SqlServerSchemaMigrationRuntime(
                configuration,
                mainDbContext,
                posmDbContext
            ),
            logger
        )
    { }

    internal SchemaMigrationCoordinator(
        ISchemaMigrationRuntime runtime,
        ILogger<SchemaMigrationCoordinator> logger
    )
    {
        _runtime = runtime;
        _logger = logger;
    }

    public async Task<SchemaOperationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        try
        {
            _runtime.EnsureSupportedProviders();
            await MigrateMainAsync(cancellationToken);
            await MigratePosmAsync(cancellationToken);

            var verification = await CheckCoreAsync(cancellationToken);
            return verification.Success
                ? SchemaOperationResult.MigrationSucceeded()
                : verification;
        }
        catch (OperationCanceledException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.Cancelled,
                SchemaDiagnosticCodes.Cancelled
            );
        }
        catch (SchemaMigrationLockUnavailableException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.MigrationLockUnavailable,
                SchemaDiagnosticCodes.MigrationLockUnavailable
            );
        }
        catch (DeviceActivationSchemaMismatchException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.SchemaNotReady,
                SchemaDiagnosticCodes.DeviceActivationIncompatible
            );
        }
        catch (SchemaProviderNotSupportedException)
        {
            LogResult(
                "All",
                "schema-migrate",
                elapsedMilliseconds: 0,
                "Failed",
                SchemaDiagnosticCodes.ProviderUnsupported
            );
            return SchemaOperationResult.Failure(
                SchemaExitCodes.DatabaseFailure,
                SchemaDiagnosticCodes.ProviderUnsupported
            );
        }
        catch (Exception)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.DatabaseFailure,
                SchemaDiagnosticCodes.MigrationFailure
            );
        }
    }

    public async Task<SchemaOperationResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            _runtime.EnsureSupportedProviders();
            return await CheckCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.Cancelled,
                SchemaDiagnosticCodes.Cancelled
            );
        }
        catch (DeviceActivationSchemaMismatchException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.SchemaNotReady,
                SchemaDiagnosticCodes.DeviceActivationIncompatible
            );
        }
        catch (SchemaProviderNotSupportedException)
        {
            LogResult(
                "All",
                "schema-check",
                elapsedMilliseconds: 0,
                "Failed",
                SchemaDiagnosticCodes.ProviderUnsupported
            );
            return SchemaOperationResult.Failure(
                SchemaExitCodes.DatabaseFailure,
                SchemaDiagnosticCodes.ProviderUnsupported
            );
        }
        catch (Exception)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.DatabaseFailure,
                SchemaDiagnosticCodes.DatabaseFailure
            );
        }
    }

    private async Task MigrateMainAsync(CancellationToken cancellationToken)
    {
        await RunMigrationAsync(
            SchemaDatabase.Main,
            MainScope,
            MainMigrationId,
            cancellationToken
        );
    }

    private async Task MigratePosmAsync(CancellationToken cancellationToken)
    {
        await RunMigrationAsync(
            SchemaDatabase.Posm,
            PosmScope,
            PosmMigrationId,
            cancellationToken
        );
    }

    private async Task RunMigrationAsync(
        SchemaDatabase database,
        string databaseScope,
        string migrationId,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var migrationSession = await _runtime.AcquireMigrationSessionAsync(
                database,
                cancellationToken
            );
            await migrationSession.EnsureHistoryTableAsync(cancellationToken);

            if (await migrationSession.IsAppliedAsync(migrationId, cancellationToken))
            {
                LogResult(
                    databaseScope,
                    migrationId,
                    stopwatch.ElapsedMilliseconds,
                    "Skipped",
                    "SCHEMA_MIGRATION_ALREADY_APPLIED"
                );
                return;
            }

            // baseline 内部迁移各自管理事务；这里只在全部成功后登记账本。
            await _runtime.ApplyBaselineAsync(database, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await migrationSession.RecordAppliedAsync(migrationId, cancellationToken);

            LogResult(
                databaseScope,
                migrationId,
                stopwatch.ElapsedMilliseconds,
                "Applied",
                "SCHEMA_MIGRATION_APPLIED"
            );
        }
        catch (Exception exception)
        {
            LogResult(
                databaseScope,
                migrationId,
                stopwatch.ElapsedMilliseconds,
                "Failed",
                exception switch
                {
                    OperationCanceledException => SchemaDiagnosticCodes.Cancelled,
                    SchemaMigrationLockUnavailableException =>
                        SchemaDiagnosticCodes.MigrationLockUnavailable,
                    _ => SchemaDiagnosticCodes.MigrationFailure,
                }
            );
            throw;
        }
    }

    private async Task<SchemaOperationResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        // 常规启动门禁固定为三批只读 SQL：两个账本各一批，设备激活签名一批。
        var mainApplied = await CheckLedgerAsync(
            SchemaDatabase.Main,
            MainScope,
            MainMigrationId,
            SchemaDiagnosticCodes.MainMigrationMissing,
            cancellationToken
        );
        var posmApplied = await CheckLedgerAsync(
            SchemaDatabase.Posm,
            PosmScope,
            PosmMigrationId,
            SchemaDiagnosticCodes.PosmMigrationMissing,
            cancellationToken
        );
        await VerifyDeviceActivationSchemaAsync(cancellationToken);

        if (!mainApplied)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.SchemaNotReady,
                SchemaDiagnosticCodes.MainMigrationMissing
            );
        }

        if (!posmApplied)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.SchemaNotReady,
                SchemaDiagnosticCodes.PosmMigrationMissing
            );
        }

        return SchemaOperationResult.Ready();
    }

    private async Task<bool> CheckLedgerAsync(
        SchemaDatabase database,
        string databaseScope,
        string migrationId,
        string missingDiagnosticCode,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var applied = await _runtime.IsMigrationAppliedAsync(
                database,
                migrationId,
                cancellationToken
            );
            LogResult(
                databaseScope,
                migrationId,
                stopwatch.ElapsedMilliseconds,
                applied ? "Ready" : "Missing",
                applied ? SchemaDiagnosticCodes.Ready : missingDiagnosticCode
            );
            return applied;
        }
        catch (Exception exception)
        {
            LogResult(
                databaseScope,
                migrationId,
                stopwatch.ElapsedMilliseconds,
                "Failed",
                exception is OperationCanceledException
                    ? SchemaDiagnosticCodes.Cancelled
                    : SchemaDiagnosticCodes.DatabaseFailure
            );
            throw;
        }
    }

    private async Task VerifyDeviceActivationSchemaAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _runtime.VerifyDeviceActivationSchemaAsync(cancellationToken);
            LogResult(
                PosmScope,
                DeviceActivationSignatureId,
                stopwatch.ElapsedMilliseconds,
                "Ready",
                SchemaDiagnosticCodes.Ready
            );
        }
        catch (Exception exception)
        {
            LogResult(
                PosmScope,
                DeviceActivationSignatureId,
                stopwatch.ElapsedMilliseconds,
                "Failed",
                exception switch
                {
                    OperationCanceledException => SchemaDiagnosticCodes.Cancelled,
                    DeviceActivationSchemaMismatchException =>
                        SchemaDiagnosticCodes.DeviceActivationIncompatible,
                    _ => SchemaDiagnosticCodes.DatabaseFailure,
                }
            );
            throw;
        }
    }

    private void LogResult(
        string databaseScope,
        string migrationId,
        long elapsedMilliseconds,
        string result,
        string diagnosticCode
    ) =>
        _logger.LogInformation(
            "Schema operation Scope={DatabaseScope} MigrationId={MigrationId} ElapsedMs={ElapsedMs} Result={Result} DiagnosticCode={DiagnosticCode}",
            databaseScope,
            migrationId,
            elapsedMilliseconds,
            result,
            diagnosticCode
        );

}
