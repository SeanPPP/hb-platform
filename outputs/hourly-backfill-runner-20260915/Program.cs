using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;

const string expectedRuleVersion = HourlySalesBackfillService.CurrentRuleVersion;
const string leaseTask = "HourlySalesBackfillWorker";
const string leaseScope = "global";
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

try
{
    var o = Options.Parse(args);
    if (o.Help) { Console.WriteLine(Options.HelpText); return 0; }
    if (o.SelfTest) return RunSelfTest();
    Validate(o);

    var configBuilder = new ConfigurationBuilder();
    if (Directory.Exists(o.ConfigRoot))
        configBuilder.SetBasePath(o.ConfigRoot)
            .AddJsonFile("appsettings.json", true, false)
            .AddJsonFile("appsettings.Development.json", true, false);
    // 容器可只使用 ConnectionStrings__*；默认本地路径不存在也不影响启动。
    var config = configBuilder.AddEnvironmentVariables()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:EnableSqlLogging"] = "false" })
        .Build();
    var mainCs = RequiredConnection(config, "DefaultConnection");
    var mainType = DetectDbType(mainCs);
    var mainClient = CreateClient(mainCs, mainType, 120);
    var posmClient = CreateClient(RequiredConnection(config, "HBPOSMConnection"), DbType.SqlServer, 120);
    var historyScope = CreateScope(RequiredConnection(config, "HBSalesRecord"));
    var main = new SqlSugarContext(mainClient, NullLogger<SqlSugarContext>.Instance);
    var posm = new POSMSqlSugarContext(posmClient);
    var history = new HBSalesRecordSqlSugarContext(historyScope);
    InstallReadOnlyGuard(posm.Db, "POSM");
    InstallReadOnlyGuard(history.Db, "HBSales");
    if (!Writes(o)) InstallReadOnlyGuard(main.Db, "HBweb");

    try
    {
        var identity = string.Join(";", new[]
        {
            $"HBweb={await TargetIdentity(main.Db, mainType)}",
            $"POSM={await TargetIdentity(posm.Db, DbType.SqlServer)}",
            $"HBSales={await TargetIdentity(history.Db, DbType.SqlServer)}",
        });
        var fingerprint = Hash(identity);
        Console.WriteLine($"TARGET {identity} fingerprint={fingerprint}");
        if (Writes(o) && !string.Equals(o.ConfirmTarget, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"写模式必须传入本次显示的 --confirm-target {fingerprint}");

        var service = new HourlySalesBackfillService(main, posm, history,
            NullLogger<HourlySalesBackfillService>.Instance);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

        if (o.Mode == "preview" && !o.PersistBatch)
        {
            var cp = Load(o.Checkpoint, o.Resume, json);
            if (cp.TargetFingerprint is not null
                && !string.Equals(cp.TargetFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("断点目标库 fingerprint 与当前连接不一致，已阻断续跑");
            for (var day = o.Start; day <= o.End; day = day.AddDays(1))
            {
                var key = $"{day:yyyy-MM-dd}";
                if (cp.Days.TryGetValue(key, out var old)
                    && (!o.RetryFailed || string.Equals(old.Status, "Valid", StringComparison.Ordinal)))
                { Console.WriteLine($"SKIP {key}"); continue; }
                var started = DateTime.UtcNow;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(o.DayTimeoutSeconds));
                    var p = await service.PreviewDayReadOnlyAsync(day, timeout.Token);
                    cp.Days[key] = new DayState(day, p.Valid ? "Valid" : "Invalid", p.SourceHash, null,
                        p.ExpectedAmount, p.CandidateAmount, p.ExpectedOrderCount, p.CandidateOrderCount,
                        p.CandidateRowCount, p.RequiredSources,
                        p.SourceStatuses.Select(x => new SourceState(x.Source, x.State, x.RowCount,
                            x.Watermark, x.ContentHash, x.ObservedAtUtc,
                            x.Error is null ? null : Redact(x.Error))).ToArray(),
                        p.Issues.Select(Redact).ToArray(), null,
                        (long)(DateTime.UtcNow - started).TotalMilliseconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
                {
                    cp.Days[key] = new DayState(day, "Failed", null, null, 0, 0, 0, 0, 0, [], [], [],
                        Redact(ex.GetBaseException().Message), (long)(DateTime.UtcNow - started).TotalMilliseconds);
                }
                cp = cp with { TargetFingerprint = fingerprint, UpdatedAtUtc = DateTime.UtcNow };
                Save(o.Checkpoint, cp, json);
                Console.WriteLine($"{cp.Days[key].Status} {key}");
                if (day < o.End) await Task.Delay(o.DelayMs, stop.Token);
            }
            return cp.Days.Values.Any(x => x.Error is not null || x.Status == "Invalid") ? 3 : 0;
        }

        if (!service.SchemaReady()) throw new InvalidOperationException("目标库缺少完整发布表/读取视图迁移，已阻断");
        if (o.Mode == "status")
        {
            var status = await service.GetSnapshotAsync(o.BatchId!.Value)
                ?? throw new InvalidOperationException("批次不存在");
            EnsureVersion(status);
            SaveSnapshot(o.Checkpoint, fingerprint, status, json);
            Console.WriteLine($"STATUS batch={status.BatchId} status={status.Status} manifest={ManifestHash(status)}");
            return 0;
        }

        var runId = Guid.NewGuid();
        var leases = new ScheduledTaskLeaseService(main,
            Microsoft.Extensions.Options.Options.Create(new ScheduledTaskOptions
            { InstanceId = $"hourly-backfill-runner-{runId:N}" }),
            NullLogger<ScheduledTaskLeaseService>.Instance);
        const int leaseMinutes = 30;
        var acquired = await leases.TryAcquireAsync(leaseTask, leaseScope, TimeSpan.FromMinutes(leaseMinutes));
        var leaseToken = acquired.Lease?.LeaseToken;
        if (!acquired.Acquired || string.IsNullOrWhiteSpace(leaseToken))
            throw new InvalidOperationException("全局回填租约正被其他执行器持有，已阻断");
        var success = false;
        try
        {
            Guid batchId;
            if (o.PersistBatch)
                batchId = o.BatchId ?? await service.PreviewAsync(o.Start, o.End, o.Actor!);
            else
                batchId = o.BatchId!.Value;
            var snapshot = await service.GetSnapshotAsync(batchId)
                ?? throw new InvalidOperationException("批次不存在");
            EnsureVersion(snapshot);

            if (o.Mode == "apply")
            {
                if (!config.GetValue<bool>("SalesStatistics:HourlyBackfillApplyEnabled"))
                    throw new InvalidOperationException("配置 SalesStatistics:HourlyBackfillApplyEnabled 未启用");
                var manifest = ManifestHash(snapshot);
                if (!string.Equals(o.ConfirmManifest, manifest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"应用前必须核对预览清单并传入 --confirm-manifest {manifest}");
                if (!await service.RequestAsync(batchId, false, o.Actor!))
                    throw new InvalidOperationException("批次不是可应用的 Previewed/PreviewedWithIssues 状态");
            }

            if (o.Mode == "revalidate")
            {
                var revalidationFailures = 0;
                foreach (var day in snapshot.Days.Where(d => d.Status == "Applied"))
                {
                    try
                    {
                        await leases.EnsureActiveAsync(leaseTask, leaseScope, leaseToken,
                            TimeSpan.FromMinutes(leaseMinutes), $"revalidate {day.Date:yyyy-MM-dd}");
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(o.DayTimeoutSeconds));
                        var valid = await service.RevalidateAsync(batchId, day.Date, timeout.Token);
                        if (valid != true) revalidationFailures++;
                        Console.WriteLine($"REVALIDATE {day.Date:yyyy-MM-dd} {(valid == true ? "VALID" : "DRIFT")}");
                        snapshot = (await service.GetSnapshotAsync(batchId))!;
                        SaveSnapshot(o.Checkpoint, fingerprint, snapshot, json);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested)
                    {
                        revalidationFailures++;
                        Console.Error.WriteLine($"REVALIDATE {day.Date:yyyy-MM-dd} FAILED {Redact(ex.GetBaseException().Message)}");
                        SaveRevalidationFailure(o.Checkpoint, fingerprint, snapshot, day.Date,
                            Redact(ex.GetBaseException().Message), json);
                    }
                    await Task.Delay(o.DelayMs, stop.Token);
                }
                success = revalidationFailures == 0;
                return success ? 0 : 3;
            }

            while (true)
            {
                await leases.EnsureActiveAsync(leaseTask, leaseScope, leaseToken,
                    TimeSpan.FromMinutes(leaseMinutes), "targeted-step");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(o.DayTimeoutSeconds));
                var step = await service.RunOneAsync(batchId, timeout.Token, () => leases.RenewAsync(
                    leaseTask, leaseScope, leaseToken, TimeSpan.FromMinutes(leaseMinutes)));
                if (step.Snapshot is not null) SaveSnapshot(o.Checkpoint, fingerprint, step.Snapshot, json);
                Console.WriteLine($"STEP {step.ProcessedDate:yyyy-MM-dd} batch={step.BatchStatus}");
                if (step.Terminal) { success = step.BatchStatus is "Previewed" or "Applied"; return success ? 0 : 3; }
                await Task.Delay(o.DelayMs, stop.Token);
            }
        }
        finally
        {
            await leases.CompleteAsync(leaseTask, leaseScope, leaseToken!, success,
                success ? null : "runner-stopped-or-failed");
        }
    }
    finally { Close(main.Db); Close(posm.Db); Close(history.Db); }
}
catch (OperationCanceledException) { Console.Error.WriteLine("已取消；逐日断点已保留。"); return 130; }
catch (Exception ex) { Console.Error.WriteLine($"CLI 失败：{Redact(ex.GetBaseException().Message)}"); return 2; }

static bool Writes(Options o) => o.PersistBatch || o.Mode is "apply" or "revalidate";
static void Validate(Options o)
{
    if (o.Mode is not ("preview" or "apply" or "revalidate" or "status")) throw new ArgumentException("未知模式");
    if (o.End < o.Start || (o.End - o.Start).Days + 1 > 365) throw new ArgumentException("范围必须为最多365日");
    if (o.DelayMs is < 0 or > 60_000) throw new ArgumentException("--delay-ms 必须为 0..60000");
    if (o.DayTimeoutSeconds is < 1 or > 600) throw new ArgumentException("--day-timeout-seconds 必须为 1..600");
    if (o.PersistBatch && o.Mode != "preview") throw new ArgumentException("--persist-batch 仅用于 preview");
    if (Writes(o) && string.IsNullOrWhiteSpace(o.Actor)) throw new ArgumentException("写模式必须提供 --actor");
    if (o.Mode is "apply" or "revalidate" or "status" && o.BatchId is null) throw new ArgumentException("该模式必须提供 --batch");
    if (o.Mode == "apply" && !o.AllowApply) throw new ArgumentException("apply 必须显式提供 --allow-apply");
}
static void EnsureVersion(HourlySalesBackfillBatchSnapshot s)
{
    if (s.RuleVersion != expectedRuleVersion) throw new InvalidOperationException($"批次规则版本 {s.RuleVersion} 不受当前读取契约支持，已阻断");
}
static string ManifestHash(HourlySalesBackfillBatchSnapshot s) => Hash(string.Join("\n", s.Days.OrderBy(x => x.Date)
    .Select(x => $"{x.Date:yyyy-MM-dd}|{x.Status}|{x.SourceHash}|{x.CandidateAmount}|{x.CandidateOrderCount}|{x.RowCount}")));
static void SaveSnapshot(string path, string fingerprint, HourlySalesBackfillBatchSnapshot s, JsonSerializerOptions j)
{
    EnsureVersion(s);
    var cp = new Checkpoint(1, fingerprint, s.BatchId, s.RuleVersion, ManifestHash(s), DateTime.UtcNow,
        s.Days.ToDictionary(x => $"{x.Date:yyyy-MM-dd}", x => new DayState(x.Date, x.Status, x.SourceHash,
            x.AfterHash, x.ExpectedAmount, x.CandidateAmount, x.ExpectedOrderCount, x.CandidateOrderCount,
            x.RowCount, ParseStatuses(x.SourceStatusJson).Select(y => y.Source).Distinct().ToArray(),
            ParseStatuses(x.SourceStatusJson), [], x.Error is null ? null : Redact(x.Error), 0)));
    Save(path, cp, j);
}
static void SaveRevalidationFailure(string path, string fingerprint, HourlySalesBackfillBatchSnapshot s,
    DateTime failedDate, string error, JsonSerializerOptions j)
{
    var days = s.Days.ToDictionary(x => $"{x.Date:yyyy-MM-dd}", x => new DayState(x.Date, x.Status,
        x.SourceHash, x.AfterHash, x.ExpectedAmount, x.CandidateAmount, x.ExpectedOrderCount,
        x.CandidateOrderCount, x.RowCount, ParseStatuses(x.SourceStatusJson).Select(y => y.Source).Distinct().ToArray(),
        ParseStatuses(x.SourceStatusJson), [], x.Error is null ? null : Redact(x.Error), 0));
    var key = $"{failedDate:yyyy-MM-dd}";
    if (days.TryGetValue(key, out var day)) days[key] = day with { Error = $"revalidate:{error}" };
    Save(path, new(1, fingerprint, s.BatchId, s.RuleVersion, ManifestHash(s), DateTime.UtcNow, days), j);
}
static Checkpoint Load(string path, bool resume, JsonSerializerOptions j)
{
    if (!File.Exists(path)) return new(1, null, null, expectedRuleVersion, null, DateTime.UtcNow, []);
    if (!resume) throw new InvalidOperationException("断点已存在；续跑请传 --resume");
    return JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path), j) ?? throw new InvalidOperationException("断点无效");
}
static void Save(string path, Checkpoint cp, JsonSerializerOptions j)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(cp, j)); File.Move(tmp, path, true);
}
static async Task<string> TargetIdentity(ISqlSugarClient db, DbType type) => type switch
{
    DbType.SqlServer => await db.Ado.GetStringAsync("SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)) + N'|' + DB_NAME()"),
    DbType.PostgreSQL => await db.Ado.GetStringAsync("SELECT current_database()"),
    _ => await db.Ado.GetStringAsync("SELECT 'sqlite|' || file FROM pragma_database_list WHERE name='main'")
};
static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static IReadOnlyList<SourceState> ParseStatuses(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return [];
    try
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var stateElement = Property(item, "State");
            var state = stateElement.ValueKind == JsonValueKind.Number
                ? stateElement.GetInt32() switch { 0 => "Success", 1 => "Empty", 2 => "Unavailable", _ => "Invalid" }
                : stateElement.GetString() ?? "Invalid";
            var error = NullableString(item, "Error");
            var observed = NullableString(item, "ObservedAtUtc");
            return new SourceState(Property(item, "Source").GetString() ?? "", state,
                Property(item, "RowCount").GetInt32(), NullableString(item, "Watermark"),
                NullableString(item, "ContentHash"),
                DateTime.TryParse(observed, out var timestamp) ? timestamp : null,
                error is null ? null : Redact(error));
        }).ToArray();
    }
    catch (JsonException) { return [new("checkpoint", "Invalid", 0, null, null, null, "source-status-json-invalid")]; }
}
static JsonElement Property(JsonElement item, string name) => item.TryGetProperty(name, out var value)
    || item.TryGetProperty(char.ToLowerInvariant(name[0]) + name[1..], out value)
    ? value : default;
