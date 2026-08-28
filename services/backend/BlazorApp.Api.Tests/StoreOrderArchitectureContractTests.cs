using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderArchitectureContractTests
{
    private static readonly Regex StoreOrderUsingRegex = new(
        @"^using\s+BlazorApp\.Api\.Features\.StoreOrders\.([A-Za-z0-9_]+)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    );

    [Fact]
    public void StoreOrder切片只能依赖自身或Common()
    {
        var repoRoot = FindRepoRoot();
        var featuresRoot = Path.Combine(
            repoRoot,
            "services",
            "backend",
            "BlazorApp.Api",
            "Features",
            "StoreOrders"
        );
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(featuresRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(featuresRoot, file);
            var pathSegments = relativePath.Split(Path.DirectorySeparatorChar);
            if (pathSegments.Length < 2)
            {
                // StoreOrderFeatureServiceCollectionExtensions 是唯一允许组合全部切片的根组合器。
                continue;
            }

            var sourceSlice = pathSegments[0];
            var source = File.ReadAllText(file);
            foreach (Match match in StoreOrderUsingRegex.Matches(source))
            {
                var targetSlice = match.Groups[1].Value;
                if (
                    !targetSlice.Equals(sourceSlice, StringComparison.Ordinal)
                    && !targetSlice.Equals("Common", StringComparison.Ordinal)
                )
                {
                    violations.Add($"{relativePath}: {sourceSlice} -> {targetSlice}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "发现跨切片直接依赖，必须提升为 Common 窄端口：\n"
                + string.Join("\n", violations.OrderBy(item => item, StringComparer.Ordinal))
        );
    }

    [Fact]
    public void StoreOrder顶级文件规模符合拆分目标()
    {
        var repoRoot = FindRepoRoot();
        var apiRoot = Path.Combine(repoRoot, "services", "backend", "BlazorApp.Api");
        var featureFiles = Directory.EnumerateFiles(
            Path.Combine(apiRoot, "Features", "StoreOrders"),
            "*.cs",
            SearchOption.AllDirectories
        );
        var controllerFiles = Directory.EnumerateFiles(
            Path.Combine(apiRoot, "Controllers", "React", "StoreOrders"),
            "*.cs",
            SearchOption.AllDirectories
        );
        var oversized = featureFiles
            .Concat(controllerFiles)
            .Select(file => new { File = file, Lines = File.ReadLines(file).Count() })
            .Where(item => item.Lines > 1500)
            .Select(item => $"{Path.GetRelativePath(repoRoot, item.File)}: {item.Lines}")
            .ToArray();

        Assert.True(
            oversized.Length == 0,
            "StoreOrder 顶级文件超过 1500 行：\n" + string.Join("\n", oversized)
        );

        var facadePath = Path.Combine(
            apiRoot,
            "Services",
            "React",
            "StoreOrderReactService.cs"
        );
        Assert.True(
            File.ReadLines(facadePath).Count() < 500,
            "StoreOrderReactService 兼容 façade 必须低于 500 行"
        );
    }

    [Fact]
    public void StoreOrderController和Facade不包含持久化事务或锁实现()
    {
        var repoRoot = FindRepoRoot();
        var apiRoot = Path.Combine(repoRoot, "services", "backend", "BlazorApp.Api");
        var controllerRoot = Path.Combine(apiRoot, "Controllers", "React", "StoreOrders");
        string[] forbiddenControllerTokens =
        [
            "IStoreOrderReactService",
            ".Queryable<",
            ".Ado.",
            "SqlSugarContext",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SemaphoreSlim",
            "IMemoryCache",
            "StoreOrderCacheKeys",
            "MemoryCacheEntryOptions",
            "IPreorderGateService",
            "RequirePreorderCompletionAsync",
            "CanBypassPreorderCompletionAsync",
            "BypassPreorderGate =",
        ];

        var controllerViolations = Directory
            .EnumerateFiles(controllerRoot, "StoreOrder*Controller.cs")
            .SelectMany(file =>
            {
                var source = File.ReadAllText(file);
                return forbiddenControllerTokens
                    .Where(source.Contains)
                    .Select(token => $"{Path.GetFileName(file)}: {token}");
            })
            .ToArray();

        var facadePath = Path.Combine(
            apiRoot,
            "Services",
            "React",
            "StoreOrderReactService.cs"
        );
        var facadeSource = File.ReadAllText(facadePath);
        string[] forbiddenFacadeTokens =
        [
            ".Queryable<",
            ".Ado.",
            "SqlFunc.",
            "SugarParameter",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SemaphoreSlim",
            "SqlSugarContext",
            "IConfiguration",
            "IMapper",
            "IOrderNumberGenerator",
            "IStoreOrderLocationProductLookupService",
            "IWarehouseProductChangeHistoryService",
            "IMemoryCache",
            "HqSqlSugarContext",
            "LegacyFactory",
            "BuildStoreOrderImportPriceVarianceDetailOrderBy",
        ];
        var facadeViolations = forbiddenFacadeTokens
            .Where(facadeSource.Contains)
            .ToArray();

        var violations = controllerViolations
            .Select(item => $"Controller: {item}")
            .Concat(facadeViolations.Select(item => $"Facade: {item}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Controller 只能做协议适配，兼容 façade 只能委派：\n"
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
