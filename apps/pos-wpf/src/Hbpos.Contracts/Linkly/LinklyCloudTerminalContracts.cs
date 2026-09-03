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
    DateTimeOffset? LastHealthAt);

public sealed record LinklyCloudTerminalListResponse(
    string Environment,
    Guid? SelectedTerminalId,
    long? SelectionRevision,
    IReadOnlyList<LinklyCloudTerminalSummary> Terminals,
    string Mode = "Legacy");

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
