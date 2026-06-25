using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// Expo EAS OTA 更新发布记录，和 APK 构建产物记录分表存储。
    /// </summary>
    [SugarTable("MobileAppOtaUpdate")]
    public class MobileAppOtaUpdate : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(Length = 120, IsNullable = false)]
        public string UpdateGroupId { get; set; } = string.Empty;

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? AndroidUpdateId { get; set; }

        [SugarColumn(Length = 120, IsNullable = false)]
        public string Channel { get; set; } = "production";

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? Branch { get; set; }

        [SugarColumn(Length = 30, IsNullable = false)]
        public string Platform { get; set; } = "android";

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? RuntimeVersion { get; set; }

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? Message { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? GitCommitHash { get; set; }

        [SugarColumn(Length = 1000, IsNullable = true)]
        public string? DashboardUrl { get; set; }

        [SugarColumn(IsNullable = false)]
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

        [SugarColumn(IsNullable = false)]
        public bool IsRollback { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? RollbackOfGroupId { get; set; }
    }
}
