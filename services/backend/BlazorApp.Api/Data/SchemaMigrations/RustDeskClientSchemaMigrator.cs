using Microsoft.Data.SqlClient;

namespace BlazorApp.Api.Data.SchemaMigrations;

/// <summary>
/// RustDesk 远程维护增量 schema 的显式入口。正常 HTTP 启动不调用此类，避免隐式写生产库。
/// </summary>
internal sealed class RustDeskClientSchemaMigrator(IConfiguration configuration)
{
    internal const string MigrationId = "20260906.002-rustdesk-client";

    private readonly string _connectionString =
        configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    private readonly int _commandTimeoutSeconds = Math.Clamp(
        configuration.GetValue("Database:CommandTimeoutSeconds", 60), 1, 1800);

    internal async Task<SchemaOperationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        try
        {
            EnsureSqlServerConnection();
            await SqlServerSchemaMigrationStore.ExecuteBatchAsync(
                _connectionString, ApplySql, _commandTimeoutSeconds, cancellationToken);
            await SqlServerSchemaMigrationStore.ExecuteReadOnlyBatchAsync(
                _connectionString, VerifySql, _commandTimeoutSeconds, cancellationToken);
            return SchemaOperationResult.MigrationSucceeded();
        }
        catch (OperationCanceledException)
        {
            return SchemaOperationResult.Failure(SchemaExitCodes.Cancelled, SchemaDiagnosticCodes.Cancelled);
        }
        catch (SchemaProviderNotSupportedException)
        {
            return SchemaOperationResult.Failure(SchemaExitCodes.ConfigurationError, SchemaDiagnosticCodes.ProviderUnsupported);
        }
        catch (Exception)
        {
            return SchemaOperationResult.Failure(SchemaExitCodes.DatabaseFailure, SchemaDiagnosticCodes.MigrationFailure);
        }
    }

    internal async Task<SchemaOperationResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            EnsureSqlServerConnection();
            await SqlServerSchemaMigrationStore.ExecuteReadOnlyBatchAsync(
                _connectionString, VerifySql, _commandTimeoutSeconds, cancellationToken);
            return SchemaOperationResult.Ready();
        }
        catch (OperationCanceledException)
        {
            return SchemaOperationResult.Failure(SchemaExitCodes.Cancelled, SchemaDiagnosticCodes.Cancelled);
        }
        catch (SchemaProviderNotSupportedException)
        {
            return SchemaOperationResult.Failure(SchemaExitCodes.ConfigurationError, SchemaDiagnosticCodes.ProviderUnsupported);
        }
        catch (Exception)
        {
            return SchemaOperationResult.Failure(SchemaExitCodes.SchemaNotReady, SchemaDiagnosticCodes.MainMigrationMissing);
        }
    }

    private void EnsureSqlServerConnection()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)
            || string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(_connectionString).DataSource))
            throw new SchemaProviderNotSupportedException();
    }

    internal const string ApplySql = """
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.HBweb_RustDeskClientSession', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.HBweb_RustDeskClientSession (
  Id uniqueidentifier NOT NULL CONSTRAINT PK_HBweb_RustDeskClientSession PRIMARY KEY,
  UserGuid nvarchar(100) NOT NULL,
  TokenHash nvarchar(64) NOT NULL,
  PasswordFingerprint nvarchar(64) NOT NULL,
  CreatedAtUtc datetime2 NOT NULL,
  ExpiresAtUtc datetime2 NOT NULL,
  RevokedAtUtc datetime2 NULL
 );
END;
IF OBJECT_ID(N'dbo.HBweb_RustDeskManagedDevice', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.HBweb_RustDeskManagedDevice (
  Id uniqueidentifier NOT NULL CONSTRAINT PK_HBweb_RustDeskManagedDevice PRIMARY KEY,
  RustdeskId nvarchar(100) NOT NULL,
  Alias nvarchar(120) NOT NULL,
  Hostname nvarchar(120) NOT NULL,
  Platform nvarchar(20) NOT NULL,
  CreatedAtUtc datetime2 NOT NULL,
  IsDisabled bit NOT NULL CONSTRAINT DF_HBweb_RustDeskManagedDevice_IsDisabled DEFAULT 0
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_HBweb_RustDeskClientSession_TokenHash' AND object_id = OBJECT_ID(N'dbo.HBweb_RustDeskClientSession'))
 CREATE UNIQUE INDEX UX_HBweb_RustDeskClientSession_TokenHash ON dbo.HBweb_RustDeskClientSession(TokenHash);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HBweb_RustDeskClientSession_ExpiresAtUtc' AND object_id = OBJECT_ID(N'dbo.HBweb_RustDeskClientSession'))
 CREATE INDEX IX_HBweb_RustDeskClientSession_ExpiresAtUtc ON dbo.HBweb_RustDeskClientSession(ExpiresAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_HBweb_RustDeskManagedDevice_RustdeskId' AND object_id = OBJECT_ID(N'dbo.HBweb_RustDeskManagedDevice'))
 CREATE UNIQUE INDEX UX_HBweb_RustDeskManagedDevice_RustdeskId ON dbo.HBweb_RustDeskManagedDevice(RustdeskId);
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
""";

    internal const string VerifySql = """
IF OBJECT_ID(N'dbo.HBweb_RustDeskClientSession', N'U') IS NULL
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskClientSession', N'TokenHash'), -1) <> 128
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskClientSession', N'PasswordFingerprint'), -1) <> 128
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskClientSession', N'UserGuid'), -1) <> 200
 OR COL_LENGTH(N'dbo.HBweb_RustDeskClientSession', N'ExpiresAtUtc') IS NULL
 OR COL_LENGTH(N'dbo.HBweb_RustDeskClientSession', N'RevokedAtUtc') IS NULL
 THROW 51720, N'RustDeskClientSession schema is missing or invalid.', 1;
IF OBJECT_ID(N'dbo.HBweb_RustDeskManagedDevice', N'U') IS NULL
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskManagedDevice', N'RustdeskId'), -1) <> 200
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskManagedDevice', N'Alias'), -1) <> 240
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskManagedDevice', N'Hostname'), -1) <> 240
 OR ISNULL(COL_LENGTH(N'dbo.HBweb_RustDeskManagedDevice', N'Platform'), -1) <> 40
 OR COL_LENGTH(N'dbo.HBweb_RustDeskManagedDevice', N'IsDisabled') IS NULL
 THROW 51721, N'RustDeskManagedDevice schema is missing or invalid.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_HBweb_RustDeskClientSession_TokenHash' AND object_id = OBJECT_ID(N'dbo.HBweb_RustDeskClientSession') AND is_unique = 1)
 OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_HBweb_RustDeskManagedDevice_RustdeskId' AND object_id = OBJECT_ID(N'dbo.HBweb_RustDeskManagedDevice') AND is_unique = 1)
 THROW 51722, N'RustDesk unique indexes are missing.', 1;
""";
}