static string? NullableString(JsonElement item, string name)
{
    var value = Property(item, name);
    return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : value.GetString();
}
static string RequiredConnection(IConfiguration c, string name) => c.GetConnectionString(name) is { Length: > 0 } s ? s : throw new InvalidOperationException($"缺少连接配置 {name}");
static ISqlSugarClient CreateClient(string cs, DbType type, int timeout) { var c = new SqlSugarClient(new ConnectionConfig { ConnectionString = cs, DbType = type, IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute, MoreSettings = new ConnMoreSettings { IsWithNoLockQuery = type == DbType.SqlServer, DisableWithNoLockWithTran = true } }); c.Ado.CommandTimeOut = timeout; return c; }
static SqlSugarScope CreateScope(string cs) { var c = new SqlSugarScope(new ConnectionConfig { ConnectionString = cs, DbType = DbType.SqlServer, IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute, ConfigureExternalServices = new ConfigureExternalServices { EntityNameService = (t, e) => e.DbTableName ??= t.Name } }); c.Ado.CommandTimeOut = 120; return c; }
static DbType DetectDbType(string cs) => cs.Contains("Host=", StringComparison.OrdinalIgnoreCase) ? DbType.PostgreSQL : cs.Contains(".db", StringComparison.OrdinalIgnoreCase) ? DbType.Sqlite : DbType.SqlServer;
static void InstallReadOnlyGuard(ISqlSugarClient db, string name) => db.Aop.OnLogExecuting = (sql, _) => { if (Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|GRANT|REVOKE)\b", RegexOptions.IgnoreCase)) throw new InvalidOperationException($"只读保护拒绝 {name} 写入"); };
static string Redact(string s) => Regex.Replace(s ?? "", @"(?i)(password|pwd|user id|uid|secret)\s*=\s*[^;\s]+", "$1=<redacted>");
static void Close(ISqlSugarClient db) { try { db.Ado.Close(); } catch { } }
static int RunSelfTest()
{
    var gated = false;
    try { Validate(Options.Parse(["apply", "--batch", Guid.NewGuid().ToString()])); }
    catch { gated = true; }
    if (!gated) return 1;
    var path = Path.Combine(Path.GetTempPath(), $"hourly-runner-{Guid.NewGuid():N}.json");
    var day = new DayState(new DateTime(2025, 9, 15), "Valid", "source", null, 1, 1, 1, 1, 1,
        ["POSM"], [new("POSM", "Empty", 0, "wm", "hash", DateTime.UtcNow, null)], [], null, 1);
    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    Save(path, new(1, "three-source", null, expectedRuleVersion, null, DateTime.UtcNow,
        new() { ["2025-09-15"] = day }), options);
    var loaded = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path), options);
    File.Delete(path);
    if (loaded?.Days["2025-09-15"].SourceStatuses.Single().State != "Empty"
        || loaded.Days["2025-09-15"].RequiredSources.Single() != "POSM") return 1;
    var persisted = ParseStatuses("[{\"Source\":\"HBSales\",\"State\":2,\"RowCount\":0,\"Watermark\":\"wm\",\"ContentHash\":\"hash\",\"ObservedAtUtc\":null}]");
    if (persisted.Single().State != "Unavailable") return 1;
    Console.WriteLine("SELF-TEST PASS");
    return 0;
}

