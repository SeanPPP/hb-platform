using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

/// <summary>
/// Expo OTA 不可变发布事实。登记和策略激活严格分离，业务代码不得更新既有行。
/// </summary>
[SugarTable("AppOtaRelease")]
public sealed class AppOtaRelease : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(IsNullable = false)]
    public Guid ReleaseBatchId { get; set; }

    [SugarColumn(Length = 80, IsNullable = false)]
    public string AppKey { get; set; } = string.Empty;

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string ClientChannel { get; set; } = string.Empty;

    [SugarColumn(Length = 160, IsNullable = false)]
    public string ReleaseChannel { get; set; } = string.Empty;

    [SugarColumn(Length = 160, IsNullable = false)]
    public string EasBranch { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string ProjectName { get; set; } = string.Empty;

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Platform { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string RuntimeVersion { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string UpdateGroupId { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string UpdateId { get; set; } = string.Empty;

    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? Message { get; set; }

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? GitCommitHash { get; set; }

    [SugarColumn(Length = 2048, IsNullable = true)]
    public string? DashboardUrl { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime PublishedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsRollback { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? RollbackOfReleaseId { get; set; }

    [SugarColumn(Length = 64, IsNullable = false)]
    public string FactFingerprint { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public bool Legacy { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? RegistrationSource { get; set; }
}

/// <summary>
/// Mobile OTA 单条环境/平台 lane 的当前策略。
/// </summary>
[SugarTable("MobileOtaPolicy")]
public sealed class MobileOtaPolicy : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Platform { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public bool Enabled { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool Required { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? TargetReleaseId { get; set; }

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? TargetRuntimeVersion { get; set; }

    [SugarColumn(Length = 1000, IsNullable = true)]
    public string? ReleaseMessage { get; set; }

    [SugarColumn(IsNullable = false)]
    public long PolicyVersion { get; set; }
}

/// <summary>
/// Mobile OTA 策略追加式完整快照。数据库迁移同时建立禁止更新和删除的触发器。
/// </summary>
[SugarTable("MobileOtaPolicyRevision")]
public sealed class MobileOtaPolicyRevision : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(IsNullable = false)]
    public Guid PolicyId { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Environment { get; set; } = string.Empty;

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Platform { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public long PolicyVersion { get; set; }

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Operation { get; set; } = "save";

    [SugarColumn(Length = 8000, IsNullable = false)]
    public string SnapshotJson { get; set; } = string.Empty;
}
