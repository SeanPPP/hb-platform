using System.Runtime.CompilerServices;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class GiantServiceArchitectureContractTests
{
    private static readonly string[] LegacyServicePaths =
    [
        "services/backend/BlazorApp.Api/Services/React/StoreOrderReactService.cs",
        "services/backend/BlazorApp.Api/Services/React/ProductWarehouseReactService.cs",
        "services/backend/BlazorApp.Api/Services/Hangfire/SalesStatisticsJobService.cs",
        "services/backend/BlazorApp.Api/Services/React/LocalSupplierInvoicesReactService.cs",
        "services/backend/BlazorApp.Api/Services/DataSyncService.cs",
    ];

    private static readonly string[] FeatureRoots =
    [
        "StoreOrders",
        "ProductWarehouse",
        "SalesStatistics",
        "LocalSupplierInvoices",
        "DataSync",
    ];

    private static readonly (string Path, string ClassName)[] ExtensibleCompatibilityServices =
    [
        (
            "services/backend/BlazorApp.Api/Services/React/StoreOrderReactService.cs",
            "StoreOrderReactService"
        ),
        (
            "services/backend/BlazorApp.Api/Services/React/ProductWarehouseReactService.cs",
            "ProductWarehouseReactService"
        ),
        (
            "services/backend/BlazorApp.Api/Services/Hangfire/SalesStatisticsJobService.cs",
            "SalesStatisticsJobService"
        ),
        (
            "services/backend/BlazorApp.Api/Services/React/LocalSupplierInvoicesReactService.cs",
            "LocalSupplierInvoicesReactService"
        ),
        ("services/backend/BlazorApp.Api/Services/DataSyncService.cs", "DataSyncService"),
    ];

    private static readonly (string FeatureRoot, string FacadeName)[] FeatureFacadeBoundaries =
    [
        ("StoreOrders", "StoreOrderReactService"),
        ("ProductWarehouse", "ProductWarehouseReactService"),
        ("SalesStatistics", "SalesStatisticsJobService"),
        ("LocalSupplierInvoices", "LocalSupplierInvoicesReactService"),
        ("DataSync", "DataSyncService"),
    ];

    [Fact]
    public void 巨型服务兼容入口必须低于1500行()
    {
        var repoRoot = FindRepoRoot();
        var violations = LegacyServicePaths
            .Select(path => new
            {
                Path = path,
                Lines = File.ReadLines(Path.Combine(repoRoot, path)).Count(),
            })
            .Where(item => item.Lines >= 1500)
            .Select(item => $"{item.Path}: {item.Lines}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "兼容业务入口必须低于 1500 行：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 兼容入口只能编排或委派不得保留持久化事务锁和映射实现()
    {
        var repoRoot = FindRepoRoot();
        string[] forbiddenTokens =
        [
            ".Queryable<",
            ".Insertable(",
            ".Updateable(",
            ".Deleteable(",
            ".Ado.",
            "SugarParameter",
            "SqlFunc.",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SemaphoreSlim",
            "sp_getapplock",
            "new AutoMapper",
        ];
        var violations = LegacyServicePaths
            .SelectMany(path =>
            {
                var source = File.ReadAllText(Path.Combine(repoRoot, path));
                return forbiddenTokens
                    .Where(source.Contains)
                    .Select(token => $"{path}: {token}");
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "兼容入口仍包含应下沉到切片的实现：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 新增业务切片文件不得超过1500行()
    {
        var repoRoot = FindRepoRoot();
        var featuresRoot = Path.Combine(
            repoRoot,
            "services",
            "backend",
            "BlazorApp.Api",
            "Features"
        );
        var violations = FeatureRoots
            .Select(root => Path.Combine(featuresRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Select(file => new { File = file, Lines = File.ReadLines(file).Count() })
            .Where(item => item.Lines >= 1500)
            .Select(item => $"{Path.GetRelativePath(repoRoot, item.File)}: {item.Lines}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "新增切片文件必须低于 1500 行：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 兼容服务不得收紧原公开继承契约()
    {
        var repoRoot = FindRepoRoot();
        var violations = ExtensibleCompatibilityServices
            .Where(item =>
                File.ReadAllText(Path.Combine(repoRoot, item.Path))
                    .Contains($"public sealed class {item.ClassName}", StringComparison.Ordinal)
            )
            .Select(item => item.Path)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "兼容服务不得从 public class 收紧为 sealed：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 业务切片不得反向依赖兼容Facade类型()
    {
        var repoRoot = FindRepoRoot();
        var featuresRoot = Path.Combine(
            repoRoot,
            "services",
            "backend",
            "BlazorApp.Api",
            "Features"
        );
        var violations = FeatureFacadeBoundaries
            .SelectMany(boundary =>
            {
                var root = Path.Combine(featuresRoot, boundary.FeatureRoot);
                if (!Directory.Exists(root))
                {
                    return [];
                }

                string[] forbiddenTokens =
                [
                    $"ILogger<{boundary.FacadeName}>",
                    $"GetRequiredService<{boundary.FacadeName}>",
                    $"using static BlazorApp.Api.Services.{boundary.FacadeName}",
                    $"using static BlazorApp.Api.Services.React.{boundary.FacadeName}",
                    $"{boundary.FacadeName}.",
                ];

                return Directory
                    .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    .SelectMany(file =>
                        File.ReadLines(file)
                            .Select(
                                (line, index) =>
                                    new
                                    {
                                        File = file,
                                        Line = line,
                                        Number = index + 1,
                                    }
                            )
                    )
                    .Where(item =>
                        forbiddenTokens.Any(token =>
                        {
                            var index = item.Line.IndexOf(token, StringComparison.Ordinal);
                            if (index < 0)
                            {
                                return false;
                            }

                            // 日志文案可保留旧服务名；这里只拦截字符串字面量之外的类型引用。
                            return token != $"{boundary.FacadeName}."
                                || item.Line[..index].Count(character => character == '"') % 2 == 0;
                        })
                    )
                    .Select(item =>
                        $"{Path.GetRelativePath(repoRoot, item.File)}:{item.Number}: {item.Line.Trim()}"
                    );
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Feature 不得反向依赖兼容 façade 类型：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 业务切片不得跨模块依赖任何巨型兼容Facade类型()
    {
        var repoRoot = FindRepoRoot();
        var featuresRoot = Path.Combine(
            repoRoot,
            "services",
            "backend",
            "BlazorApp.Api",
            "Features"
        );
        string[] facadeNames =
        [
            "StoreOrderReactService",
            "ProductWarehouseReactService",
            "SalesStatisticsJobService",
            "LocalSupplierInvoicesReactService",
            "DataSyncService",
        ];

        var violations = FeatureRoots
            .Select(root => Path.Combine(featuresRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select(
                        (line, index) =>
                            new
                            {
                                File = file,
                                Line = line,
                                Number = index + 1,
                            }
                    )
            )
            .Where(item =>
            {
                var trimmed = item.Line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    return false;
                }

                return facadeNames.Any(name =>
                {
                    var index = item.Line.IndexOf(name, StringComparison.Ordinal);
                    return index >= 0
                        && item.Line[..index].Count(character => character == '"') % 2 == 0;
                });
            })
            .Select(item =>
                $"{Path.GetRelativePath(repoRoot, item.File)}:{item.Number}: {item.Line.Trim()}"
            )
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Feature 必须依赖窄接口，不能解析任何巨型兼容 façade：\n"
                + string.Join("\n", violations)
        );
    }

    private static string FindRepoRoot([CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory != null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
