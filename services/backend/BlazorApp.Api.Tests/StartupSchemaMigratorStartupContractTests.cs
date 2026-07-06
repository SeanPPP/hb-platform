using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StartupSchemaMigratorStartupContractTests
{
    [Fact]
    public async Task Program_启动初始化接入统一StartupSchemaMigrator()
    {
        var repoRoot = FindRepoRoot();
        var programPath = Path.Combine(repoRoot, "services/backend/BlazorApp.Api/Program.cs");
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var program = await File.ReadAllTextAsync(programPath);
        var migrator = await File.ReadAllTextAsync(migratorPath);

        Assert.Contains(
            "await StartupSchemaMigrator.EnsureAsync(dbContext.Db, app.Logger);",
            program
        );
        Assert.DoesNotContain(
            "await LocalSupplierInvoiceStartupSchemaMigrator.EnsureAsync(dbContext.Db, app.Logger);",
            program
        );

        var localSupplierIndex = migrator.IndexOf(
            "await LocalSupplierInvoiceStartupSchemaMigrator.EnsureAsync(db, logger);",
            StringComparison.Ordinal
        );
        var mobileBuildIndex = migrator.IndexOf(
            "await EnsureMobileAppBuildSchemaAsync(db, logger);",
            StringComparison.Ordinal
        );
        var serviceTokenIndex = migrator.IndexOf(
            "await EnsureServiceApiTokenSchemaAsync(db, logger);",
            StringComparison.Ordinal
        );
        var wpfReleaseIndex = migrator.IndexOf(
            "await EnsureWpfAppReleaseSchemaAsync(db, logger);",
            StringComparison.Ordinal
        );

        // 关键位置：统一 migrator 必须先保留既有 LocalSupplier 兜底，再补移动端 APK/OTA 表结构。
        Assert.True(localSupplierIndex >= 0, "统一启动迁移必须继续保留 LocalSupplier 发票表兜底。");
        Assert.True(
            mobileBuildIndex > localSupplierIndex,
            "移动端 APK/OTA 表结构迁移必须在 LocalSupplier 兜底之后执行。"
        );
        Assert.True(
            serviceTokenIndex > mobileBuildIndex,
            "Service API Token 表必须随移动端 OTA 管理链路一起启动自举。"
        );
        Assert.True(
            wpfReleaseIndex > serviceTokenIndex,
            "WPF 发布表结构迁移必须在移动端发布表迁移之后执行，且保持独立表结构。"
        );
        Assert.Contains("IF OBJECT_ID('MobileAppBuild', 'U') IS NULL", migrator);
        Assert.Contains("IF OBJECT_ID('MobileAppOtaUpdate', 'U') IS NULL", migrator);
        Assert.Contains("IF OBJECT_ID('ServiceApiToken', 'U') IS NULL", migrator);
        Assert.Contains("IF OBJECT_ID('WpfAppRelease', 'U') IS NULL", migrator);
        Assert.Contains("IF OBJECT_ID('WpfUpdatePolicy', 'U') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WareHouseOrder', 'CartOwnerUserGuid') IS NULL", migrator);
        Assert.Contains("ADD [CartOwnerUserGuid] nvarchar(50) NULL", migrator);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_WareHouseOrder_CartScope]", migrator);
        Assert.Contains("CREATE UNIQUE INDEX [IX_MobileAppBuild_EasBuildId]", migrator);
        Assert.Contains("CREATE UNIQUE INDEX [IX_MobileAppOtaUpdate_Group_Platform]", migrator);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ServiceApiToken_TokenHash]", migrator);
        Assert.Contains("CREATE UNIQUE INDEX [IX_WpfAppRelease_Channel_Version]", migrator);
        Assert.Contains("CREATE UNIQUE INDEX [IX_WpfUpdatePolicy_Channel]", migrator);
        Assert.Contains("IF COL_LENGTH('MobileAppBuild', 'CosArtifactUrl') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'Channel') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'Version') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'FileName') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'FileSize') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'Sha256') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'DownloadUrl') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'InstallerType') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'ReleaseNotes') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'PublishedAt') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfAppRelease', 'CreatedAt') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfUpdatePolicy', 'Channel') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfUpdatePolicy', 'TargetVersion') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfUpdatePolicy', 'MinimumSupportedVersion') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('WpfUpdatePolicy', 'ForceUpdate') IS NULL", migrator);
        Assert.Contains("IF COL_LENGTH('CashRegisterUsers', 'UserGUID') IS NULL", migrator);
        Assert.Contains("ADD [UserGUID] nvarchar(50) NULL", migrator);
        Assert.Contains("SET [UserGUID] = LTRIM(RTRIM([UserGUID]))", migrator);
        Assert.Contains("INNER JOIN [User] AS [linkedUser]", migrator);
        Assert.Contains(
            "ON [linkedUser].[UserGUID] = LTRIM(RTRIM([cashier].[OperatorUser]))",
            migrator
        );
        Assert.Contains("CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserGUID_Active]", migrator);
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY [UserGUID]", migrator);
        Assert.Contains("CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserBarcode_Active]", migrator);
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY [UserBarcode]", migrator);
        Assert.Contains("COL_LENGTH('CashRegisterUsers', 'Status') IS NOT NULL", migrator);
        Assert.Contains("COL_LENGTH('CashRegisterUsers', 'Id') IS NOT NULL", migrator);
        Assert.Contains("COL_LENGTH('CashRegisterUsers', 'LastModifyDate') IS NOT NULL", migrator);
        Assert.Contains("COL_LENGTH('CashRegisterUsers', 'CreateDate') IS NOT NULL", migrator);
        Assert.Contains(
            "AND ([UserBarcode] IS NULL OR LTRIM(RTRIM([UserBarcode])) = '')",
            migrator
        );
        Assert.Contains(
            "WHERE [Status] = 1 AND [UserBarcode] IS NOT NULL AND [UserBarcode] <> ''",
            migrator
        );

        var wpfReleaseColumnBackfillIndex = migrator.IndexOf(
            "IF COL_LENGTH('WpfAppRelease', 'PublishedAt') IS NULL",
            StringComparison.Ordinal
        );
        var wpfReleasePublishedIndex = migrator.IndexOf(
            "CREATE INDEX [IX_WpfAppRelease_Channel_PublishedAt]",
            StringComparison.Ordinal
        );
        var wpfPolicyColumnBackfillIndex = migrator.IndexOf(
            "IF COL_LENGTH('WpfUpdatePolicy', 'Channel') IS NULL",
            StringComparison.Ordinal
        );
        var wpfPolicyIndex = migrator.IndexOf(
            "CREATE UNIQUE INDEX [IX_WpfUpdatePolicy_Channel]",
            StringComparison.Ordinal
        );
        var cashierUserGuidColumnIndex = migrator.IndexOf(
            "ADD [UserGUID] nvarchar(50) NULL",
            StringComparison.Ordinal
        );
        var cashierUserGuidNormalizeIndex = migrator.IndexOf(
            "SET [UserGUID] = LTRIM(RTRIM([UserGUID]))",
            StringComparison.Ordinal
        );
        var cashierUserGuidBackfillIndex = migrator.IndexOf(
            "INNER JOIN [User] AS [linkedUser]",
            StringComparison.Ordinal
        );
        var cashierDuplicateCleanupIndex = migrator.IndexOf(
            "ROW_NUMBER() OVER (PARTITION BY [UserGUID]",
            StringComparison.Ordinal
        );
        var cashierUniqueIndex = migrator.IndexOf(
            "CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserGUID_Active]",
            StringComparison.Ordinal
        );
        var cashierBarcodeDuplicateCleanupIndex = migrator.IndexOf(
            "ROW_NUMBER() OVER (PARTITION BY [UserBarcode]",
            StringComparison.Ordinal
        );
        var cashierBarcodeUniqueIndex = migrator.IndexOf(
            "CREATE UNIQUE INDEX [IX_CashRegisterUsers_UserBarcode_Active]",
            StringComparison.Ordinal
        );

        // 关键位置：旧表缺列时必须先补齐索引依赖列，再创建索引，否则启动迁移会在补列前失败。
        Assert.True(
            wpfReleasePublishedIndex > wpfReleaseColumnBackfillIndex,
            "WPF 发布表索引必须晚于 PublishedAt/IsActive 等缺列补齐。"
        );
        Assert.True(
            wpfPolicyIndex > wpfPolicyColumnBackfillIndex,
            "WPF 策略表唯一索引必须晚于 Channel 等缺列补齐。"
        );
        Assert.True(
            cashierUserGuidNormalizeIndex > cashierUserGuidColumnIndex,
            "收银条码 UserGUID 必须先 trim 规范化，避免空格绕过有效唯一约束。"
        );
        Assert.True(
            cashierUserGuidBackfillIndex > cashierUserGuidNormalizeIndex,
            "收银条码 UserGUID 规范化后才能从旧 OperatorUser 的确定后台 UserGUID 做兼容回填。"
        );
        Assert.True(
            cashierDuplicateCleanupIndex > cashierUserGuidBackfillIndex,
            "收银条码有效唯一索引前必须先补齐 UserGUID 并清理历史重复有效条码。"
        );
        Assert.True(
            cashierUniqueIndex > cashierDuplicateCleanupIndex,
            "收银条码 UserGUID+Status 过滤唯一索引必须晚于重复数据清理 SQL。"
        );
        Assert.True(
            cashierBarcodeDuplicateCleanupIndex > cashierUserGuidColumnIndex,
            "收银条码 UserBarcode 过滤唯一索引前必须先清理历史重复有效条码。"
        );
        Assert.True(
            cashierBarcodeUniqueIndex > cashierBarcodeDuplicateCleanupIndex,
            "收银条码 UserBarcode+Status 过滤唯一索引必须晚于重复条码清理 SQL。"
        );
    }

    [Fact]
    public async Task Program_默认认证保持JwtServiceToken只能显式启用()
    {
        var repoRoot = FindRepoRoot();
        var programPath = Path.Combine(repoRoot, "services/backend/BlazorApp.Api/Program.cs");
        var program = await File.ReadAllTextAsync(programPath);

        // 关键位置：hbsvc_ 不能成为全站默认认证，否则会通过普通 [Authorize] 业务接口。
        Assert.Contains(
            "options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;",
            program
        );
        Assert.Contains(
            "options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;",
            program
        );
        Assert.DoesNotContain(
            "options.DefaultAuthenticateScheme = ServiceApiTokenAuthenticationDefaults.PolicyScheme;",
            program
        );
        Assert.Contains(
            "ServiceApiTokenAuthenticationDefaults.RequestHasServiceApiToken(context.Request)",
            program
        );
    }

    [Fact]
    public async Task StartupSchemaMigrator_UserGuidBackfillUsesSeparateBatchAfterAddingColumn()
    {
        var repoRoot = FindRepoRoot();
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var migrator = await File.ReadAllTextAsync(migratorPath);

        var addColumnIndex = migrator.IndexOf(
            "ADD [UserGUID] nvarchar(50) NULL",
            StringComparison.Ordinal
        );
        var normalizeIndex = migrator.IndexOf(
            "SET [UserGUID] = LTRIM(RTRIM([UserGUID]))",
            StringComparison.Ordinal
        );

        Assert.True(addColumnIndex >= 0, "收银条码迁移必须先补齐 UserGUID 列。");
        Assert.True(normalizeIndex > addColumnIndex, "UserGUID 清理必须晚于补列。");

        var executeAfterAddIndex = migrator.IndexOf(
            "await db.Ado.ExecuteCommandAsync",
            addColumnIndex,
            StringComparison.Ordinal
        );

        // 关键位置：SQL Server 会先编译整个 batch，同一 batch 中 ADD 后再静态引用新列会触发 Invalid column name。
        Assert.True(
            executeAfterAddIndex > addColumnIndex && executeAfterAddIndex < normalizeIndex,
            "CashRegisterUsers.UserGUID 补列必须先作为独立 batch 执行，再执行引用 UserGUID 的清理/回填 SQL。"
        );
    }

    [Fact]
    public async Task StartupSchemaMigrator_WarehouseCartOwnerColumnFailureIsNotSwallowed()
    {
        var repoRoot = FindRepoRoot();
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var migrator = await File.ReadAllTextAsync(migratorPath);

        var addColumnIndex = migrator.IndexOf(
            "ADD [CartOwnerUserGuid] nvarchar(50) NULL",
            StringComparison.Ordinal
        );
        var throwIndex = migrator.IndexOf(
            "THROW 51000, 'WareHouseOrder.CartOwnerUserGuid migration failed', 1;",
            StringComparison.Ordinal
        );
        var indexWarningIndex = migrator.IndexOf(
            "logger.LogWarning(ex, \"仓库订货购物车归属索引迁移失败\");",
            StringComparison.Ordinal
        );

        // 关键位置：CartOwnerUserGuid 缺列会让所有活动购物车 scope 查询失败，补列不能被 catch 吞掉。
        Assert.True(addColumnIndex >= 0, "仓库购物车 owner 补列 SQL 必须存在。");
        Assert.True(throwIndex > addColumnIndex, "补列后必须验证列存在，不存在就抛错阻断启动。");
        Assert.True(indexWarningIndex > throwIndex, "只有索引创建可以降级 warning，必需列不能降级。");
        Assert.DoesNotContain("仓库订货购物车归属字段迁移失败", migrator);
    }

    [Fact]
    public async Task StartupSchemaMigrator_唯一索引前先清理Wpf重复数据()
    {
        var repoRoot = FindRepoRoot();
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var migrator = await File.ReadAllTextAsync(migratorPath);

        var releaseDedupIndex = migrator.IndexOf(
            "ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel], [NormalizedVersion]",
            StringComparison.Ordinal
        );
        var releaseDeactivateIndex = migrator.IndexOf(
            "SET [IsActive] = 0",
            StringComparison.Ordinal
        );
        var releaseUniqueIndex = migrator.IndexOf(
            "CREATE UNIQUE INDEX [IX_WpfAppRelease_Channel_Version]",
            StringComparison.Ordinal
        );
        var policyDedupIndex = migrator.IndexOf(
            "ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel]",
            StringComparison.Ordinal
        );
        var policyDeleteIndex = migrator.IndexOf(
            "DELETE FROM [WpfUpdatePolicy]",
            StringComparison.Ordinal
        );
        var policyUniqueIndex = migrator.IndexOf(
            "CREATE UNIQUE INDEX [IX_WpfUpdatePolicy_Channel]",
            StringComparison.Ordinal
        );

        // 关键位置：旧库里可能已经存在重复 channel/version 或 channel，启动迁移必须先整理脏数据再建唯一索引。
        Assert.True(
            releaseDedupIndex >= 0,
            "WpfAppRelease 建唯一索引前必须先按规范化后的 Channel+Version 做窗口去重排序。"
        );
        Assert.True(
            releaseDeactivateIndex > releaseDedupIndex,
            "WpfAppRelease 重复记录必须先把非保留行安全失活，避免唯一索引创建失败。"
        );
        Assert.True(
            releaseUniqueIndex > releaseDeactivateIndex,
            "WpfAppRelease 唯一索引必须晚于重复数据清理 SQL。"
        );
        Assert.True(
            policyDedupIndex >= 0,
            "WpfUpdatePolicy 建唯一索引前必须先按规范化后的 Channel 做窗口去重排序。"
        );
        Assert.True(
            policyDeleteIndex > policyDedupIndex,
            "WpfUpdatePolicy 重复记录必须先删除或归并非保留行。"
        );
        Assert.True(
            policyUniqueIndex > policyDeleteIndex,
            "WpfUpdatePolicy 唯一索引必须晚于重复数据清理 SQL。"
        );
    }

    [Fact]
    public async Task StartupSchemaMigrator_先规范化Wpf版本与渠道再去重()
    {
        var repoRoot = FindRepoRoot();
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var migrator = await File.ReadAllTextAsync(migratorPath);

        var releaseNormalizationIndex = migrator.IndexOf(
            "-- 关键位置：先把 WPF 发布历史数据规范成业务层可比较的渠道和版本，避免大小写和 v 前缀把重复版本漏过去。",
            StringComparison.Ordinal
        );
        var policyNormalizationIndex = migrator.IndexOf(
            "-- 关键位置：策略表也要先按业务层语义规范化渠道和版本，避免引用不到已规范化的发布版本。",
            StringComparison.Ordinal
        );
        var releaseDedupIndex = releaseNormalizationIndex < 0
            ? -1
            : migrator.IndexOf(
                "ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel], [NormalizedVersion]",
                releaseNormalizationIndex,
                StringComparison.Ordinal
            );
        var policyDedupIndex = policyNormalizationIndex < 0
            ? -1
            : migrator.IndexOf(
                "ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel]",
                policyNormalizationIndex,
                StringComparison.Ordinal
            );

        // 关键位置：迁移必须先把历史数据按业务语义规范化，再按规范化值去重，否则 v1.2.3/1.2.3 与大小写渠道会漏掉脏数据。
        Assert.Contains("LOWER(LTRIM(RTRIM([Channel])))", migrator);
        Assert.Contains("LEFT(LTRIM(RTRIM([Version])), 1) IN ('v', 'V')", migrator);
        Assert.Contains("LEFT(LTRIM(RTRIM([TargetVersion])), 1) IN ('v', 'V')", migrator);
        Assert.Contains(
            "LEFT(LTRIM(RTRIM([MinimumSupportedVersion])), 1) IN ('v', 'V')",
            migrator
        );
        Assert.Contains("ISNULL([Channel], '') <>", migrator);
        Assert.Contains("ISNULL([Version], '') <>", migrator);
        Assert.Contains("ISNULL([TargetVersion], '') <>", migrator);
        Assert.True(
            releaseNormalizationIndex >= 0,
            "WpfAppRelease 必须先执行 channel/version 规范化更新 SQL。"
        );
        Assert.True(
            releaseDedupIndex > releaseNormalizationIndex,
            "WpfAppRelease 必须在规范化 SQL 之后按规范化后的 Channel/Version 去重。"
        );
        Assert.True(
            policyNormalizationIndex >= 0,
            "WpfUpdatePolicy 必须先执行 channel/target/minimum 规范化更新 SQL。"
        );
        Assert.True(
            policyDedupIndex > policyNormalizationIndex,
            "WpfUpdatePolicy 必须在规范化 SQL 之后按规范化后的 Channel 去重。"
        );
    }

    [Fact]
    public async Task StartupSchemaMigrator_WpfDuplicateCleanup_prefers_non_deleted_rows()
    {
        var repoRoot = FindRepoRoot();
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var migrator = await File.ReadAllTextAsync(migratorPath);

        Assert.DoesNotContain(
            migrator.Split(Environment.NewLine),
            line => line.TrimStart().StartsWith("--", StringComparison.Ordinal) &&
                (line.Contains("IF OBJECT_ID('WpfAppRelease'", StringComparison.Ordinal) ||
                    line.Contains("IF OBJECT_ID('WpfUpdatePolicy'", StringComparison.Ordinal))
        );
        Assert.Contains("CASE WHEN ISNULL([IsDeleted], 0) = 0 THEN 0 ELSE 1 END", migrator);

        var releaseDeletedPreferenceIndex = migrator.IndexOf(
            "CASE WHEN ISNULL([IsDeleted], 0) = 0 THEN 0 ELSE 1 END",
            StringComparison.Ordinal
        );
        var releaseActivePreferenceIndex = migrator.IndexOf(
            "CASE WHEN [IsActive] = 1 THEN 0 ELSE 1 END",
            StringComparison.Ordinal
        );
        Assert.True(
            releaseDeletedPreferenceIndex >= 0 &&
                releaseDeletedPreferenceIndex < releaseActivePreferenceIndex,
            "WpfAppRelease duplicate cleanup must prefer non-deleted rows before active/newer rows."
        );

        var policyDedupIndex = migrator.IndexOf(
            "ROW_NUMBER() OVER (PARTITION BY [NormalizedChannel]",
            StringComparison.Ordinal
        );
        var policyDeletedPreferenceIndex = migrator.IndexOf(
            "CASE WHEN ISNULL([IsDeleted], 0) = 0 THEN 0 ELSE 1 END",
            policyDedupIndex,
            StringComparison.Ordinal
        );
        var policyLastChangedPreferenceIndex = migrator.IndexOf(
            "[LastChangedAt] DESC",
            policyDedupIndex,
            StringComparison.Ordinal
        );
        Assert.True(
            policyDeletedPreferenceIndex >= 0 &&
                policyDeletedPreferenceIndex < policyLastChangedPreferenceIndex,
            "WpfUpdatePolicy duplicate cleanup must prefer non-deleted rows before newer rows."
        );
    }

    [Fact]
    public async Task StartupSchemaMigrator_EnsuresNullableMinimumSupportedVersionBeforePolicyNormalization()
    {
        var repoRoot = FindRepoRoot();
        var migratorPath = Path.Combine(
            repoRoot,
            "services/backend/BlazorApp.Api/Data/StartupSchemaMigrator.cs"
        );

        var migrator = await File.ReadAllTextAsync(migratorPath);

        var ensureNullableIndex = migrator.IndexOf(
            "ALTER COLUMN [MinimumSupportedVersion] nvarchar(80) NULL;",
            StringComparison.Ordinal
        );
        var normalizationIndex = migrator.IndexOf(
            "UPDATE [WpfUpdatePolicy]",
            StringComparison.Ordinal
        );

        // 关键位置：旧库可能把 MinimumSupportedVersion 建成 NOT NULL，规范化空白值写回 NULL 之前必须先放宽列可空。
        Assert.Contains(
            "IF COL_LENGTH('WpfUpdatePolicy', 'MinimumSupportedVersion') IS NOT NULL",
            migrator
        );
        Assert.True(
            ensureNullableIndex >= 0,
            "WpfUpdatePolicy.MinimumSupportedVersion 已存在时，启动迁移必须显式 ALTER COLUMN 为 nvarchar(80) NULL。"
        );
        Assert.True(
            normalizationIndex > ensureNullableIndex,
            "WpfUpdatePolicy.MinimumSupportedVersion 的可空兜底必须早于规范化 UPDATE SQL。"
        );
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var programPath = Path.Combine(
                directory.FullName,
                "services/backend/BlazorApp.Api/Program.cs"
            );
            if (
                (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git"))
                    || Directory.Exists(Path.Combine(directory.FullName, ".gitnexus")))
                && File.Exists(programPath)
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var programPath = Path.Combine(
                directory.FullName,
                "services/backend/BlazorApp.Api/Program.cs"
            );
            if (File.Exists(programPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 hb-platform 仓库根目录");
    }
}
