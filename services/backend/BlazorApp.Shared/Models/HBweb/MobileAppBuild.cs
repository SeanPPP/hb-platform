using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// Expo EAS Android APK 构建产物记录。
    /// </summary>
    [SugarTable("MobileAppBuild")]
    public class MobileAppBuild : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(Length = 120, IsNullable = false)]
        public string EasBuildId { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = false)]
        public string AccountName { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = false)]
        public string ProjectName { get; set; } = string.Empty;

        [SugarColumn(Length = 160, IsNullable = true)]
        public string? AppName { get; set; }

        [SugarColumn(Length = 30, IsNullable = false)]
        public string Platform { get; set; } = string.Empty;

        [SugarColumn(Length = 40, IsNullable = false)]
        public string Status { get; set; } = string.Empty;

        [SugarColumn(Length = 80, IsNullable = false)]
        public string BuildProfile { get; set; } = string.Empty;

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? Distribution { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? Channel { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? RuntimeVersion { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? AppVersion { get; set; }

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? AppBuildVersion { get; set; }

        [SugarColumn(Length = 1000, IsNullable = false)]
        public string ArtifactUrl { get; set; } = string.Empty;

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? BuildDetailsPageUrl { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? GitCommitHash { get; set; }

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? GitCommitMessage { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? CompletedAt { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? ExpirationDate { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    }
}
