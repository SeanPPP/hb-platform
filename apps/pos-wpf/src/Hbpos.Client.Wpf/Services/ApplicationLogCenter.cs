using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hbpos.Client.Wpf.Services;

internal sealed record ApplicationLogDefaults(
    string ProjectCode,
    string Environment,
    string SourceType)
{
    public static ApplicationLogDefaults Default { get; } = new("hbpos_win", "production", "POS");
}

internal sealed record ApplicationLogEntry(
    string Level,
    string Message,
    DateTimeOffset TimestampUtc,
    string ProjectCode,
    string Environment,
    string SourceType,
    string? Category = null,
    string? ServiceName = null,
    string? TraceId = null,
    string? RequestPath = null,
    string? RequestMethod = null,
    int? StatusCode = null,
    string? UserId = null,
    string? UserName = null,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    string? StackTrace = null,
    IReadOnlyDictionary<string, object?>? Properties = null);

internal sealed record ApplicationLogContext(
    string? TraceId = null,
    string? RequestPath = null,
    string? RequestMethod = null,
    int? StatusCode = null,
    string? UserId = null,
    string? UserName = null,
    IReadOnlyDictionary<string, object?>? Properties = null);

internal sealed record ApplicationLogOptions(
    bool Enabled,
    string? ApiKey,
    string ProjectCode,
    string Environment,
    string SourceType,
    string ServiceName,
    Uri IngestUri,
    int BatchSize,
    int QueueCapacity,
    string LocalBufferPath)
{
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    public ApplicationLogDefaults ToDefaults()
    {
        return new ApplicationLogDefaults(ProjectCode, Environment, SourceType);
    }

    public static ApplicationLogOptions FromConfiguration(IConfiguration configuration, Uri apiBaseAddress)
    {
        var enabled = ReadBool(configuration, "CentralLogging:Enabled", "HBPOS_LOG_CENTER_ENABLED")
            ?? !string.IsNullOrWhiteSpace(ReadText(configuration, "CentralLogging:ApiKey", "HBPOS_LOG_CENTER_API_KEY"));
        var ingestUrl = ReadText(configuration, "CentralLogging:IngestUrl", "HBPOS_LOG_CENTER_INGEST_URL");
        var projectCode = ReadText(configuration, "CentralLogging:ProjectCode", "HBPOS_LOG_CENTER_PROJECT_CODE") ?? "hbpos_win";
        var environment = ReadText(configuration, "CentralLogging:Environment", "HBPOS_LOG_CENTER_ENVIRONMENT") ??
            System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
            "production";
        var sourceType = ReadText(configuration, "CentralLogging:SourceType", "HBPOS_LOG_CENTER_SOURCE_TYPE") ?? "POS";
        var serviceName = ReadText(configuration, "CentralLogging:ServiceName", "HBPOS_LOG_CENTER_SERVICE_NAME") ?? "Hbpos.Client.Wpf";
        var batchSize = ReadInt(configuration, "CentralLogging:BatchSize", "HBPOS_LOG_CENTER_BATCH_SIZE") ?? 20;
        var queueCapacity = ReadInt(configuration, "CentralLogging:QueueCapacity", "HBPOS_LOG_CENTER_QUEUE_CAPACITY") ?? 200;
        var localBufferPath = ReadText(configuration, "CentralLogging:LocalBufferPath", "HBPOS_LOG_CENTER_LOCAL_BUFFER_PATH") ??
            Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "HotBargain",
                "hbpos",
                "central-log-buffer.jsonl");

        return new ApplicationLogOptions(
            enabled,
            ReadText(configuration, "CentralLogging:ApiKey", "HBPOS_LOG_CENTER_API_KEY"),
            projectCode,
            environment,
            sourceType,
            serviceName,
            ResolveIngestUri(ingestUrl, apiBaseAddress),
            Math.Clamp(batchSize, 1, 100),
            Math.Clamp(queueCapacity, 10, 5000),
            localBufferPath);
    }

    private static Uri ResolveIngestUri(string? ingestUrl, Uri apiBaseAddress)
    {
        if (Uri.TryCreate(ingestUrl, UriKind.Absolute, out var absoluteIngestUri))
        {
            return absoluteIngestUri;
        }

        var normalizedBase = apiBaseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? apiBaseAddress
            : new Uri(apiBaseAddress.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBase, "api/system/logs/ingest");
    }

    private static string? ReadText(IConfiguration configuration, string configKey, string environmentKey)
    {
        // 环境变量用于部署覆盖，必须优先于 appsettings 中的默认值。
        var environmentValue = System.Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        var configuredValue = configuration[configKey];
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue.Trim();
        }

        return null;
    }

    private static int? ReadInt(IConfiguration configuration, string configKey, string environmentKey)
    {
        var text = ReadText(configuration, configKey, environmentKey);
        return int.TryParse(text, out var value) ? value : null;
    }

    private static bool? ReadBool(IConfiguration configuration, string configKey, string environmentKey)
    {
        var text = ReadText(configuration, configKey, environmentKey);
        return bool.TryParse(text, out var value) ? value : null;
    }
}