sealed record SourceState(string Source, string State, int RowCount, string? Watermark,
    string? ContentHash, DateTime? ObservedAtUtc, string? Error);
sealed record DayState(DateTime Date, string Status, string? SourceHash, string? AfterHash,
    decimal ExpectedAmount, decimal CandidateAmount, int ExpectedOrders, int CandidateOrders,
    int RowCount, IReadOnlyList<string> RequiredSources, IReadOnlyList<SourceState> SourceStatuses,
    IReadOnlyList<string> Issues, string? Error, long DurationMs);
sealed record Checkpoint(int Version, string? TargetFingerprint, Guid? BatchId, string RuleVersion, string? ManifestHash, DateTime UpdatedAtUtc, Dictionary<string, DayState> Days);
sealed record Options(string Mode, DateTime Start, DateTime End, string ConfigRoot, string Checkpoint, int DelayMs, int DayTimeoutSeconds, bool Resume, bool RetryFailed, bool PersistBatch, bool AllowApply, bool Help, bool SelfTest, Guid? BatchId, string? Actor, string? ConfirmTarget, string? ConfirmManifest)
{
    internal const string HelpText = """
      分时全年回填 runner（默认纯只读 preview）
      preview [--persist-batch] --start yyyy-MM-dd --end yyyy-MM-dd
      apply --batch UUID --allow-apply --confirm-target HASH --confirm-manifest HASH --actor NAME
      revalidate --batch UUID --confirm-target HASH --actor NAME
      status --batch UUID
      通用：--checkpoint PATH --resume --retry-failed --delay-ms 250 --day-timeout-seconds 300 --config-root PATH
      验证：--self-test；帮助：--help
      """;
    internal static Options Parse(string[] a)
    {
        var mode = a.Length > 0 && !a[0].StartsWith("--") ? a[0] : "preview";
        var flags = new HashSet<string>(a.Where(x => x.StartsWith("--")));
        string? V(string n) { var i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[i + 1] : null; }
        DateTime D(string n, string d) => DateTime.ParseExact(V(n) ?? d, "yyyy-MM-dd", null);
        return new(mode, D("--start", "2025-09-15"), D("--end", "2026-09-14"), V("--config-root") ?? "/Users/sean/DEV/hb-platform/services/backend/BlazorApp.Api", V("--checkpoint") ?? "hourly-backfill-checkpoint.json", int.Parse(V("--delay-ms") ?? "250"), int.Parse(V("--day-timeout-seconds") ?? "300"), flags.Contains("--resume"), flags.Contains("--retry-failed"), flags.Contains("--persist-batch"), flags.Contains("--allow-apply"), flags.Contains("--help") || flags.Contains("-h"), flags.Contains("--self-test"), Guid.TryParse(V("--batch"), out var id) ? id : null, V("--actor"), V("--confirm-target"), V("--confirm-manifest"));
    }
}
