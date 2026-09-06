using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using Microsoft.Extensions.DependencyInjection;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;

// 任务专用历史回填工具：默认只读 dry-run，不初始化 schema、不读取 appsettings。
var write = args.Any(arg => arg.Equals("--write", StringComparison.OrdinalIgnoreCase));
var enqueueRecovery = args.Any(arg => arg.Equals("--enqueue-recovery", StringComparison.OrdinalIgnoreCase));
var verifyBackup = args.Any(arg => arg.Equals("--verify-backup", StringComparison.OrdinalIgnoreCase));
var dateValues = new List<string>();
for (var index = 0; index < args.Length; index++)
{
    var arg = args[index];
    if (arg.Equals("--write", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--enqueue-recovery", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--verify-backup", StringComparison.OrdinalIgnoreCase))
        continue;
    if (arg.Equals("--backup-path", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--restore-path", StringComparison.OrdinalIgnoreCase))
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{arg} 必须提供路径");
        index++;
        continue;
    }
    if (arg.StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException($"不支持的参数: {arg}");
    dateValues.Add(arg);
}

var dates = dateValues.Select(value =>
{
    if (!DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed))
        throw new ArgumentException($"日期必须为 yyyy-MM-dd: {value}");
    return parsed.Date;
}).Distinct().OrderBy(value => value).ToList();
if (dates.Count == 0)
    throw new ArgumentException("用法: dotnet run --project services/backend/HbSupplierStatisticsBackfillTmp -- yyyy-MM-dd [yyyy-MM-dd ...] [--write --backup-path PATH --restore-path PATH]");

var backupPath = GetOption("--backup-path");
var restorePath = GetOption("--restore-path");
if ((write || verifyBackup) && (string.IsNullOrWhiteSpace(backupPath) || string.IsNullOrWhiteSpace(restorePath)))
    throw new InvalidOperationException("--write 必须同时提供已完成备份的 --backup-path 和 --restore-path；工具不会自动备份");
if ((write || verifyBackup) && (!File.Exists(backupPath!) || !File.Exists(restorePath!)))
    throw new FileNotFoundException("写模式要求备份路径和恢复路径已存在，工具不会创建或覆盖备份");

var configText = await Console.In.ReadToEndAsync();
if (string.IsNullOrWhiteSpace(configText))
    throw new InvalidOperationException("连接配置只允许通过 stdin JSON 提供；未读取 appsettings 文件");
var config = JsonSerializer.Deserialize<Dictionary<string, string>>(configText)
    ?? throw new InvalidOperationException("stdin 必须是 JSON 对象");
string Connection(string name, params string[] aliases)
{
    foreach (var key in new[] { name }.Concat(aliases))
        if (config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;
    throw new InvalidOperationException($"stdin 缺少 {name} 连接字符串");
}

using var hbweb = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = Connection("HBweb", "ConnectionStrings__DefaultConnection"),
    DbType = DbType.SqlServer, IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
});
using var posm = new SqlSugarClient(new ConnectionConfig
{
    ConnectionString = Connection("POSM", "ConnectionStrings__HBPOSMConnection"),
    DbType = DbType.SqlServer, IsAutoCloseConnection = true, InitKeyType = InitKeyType.Attribute,
});
GuardReadOnly(hbweb, "HBweb", write);
// POSM 只作为来源读取库，即使 HBweb 进入写模式也必须保持只读。
GuardReadOnly(posm, "POSM", allowWrites: false);
var context = CreateContext<SqlSugarContext>(hbweb);
var posmContext = CreateContext<POSMSqlSugarContext>(posm);
var service = new SalesStatisticsSupplierStoreSummaryService();

