using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb;

/// <summary>远程维护设备最新快照。secret 字段只供服务层读取，禁止映射到列表 DTO。</summary>
[SugarTable("HBweb_RemoteMaintenanceDevice")]
public sealed class RemoteMaintenanceDevice
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    [SugarColumn(IsNullable = false)]
    public int DeviceRegistrationId { get; set; }

    [SugarColumn(Length = 100, IsNullable = false)]
    public string HardwareId { get; set; } = string.Empty;

    [SugarColumn(Length = 50, IsNullable = false)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(Length = 100, IsNullable = false)]
    public string DeviceCode { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = false)]
    public string ComputerName { get; set; } = string.Empty;

    [SugarColumn(Length = 120, IsNullable = true)]
    public string? RustdeskId { get; set; }

    [SugarColumn(Length = 80, IsNullable = true)]
    public string? ClientVersion { get; set; }

    [SugarColumn(Length = 80, IsNullable = true)]
    public string? AgentVersion { get; set; }

    [SugarColumn(Length = 20, IsNullable = false)]
    public string ServiceStatus { get; set; } = "notInstalled";

    [SugarColumn(IsNullable = true)]
    public DateTime? LastSeenAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime RegisteredAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public long LastAcceptedSequence { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastAcceptedAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public Guid? LastOperationId { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? MonitorTokenHash { get; set; }

    [SugarColumn(Length = 2048, IsNullable = true)]
    public string? CredentialCiphertext { get; set; }

    [SugarColumn(Length = 8192, IsNullable = true)]
    public string? CommitResponseCiphertext { get; set; }

    [SugarColumn(IsNullable = false)]
    public bool IsDeleted { get; set; }
}
