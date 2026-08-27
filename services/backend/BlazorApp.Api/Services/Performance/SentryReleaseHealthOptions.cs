namespace BlazorApp.Api.Services.Performance;

public sealed class SentryReleaseHealthOptions
{
    public const string SectionName = "PerformanceMetrics:SentryReleaseHealth";

    public static IReadOnlyList<string> ProjectWhitelist { get; } =
        Array.AsReadOnly(["hb-pos-ipad", "hb-pos-handheld"]);

    // 默认关闭；只有显式启用且只读 token、组织等配置完整时才会访问 Sentry。
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://sentry.io/";

    public string OrganizationSlug { get; set; } = string.Empty;

    /// <summary>
    /// 仅配置具备 org:read 权限的 Sentry token；该值不得写入日志或指标维度。
    /// </summary>
    public string ReadOnlyAuthToken { get; set; } = string.Empty;

    public string Environment { get; set; } = "production";

    /// <summary>写入统一性能看板的环境名；Sentry 查询环境仍使用 <see cref="Environment"/>。</summary>
    public string MetricEnvironment { get; set; } = "Production";

    public int LookbackHours { get; set; } = 24;

    public int SyncIntervalMinutes { get; set; } = 60;

    public int HttpTimeoutSeconds { get; set; } = 15;

    public int MaxResponseBodyBytes { get; set; } = 512 * 1024;
}
