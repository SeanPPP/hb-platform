using System.Data;
using BlazorApp.Api.Services;
using Microsoft.Data.SqlClient;

namespace BlazorApp.Api.Data.SchemaMigrations;

internal sealed record SalesDetailProjectionMaintenanceSettings(
    DateTime StartDate,
    DateTime EndDate,
    string ExpectedDatabase,
    string ExpectedServer,
    string MainConnectionString,
    string PosmDatabase,
    int CommandTimeoutSeconds);

/// <summary>显式回填或核验销售明细投影；不注册 HTTP 和后台任务。</summary>
internal sealed class SalesDetailQueryProjectionMaintenanceRunner(
    IConfiguration configuration,
    ILogger<SalesDetailQueryProjectionMaintenanceRunner> logger)
{
    internal async Task<SchemaOperationResult> RunAsync(
        bool checkOnly,
        CancellationToken cancellationToken)
    {
        if (!TryReadSettings(configuration, out var settings))
            return SchemaOperationResult.Failure(
                SchemaExitCodes.ConfigurationError,
                SchemaDiagnosticCodes.SalesDetailProjectionConfigurationInvalid);

        try
        {
            await using var connection = new SqlConnection(settings.MainConnectionString);
            await connection.OpenAsync(cancellationToken);

            if (!await ValidateTargetAsync(connection, settings, cancellationToken))
                return SchemaOperationResult.Failure(
                    SchemaExitCodes.ConfigurationError,
                    SchemaDiagnosticCodes.SalesDetailProjectionTargetMismatch);

            if (!await IsSchemaReadyAsync(connection, settings, cancellationToken))
                return SchemaOperationResult.Failure(
                    SchemaExitCodes.SchemaNotReady,
                    SchemaDiagnosticCodes.SalesDetailQueryProjectionIncompatible);

            if (checkOnly)
            {
                await using var checkTransaction = (SqlTransaction)await connection.BeginTransactionAsync(
                    IsolationLevel.Snapshot,
                    cancellationToken);
                var checkResult = await CheckCoverageAsync(
                    connection, settings, EnumerateDates(settings), checkTransaction, cancellationToken);
                await checkTransaction.CommitAsync(cancellationToken);
                return checkResult;
            }

            var dates = await LoadPublishedDatesAsync(
                connection, settings, transaction: null, cancellationToken: cancellationToken);

            foreach (var date in dates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RefreshDateAsync(connection, settings, date, cancellationToken);
                    if (!await IsDateCoveredAsync(connection, settings, date, cancellationToken))
                    {
                        logger.LogError(
                            "销售明细投影回填确认失败。Date={Date:yyyy-MM-dd} Code={Code}",
                            date,
                            SchemaDiagnosticCodes.SalesDetailProjectionDateFailed);
                        return SchemaOperationResult.Failure(
                            SchemaExitCodes.SchemaNotReady,
                            SchemaDiagnosticCodes.SalesDetailProjectionDateFailed);
                    }

                    logger.LogInformation("销售明细投影回填完成。Date={Date:yyyy-MM-dd}", date);
                }
                catch (SqlException)
                {
                    // 每天单独事务且不自动重试；超时、死锁或取消后的结果不能凭猜测重放。
                    logger.LogError(
                        "销售明细投影回填失败。Date={Date:yyyy-MM-dd} Code={Code}",
                        date,
                        SchemaDiagnosticCodes.SalesDetailProjectionDateFailed);
                    return SchemaOperationResult.Failure(
                        SchemaExitCodes.DatabaseFailure,
                        SchemaDiagnosticCodes.SalesDetailProjectionDateFailed);
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning(
                        "销售明细投影回填已取消。Date={Date:yyyy-MM-dd} Code={Code}",
                        date,
                        SchemaDiagnosticCodes.Cancelled);
                    throw;
                }
            }

            // 所有逐日事务完成后重新固定一个全范围快照，防止期间映射或发布状态变化造成假成功。
            await using var finalTransaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Snapshot,
                cancellationToken);
            var finalCheck = await CheckCoverageAsync(
                connection, settings, EnumerateDates(settings), finalTransaction, cancellationToken);
            await finalTransaction.CommitAsync(cancellationToken);
            if (!finalCheck.Success)
                return finalCheck;

            logger.LogInformation(
                "销售明细投影回填完成。StartDate={StartDate:yyyy-MM-dd} EndDate={EndDate:yyyy-MM-dd} BackfilledDateCount={Count}",
                settings.StartDate,
                settings.EndDate,
                dates.Count);
            return SchemaOperationResult.MigrationSucceeded();
        }
        catch (OperationCanceledException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.Cancelled,
                SchemaDiagnosticCodes.Cancelled);
        }
        catch (SqlException)
        {
            return SchemaOperationResult.Failure(
                SchemaExitCodes.DatabaseFailure,
                SchemaDiagnosticCodes.DatabaseFailure);
        }
    }

    internal static bool TryReadSettings(
        IConfiguration configuration,
        out SalesDetailProjectionMaintenanceSettings settings)
    {
        settings = null!;
        var startValue = configuration["SalesDetailProjection:StartDate"];
        var endValue = configuration["SalesDetailProjection:EndDate"];
        var expectedDatabase = configuration["SalesDetailProjection:ExpectedDatabase"]?.Trim();
        var expectedServer = configuration["SalesDetailProjection:ExpectedServer"]?.Trim();
        var mainConnection = configuration.GetConnectionString("DefaultConnection");
        var posmConnection = configuration.GetConnectionString("HBPOSMConnection");
        if (!DateTime.TryParseExact(startValue, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var start)
            || !DateTime.TryParseExact(endValue, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var end)
            || end.Date < start.Date || (end.Date - start.Date).Days > 365
            || string.IsNullOrWhiteSpace(expectedDatabase)
            || string.IsNullOrWhiteSpace(expectedServer)
            || string.IsNullOrWhiteSpace(mainConnection)
            || string.IsNullOrWhiteSpace(posmConnection))
            return false;

        try
        {
            var main = new SqlConnectionStringBuilder(mainConnection);
            var posm = new SqlConnectionStringBuilder(posmConnection);
            if (!string.Equals(main.DataSource, posm.DataSource, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(posm.InitialCatalog))
                return false;
            settings = new SalesDetailProjectionMaintenanceSettings(
                start.Date,
                end.Date,
                expectedDatabase,
                expectedServer,
                main.ConnectionString,
                posm.InitialCatalog,
                Math.Clamp(configuration.GetValue("Database:CommandTimeoutSeconds", 60), 1, 1800));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<bool> ValidateTargetAsync(
        SqlConnection connection,
        SalesDetailProjectionMaintenanceSettings settings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(nvarchar(128), DB_NAME()), CONVERT(nvarchar(128), @@SERVERNAME);";
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            && string.Equals(reader.GetString(0), settings.ExpectedDatabase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(reader.GetString(1), settings.ExpectedServer, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsSchemaReadyAsync(
        SqlConnection connection,
        SalesDetailProjectionMaintenanceSettings settings,
        CancellationToken cancellationToken)
    {
        await using var snapshot = connection.CreateCommand();
        snapshot.CommandText = """
SELECT COUNT(*) FROM sys.databases
WHERE [name] IN (DB_NAME(), @PosmDatabase) AND [snapshot_isolation_state] = 1;
""";
        snapshot.Parameters.AddWithValue("@PosmDatabase", settings.PosmDatabase);
        snapshot.CommandTimeout = settings.CommandTimeoutSeconds;
        if (Convert.ToInt32(await snapshot.ExecuteScalarAsync(cancellationToken)) != 2)
            return false;

        await using var ledger = connection.CreateCommand();
        ledger.CommandText = $"""
SELECT COUNT(*) FROM [dbo].[{SqlServerSchemaMigrationRuntime.MainHistoryTable}]
WHERE [MigrationId] = @MigrationId;
""";
        ledger.Parameters.AddWithValue("@MigrationId", SchemaMigrationCoordinator.SalesDetailQueryMappingUseMigrationId);
        ledger.CommandTimeout = settings.CommandTimeoutSeconds;
        try
        {
            if (Convert.ToInt32(await ledger.ExecuteScalarAsync(cancellationToken)) != 1)
                return false;
            await using var verify = connection.CreateCommand();
            verify.CommandText = SalesDetailQueryProjectionSchema.VerifySql;
            verify.CommandTimeout = settings.CommandTimeoutSeconds;
            await verify.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static async Task<List<DateTime>> LoadPublishedDatesAsync(
        SqlConnection connection,
        SalesDetailProjectionMaintenanceSettings settings,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT DISTINCT CONVERT(date, [Date])
FROM [dbo].[SalesStatisticRefreshState]
WHERE [StatisticType] = N'ProductStoreDaily'
  AND [Date] >= @StartDate AND [Date] < DATEADD(day, 1, @EndDate)
  AND [Status] IN (N'Fresh', N'ProvisionalFresh')
  AND NULLIF(LTRIM(RTRIM([SourceProductVersion])), N'') IS NOT NULL
  AND [LastAggregatedAtUtc] IS NOT NULL
ORDER BY CONVERT(date, [Date]);
""";
        command.Parameters.AddWithValue("@StartDate", settings.StartDate);
        command.Parameters.AddWithValue("@EndDate", settings.EndDate);
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        var dates = new List<DateTime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            dates.Add(reader.GetDateTime(0).Date);
        return dates;
    }

    private static async Task RefreshDateAsync(
        SqlConnection connection,
        SalesDetailProjectionMaintenanceSettings settings,
        DateTime date,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Snapshot,
            cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = SalesDetailQueryProjection.BuildRefreshDaySql(settings.PosmDatabase);
            command.CommandTimeout = settings.CommandTimeoutSeconds;
            command.Parameters.AddWithValue("@sdpDate", date);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private async Task<SchemaOperationResult> CheckCoverageAsync(
        SqlConnection connection,
        SalesDetailProjectionMaintenanceSettings settings,
        IReadOnlyList<DateTime> dates,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var missing = new List<DateTime>();
        foreach (var date in dates)
        {
            bool covered;
            try
            {
                covered = await IsDateCoveredAsync(
                    connection, settings, date, cancellationToken, transaction);
            }
            catch (SqlException)
            {
                logger.LogError(
                    "销售明细投影覆盖检查失败。Date={Date:yyyy-MM-dd} Code={Code}",
                    date,
                    SchemaDiagnosticCodes.SalesDetailProjectionDateFailed);
                return SchemaOperationResult.Failure(
                    SchemaExitCodes.DatabaseFailure,
                    SchemaDiagnosticCodes.SalesDetailProjectionDateFailed);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "销售明细投影覆盖检查已取消。Date={Date:yyyy-MM-dd} Code={Code}",
                    date,
                    SchemaDiagnosticCodes.Cancelled);
                throw;
            }

            if (!covered)
            {
                missing.Add(date);
                logger.LogWarning(
                    "销售明细投影覆盖缺口。Date={Date:yyyy-MM-dd} Code={Code}",
                    date,
                    SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing);
            }
        }

        logger.LogInformation(
            "销售明细投影覆盖检查完成。StartDate={StartDate:yyyy-MM-dd} EndDate={EndDate:yyyy-MM-dd} PublishedDateCount={PublishedCount} CoveredDateCount={CoveredCount} MissingDateCount={MissingCount}",
            settings.StartDate,
            settings.EndDate,
            dates.Count,
            dates.Count - missing.Count,
            missing.Count);
        return missing.Count == 0
            ? SchemaOperationResult.Ready()
            : SchemaOperationResult.Failure(
                SchemaExitCodes.SchemaNotReady,
                SchemaDiagnosticCodes.SalesDetailProjectionCoverageMissing);
    }

    private static IReadOnlyList<DateTime> EnumerateDates(
        SalesDetailProjectionMaintenanceSettings settings)
    {
        var dates = new List<DateTime>((settings.EndDate - settings.StartDate).Days + 1);
        for (var date = settings.StartDate; date <= settings.EndDate; date = date.AddDays(1))
            dates.Add(date);
        return dates;
    }

    private static async Task<bool> IsDateCoveredAsync(
        SqlConnection connection,
        SalesDetailProjectionMaintenanceSettings settings,
        DateTime date,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
DECLARE @MappingVersion varchar(64) = {{SalesDetailQueryProjection.BuildMappingSignatureSql(settings.PosmDatabase)}};
SELECT COUNT(*)
FROM [dbo].[SalesStatisticRefreshState] r
JOIN [dbo].[SalesDetailQueryProjectionState] p ON p.[Date] = CONVERT(date, r.[Date])
WHERE r.[StatisticType] = N'ProductStoreDaily' AND r.[Date] >= @Date AND r.[Date] < DATEADD(day, 1, @Date)
  AND r.[Status] IN (N'Fresh', N'ProvisionalFresh')
  AND NULLIF(LTRIM(RTRIM(r.[SourceProductVersion])), N'') IS NOT NULL
  AND r.[LastAggregatedAtUtc] IS NOT NULL
  AND p.[ProjectionSchemaVersion] = {{SalesDetailQueryProjection.SchemaVersion}}
  AND p.[MappingVersion] = @MappingVersion
  AND NOT EXISTS (
      SELECT p.[SourceProductVersion], p.[SourceLastAggregatedAtUtc], p.[SourceJobId]
      EXCEPT
      SELECT r.[SourceProductVersion], r.[LastAggregatedAtUtc], r.[JobId]
  );
""";
        command.Parameters.AddWithValue("@Date", date);
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
