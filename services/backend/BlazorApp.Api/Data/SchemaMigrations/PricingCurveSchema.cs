namespace BlazorApp.Api.Data.SchemaMigrations;

/// <summary>只新增可空列；旧策略继续由成本和成率推导端点，不改写已有业务数据。</summary>
internal static class PricingCurveSchema
{
    internal const string ApplySql = """
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.PricingStrategyDetail', N'U') IS NULL
        THROW 51710, 'PricingStrategyDetail table is missing.', 1;
    IF EXISTS (
        SELECT 1
        FROM (VALUES (N'StartRetailPrice', 2), (N'EndRetailPrice', 2), (N'CurveBend', 6)) AS expected(name, scale)
        JOIN sys.columns AS actual
            ON actual.object_id = OBJECT_ID(N'dbo.PricingStrategyDetail') AND actual.name = expected.name
        WHERE actual.system_type_id <> TYPE_ID(N'decimal') OR actual.precision <> 18
            OR actual.scale <> expected.scale OR actual.is_nullable <> 1
    )
        THROW 51711, 'Existing pricing curve column signature is incompatible.', 1;
    IF COL_LENGTH(N'dbo.PricingStrategyDetail', N'StartRetailPrice') IS NULL
        ALTER TABLE dbo.PricingStrategyDetail ADD StartRetailPrice decimal(18,2) NULL;
    IF COL_LENGTH(N'dbo.PricingStrategyDetail', N'EndRetailPrice') IS NULL
        ALTER TABLE dbo.PricingStrategyDetail ADD EndRetailPrice decimal(18,2) NULL;
    IF COL_LENGTH(N'dbo.PricingStrategyDetail', N'CurveBend') IS NULL
        ALTER TABLE dbo.PricingStrategyDetail ADD CurveBend decimal(18,6) NULL;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";

    // 常规启动只读核对类型、精度与可空性，不通过 ALTER 自动修复不兼容结构。
    internal const string VerifySql = """
IF OBJECT_ID(N'dbo.PricingStrategyDetail', N'U') IS NULL
    THROW 51710, 'PricingStrategyDetail table is missing.', 1;
IF EXISTS (
    SELECT 1
    FROM (VALUES (N'StartRetailPrice', 2), (N'EndRetailPrice', 2), (N'CurveBend', 6)) AS expected(name, scale)
    LEFT JOIN sys.columns AS actual
        ON actual.object_id = OBJECT_ID(N'dbo.PricingStrategyDetail') AND actual.name = expected.name
    WHERE actual.column_id IS NULL OR actual.system_type_id <> TYPE_ID(N'decimal')
        OR actual.precision <> 18 OR actual.scale <> expected.scale OR actual.is_nullable <> 1
)
    THROW 51711, 'Pricing curve column signature is incompatible.', 1;
""";
}
