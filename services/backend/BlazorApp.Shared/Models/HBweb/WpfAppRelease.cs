using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// WPF 客户端安装包发布记录。
    /// </summary>
    [SugarTable("WpfAppRelease")]
    public class WpfAppRelease : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(Length = 80, IsNullable = false)]
        public string Channel { get; set; } = "production";

        [SugarColumn(Length = 80, IsNullable = false)]
        public string Version { get; set; } = string.Empty;

        [SugarColumn(Length = 260, IsNullable = false)]
        public string FileName { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public long FileSize { get; set; }

        [SugarColumn(Length = 128, IsNullable = false)]
        public string Sha256 { get; set; } = string.Empty;

        [SugarColumn(Length = 1000, IsNullable = false)]
        public string DownloadUrl { get; set; } = string.Empty;

        [SugarColumn(Length = 500, IsNullable = false)]
        public string CosObjectKey { get; set; } = string.Empty;

        [SugarColumn(Length = 40, IsNullable = false)]
        public string InstallerType { get; set; } = "exe";

        [SugarColumn(Length = 500, IsNullable = true)]
        public string? InstallerArguments { get; set; }

        [SugarColumn(Length = 2000, IsNullable = true)]
        public string? ReleaseNotes { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool IsActive { get; set; } = true;

        [SugarColumn(IsNullable = false)]
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// WPF 客户端按发布通道生效的更新策略。
    /// </summary>
    [SugarTable("WpfUpdatePolicy")]
    public class WpfUpdatePolicy : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(Length = 80, IsNullable = false)]
        public string Channel { get; set; } = "production";

        [SugarColumn(Length = 80, IsNullable = false)]
        public string TargetVersion { get; set; } = string.Empty;

        [SugarColumn(Length = 80, IsNullable = true)]
        public string? MinimumSupportedVersion { get; set; }

        [SugarColumn(IsNullable = false)]
        public bool ForceUpdate { get; set; }

        [SugarColumn(Length = 16, IsNullable = false)]
        public string TargetScope { get; set; } = "all";
    }

    /// <summary>
    /// WPF 更新策略的分店或设备定向目标。
    /// </summary>
    [SugarTable("WpfUpdatePolicyTarget")]
    public class WpfUpdatePolicyTarget : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(IsNullable = false)]
        public Guid PolicyId { get; set; }

        // 每条目标只能指定一个分店或一台设备；数据库 CHECK 约束同步保证二选一。
        [SugarColumn(Length = 100, IsNullable = true)]
        public string? StoreGuid { get; set; }

        [SugarColumn(IsNullable = true)]
        public int? DeviceRegistrationId { get; set; }
    }
}
