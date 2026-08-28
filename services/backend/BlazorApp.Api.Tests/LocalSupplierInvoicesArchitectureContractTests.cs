using System.Runtime.CompilerServices;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// LocalSupplierInvoices 的架构边界独立守护，避免兼容 façade 再次承载业务实现。
/// </summary>
public sealed class LocalSupplierInvoicesArchitectureContractTests
{
    [Fact]
    public void Feature不得反向依赖ReactFacade或其专用日志类型()
    {
        var featureRoot = Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices"
        );
        var violations = Directory
            .EnumerateFiles(featureRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("BlazorApp.Api.Services.React", StringComparison.Ordinal)
                    || source.Contains("ILogger<LocalSupplierInvoicesReactService>", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(FindRepoRoot(), path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Feature 不得反向依赖 React façade：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void CheckProducts必须分离编排查询规则组装和写入边界()
    {
        var root = FindRepoRoot();
        string[] requiredPaths =
        [
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductReview/LocalSupplierInvoicesProductReviewStore.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductReview/LocalSupplierInvoicesProductReviewEvaluator.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductReview/LocalSupplierInvoicesProductReviewAssembler.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductReview/LocalSupplierInvoicesProductReviewWriter.cs",
        ];

        var missing = requiredPaths.Where(path => !File.Exists(Path.Combine(root, path))).ToArray();
        Assert.True(
            missing.Length == 0,
            "CheckProducts 缺少独立的查询、规则、组装或持久化边界：\n"
                + string.Join("\n", missing)
        );

        var handler = ReadRepoFile(
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductReview/LocalSupplierInvoicesProductReviewHandler.cs"
        );
        string[] forbiddenHandlerTokens =
        [
            ".Queryable<",
            ".Insertable(",
            ".Updateable(",
            ".Deleteable(",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "CheckProductsLegacyAsync",
            "旧实现",
        ];
        var violations = forbiddenHandlerTokens.Where(handler.Contains).ToArray();
        Assert.True(
            violations.Length == 0,
            "ProductReview Handler 只能编排或委派：\n" + string.Join("\n", violations)
        );
        Assert.Contains("QueryInChunksParallelAsync<T, TKey>", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Facade必须保持纯委派且Feature文件低于1500行()
    {
        var root = FindRepoRoot();
        var facade = ReadRepoFile(
            "services/backend/BlazorApp.Api/Services/React/LocalSupplierInvoicesReactService.cs"
        );
        string[] forbiddenFacadeTokens =
        [
            ".Queryable<",
            ".Insertable(",
            ".Updateable(",
            ".Ado.",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
        ];
        Assert.DoesNotContain(
            forbiddenFacadeTokens,
            token => facade.Contains(token, StringComparison.Ordinal)
        );

        var oversized = Directory
            .EnumerateFiles(
                Path.Combine(root, "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices"),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(path => File.ReadLines(path).Count() >= 1500)
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();
        Assert.True(oversized.Length == 0, string.Join("\n", oversized));
    }

    [Fact]
    public void BatchExecuteActions必须分离请求校验读取计划写入和结果组装边界()
    {
        var root = FindRepoRoot();
        string[] requiredPaths =
        [
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductExecution/LocalSupplierInvoicesProductExecutionRequestValidator.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductExecution/LocalSupplierInvoicesProductExecutionSource.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductExecution/LocalSupplierInvoicesProductExecutionPlan.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductExecution/LocalSupplierInvoicesProductExecutionCommandWriter.cs",
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductExecution/LocalSupplierInvoicesProductExecutionResultAccumulator.cs",
        ];
        var missing = requiredPaths.Where(path => !File.Exists(Path.Combine(root, path))).ToArray();
        Assert.True(
            missing.Length == 0,
            "BatchExecuteActions 缺少请求校验、读取、计划、事务写入或结果组装边界：\n"
                + string.Join("\n", missing)
        );

        var handler = ReadRepoFile(
            "services/backend/BlazorApp.Api/Features/LocalSupplierInvoices/ProductExecution/LocalSupplierInvoicesProductExecutionHandler.cs"
        );
        var method = ExtractMethod(handler, "BatchExecuteActionsAsync");
        Assert.True(
            method.Split('\n').Length <= 180,
            $"BatchExecuteActionsAsync 不能超过 180 行，当前 {method.Split('\n').Length} 行"
        );
        string[] forbiddenHandlerTokens =
        [
            ".Queryable<",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SetChildPurchasePriceMutationLock",
            "detail.AdditionalBarcodesJson",
            "detail.PurchasePrice",
            "detail.RetailPrice",
        ];
        var violations = forbiddenHandlerTokens.Where(method.Contains).ToArray();
        Assert.True(
            violations.Length == 0,
            "BatchExecuteActions Handler 只能编排和异常到 ApiResponse：\n"
                + string.Join("\n", violations)
        );
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static string ExtractMethod(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"未找到方法 {methodName}");
        var openBrace = source.IndexOf('{', methodStart);
        Assert.True(openBrace >= 0, $"未找到方法 {methodName} 的起始大括号");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0) return source[methodStart..(index + 1)];
        }

        throw new InvalidOperationException($"未找到方法 {methodName} 的结束大括号");
    }

    private static string FindRepoRoot([CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory != null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }
}