internal interface IApplicationLogSink
{
    void Enqueue(ApplicationLogEntry entry);
}

internal sealed class NoopApplicationLogSink : IApplicationLogSink
{
    public static NoopApplicationLogSink Instance { get; } = new();

    private NoopApplicationLogSink()
    {
    }

    public void Enqueue(ApplicationLogEntry entry)
    {
        _ = entry;
    }
}

internal sealed class ApplicationLogBackgroundService(
    ApplicationLogOptions options,
    HttpClient httpClient) : BackgroundService, IApplicationLogSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // 队列满时直接丢弃最旧日志，保证支付/下单主链路永不被日志阻塞。
    private readonly Channel<ApplicationLogEntry> _channel = Channel.CreateBounded<ApplicationLogEntry>(new BoundedChannelOptions(options.QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public void Enqueue(ApplicationLogEntry entry)
    {
        _channel.Writer.TryWrite(entry);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                var batch = new List<ApplicationLogEntry>(options.BatchSize);
                while (batch.Count < options.BatchSize && _channel.Reader.TryRead(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                try
                {
                    await SendBatchAsync(batch, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await AppendLocalBufferAsync(batch, stoppingToken);
                    WriteInternalDiagnostic($"ingest failed count={batch.Count} error={ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        var remaining = new List<ApplicationLogEntry>();
        while (_channel.Reader.TryRead(out var entry))
        {
            remaining.Add(entry);
        }

        if (remaining.Count > 0)
        {
            try
            {
                await SendBatchAsync(remaining, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await AppendLocalBufferAsync(remaining, CancellationToken.None);
                WriteInternalDiagnostic($"final ingest failed count={remaining.Count} error={ex.Message}");
            }
        }
    }

    private async Task SendBatchAsync(
        IReadOnlyList<ApplicationLogEntry> batch,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.IngestUri)
        {
            Content = JsonContent.Create(new
            {
                logs = batch.Select(ToIngestItem).ToList()
            }, options: JsonOptions)
        };
        request.Headers.Add("X-Log-Project", options.ProjectCode);
        request.Headers.Add("X-Log-Key", options.ApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Central log ingest failed with HTTP {(int)response.StatusCode}: {responseBody}");
    }

    private async Task AppendLocalBufferAsync(
        IReadOnlyList<ApplicationLogEntry> batch,
        CancellationToken cancellationToken)
    {
        try
        {
            // 远端失败时只做本地缓冲，不把异常继续抛回业务线程。
            var directory = Path.GetDirectoryName(options.LocalBufferPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = string.Join(
                Environment.NewLine,
                batch.Select(item => JsonSerializer.Serialize(ToIngestItem(item), JsonOptions)));
            await File.AppendAllTextAsync(
                options.LocalBufferPath,
                payload + Environment.NewLine,
                Encoding.UTF8,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteInternalDiagnostic($"buffer append failed path={options.LocalBufferPath} error={ex.Message}");
        }
    }

    private static void WriteInternalDiagnostic(string message)
    {
        var line = $"[HBPOS][Client][CentralLog] {DateTimeOffset.Now:O} {message}";
        Console.WriteLine(line);
        Debug.WriteLine(line);
    }

    private static object ToIngestItem(ApplicationLogEntry entry)
    {
        return new
        {
            level = entry.Level,
            message = entry.Message,
            timestampUtc = entry.TimestampUtc,
            projectCode = entry.ProjectCode,
            environment = entry.Environment,
            sourceType = entry.SourceType,
            category = entry.Category,
            serviceName = entry.ServiceName,
            traceId = entry.TraceId,
            requestPath = entry.RequestPath,
            requestMethod = entry.RequestMethod,
            statusCode = entry.StatusCode,
            userId = entry.UserId,
            userName = entry.UserName,
            exceptionType = entry.ExceptionType,
            exceptionMessage = entry.ExceptionMessage,
            stackTrace = entry.StackTrace,
            properties = entry.Properties
        };
    }
}
