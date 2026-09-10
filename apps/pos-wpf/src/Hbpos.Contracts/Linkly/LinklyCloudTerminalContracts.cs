namespace Hbpos.Contracts.Linkly;

/// <summary>
/// POS 可见的刷卡机摘要。账号、密码、Secret 与 PosId 永不进入该合同。
/// </summary>
public sealed record LinklyCloudTerminalSummary(
    Guid TerminalId,
    int LaneNo,
    string DisplayName,
    string PairingState,
    bool IsBusy,
    bool IsReady,
    string? LastHealthStatus,
    DateTimeOffset? LastHealthAt,
    string? AssignedDeviceCode = null,
    long AssignmentRevision = 0,
    string? TerminalVersion = null);

public sealed record LinklyCloudTerminalListResponse(
    string Environment,
    Guid? SelectedTerminalId,
    long? SelectionRevision,
    IReadOnlyList<LinklyCloudTerminalSummary> Terminals,
    string Mode = "Legacy",
    IReadOnlyList<LinklyCloudAssignableDevice>? Devices = null);

/// <summary>同店设备的公开分配状态；历史失效设备可展示，但不能成为新的绑定目标。</summary>
public sealed record LinklyCloudAssignableDevice(
    string DeviceCode,
    string DeviceSystem,
    bool IsAvailable,
    Guid? SelectedTerminalId,
    long SelectionRevision);

/// <summary>客户端原样回传终端版本，避免 JavaScript 日期转换丢失 SQL 时间戳精度。</summary>
public sealed record LinklyCloudTerminalConnectionTestRequest(
    string Environment,
    string ExpectedTerminalVersion,
    string? ExpectedAssignedDeviceCode,
    long ExpectedAssignmentRevision);

public sealed record LinklyCloudTerminalConnectionTestResponse(
    Guid TerminalId,
    string Environment,
    string TerminalVersion,
    string? AssignedDeviceCode,
    long AssignmentRevision,
    bool Succeeded,
    string Status,
    DateTimeOffset CheckedAt,
    string Message,
    string? ResponseCode = null);

/// <summary>目标为空时只解绑；其余情况在同一事务内释放旧归属并替换目标设备的线路。</summary>
public sealed record LinklyCloudTerminalAssignmentRequest(
    string Environment,
    string ExpectedTerminalVersion,
    string? ExpectedAssignedDeviceCode,
    long ExpectedAssignmentRevision,
    string? TargetDeviceCode,
    Guid? ExpectedTargetTerminalId,
    long ExpectedTargetSelectionRevision);

public sealed record LinklyCloudTerminalSelectionRequest(
    string Environment,
    Guid TerminalId,
    long? ExpectedRevision);

public sealed record LinklyCloudTerminalSelectionResponse(
    string Environment,
    Guid TerminalId,
    long Revision);

public sealed record LinklyCloudTerminalPairResponse(
    Guid TerminalId,
    string Environment,
    string DisplayName,
    string PairingState,
    bool IsReady,
    string Message);