if (write || verifyBackup)
{
    var hbwebName = Convert.ToString(await hbweb.Ado.GetScalarAsync("SELECT DB_NAME()"));
    var posmName = Convert.ToString(await posm.Ado.GetScalarAsync("SELECT DB_NAME()"));
    var hbwebServer = Convert.ToString(await hbweb.Ado.GetScalarAsync("SELECT @@SERVERNAME"));
    var posmServer = Convert.ToString(await posm.Ado.GetScalarAsync("SELECT @@SERVERNAME"));
    if (!string.Equals(hbwebName, "HBweb", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(posmName, "POSM", StringComparison.OrdinalIgnoreCase)
        || hbwebServer != "10_3_8_3" || posmServer != "10_3_8_3")
        throw new InvalidOperationException($"写模式数据库核对失败: HBweb={hbwebName}, POSM={posmName}");
    await ValidateBackupAsync(hbweb, backupPath!, restorePath!, dates);
    Console.WriteLine($"BACKUP_GUARD database=HBweb/POSM backup={backupPath} restore={restorePath} dates={string.Join(",", dates.Select(date => date.ToString("yyyy-MM-dd")))}");
}

if (verifyBackup) return;

if (enqueueRecovery)
{
    // 本次恢复仅限已备份的四个失败日期；使用既有持久队列，由正式后端的租约执行器串行处理。
    var recoveryDates = new[] { new DateTime(2026, 8, 5), new DateTime(2026, 8, 12), new DateTime(2026, 8, 14), new DateTime(2026, 8, 21) };
    if (!dates.SequenceEqual(recoveryDates))
        throw new InvalidOperationException("恢复模式仅接受本次计划的四个日期，必须一次完整提交");
    var states = await context.Db.Queryable<SalesStatisticRefreshState>()
        .Where(state => state.StatisticType == SalesStatisticType.ProductStoreDaily && dates.Contains(state.Date))
        .ToListAsync();
    if (states.Count != dates.Count || states.Any(state => state.Status != SalesStatisticRefreshStatus.Failed
        && state.Status != SalesStatisticRefreshStatus.Queued && state.Status != SalesStatisticRefreshStatus.Running))
        throw new InvalidOperationException("恢复日期状态已变化，拒绝重新排队已完成或未知日期");
    Console.WriteLine(JsonSerializer.Serialize(new { mode = "enqueue-recovery", write, maxConcurrency = 1,
        states = states.Select(state => new { date = state.Date.ToString("yyyy-MM-dd"), state.Status, state.JobId }) }));
    if (!write) return;
    using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(backupPath!));
    var sources = manifestDocument.RootElement.GetProperty("tables").EnumerateArray()
        .Select(table => table.GetProperty("source").GetString()).ToHashSet();
    if (!sources.Contains("dbo.ProductStoreDailySalesStatistic") || !sources.Contains("dbo.StoreSalesStatistic"))
        throw new InvalidOperationException("恢复模式要求商品与分店原始统计备份均已验证");
    using var scopeProvider = new ServiceCollection().BuildServiceProvider();
    var queue = new ProductStoreDailyStatisticQueueService(context,
        new ScheduledTaskLogService(context, NullLogger<ScheduledTaskLogService>.Instance),
        scopeProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ProductStoreDailyStatisticQueueService>.Instance);
    var submitted = await queue.EnqueueAsync(dates, "报表统计优化恢复 2026-09-06", maxConcurrency: 1);
    Console.WriteLine(JsonSerializer.Serialize(submitted));
    return;
}

foreach (var date in dates)
{
    var oldAustralian = await context.Db.Queryable<AustralianSupplierStoreSalesDetail>()
        .Where(row => row.Date >= date && row.Date < date.AddDays(1)).CountAsync();
    var oldChina = await context.Db.Queryable<ChinaSupplierStoreSalesDetail>()
        .Where(row => row.Date >= date && row.Date < date.AddDays(1)).CountAsync();
    var (build, productVersion, productStatus, sourceWatermark, fence) = await service.BuildFromCompletedProductSnapshotAsync(
        context, posmContext, date, DateTime.Now);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        date = date.ToString("yyyy-MM-dd"), mode = write ? "write" : "dry-run", productStatus,
        productVersion, sourceComplete = true, oldAustralian, oldChina,
        newAustralian = build.Australian.Count, newChina = build.China.Count,
    }));
    if (!write) continue;

    await SalesStatisticsTransactionExecutor.ExecuteAsync(
        beginAsync: () => context.Db.Ado.BeginTranAsync(),
        workAsync: async () =>
        {
            await service.EnsureProductVersionUnchangedAsync(context, date, productVersion, fence);
            await service.PersistWithinTransactionAsync(context, date, build, productStatus, productVersion, sourceWatermark);
        },
        commitAsync: () => context.Db.Ado.CommitTranAsync(),
        rollbackAsync: () => context.Db.Ado.RollbackTranAsync(),
        logger: NullLogger.Instance, operationName: "供应商历史回填");
}

string? GetOption(string name)
{
    var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static T CreateContext<T>(ISqlSugarClient db) where T : class
{
    var context = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    typeof(T).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, db);
    return context;
}

