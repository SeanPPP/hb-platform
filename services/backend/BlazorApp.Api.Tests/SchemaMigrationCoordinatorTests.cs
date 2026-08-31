using System.Reflection;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data.SchemaMigrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>
/// 启动 schema 协调器的无数据库契约测试。
///
/// 这些测试刻意不构造 SqlSugarContext，也不连接数据库；它们锁定命令入口、
/// 迁移账本、锁语义和正常启动的只读边界。真正的 SQL Server 幂等性由集成测试覆盖。
/// </summary>
public sealed class SchemaMigrationCoordinatorTests
{
    [Theory]
    [MemberData(nameof(ValidCommands))]
    public void SchemaCommand_只接受无参数和两个精确模式(string[] args, string expectedMode)
    {
        var command = SchemaCommand.Parse(args);

        Assert.Equal(expectedMode, command.Mode.ToString());
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void SchemaCommand_未知或重复参数必须拒绝(string[] args)
    {
        var command = SchemaCommand.Parse(args);

        Assert.Equal(SchemaCommandMode.Invalid, command.Mode);
        Assert.False(string.IsNullOrWhiteSpace(command.Error));
    }

    public static IEnumerable<object[]> ValidCommands =>
    [
        [Array.Empty<string>(), "Server"],
        [new[] { "--schema=check" }, "Check"],
        [new[] { "--schema=migrate" }, "Migrate"],
    ];

    public static IEnumerable<object[]> InvalidCommands =>
    [
        [new[] { "--schema=CHECK" }],
        [new[] { "--Schema=migrate" }],
        [new[] { "--SCHEMA=check" }],
        [new[] { "--schema=unknown" }],
        [new[] { "--schema=check", "--schema=migrate" }],
        [new[] { "--schema=check", "--schema=check" }],
    ];

    [Fact]
    public void SchemaExitCodes_固定且可由部署脚本依赖()
    {
        var values = typeof(SchemaExitCodes)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(int) && field.IsLiteral)
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToHashSet();

        Assert.Equal(6, values.Count);
        Assert.Contains(0, values);
        Assert.Contains(2, values);
        Assert.Contains(20, values);
        Assert.Contains(22, values);
        Assert.Contains(23, values);
        Assert.Contains(130, values);
    }

    [Fact]
    public void SchemaMigrationStep_必须绑定明确执行器而不是由运行时按ID猜测()
    {
        var stepExecutor = typeof(SchemaMigrationStep).GetProperty("ApplyAsync");
        var runtimeMethods = typeof(ISchemaMigrationRuntime)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToArray();

        Assert.NotNull(stepExecutor);
        Assert.Contains("ApplyMainBaselineAsync", runtimeMethods);
        Assert.Contains("ApplyPosmBaselineAsync", runtimeMethods);
        Assert.Contains("ApplyMobileDeviceActivationAsync", runtimeMethods);
        Assert.Contains("VerifyMobileDeviceActivationSchemaAsync", runtimeMethods);
        Assert.Contains("ValidatePrerequisitesAsync", runtimeMethods);
        Assert.DoesNotContain("ApplyMigrationAsync", runtimeMethods);
    }

