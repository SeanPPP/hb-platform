using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using BlazorApp.Api.Services.React;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// ProductWarehouse 的架构边界由本模块单独守护，避免与其他巨型服务重构测试产生写入冲突。
/// </summary>
public sealed class ProductWarehouseArchitectureContractTests
{
    [Fact]
    public void 兼容门面必须保持可继承且只负责委派()
    {
        var source = ReadRepoFile(
            "services/backend/BlazorApp.Api/Services/React/ProductWarehouseReactService.cs"
        );

        Assert.DoesNotContain("public sealed class ProductWarehouseReactService", source);
        Assert.DoesNotContain(".Queryable<", source);
        Assert.DoesNotContain(".Insertable(", source);
        Assert.DoesNotContain(".Updateable(", source);
        Assert.DoesNotContain(".Ado.", source);
        Assert.True(
            source.Split('\n').Length < 500,
            "ProductWarehouse 兼容门面必须低于 500 行。"
        );
    }

    [Fact]
    public void Feature不得反向依赖React门面或其专用日志类型()
    {
        var featureRoot = Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Api/Features/ProductWarehouse"
        );
        var violations = Directory
            .EnumerateFiles(featureRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("using BlazorApp.Api.Services.React;", StringComparison.Ordinal)
                    || source.Contains("ILogger<ProductWarehouseReactService>", StringComparison.Ordinal)
                    || Regex.IsMatch(source, @"\bProductWarehouseReactService\b")
                        && !source.Contains(
                            "[ProductWarehouseReactService.",
                            StringComparison.Ordinal
                        );
            })
            .Select(path => Path.GetRelativePath(FindRepoRoot(), path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Feature 不得反向依赖 React façade：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void Feature文件必须受行数边界约束()
    {
        var featureRoot = Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Api/Features/ProductWarehouse"
        );
        var violations = Directory
            .EnumerateFiles(featureRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Lines = File.ReadLines(path).Count() })
            .Where(item => item.Lines >= 1500)
            .Select(item => $"{Path.GetRelativePath(FindRepoRoot(), item.Path)}: {item.Lines}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "ProductWarehouse 切片文件必须低于 1500 行：\n" + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 表格诊断与候选类型必须保持旧命名空间和公开成员快照()
    {
        var assembly = typeof(ProductWarehouseReactService).Assembly;
        const string legacyNamespace = "BlazorApp.Api.Services.React.";
        var timingSnapshot = RequireInternalSealedType(
            assembly,
            legacyNamespace + "WarehouseProductTableTimingSnapshot"
        );
        var requestSnapshot = RequireInternalSealedType(
            assembly,
            legacyNamespace + "WarehouseProductTableRequestSnapshot"
        );
        var queryException = RequireInternalSealedType(
            assembly,
            legacyNamespace + "WarehouseProductTableQueryException"
        );
        var timings = RequireInternalSealedType(
            assembly,
            legacyNamespace + "WarehouseProductTableTimings"
        );
        var candidate = RequireInternalSealedType(
            assembly,
            legacyNamespace + "WarehouseProductCodeSearchCandidate"
        );

        AssertPublicProperties(
            timingSnapshot,
            true,
            ("CandidateMs", typeof(long)),
            ("CountMs", typeof(long)),
            ("PageMs", typeof(long)),
            ("LocationMs", typeof(long)),
            ("RowsMs", typeof(long)),
            ("MapMs", typeof(long)),
            ("TotalMs", typeof(long))
        );
        AssertPublicProperties(
            requestSnapshot,
            true,
            ("PageNumber", typeof(int)),
            ("PageSize", typeof(int)),
            ("CategoryCount", typeof(int)),
            ("FilterCount", typeof(int)),
            ("KeywordType", typeof(string)),
            ("KeywordLength", typeof(int)),
            ("SortBy", typeof(string)),
            ("SortOrder", typeof(string))
        );
        AssertPublicProperties(
            queryException,
            false,
            ("FailedStage", typeof(string)),
            ("Timings", timingSnapshot),
            ("Request", requestSnapshot)
        );
        AssertPublicProperties(
            timings,
            true,
            ("CandidateMs", typeof(long)),
            ("CountMs", typeof(long)),
            ("PageMs", typeof(long)),
            ("LocationMs", typeof(long)),
            ("RowsMs", typeof(long)),
            ("MapMs", typeof(long))
        );
        AssertPublicProperties(candidate, true, ("ProductCode", typeof(string)));

        AssertPublicConstructor(
            timingSnapshot,
            typeof(long),
            typeof(long),
            typeof(long),
            typeof(long),
            typeof(long),
            typeof(long),
            typeof(long)
        );
        AssertPublicConstructor(
            requestSnapshot,
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(string),
            typeof(int),
            typeof(string),
            typeof(string)
        );
        AssertPublicConstructor(timings);
        AssertPublicConstructor(candidate);

        var exceptionConstructor = queryException.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(string), timingSnapshot, typeof(Exception), requestSnapshot],
            modifiers: null
        );
        Assert.NotNull(exceptionConstructor);
        Assert.True(exceptionConstructor!.GetParameters()[3].IsOptional);
        var snapshotMethod = timings.GetMethod(
            "Snapshot",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(long)],
            modifiers: null
        );
        Assert.NotNull(snapshotMethod);
        Assert.Equal(timingSnapshot, snapshotMethod!.ReturnType);

        string[] legacyTypeNames =
        [
            "WarehouseProductTableTimingSnapshot",
            "WarehouseProductTableRequestSnapshot",
            "WarehouseProductTableQueryException",
            "WarehouseProductTableTimings",
            "WarehouseProductCodeSearchCandidate",
        ];
        Assert.All(
            legacyTypeNames,
            name =>
                Assert.Null(
                    assembly.GetType(
                        "BlazorApp.Api.Features.ProductWarehouse." + name,
                        throwOnError: false
                    )
                )
        );
    }

    [Fact]
    public void 四个Handler编排方法必须短小且不得混合ORM事务或DTO构造()
    {
        var methods = new[]
        {
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Update/ProductWarehouseBatchUpdateSlice.cs",
                Method: "ExecuteBatchUpdateAsync"
            ),
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Table/ProductWarehouseTableSlice.cs",
                Method: "ExecuteTableQueryAsync"
            ),
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Creation/ProductWarehouseSingleCreationSlice.cs",
                Method: "ExecuteSingleCreationAsync"
            ),
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/ProductWarehouseImportSlice.cs",
                Method: "ExecuteDomesticImportAsync"
            ),
        };
        string[] forbiddenTokens =
        [
            ".Queryable<",
            ".Insertable(",
            ".Updateable(",
            ".Deleteable(",
            "BeginTran",
            "CommitTran",
            "RollbackTran",
            "SugarParameter",
            "SqlFunc.",
        ];
        var violations = new List<string>();

        foreach (var method in methods)
        {
            var body = ExtractMethodBody(ReadRepoFile(method.Path), method.Method);
            var lines = body.Count(ch => ch == '\n') + 1;
            if (lines > 180)
                violations.Add($"{method.Method}: {lines} 行，超过 180 行");

            violations.AddRange(
                forbiddenTokens
                    .Where(body.Contains)
                    .Select(token => $"{method.Method}: {token}")
            );
            if (Regex.IsMatch(body, @"\bnew\s+[A-Za-z0-9_<>,?]+Dto\s*[{(]"))
                violations.Add($"{method.Method}: 直接构造 DTO");
        }

        Assert.True(
            violations.Count == 0,
            "Handler 编排方法仍混合基础设施、事务或映射职责：\n"
                + string.Join("\n", violations)
        );
    }

    [Fact]
    public void 高风险写入和查询必须具备明确的领域与基础设施边界()
    {
        var repoRoot = FindRepoRoot();
        string[] requiredPaths =
        [
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Update/Domain/WarehouseProductBatchUpdatePlan.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Update/Infrastructure/WarehouseProductBatchUpdateCommandWriter.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Update/Mapping/WarehouseProductBatchUpdateEntityMapper.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Domain/WarehouseProductDomesticImportPlan.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductDomesticImportSourceQueryStore.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductImportQueryStore.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductDomesticImportCommandWriter.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Mapping/WarehouseProductDomesticImportEntityMapper.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Mapping/WarehouseProductDomesticImportResultAssembler.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Mapping/WarehouseProductImportQueryResultAssembler.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Table/Infrastructure/WarehouseProductTableQueryBuilder.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Table/Infrastructure/WarehouseProductTableQueryStore.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Table/Mapping/WarehouseProductTableResultAssembler.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Creation/Domain/WarehouseProductSingleCreationPlan.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Creation/Infrastructure/WarehouseProductSingleCreationCommandWriter.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Creation/Mapping/WarehouseProductSingleCreationEntityMapper.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Creation/Mapping/WarehouseProductSingleCreationResultAssembler.cs",
        ];

        var missing = requiredPaths
            .Where(path => !File.Exists(Path.Combine(repoRoot, path)))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "ProductWarehouse 垂直切片缺少领域、查询或 DTO 映射边界：\n"
                + string.Join("\n", missing)
        );

        var transactionMethods = new[]
        {
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Update/Infrastructure/WarehouseProductBatchUpdateCommandWriter.cs",
                Method: "ExecuteAsync"
            ),
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Creation/Infrastructure/WarehouseProductSingleCreationCommandWriter.cs",
                Method: "ExecuteAsync"
            ),
            (
                Path: "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductDomesticImportCommandWriter.cs",
                Method: "ExecuteDomesticImportAsync"
            ),
        };
        foreach (var transactionMethod in transactionMethods)
        {
            var body = ExtractMethodBody(
                ReadRepoFile(transactionMethod.Path),
                transactionMethod.Method
            );
            Assert.Equal(1, CountOccurrences(body, "BeginTran()"));
            Assert.Equal(1, CountOccurrences(body, "CommitTran()"));
            Assert.Equal(1, CountOccurrences(body, "RollbackTran()"));
            Assert.False(
                Regex.IsMatch(
                    body,
                    @"\bnew\s+(?:WarehouseProduct|DomesticProduct|Product|ProductSetCode|StoreMultiCodeProduct|StoreRetailPrice|[A-Za-z0-9_]+Dto)\b"
                ),
                $"{transactionMethod.Method} 必须把 DTO/实体映射交给 Mapping/Assembler。"
            );
        }

        string[] queryStorePaths =
        [
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Table/Infrastructure/WarehouseProductTableQueryStore.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductDomesticImportSourceQueryStore.cs",
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductImportQueryStore.cs",
        ];
        foreach (var queryStorePath in queryStorePaths)
        {
            var queryStore = ReadRepoFile(queryStorePath);
            Assert.DoesNotContain("BeginTran", queryStore);
            Assert.DoesNotContain("CommitTran", queryStore);
            Assert.DoesNotContain("RollbackTran", queryStore);
        }

        var importCommandWriter = ReadRepoFile(
            "services/backend/BlazorApp.Api/Features/ProductWarehouse/Import/Infrastructure/WarehouseProductDomesticImportCommandWriter.cs"
        );
        Assert.DoesNotContain("ReactTableResponseDto<", importCommandWriter);
        Assert.DoesNotContain("GetDomesticProductsNotInWarehouseAsync", importCommandWriter);
        Assert.DoesNotContain("GetNonHotbargainProductsNotInWarehouseAsync", importCommandWriter);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, $"未找到方法 {methodName}");
        var openBrace = source.IndexOf('{', methodIndex);
        Assert.True(openBrace >= 0, $"方法 {methodName} 没有方法体");

        var depth = 0;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        var verbatimString = false;
        var escaped = false;

        for (var index = openBrace; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                    inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }
                continue;
            }
            if (inString)
            {
                if (verbatimString && current == '"' && next == '"')
                {
                    index++;
                    continue;
                }
                if ((!verbatimString && escaped) || current == '\\')
                {
                    escaped = !escaped;
                    continue;
                }
                if (current == '"' && !escaped)
                    inString = false;
                escaped = false;
                continue;
            }
            if (inChar)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == '\'')
                    inChar = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                verbatimString = index > 0 && source[index - 1] == '@';
                escaped = false;
                continue;
            }
            if (current == '\'')
            {
                inChar = true;
                escaped = false;
                continue;
            }
            if (current == '{')
                depth++;
            else if (current == '}' && --depth == 0)
                return source.Substring(openBrace, index - openBrace + 1);
        }

        throw new InvalidOperationException($"方法 {methodName} 的方法体未闭合。");
    }

    private static Type RequireInternalSealedType(Assembly assembly, string fullName)
    {
        var type = assembly.GetType(fullName, throwOnError: false);
        Assert.NotNull(type);
        Assert.True(type!.IsNotPublic, $"{fullName} 必须保持 internal");
        Assert.True(type.IsSealed, $"{fullName} 必须保持 sealed");
        return type;
    }

    private static void AssertPublicProperties(
        Type type,
        bool hasPublicSetter,
        params (string Name, Type PropertyType)[] properties
    )
    {
        foreach (var expected in properties)
        {
            var property = type.GetProperty(
                expected.Name,
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.NotNull(property);
            Assert.Equal(expected.PropertyType, property!.PropertyType);
            Assert.True(
                property.GetMethod?.IsPublic == true,
                $"{type.FullName}.{expected.Name} getter"
            );
            Assert.Equal(hasPublicSetter, property.SetMethod?.IsPublic == true);
        }
    }

    private static void AssertPublicConstructor(Type type, params Type[] parameterTypes)
    {
        Assert.NotNull(
            type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                parameterTypes,
                modifiers: null
            )
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

        throw new DirectoryNotFoundException("无法定位仓库根目录。");
    }
}
