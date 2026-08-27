using System.Text.RegularExpressions;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class SqlPerformanceAttachmentContractTests
{
    private static readonly Regex ClientCreationPattern = new(
        @"new\s+SqlSugar(?:Client|Scope)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex OnLogExecutedAssignmentPattern = new(
        @"\b\w+(?:\.\w+)*\.OnLogExecuted\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    [Fact]
    public void 所有生产SqlSugar客户端创建路径均就近挂载性能采集()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiRoot = Path.Combine(repositoryRoot, "services", "backend", "BlazorApp.Api");
        var sourceFiles = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.EndsWith(
                    $"{Path.DirectorySeparatorChar}SqlPerformanceAttachmentService.cs",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        var uncovered = new List<string>();
        var creationCount = 0;

        foreach (var path in sourceFiles)
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!ClientCreationPattern.IsMatch(lines[index]))
                {
                    continue;
                }

                creationCount++;
                // 主库构造器包含完整的 SqlSugar 行为配置，创建到挂载之间约 130 行。
                var end = Math.Min(lines.Length, index + 161);
                var attached = lines[index..end].Any(line =>
                    line.Contains(
                        "SqlPerformanceAttachmentService.Attach(",
                        StringComparison.Ordinal
                    )
                );
                if (!attached)
                {
                    uncovered.Add(
                        $"{Path.GetRelativePath(repositoryRoot, path)}:{index + 1}"
                    );
                }
            }
        }

        Assert.Equal(16, creationCount);
        Assert.True(
            uncovered.Count == 0,
            $"以下 SqlSugar client 创建路径未在后续 160 行内挂载 SQL 性能采集：{string.Join(", ", uncovered)}"
        );
    }

    [Fact]
    public void 所有生产OnLogExecuted赋值均经统一组合器传播性能采集()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiRoot = Path.Combine(repositoryRoot, "services", "backend", "BlazorApp.Api");
        var attachmentPath = Path.Combine(
            apiRoot,
            "Services",
            "Performance",
            "SqlPerformanceAttachmentService.cs"
        );
        var directAssignments = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .SelectMany(path =>
                File.ReadLines(path)
                    .Select((line, index) => new { path, line, index })
                    .Where(match => OnLogExecutedAssignmentPattern.IsMatch(match.line))
            )
            .ToArray();

        // 请求级日志只能经统一入口登记，避免把性能采集组合器直接替换或清空。
        Assert.Single(directAssignments);
        Assert.Equal(attachmentPath, directAssignments[0].path);

        var attachmentSource = File.ReadAllText(attachmentPath);
        Assert.Contains("SetOnLogExecuted", attachmentSource, StringComparison.Ordinal);
        Assert.Contains(
            "client.Aop.OnLogExecuted = attachment.OnLogExecuted;",
            attachmentSource,
            StringComparison.Ordinal
        );
        Assert.Contains("TryRecord", attachmentSource, StringComparison.Ordinal);

        var requestLevelRegistrations = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, attachmentPath, StringComparison.Ordinal))
            .Sum(path =>
                Regex.Matches(
                    File.ReadAllText(path),
                    @"SqlPerformanceAttachmentService\.SetOnLogExecuted\s*\(",
                    RegexOptions.CultureInvariant
                ).Count
            );
        Assert.True(
            requestLevelRegistrations > 0,
            "生产请求级 SQL 回调必须通过 SqlPerformanceAttachmentService.SetOnLogExecuted 登记。"
        );
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (
                Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git"))
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法从测试输出目录定位仓库根目录。");
    }
}
