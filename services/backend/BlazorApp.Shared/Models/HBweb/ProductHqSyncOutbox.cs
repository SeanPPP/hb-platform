using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

/// <summary>
/// 商品变更推送 HQ 的持久化 outbox。业务写入与本记录必须使用同一个本地数据库事务。
/// </summary>
[SugarTable("ProductHqSyncOutbox")]
public sealed class ProductHqSyncOutbox : BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [SugarColumn(Length = 200, IsNullable = false)]
    public string OperationKey { get; set; } = string.Empty;

    [SugarColumn(Length = 80, IsNullable = false)]
    public string OperationKind { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = false)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(Length = 600, IsNullable = false)]
    public string ScopeKey { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public string TargetStoreCodesJson { get; set; } = "null";

    [SugarColumn(IsNullable = false)]
    public string AuthorizedStoreCodesJson { get; set; } = "null";

    [SugarColumn(IsNullable = false)]
    public string FieldMaskJson { get; set; } = "[]";

    [SugarColumn(IsNullable = false)]
    public string PayloadJson { get; set; } = "{}";

    [SugarColumn(IsNullable = false)]
    public string TombstonesJson { get; set; } = "[]";

    [SugarColumn(Length = 100, IsNullable = false)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(Length = 80, IsNullable = true)]
    public string? RequestedByUserGuid { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? RequestedByDeviceId { get; set; }

    [SugarColumn(Length = 30, IsNullable = false)]
    public string Status { get; set; } = ProductHqSyncOutboxStatuses.Pending;

    [SugarColumn(IsNullable = false)]
    public DateTime OccurredAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public int AttemptCount { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime NextAttemptAtUtc { get; set; }

    [SugarColumn(Length = 200, IsNullable = true)]
    public string? LeaseOwner { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? LeaseToken { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LeaseExpiresAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastAttemptAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? CompletedAtUtc { get; set; }

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? LastErrorCode { get; set; }

    [SugarColumn(Length = 500, IsNullable = true)]
    public string? LastErrorMessage { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? SupersededById { get; set; }
}