static void GuardReadOnly(ISqlSugarClient db, string name, bool allowWrites)
{
    if (allowWrites) return;
    db.Aop.OnLogExecuting = (sql, _) =>
    {
        var trimmed = sql.TrimStart();
        var selectLike = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(";WITH", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        if (!selectLike || System.Text.RegularExpressions.Regex.IsMatch(
                sql, @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|TRUNCATE|EXEC|INTO)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidOperationException($"{name} dry-run 拒绝非 SELECT SQL");
    };
}

static async Task ValidateBackupAsync(ISqlSugarClient db, string backupPath, string restorePath, IReadOnlyList<DateTime> dates)
{
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(backupPath));
    var manifest = document.RootElement;
    if (manifest.GetProperty("status").GetString() != "committed-and-verified"
        || manifest.GetProperty("database").GetString() != "HBweb"
        || manifest.GetProperty("server").GetString() != "10_3_8_3")
        throw new InvalidOperationException("备份 manifest 未完成提交核对，或目标数据库不匹配");
    var coveredDates = manifest.GetProperty("targetDates").EnumerateArray()
        .Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
    if (dates.Any(date => !coveredDates.Contains(date.ToString("yyyy-MM-dd"))))
        throw new InvalidOperationException("备份日期没有覆盖全部请求日期");
    var actualRestoreHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(restorePath)));
    if (!string.Equals(actualRestoreHash, manifest.GetProperty("restoreSha256").GetString(), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("恢复脚本校验和与已验证备份不一致");

    var allowedSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "dbo.AustralianSupplierStoreSalesDetail", "dbo.ChinaSupplierStoreSalesDetail",
        "dbo.SalesStatisticRefreshState", "dbo.ProductStoreDailySalesStatistic", "dbo.StoreSalesStatistic",
    };
    var verifiedSources = new HashSet<string>(StringComparer.Ordinal);
    foreach (var table in manifest.GetProperty("tables").EnumerateArray())
    {
        var source = table.GetProperty("source").GetString() ?? string.Empty;
        var backup = table.GetProperty("backup").GetString() ?? string.Empty;
        if (!allowedSources.Contains(source) || !verifiedSources.Add(source)
            || !Regex.IsMatch(backup, @"^dbo\.ReportOptBackup_[0-9]{8}_" + Regex.Escape(source[4..]) + "$"))
            throw new InvalidOperationException("备份对象不在本次统计表白名单内");
        var columns = table.GetProperty("columns").EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        if (columns.Length == 0 || columns.Any(column => !Regex.IsMatch(column, @"^[A-Za-z_][A-Za-z0-9_]*$")))
            throw new InvalidOperationException("备份列名无效");
        var actualColumns = await db.Ado.SqlQueryAsync<BackupColumn>(
            "SELECT [name] AS [ColumnName] FROM sys.columns WHERE object_id=OBJECT_ID(@table) ORDER BY column_id",
            new SugarParameter("@table", backup));
        if (!columns.SequenceEqual(actualColumns.Select(column => column.ColumnName), StringComparer.Ordinal))
            throw new InvalidOperationException($"备份表缺失或列集合变化: {backup}");
        // 标识符仅来自上述严格白名单；业务值不参与 SQL 拼接。
        var selected = string.Join(',', columns.Select(column => $"[{column}]"));
        var metrics = (await db.Ado.SqlQueryAsync<BackupMetrics>(
            $"SELECT COUNT_BIG(*) AS [RowCount], COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM({selected})),0) AS [ChecksumXor], COALESCE(SUM(CONVERT(bigint,BINARY_CHECKSUM({selected}))),0) AS [ChecksumSum] FROM [dbo].[{backup[4..]}]"))
            .Single();
        var expected = table.GetProperty("metrics");
        if (metrics.RowCount != expected.GetProperty("RowCount").GetInt64()
            || metrics.ChecksumXor != expected.GetProperty("ChecksumXor").GetInt32()
            || metrics.ChecksumSum != expected.GetProperty("ChecksumSum").GetInt64())
            throw new InvalidOperationException($"备份行数或校验和变化: {backup}");
    }
    if (!new[] { "dbo.AustralianSupplierStoreSalesDetail", "dbo.ChinaSupplierStoreSalesDetail", "dbo.SalesStatisticRefreshState" }.All(verifiedSources.Contains))
        throw new InvalidOperationException("备份清单缺少供应商表或统计状态表");
}

sealed class BackupColumn
{
    public string ColumnName { get; set; } = string.Empty;
}

sealed class BackupMetrics
{
    public long RowCount { get; set; }
    public int ChecksumXor { get; set; }
    public long ChecksumSum { get; set; }
}
