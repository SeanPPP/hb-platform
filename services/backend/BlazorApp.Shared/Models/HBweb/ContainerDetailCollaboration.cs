using SqlSugar;

namespace BlazorApp.Shared.Models;

/// <summary>货柜明细页面的短租约协作状态；同一用户多标签按会话记录。</summary>
[SugarTable("ContainerDetailEditLease")]
public sealed class ContainerDetailEditLease
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 64)]
    public string LeaseKey { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 64)]
    public string ContainerGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 64)]
    public string UserGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 128)]
    public string UserName { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 128)]
    public string ClientSessionId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 16)]
    public string State { get; set; } = "viewing";

    [SugarColumn(IsNullable = false)]
    public DateTime LastActiveAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>人工确认覆盖的追加式审计；数据库迁移会拒绝更新和删除。</summary>
[SugarTable("ContainerDetailFieldOverrideAudit")]
public sealed class ContainerDetailFieldOverrideAudit
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public Guid Id { get; set; }

    [SugarColumn(IsNullable = false, Length = 64)]
    public string ContainerGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 64)]
    public string DetailHguid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 64)]
    public string Field { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public string? ServerValue { get; set; }

    [SugarColumn(IsNullable = true)]
    public string? OverrideValue { get; set; }

    [SugarColumn(IsNullable = false, Length = 128)]
    public string ConfirmationToken { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 64)]
    public string ActorUserGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? ActorName { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime OccurredAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public Guid BatchGuid { get; set; }
}
