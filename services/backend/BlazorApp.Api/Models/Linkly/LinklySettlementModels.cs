using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Models.Linkly;

[SugarTable("POSM_LinklySettlement")]
internal sealed class PosmLinklySettlement
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    public Guid SettlementGuid { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public string ConnectionMode { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSubmissionState { get; set; }
    public string? ProviderSessionId { get; set; }
    public long? CloudBackendSessionId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseText { get; set; }
    public string? SettlementData { get; set; }
    public string? ReceiptTextsJson { get; set; }
    public int PrintCount { get; set; }
    public DateTime? FirstPrintedAtUtc { get; set; }
    public DateTime? LastPrintedAtUtc { get; set; }
    public string? LastPrintError { get; set; }
    public long ClientRevision { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal enum LinklySettlementAmountParseStatus
{
    Parsed,
    Missing,
    Unsupported,
    Invalid,
}

internal sealed class LinklySettlementAmountParseResult
{
    public LinklySettlementAmountParseStatus Status { get; init; }
    public LinklySettlementAmountDto? Summary { get; init; }
    public IReadOnlyList<LinklySettlementCardTotalDto> CardTotals { get; init; } = [];

    public static LinklySettlementAmountParseResult Missing { get; } = new()
    {
        Status = LinklySettlementAmountParseStatus.Missing,
    };

    public static LinklySettlementAmountParseResult Unsupported { get; } = new()
    {
        Status = LinklySettlementAmountParseStatus.Unsupported,
    };

    public static LinklySettlementAmountParseResult Invalid { get; } = new()
    {
        Status = LinklySettlementAmountParseStatus.Invalid,
    };
}

internal sealed class LinklySettlementExportRow
{
    public required LinklySettlementListItemDto Item { get; init; }
    public string? ProviderSessionId { get; init; }
    public string? CloudBackendSessionId { get; init; }
    public required string ClientRevision { get; init; }
}

internal sealed class LinklySettlementExportSnapshot
{
    public long Id { get; init; }
    public long ClientRevision { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class LinklySettlementExportResult
{
    public required byte[] Content { get; init; }
    public required string FileName { get; init; }
    public string ContentType { get; init; } =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed class LinklySettlementRequestException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class LinklySettlementExportChangedException()
    : Exception("结算记录在导出期间发生变化，请重新导出。")
{
    public string Code { get; } = "EXPORT_SNAPSHOT_CHANGED";
}
