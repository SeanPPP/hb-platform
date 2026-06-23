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
            "await EnsureMobileAppBuildCosMirrorColumnsAsync(db, logger);",
            StringComparison.Ordinal
        );

        // 关键位置：统一 migrator 必须先保留既有 LocalSupplier 兜底，再补 APK 镜像字段。
        Assert.True(localSupplierIndex >= 0);
        Assert.True(mobileBuildIndex > localSupplierIndex);
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
            if (File.Exists(programPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 hb-platform 仓库根目录");
    }
}
