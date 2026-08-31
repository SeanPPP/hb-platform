namespace BlazorApp.Shared.DTOs
{
    public class EasWebhookOptions
    {
        public string Secret { get; set; } = string.Empty;

        public string? AllowedAccountName { get; set; }

        public string? AllowedProjectName { get; set; }

        public Dictionary<string, string> ProjectAppKeys { get; set; } = [];

        /// <summary>
        /// 可选的 EAS project name 到 project UUID 权威映射；配置后发布预检必须精确匹配。
        /// </summary>
        public Dictionary<string, string> ProjectIds { get; set; } = [];

        /// <summary>
        /// 迁移窗口内临时允许最后一次 fixed-channel bootstrap；默认关闭，完成后无需改代码即可封口。
        /// </summary>
        public bool AllowLegacyOtaBootstrapRegistration { get; set; }

        /// <summary>
        /// 手持 POS 完成 legacy 回填后才可临时开启新 release-channel 发布预检；默认关闭。
        /// </summary>
        public bool PosHandheldReleaseChannelPublishingEnabled { get; set; }

        public string[] AcceptedProfiles { get; set; } =
            ["preview", "production", "android-internal"];
    }

    public class MobileAppBuildDto
    {
        public Guid Id { get; set; }

        public string AppKey { get; set; } = "mobile";

        public string EasBuildId { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public string? AppName { get; set; }

        public string Platform { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string BuildProfile { get; set; } = string.Empty;

        public string? Distribution { get; set; }

        public string? Channel { get; set; }

        public string? RuntimeVersion { get; set; }

        public string? AppVersion { get; set; }

        public string? AppBuildVersion { get; set; }

        public string ArtifactUrl { get; set; } = string.Empty;

        public string? OriginalArtifactUrl { get; set; }

        public string? CosArtifactUrl { get; set; }

        public string? CosObjectKey { get; set; }

        public string? ArtifactSha256 { get; set; }

        public long? ArtifactSize { get; set; }

        public DateTime? CosMirroredAt { get; set; }

        public string? CosMirrorError { get; set; }

        public string CosMirrorStatus { get; set; } = "pending";

        public int CosMirrorAttempts { get; set; }

        public DateTime? CosMirrorLastAttemptAtUtc { get; set; }

        public string? BuildDetailsPageUrl { get; set; }

        public string? GitCommitHash { get; set; }

        public string? GitCommitMessage { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime? ExpirationDate { get; set; }

        public DateTime ReceivedAt { get; set; }
    }

    public class MobileAppBuildPublicDto
    {
        public string EasBuildId { get; set; } = string.Empty;

        public string BuildProfile { get; set; } = string.Empty;

        public string? AppVersion { get; set; }

        public string? AppBuildVersion { get; set; }

        public string ArtifactUrl { get; set; } = string.Empty;

        public string? CosArtifactUrl { get; set; }

        public string? ArtifactSha256 { get; set; }

        public long? ArtifactSize { get; set; }
    }

    public class MobileAppBuildQueryDto
    {
        public string? AppKey { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? Profile { get; set; }
    }

    public class MobileAppBuildWebhookResultDto
    {
        public string Action { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public string? EasBuildId { get; set; }
    }

    public class MobileAppOtaUpdateDto
    {
        public Guid Id { get; set; }

        public string AppKey { get; set; } = "mobile";

        public string ProjectName { get; set; } = string.Empty;

        public string UpdateGroupId { get; set; } = string.Empty;

        public string? UpdateId { get; set; }

        public string? AndroidUpdateId { get; set; }

        public string Channel { get; set; } = string.Empty;

        public string? Branch { get; set; }

        public string Platform { get; set; } = string.Empty;

        public string? RuntimeVersion { get; set; }

        public string? Message { get; set; }

        public string? GitCommitHash { get; set; }

        public string? DashboardUrl { get; set; }

        public DateTime PublishedAt { get; set; }

        public bool IsRollback { get; set; }

        public string? RollbackOfGroupId { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    public class MobileAppOtaUpdateQueryDto
    {
        public string? AppKey { get; set; }

        public string? ProjectName { get; set; }

        public string? Platform { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? Channel { get; set; } = "production";

        public string? RuntimeVersion { get; set; }
    }

    public class MobileAppOtaUpdateUpsertDto
    {
        public string? ProjectName { get; set; }

        public string UpdateGroupId { get; set; } = string.Empty;

        public string? UpdateId { get; set; }

        public string? AndroidUpdateId { get; set; }

        public string? Channel { get; set; } = "production";

        public string? Branch { get; set; }

        public string? Platform { get; set; } = "android";

        public string? RuntimeVersion { get; set; }

        public string? Message { get; set; }

        public string? GitCommitHash { get; set; }

        public string? DashboardUrl { get; set; }

        public DateTime? PublishedAt { get; set; }

        public bool IsRollback { get; set; }

        public string? RollbackOfGroupId { get; set; }

        /// <summary>
        /// 仅供切换期最后一次 fixed-channel bootstrap；普通旧登记请求默认拒绝。
        /// </summary>
        public bool BootstrapLegacyFixedChannel { get; set; }
    }

    public class MobileAppOtaRollbackCommandDto
    {
        public string UpdateGroupId { get; set; } = string.Empty;

        public string? Platform { get; set; } = "android";

        public string? Message { get; set; }

        public string Command { get; set; } = string.Empty;
    }
}
