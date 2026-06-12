namespace BlazorApp.Api.Services.Logging
{
    public class ApplicationLoggingOptions
    {
        public bool Enabled { get; set; } = true;
        public string DefaultProjectCode { get; set; } = "HBBBackend";
        public string DefaultEnvironment { get; set; } = "Production";
        public string DefaultSourceType { get; set; } = "Backend";
        public string ServiceName { get; set; } = "HBBBackend.Api";
        public string? InstanceId { get; set; }
        public string MinimumLevel { get; set; } = "Warning";
        // 中心日志默认只保留 7 天，避免未显式配置的项目长期堆积日志。
        public int DefaultRetentionDays { get; set; } = 7;
        public int MaxBatchSize { get; set; } = 200;
        public int MaxIngestRequestsPerMinute { get; set; } = 120;
        public int MaxIngestLogsPerMinute { get; set; } = 5000;
        public int MaxMessageLength { get; set; } = 4000;
        public int MaxStackTraceLength { get; set; } = 12000;
        public int MaxPropertiesLength { get; set; } = 12000;
        public List<ApplicationLoggingProjectOptions> Projects { get; set; } = new();
    }

    public class ApplicationLoggingProjectOptions
    {
        public string ProjectCode { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string ApiKeyHash { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int? RetentionDays { get; set; }
    }
}