    [Fact]
    public void DeviceActivation主键签名_必须固定为Clustered主键()
    {
        var sourcePath = Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Shared/Models/POSM/DeviceActivationCodeGrant.cs"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("indexInfo.[type] = 1", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_迁移账本和SQL_Server会话锁保持固定契约()
    {
        var coordinatorSource = await ReadCoordinatorSourceAsync();
        var runtimeSource = await ReadRuntimeSourceAsync();
        var storeSource = await ReadStoreSourceAsync();

        Assert.Contains("HBWebSchemaMigrationHistory", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("HBWebPosmSchemaMigrationHistory", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("MigrationId", storeSource, StringComparison.Ordinal);
        Assert.Contains("AppliedAtUtc", storeSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationVersion", storeSource, StringComparison.Ordinal);

        // lock 必须独立连接、session-owned 且不等待，避免常规启动占用 schema 锁。
        Assert.Contains("sp_getapplock", storeSource, StringComparison.Ordinal);
        Assert.Contains("@LockOwner = N'Session'", storeSource, StringComparison.Ordinal);
        Assert.Contains("@LockTimeout = 0", storeSource, StringComparison.Ordinal);
        Assert.Contains("SqlConnection", storeSource, StringComparison.Ordinal);
        Assert.Contains("SchemaExitCodes.MigrationLockUnavailable", coordinatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_两个baseline维持既定顺序且成功后才登记()
    {
        var source = await ReadCoordinatorSourceAsync();
        var runtimeSource = await ReadRuntimeSourceAsync();
        var baseline = ExtractMethod(runtimeSource, "ApplyBaselineAsync");

        var mainLoginSession = IndexOfRequired(baseline, "EnsureLoginSessionSchema()");
        var mainCreateTable = IndexOfRequired(baseline, "CreateTable()");
        var mainStartup = IndexOfRequired(baseline, "StartupSchemaMigrator.EnsureAsync");
        var mainApplicationLog = IndexOfRequired(baseline, "ApplicationLogSchemaMigrator.EnsureAsync");
        var mainPerformance = IndexOfRequired(baseline, "PerformanceBaselineSchemaMigrator.EnsureAsync");
        Assert.True(
            mainLoginSession < mainCreateTable
                && mainCreateTable < mainStartup
                && mainStartup < mainApplicationLog
                && mainApplicationLog < mainPerformance,
            "主库 baseline 必须依次执行 LoginSession、CreateTable、Startup、ApplicationLog 和 Performance 迁移。"
        );
        Assert.Contains("database.Aop.OnError", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("SchemaBaselineSqlFailureException", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("Console.SetOut(TextWriter.Null)", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("Console.SetError(TextWriter.Null)", runtimeSource, StringComparison.Ordinal);

        var installment = IndexOfRequired(baseline, "EnsurePosmAsync");
        var paymentTerminal = IndexOfRequired(baseline, "PaymentTerminalSettingsSchemaMigrator.EnsureAsync");
        var deviceRuntime = IndexOfRequired(baseline, "DeviceRuntimeStatusSchemaMigrator.EnsureAsync");
        var emergencyGrant = IndexOfRequired(baseline, "EmergencyLoginGrantSchemaMigrator.EnsureAsync");
        var emergencyKey = IndexOfRequired(baseline, "EmergencyLoginKeySchemaMigrator.EnsureAsync");
        Assert.True(
            installment < paymentTerminal
                && paymentTerminal < deviceRuntime
                && deviceRuntime < emergencyGrant
                && emergencyGrant < emergencyKey,
            "POSM baseline 必须沿用分期订单、终端设置、设备状态、紧急 Grant、紧急 Key 的顺序。"
        );

        Assert.True(
            IndexOfRequired(source, "RecordAppliedAsync")
                > IndexOfRequired(source, "await migration.ApplyAsync(_runtime, cancellationToken)"),
            "任一 baseline 失败不得提前写迁移账本；登记必须在对应 baseline 成功之后。"
        );

        var migrateMethod = ExtractMethod(source, "MigrateAsync");
        Assert.True(
            IndexOfRequired(migrateMethod, "MigrateMainAsync")
                < IndexOfRequired(migrateMethod, "MigratePosmAsync"),
            "主库成功登记后才开始 POSM；POSM 失败重跑时必须保留并跳过主库记录。"
        );
    }

    [Fact]
    public async Task Coordinator_已应用跳过并支持主库成功后仅重跑POSM()
    {
        var source = await ReadCoordinatorSourceAsync();
        var runMigration = ExtractMethod(source, "RunMigrationAsync");

        Assert.Contains("migrationSession.IsAppliedAsync", runMigration, StringComparison.Ordinal);
        Assert.Contains("SCHEMA_MIGRATION_ALREADY_APPLIED", runMigration, StringComparison.Ordinal);
        Assert.Contains("migration.ApplyAsync", runMigration, StringComparison.Ordinal);
        Assert.Contains("migrationSession.RecordAppliedAsync", runMigration, StringComparison.Ordinal);

        Assert.True(
            IndexOfRequired(runMigration, "SCHEMA_MIGRATION_ALREADY_APPLIED")
                < IndexOfRequired(runMigration, "migration.ApplyAsync"),
            "已有 migration ID 时必须先返回，不能再次执行 baseline。"
        );
        Assert.True(
            IndexOfRequired(source, "MigrateMainAsync")
                < IndexOfRequired(source, "MigratePosmAsync"),
            "主库完成并登记后，POSM 失败重跑必须能跳过主库并继续 POSM。"
        );
    }

    [Fact]
    public async Task MigrateAsync_可追加迁移步骤必须按声明顺序独立记账()
    {
        const string appendedMigrationId = "20260828.002-main-appendable-contract";
        var runtime = new FakeSchemaMigrationRuntime();
        var coordinator = new SchemaMigrationCoordinator(
            runtime,
            NullLogger<SchemaMigrationCoordinator>.Instance,
            [
                new SchemaMigrationStep(
                    SchemaMigrationCoordinator.MainMigrationId,
                    static (migrationRuntime, cancellationToken) =>
                        migrationRuntime.ApplyMainBaselineAsync(cancellationToken)
                ),
                new SchemaMigrationStep(
                    appendedMigrationId,
                    (migrationRuntime, cancellationToken) =>
                        ((FakeSchemaMigrationRuntime)migrationRuntime).ApplyMainAppendAsync(
                            appendedMigrationId,
                            cancellationToken
                        )
                ),
            ],
            [
                new SchemaMigrationStep(
                    SchemaMigrationCoordinator.PosmMigrationId,
                    static (migrationRuntime, cancellationToken) =>
                        migrationRuntime.ApplyPosmBaselineAsync(cancellationToken)
                ),
            ]
        );

        var result = await coordinator.MigrateAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(
            runtime.Events.IndexOf($"Apply:Main:{SchemaMigrationCoordinator.MainMigrationId}")
                < runtime.Events.IndexOf($"Apply:Main:{appendedMigrationId}")
        );
        Assert.True(
            runtime.Events.IndexOf($"Record:Main:{SchemaMigrationCoordinator.MainMigrationId}")
                < runtime.Events.IndexOf($"Record:Main:{appendedMigrationId}")
        );
        Assert.Equal(1, runtime.Events.Count(entry => entry == "Acquire:Main"));
    }

    [Fact]
    public void Coordinator_迁移ID长度上限160字符可用()
    {
        var migrationId = new string('m', 160);

        var exception = Record.Exception(() =>
            new SchemaMigrationCoordinator(
                new FakeSchemaMigrationRuntime(),
                NullLogger<SchemaMigrationCoordinator>.Instance,
                [new SchemaMigrationStep(migrationId, static (_, _) => Task.CompletedTask)],
                SchemaMigrationCoordinator.PosmMigrationSteps
            )
        );

        Assert.Null(exception);
    }

    [Fact]
    public void Coordinator_迁移ID超过160字符且仅尾部不同时均在数据库IO前拒绝()
    {
        var sharedPrefix = new string('m', 160);
        var runtime = new FakeSchemaMigrationRuntime();

        var exception = Assert.Throws<ArgumentException>(() =>
            new SchemaMigrationCoordinator(
                runtime,
                NullLogger<SchemaMigrationCoordinator>.Instance,
                [
                    new SchemaMigrationStep(
                        $"{sharedPrefix}a",
                        static (_, _) => Task.CompletedTask
                    ),
                    new SchemaMigrationStep(
                        $"{sharedPrefix}b",
                        static (_, _) => Task.CompletedTask
                    ),
                ],
                SchemaMigrationCoordinator.PosmMigrationSteps
            )
        );

        Assert.Contains("160", exception.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.Events);
    }

    [Fact]
    public async Task Coordinator_Check仅做四批只读门禁且不执行迁移器()
    {
        var source = await ReadCoordinatorSourceAsync();
        var checkMethod = ExtractMethod(source, "CheckCoreAsync");
        var runtimeSource = await ReadRuntimeSourceAsync();

        Assert.Contains("CheckLedgerAsync", checkMethod, StringComparison.Ordinal);
        Assert.Contains("VerifyDeviceActivationSchemaAsync", checkMethod, StringComparison.Ordinal);
        Assert.Contains("VerifyMobileDeviceActivationSchemaAsync", checkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTable", checkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupSchemaMigrator", checkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTerminalSettingsSchemaMigrator", checkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DeviceRuntimeStatusSchemaMigrator", checkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("EmergencyLogin", checkMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("sp_getapplock", checkMethod, StringComparison.Ordinal);

        // 两个账本查询 + 两条设备激活 VerifySql，启动门禁不能重新扫描/写入 schema。
        Assert.Equal(2, CountOccurrences(checkMethod, "CheckLedgerAsync"));
        Assert.Equal(1, CountOccurrences(checkMethod, "VerifyDeviceActivationSchemaAsync"));
        Assert.Equal(1, CountOccurrences(checkMethod, "VerifyMobileDeviceActivationSchemaAsync"));
        Assert.Contains("DeviceActivationCodeSchema.VerifySql", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("MobileDeviceActivationSchema.VerifySql", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrateAsync_两个账本已应用时跳过baseline和登记()
    {
        var runtime = new FakeSchemaMigrationRuntime();
        runtime.MarkApplied(SchemaDatabase.Main, SchemaMigrationCoordinator.MainMigrationId);
        runtime.MarkApplied(SchemaDatabase.Posm, SchemaMigrationCoordinator.PosmMigrationId);
        runtime.MarkApplied(
            SchemaDatabase.Posm,
            SchemaMigrationCoordinator.MobileDeviceActivationMigrationId
        );
        var coordinator = CreateCoordinator(runtime);

        var result = await coordinator.MigrateAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(SchemaExitCodes.Success, result.ExitCode);
        Assert.DoesNotContain(runtime.Events, entry => entry.StartsWith("Apply:", StringComparison.Ordinal));
        Assert.DoesNotContain(runtime.Events, entry => entry.StartsWith("Record:", StringComparison.Ordinal));
        Assert.Equal(2, runtime.Events.Count(entry => entry.StartsWith("Acquire:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MigrateAsync_旧PosmBaseline已应用_仍执行并记账Mobile设备绑定迁移()
    {
        var runtime = new FakeSchemaMigrationRuntime();
        runtime.MarkApplied(SchemaDatabase.Main, SchemaMigrationCoordinator.MainMigrationId);
        runtime.MarkApplied(SchemaDatabase.Posm, SchemaMigrationCoordinator.PosmMigrationId);

        var result = await CreateCoordinator(runtime).MigrateAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain(
            $"Apply:Posm:{SchemaMigrationCoordinator.PosmMigrationId}",
            runtime.Events
        );
        Assert.Contains(
            $"Apply:Posm:{SchemaMigrationCoordinator.MobileDeviceActivationMigrationId}",
            runtime.Events
        );
        Assert.Contains(
            $"Record:Posm:{SchemaMigrationCoordinator.MobileDeviceActivationMigrationId}",
            runtime.Events
        );
    }

    [Fact]
    public async Task MigrateAsync_baseline失败不得写入该数据库账本()
    {
        var runtime = new FakeSchemaMigrationRuntime();
        runtime.ApplyFailures[SchemaDatabase.Main] = new InvalidOperationException("main failed");

        var result = await CreateCoordinator(runtime).MigrateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SchemaExitCodes.DatabaseFailure, result.ExitCode);
        Assert.Contains(
            $"Apply:Main:{SchemaMigrationCoordinator.MainMigrationId}",
            runtime.Events
        );
        Assert.DoesNotContain("Record:Main:20260827.001-hbweb-baseline", runtime.Events);
        Assert.DoesNotContain(runtime.Events, entry => entry.StartsWith("Acquire:Posm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MigrateAsync_POSM中途失败后重跑只继续POSM()
    {
        var runtime = new FakeSchemaMigrationRuntime();
        runtime.ApplyFailures[SchemaDatabase.Posm] = new InvalidOperationException("posm failed");
        var coordinator = CreateCoordinator(runtime);

        var first = await coordinator.MigrateAsync(CancellationToken.None);

        Assert.Equal(SchemaExitCodes.DatabaseFailure, first.ExitCode);
        Assert.Contains("Record:Main:20260827.001-hbweb-baseline", runtime.Events);
        Assert.DoesNotContain("Record:Posm:20260827.001-hbweb-posm-baseline", runtime.Events);

        runtime.ApplyFailures.Remove(SchemaDatabase.Posm);
        runtime.Events.Clear();
        var second = await coordinator.MigrateAsync(CancellationToken.None);

        Assert.True(second.Success);
        Assert.DoesNotContain(
            $"Apply:Main:{SchemaMigrationCoordinator.MainMigrationId}",
            runtime.Events
        );
        Assert.DoesNotContain("Record:Main:20260827.001-hbweb-baseline", runtime.Events);
        Assert.Contains(
            $"Apply:Posm:{SchemaMigrationCoordinator.PosmMigrationId}",
            runtime.Events
        );
        Assert.Contains("Record:Posm:20260827.001-hbweb-posm-baseline", runtime.Events);
    }

    [Fact]
    public async Task MigrateAsync_迁移锁不可用返回23()
    {
        var runtime = new FakeSchemaMigrationRuntime
        {
            AcquireException = new SchemaMigrationLockUnavailableException(-1),
        };

        var result = await CreateCoordinator(runtime).MigrateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SchemaExitCodes.MigrationLockUnavailable, result.ExitCode);
        Assert.Equal(SchemaDiagnosticCodes.MigrationLockUnavailable, result.DiagnosticCode);
    }

    [Fact]
    public async Task MigrateAsync_数据库异常返回22()
    {
        var runtime = new FakeSchemaMigrationRuntime
        {
            EnsureProvidersException = new InvalidOperationException("database unavailable"),
        };

        var result = await CreateCoordinator(runtime).MigrateAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SchemaExitCodes.DatabaseFailure, result.ExitCode);
        Assert.Equal(SchemaDiagnosticCodes.MigrationFailure, result.DiagnosticCode);
    }

    [Fact]
    public async Task MigrateAsync_取消返回130()
    {
        var runtime = new FakeSchemaMigrationRuntime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await CreateCoordinator(runtime).MigrateAsync(cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(SchemaExitCodes.Cancelled, result.ExitCode);
        Assert.Equal(SchemaDiagnosticCodes.Cancelled, result.DiagnosticCode);
    }

    [Fact]
    public async Task CheckAsync_缺少账本返回20且固定执行四批只读门禁()
    {
        var runtime = new FakeSchemaMigrationRuntime();
        runtime.MarkApplied(SchemaDatabase.Posm, SchemaMigrationCoordinator.PosmMigrationId);

        var result = await CreateCoordinator(runtime).CheckAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SchemaExitCodes.SchemaNotReady, result.ExitCode);
        Assert.Equal(SchemaDiagnosticCodes.MainMigrationMissing, result.DiagnosticCode);
        Assert.Equal(
            [
                "EnsureProviders",
                "Preflight",
                "Check:Main:20260827.001-hbweb-baseline",
                "Check:Posm:20260827.001-hbweb-posm-baseline",
                "Check:Posm:20260831.001-mobile-device-activation",
                "Verify",
                "VerifyMobile",
            ],
            runtime.Events
        );
        Assert.DoesNotContain(runtime.Events, entry => entry.StartsWith("Acquire:", StringComparison.Ordinal));
        Assert.DoesNotContain(runtime.Events, entry => entry.StartsWith("Apply:", StringComparison.Ordinal));
        Assert.DoesNotContain(runtime.Events, entry => entry.StartsWith("Record:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckAsync_Mobile设备绑定签名漂移返回20()
    {
        var runtime = new FakeSchemaMigrationRuntime
        {
            MobileVerifyException = new DeviceActivationSchemaMismatchException(),
        };
        runtime.MarkApplied(SchemaDatabase.Main, SchemaMigrationCoordinator.MainMigrationId);
        runtime.MarkApplied(SchemaDatabase.Posm, SchemaMigrationCoordinator.PosmMigrationId);
        runtime.MarkApplied(
            SchemaDatabase.Posm,
            SchemaMigrationCoordinator.MobileDeviceActivationMigrationId
        );

        var result = await CreateCoordinator(runtime).CheckAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(SchemaExitCodes.SchemaNotReady, result.ExitCode);
        Assert.Equal(SchemaDiagnosticCodes.DeviceActivationIncompatible, result.DiagnosticCode);
        Assert.Contains("VerifyMobile", runtime.Events);
    }

    private static SchemaMigrationCoordinator CreateCoordinator(FakeSchemaMigrationRuntime runtime) =>
        new(runtime, NullLogger<SchemaMigrationCoordinator>.Instance);

    private static async Task<string> ReadCoordinatorSourceAsync()
    {
        var sourcePath = SchemaMigrationSourcePath("SchemaMigrationCoordinator.cs");
        Assert.True(File.Exists(sourcePath), $"缺少 schema 协调器源码: {sourcePath}");
        return await File.ReadAllTextAsync(sourcePath);
    }

    private static async Task<string> ReadStoreSourceAsync()
    {
        var sourcePath = SchemaMigrationSourcePath("SqlServerSchemaMigrationStore.cs");
        Assert.True(File.Exists(sourcePath), $"缺少 schema 存储源码: {sourcePath}");
        return await File.ReadAllTextAsync(sourcePath);
    }

    private static async Task<string> ReadRuntimeSourceAsync()
    {
        var sourcePath = SchemaMigrationSourcePath("SchemaMigrationRuntime.cs");
        Assert.True(File.Exists(sourcePath), $"缺少 schema runtime 源码: {sourcePath}");
        return await File.ReadAllTextAsync(sourcePath);
    }

    private static string SchemaMigrationSourcePath(string fileName) =>
        Path.Combine(
            FindRepoRoot(),
            "services/backend/BlazorApp.Api/Data/SchemaMigrations",
            fileName
        );

    private static int IndexOfRequired(string source, string value)
    {
        var index = source.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"未找到预期迁移契约: {value}");
        return index;
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var match = Regex.Match(
            source,
            $@"\b(?:public|private|internal)\s+(?:async\s+)?Task(?:<[^>]+>)?\s+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.CultureInvariant
        );
        Assert.True(match.Success, $"未找到方法定义: {methodName}");
        var start = match.Index;

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, $"无法定位方法主体: {methodName}");
        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException($"方法主体未闭合: {methodName}");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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

    private sealed class FakeSchemaMigrationRuntime : ISchemaMigrationRuntime
    {
        private readonly HashSet<(SchemaDatabase Database, string MigrationId)> _applied = [];

        public List<string> Events { get; } = [];
        public Dictionary<SchemaDatabase, Exception> ApplyFailures { get; } = [];
        public Exception? EnsureProvidersException { get; init; }
        public Exception? AcquireException { get; init; }
        public Exception? VerifyException { get; init; }
        public Exception? MobileVerifyException { get; init; }

        public void MarkApplied(SchemaDatabase database, string migrationId) =>
            _applied.Add((database, migrationId));

        public void EnsureSupportedProviders()
        {
            Events.Add("EnsureProviders");
            if (EnsureProvidersException is not null)
            {
                throw EnsureProvidersException;
            }
        }

        public Task ValidatePrerequisitesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("Preflight");
            return Task.CompletedTask;
        }

        public Task<ISchemaMigrationSession> AcquireMigrationSessionAsync(
            SchemaDatabase database,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"Acquire:{database}");
            if (AcquireException is not null)
            {
                throw AcquireException;
            }

            return Task.FromResult<ISchemaMigrationSession>(new FakeSchemaMigrationSession(this, database));
        }

        public Task<bool> IsMigrationAppliedAsync(
            SchemaDatabase database,
            string migrationId,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"Check:{database}:{migrationId}");
            return Task.FromResult(_applied.Contains((database, migrationId)));
        }

        public Task ApplyMainBaselineAsync(CancellationToken cancellationToken) =>
            ApplyAsync(
                SchemaDatabase.Main,
                SchemaMigrationCoordinator.MainMigrationId,
                cancellationToken
            );

        public Task ApplyPosmBaselineAsync(CancellationToken cancellationToken) =>
            ApplyAsync(
                SchemaDatabase.Posm,
                SchemaMigrationCoordinator.PosmMigrationId,
                cancellationToken
            );

        public Task ApplyMobileDeviceActivationAsync(CancellationToken cancellationToken) =>
            ApplyAsync(
                SchemaDatabase.Posm,
                SchemaMigrationCoordinator.MobileDeviceActivationMigrationId,
                cancellationToken
            );

        public Task ApplyMainAppendAsync(
            string migrationId,
            CancellationToken cancellationToken
        ) => ApplyAsync(SchemaDatabase.Main, migrationId, cancellationToken);

        private Task ApplyAsync(
            SchemaDatabase database,
            string migrationId,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"Apply:{database}:{migrationId}");
            if (ApplyFailures.TryGetValue(database, out var exception))
            {
                throw exception;
            }

            return Task.CompletedTask;
        }

        public Task VerifyDeviceActivationSchemaAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("Verify");
            if (VerifyException is not null)
            {
                throw VerifyException;
            }

            return Task.CompletedTask;
        }

        public Task VerifyMobileDeviceActivationSchemaAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("VerifyMobile");
            if (MobileVerifyException is not null)
            {
                throw MobileVerifyException;
            }

            return Task.CompletedTask;
        }

        private sealed class FakeSchemaMigrationSession(
            FakeSchemaMigrationRuntime runtime,
            SchemaDatabase database
        ) : ISchemaMigrationSession
        {
            public Task EnsureHistoryTableAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                runtime.Events.Add($"EnsureHistory:{database}");
                return Task.CompletedTask;
            }

            public Task<bool> IsAppliedAsync(string migrationId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                runtime.Events.Add($"SessionCheck:{database}:{migrationId}");
                return Task.FromResult(runtime._applied.Contains((database, migrationId)));
            }

            public Task RecordAppliedAsync(string migrationId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                runtime.Events.Add($"Record:{database}:{migrationId}");
                runtime._applied.Add((database, migrationId));
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
