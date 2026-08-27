using System.Runtime.CompilerServices;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.Performance;

/// <summary>
/// 保存进程级指标接收器，并为每个 SqlSugar client 组合执行完成回调。
/// Attach 可在 HostedService 启动前调用，避免数据库上下文需要依赖单例指标服务。
/// </summary>
public sealed class SqlPerformanceAttachmentService : IHostedService
{
    private static readonly ConditionalWeakTable<ISqlSugarClient, SqlPerformanceAttachment> AttachedClients = new();
    private static IPerformanceMetricRecorder? _recorder;
    private static PerformanceMetricsOptions? _options;

    private readonly IPerformanceMetricRecorder _instanceRecorder;
    private readonly PerformanceMetricsOptions _instanceOptions;

    public SqlPerformanceAttachmentService(
        IPerformanceMetricRecorder recorder,
        IOptions<PerformanceMetricsOptions> options
    )
    {
        _instanceRecorder = recorder;
        _instanceOptions = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _options, _instanceOptions);
        Volatile.Write(ref _recorder, _instanceRecorder);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _recorder, null);
        Volatile.Write(ref _options, null);
        return Task.CompletedTask;
    }

    public static void Attach(ISqlSugarClient client, string databaseContext)
    {
        ArgumentNullException.ThrowIfNull(client);
        var normalizedContext = string.IsNullOrWhiteSpace(databaseContext)
            ? "unknown"
            : databaseContext.Trim();

        AttachedClients.GetValue(client, db => CreateAttachment(db, normalizedContext));
    }

    /// <summary>
    /// 登记请求级 SQL 完成回调，但始终保留性能采集组合器。
    /// </summary>
    public static void SetOnLogExecuted(
        ISqlSugarClient client,
        Action<string, SugarParameter[]>? requestCallback
    )
    {
        ArgumentNullException.ThrowIfNull(client);

        // 正常路径已由数据上下文调用 Attach；兜底确保遗漏路径也不会丢失采集。
        var attachment = AttachedClients.GetValue(client, db => CreateAttachment(db, "unknown"));
        attachment.SetRequestCallback(requestCallback);
    }

    private static SqlPerformanceAttachment CreateAttachment(
        ISqlSugarClient client,
        string databaseContext
    )
    {
        // AopProvider 的 OnLogExecuted 只能整体赋值，所有业务回调必须在这里组合。
        var attachment = new SqlPerformanceAttachment(
            client,
            databaseContext,
            client.CurrentConnectionConfig.AopEvents?.OnLogExecuted
        );
        client.Aop.OnLogExecuted = attachment.OnLogExecuted;
        return attachment;
    }

    private static void TryRecord(ISqlSugarClient db, string databaseContext, string? sql)
    {
        var recorder = Volatile.Read(ref _recorder);
        var options = Volatile.Read(ref _options);
        if (recorder == null || options == null || !options.Enabled || IsSelfTelemetry(sql))
        {
            return;
        }

        try
        {
            var durationMs = db.Ado.SqlExecutionTime.TotalMilliseconds;
            if (!double.IsFinite(durationMs) || durationMs < 0)
            {
                return;
            }

            var fingerprint = SqlPerformanceFingerprint.Create(sql);
            recorder.Record(
                new PerformanceMetricRecord(
                    PerformanceMetricNames.SqlCommandDuration,
                    options.BackendProjectCode,
                    options.DefaultEnvironment,
                    "sql",
                    durationMs,
                    DateTime.UtcNow,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["databaseContext"] = databaseContext,
                        ["sqlFingerprint"] = fingerprint.Hash,
                        ["sqlTemplate"] = fingerprint.Template,
                    }
                )
            );
        }
        catch
        {
            // 指标采集永远不能改变业务 SQL 的结果或异常语义。
        }
    }

    internal static bool IsSelfTelemetry(string? sql) =>
        !string.IsNullOrWhiteSpace(sql)
        && (
            sql.Contains("PerformanceMetricBucket", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceMetricSample", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceMetricDailyAggregate", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceBaseline", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceOperationalRun", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceReleaseEvent", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceIngestRateWindow", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("PerformanceCollectorState", StringComparison.OrdinalIgnoreCase)
        );

    private sealed class SqlPerformanceAttachment
    {
        private readonly ISqlSugarClient _client;
        private readonly string _databaseContext;
        private readonly Action<string, SugarParameter[]>? _initialCallback;
        private Action<string, SugarParameter[]>? _requestCallback;

        public SqlPerformanceAttachment(
            ISqlSugarClient client,
            string databaseContext,
            Action<string, SugarParameter[]>? initialCallback
        )
        {
            _client = client;
            _databaseContext = databaseContext;
            _initialCallback = initialCallback;
        }

        public void SetRequestCallback(Action<string, SugarParameter[]>? requestCallback) =>
            Volatile.Write(ref _requestCallback, requestCallback);

        public void OnLogExecuted(string sql, SugarParameter[] parameters)
        {
            try
            {
                _initialCallback?.Invoke(sql, parameters);
                Volatile.Read(ref _requestCallback)?.Invoke(sql, parameters);
            }
            finally
            {
                // 即使业务日志回调异常，性能采集也保持最佳努力且不改变原异常语义。
                TryRecord(_client, _databaseContext, sql);
            }
        }
    }
}
