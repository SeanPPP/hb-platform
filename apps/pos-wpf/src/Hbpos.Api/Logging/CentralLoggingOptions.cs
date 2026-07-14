namespace Hbpos.Api.Logging;

internal sealed class CentralLoggingOptions
{
    public const string SectionName = "CentralLogging";

    public bool Enabled { get; set; }

    public string? IngestUrl { get; set; }

    public string? ApiKey { get; set; }

    public string ProjectCode { get; set; } = "hbpos_api";

    public string Environment { get; set; } = "Production";

    public string SourceType { get; set; } = "Backend";

    public string ServiceName { get; set; } = "Hbpos.Api";

    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    public int QueueCapacity { get; set; } = 1_000;

    public int BatchSize { get; set; } = 100;

    public int HttpTimeoutSeconds { get; set; } = 15;

    public Uri? IngestUri => Uri.TryCreate(IngestUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    public bool IsConfigured => Enabled &&
        IngestUri is not null &&
        !string.IsNullOrWhiteSpace(ApiKey);

    public static CentralLoggingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new CentralLoggingOptions
        {
            Enabled = section.GetValue(nameof(Enabled), false),
            IngestUrl = Normalize(section[nameof(IngestUrl)]),
            ApiKey = Normalize(section[nameof(ApiKey)]),
            ProjectCode = Normalize(section[nameof(ProjectCode)]) ?? "hbpos_api",
            Environment = Normalize(section[nameof(Environment)]) ?? "Production",
            SourceType = Normalize(section[nameof(SourceType)]) ?? "Backend",
            ServiceName = Normalize(section[nameof(ServiceName)]) ?? "Hbpos.Api",
            MinimumLevel = ParseMinimumLevel(section[nameof(MinimumLevel)]),
            QueueCapacity = Math.Clamp(section.GetValue(nameof(QueueCapacity), 1_000), 1, 100_000),
            BatchSize = Math.Clamp(section.GetValue(nameof(BatchSize), 100), 1, 100),
            HttpTimeoutSeconds = Math.Clamp(section.GetValue(nameof(HttpTimeoutSeconds), 15), 1, 300)
        };

        return options;
    }

    private static LogLevel ParseMinimumLevel(string? value)
    {
        if (!Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed))
        {
            return LogLevel.Warning;
        }

        // 中心端只接收 Warning 及以上，配置不能放宽到业务请求的低等级日志。
        return parsed < LogLevel.Warning ? LogLevel.Warning : parsed;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
