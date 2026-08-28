namespace BlazorApp.Api.Data.SchemaMigrations;

internal static class SchemaExitCodes
{
    public const int Success = 0;
    public const int ConfigurationError = 2;
    public const int SchemaNotReady = 20;
    public const int DatabaseFailure = 22;
    public const int MigrationLockUnavailable = 23;
    public const int Cancelled = 130;
}

internal static class SchemaDiagnosticCodes
{
    public const string Ready = "SCHEMA_READY";
    public const string MigrationSucceeded = "SCHEMA_MIGRATION_SUCCEEDED";
    public const string MainMigrationMissing = "SCHEMA_MAIN_MIGRATION_MISSING";
    public const string PosmMigrationMissing = "SCHEMA_POSM_MIGRATION_MISSING";
    public const string DeviceActivationIncompatible = "SCHEMA_DEVICE_ACTIVATION_INCOMPATIBLE";
    public const string ProviderUnsupported = "SCHEMA_PROVIDER_UNSUPPORTED";
    public const string DatabaseFailure = "SCHEMA_DATABASE_FAILURE";
    public const string MigrationFailure = "SCHEMA_MIGRATION_FAILURE";
    public const string MigrationLockUnavailable = "SCHEMA_MIGRATION_LOCK_UNAVAILABLE";
    public const string Cancelled = "SCHEMA_CANCELLED";
}

internal sealed record SchemaOperationResult(bool Success, int ExitCode, string DiagnosticCode)
{
    public static SchemaOperationResult Ready() =>
        new(true, SchemaExitCodes.Success, SchemaDiagnosticCodes.Ready);

    public static SchemaOperationResult MigrationSucceeded() =>
        new(true, SchemaExitCodes.Success, SchemaDiagnosticCodes.MigrationSucceeded);

    public static SchemaOperationResult Failure(int exitCode, string diagnosticCode) =>
        new(false, exitCode, diagnosticCode);
}
