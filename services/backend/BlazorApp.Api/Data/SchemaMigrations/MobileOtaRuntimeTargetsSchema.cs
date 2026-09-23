namespace BlazorApp.Api.Data.SchemaMigrations;

/// <summary>
/// 为移动端 OTA 策略追加并行 runtime 目标 JSON。迁移只新增可空列，绝不改写既有策略行。
/// </summary>
internal static class MobileOtaRuntimeTargetsSchema
{
    internal const string ApplySql = """
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.MobileOtaPolicy', N'U') IS NULL
        THROW 51910, 'MobileOtaPolicy table is missing.', 1;
    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.MobileOtaPolicy')
          AND name = N'AdditionalTargetsJson'
          AND (
              system_type_id <> TYPE_ID(N'nvarchar')
              OR max_length <> -1
              OR is_nullable <> 1
          )
    )
        THROW 51911, 'Existing MobileOtaPolicy.AdditionalTargetsJson signature is incompatible.', 1;
    IF COL_LENGTH(N'dbo.MobileOtaPolicy', N'AdditionalTargetsJson') IS NULL
        ALTER TABLE dbo.MobileOtaPolicy ADD AdditionalTargetsJson nvarchar(max) NULL;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

    // 启动检查只读核验表、类型、MAX 长度和可空性，不自动修复错误结构。
    internal const string VerifySql = """
IF OBJECT_ID(N'dbo.MobileOtaPolicy', N'U') IS NULL
    THROW 51910, 'MobileOtaPolicy table is missing.', 1;
IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.MobileOtaPolicy')
      AND name = N'AdditionalTargetsJson'
      AND (
          system_type_id <> TYPE_ID(N'nvarchar')
          OR max_length <> -1
          OR is_nullable <> 1
      )
)
    THROW 51911, 'MobileOtaPolicy.AdditionalTargetsJson signature is incompatible.', 1;
IF COL_LENGTH(N'dbo.MobileOtaPolicy', N'AdditionalTargetsJson') IS NULL
    THROW 51912, 'MobileOtaPolicy.AdditionalTargetsJson is missing.', 1;
""";
}
