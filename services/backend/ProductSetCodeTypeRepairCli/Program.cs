using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

return await ProductSetCodeTypeRepairCliProgram.RunAsync(args);

internal static class ProductSetCodeTypeRepairCliProgram
{
    private const int Failed = 1;
    private const int InvalidArguments = 2;
    private const int IdentityMismatch = 3;
    private const int VerificationFailed = 4;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
            var options = ParseOptions(args.Skip(1));
            if (command is not ("prepare" or "apply" or "verify" or "rollback"))
            {
                return Usage();
            }
            if (!options.TryGetValue("expected-server", out var expectedServer)
                || !options.TryGetValue("expected-database", out var expectedDatabase)
                || string.IsNullOrWhiteSpace(expectedServer)
                || string.IsNullOrWhiteSpace(expectedDatabase))
            {
                return Usage();
            }

            var configuration = BuildConfiguration(options);
            var context = new SqlSugarContext(configuration, NullLogger<SqlSugarContext>.Instance, new CliCurrentUserService());
            var identity = await context.Db.Ado.SqlQuerySingleAsync<DatabaseIdentity>(
                "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)) AS ServerName, DB_NAME() AS DatabaseName"
            );
            // 唯一允许输出的数据库身份信息；永不输出连接串、商品数据或 SQL 文本。
            Console.WriteLine($"database_identity server={identity.ServerName} database={identity.DatabaseName}");
            if (!string.Equals(identity.ServerName, expectedServer, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(identity.DatabaseName, expectedDatabase, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("FAILED database identity mismatch");
                return IdentityMismatch;
            }

            var runner = new ProductSetCodeTypeRepairRunner(context.Db);
            return command switch
            {
                "prepare" => await PrepareAsync(runner, options),
                "apply" => await ApplyAsync(runner, options),
                "verify" => await VerifyAsync(runner, options),
                "rollback" => await RollbackAsync(runner, options),
                _ => Usage(),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("FAILED cancelled");
            return Failed;
        }
        catch (Exception ex)
        {
            // 数据库驱动异常可能携带连接端点或 SQL；仅保留类型，避免敏感输出。
            Console.Error.WriteLine($"FAILED {ex.GetType().Name}");
            return Failed;
        }
    }

    private static async Task<int> PrepareAsync(ProductSetCodeTypeRepairRunner runner, IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("output-dir", out var outputDirectory) || string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Usage();
        }
        var report = await runner.RunAsync(new ProductSetCodeTypeRepairOptions
        {
            OutputDirectory = outputDirectory,
            RunId = options.GetValueOrDefault("run-id"),
            ActorName = options.GetValueOrDefault("actor") ?? "ProductSetCodeTypeRepairCli",
            Apply = false,
        });
        Console.WriteLine(
            $"prepare_ok manifest={report.ManifestPath} snapshot={report.SnapshotPath} sha256={report.SnapshotSha256}"
        );
        return 0;
    }

    private static async Task<int> ApplyAsync(ProductSetCodeTypeRepairRunner runner, IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetRequired(options, "snapshot", out var snapshotPath)
            || !TryGetRequired(options, "expected-sha", out var expectedSha)
            || !TryGetRequired(options, "actor", out var actor))
        {
            return Usage();
        }
        // ApplyPreparedAsync 只接受 prepare 产生的快照与明确 SHA，不重新扫描并猜测目标范围。
        var report = await runner.ApplyPreparedAsync(snapshotPath, expectedSha, actor);
        var verificationPassed = report.Verification?.IsValid == true;
        Console.WriteLine(
            $"apply_completed journal={report.JournalPath ?? string.Empty} verification={report.VerificationPath ?? string.Empty} " +
            $"success={report.Succeeded.Count} failed={report.Failed.Count} valid={verificationPassed} " +
            $"violations={report.Verification?.Violations.Count ?? 0}"
        );
        return report.Failed.Count == 0 && verificationPassed ? 0 : VerificationFailed;
    }

    private static async Task<int> VerifyAsync(ProductSetCodeTypeRepairRunner runner, IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetRequired(options, "snapshot", out var snapshotPath)
            || !TryGetRequired(options, "journal", out var journalPath)
            || !TryGetRequired(options, "report", out var reportPath))
        {
            return Usage();
        }
        var report = await runner.VerifyAsync(snapshotPath, journalPath);
        await WriteAtomicallyAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"verify_ok report={reportPath} valid={report.IsValid} violations={report.Violations.Count}");
        return report.IsValid ? 0 : VerificationFailed;
    }

    private static async Task<int> RollbackAsync(ProductSetCodeTypeRepairRunner runner, IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetRequired(options, "snapshot", out var snapshotPath)
            || !TryGetRequired(options, "journal", out var journalPath)
            || !TryGetRequired(options, "actor", out var actor)
            || !TryGetRequired(options, "report", out var reportPath))
        {
            return Usage();
        }
        var report = await runner.RollbackAsync(snapshotPath, journalPath, actor);
        await WriteAtomicallyAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"rollback_ok report={reportPath} rolled_back={report.RolledBackProductCodes.Count} failed={report.Failures.Count}");
        return report.Failures.Count == 0 ? 0 : VerificationFailed;
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string> options)
    {
        var settingsDirectory = options.TryGetValue("api-settings-dir", out var explicitDirectory)
            ? explicitDirectory
            : FindApiSettingsDirectory();
        if (string.IsNullOrWhiteSpace(settingsDirectory) || !Directory.Exists(settingsDirectory))
        {
            throw new DirectoryNotFoundException("Api appsettings 目录不可用");
        }
        return new ConfigurationBuilder()
            .SetBasePath(settingsDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            // CLI 不启动宿主；仍强制关闭任何初始化、结构同步与 SQL 日志路径。
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:InitializeOnStartup"] = "false",
                ["Database:SyncExistingTableStructureOnStartup"] = "false",
                ["Database:EnableSqlLogging"] = "false",
                ["Database:CommandTimeoutSeconds"] = "300",
            })
            .Build();
    }

    private static string? FindApiSettingsDirectory()
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(root);
            while (current != null)
            {
                var sibling = Path.Combine(current.FullName, "BlazorApp.Api");
                if (Directory.Exists(sibling)) return sibling;
                var repositoryPath = Path.Combine(current.FullName, "services", "backend", "BlazorApp.Api");
                if (Directory.Exists(repositoryPath)) return repositoryPath;
                current = current.Parent;
            }
        }
        return null;
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = values.ToList();
        for (var index = 0; index < list.Count; index += 2)
        {
            if (!list[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= list.Count)
            {
                throw new ArgumentException("命令参数格式无效");
            }
            result[list[index][2..]] = list[index + 1];
        }
        return result;
    }

    private static bool TryGetRequired(IReadOnlyDictionary<string, string> options, string key, out string value) =>
        options.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);

    private static async Task WriteAtomicallyAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("报告路径必须包含目录", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: prepare|apply|verify|rollback --expected-server <server> --expected-database <database> [options]");
        return InvalidArguments;
    }

    private sealed class DatabaseIdentity
    {
        public string? ServerName { get; init; }
        public string? DatabaseName { get; init; }
    }

    private sealed class CliCurrentUserService : ICurrentUserService
    {
        public string GetCurrentUsername() => "ProductSetCodeTypeRepairCli";
        public string GetCurrentUserGuid() => string.Empty;
    }
}
