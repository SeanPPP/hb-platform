namespace Hbpos.Contracts.OperationAudits;

/// <summary>
/// iPad POS 操作审计只读列表。门店和终端范围不属于请求合同，必须由服务端认证 claims 决定。
/// </summary>
public sealed class OperationAuditReadListDto
{
    public List<OperationAuditReadRecordDto> Items { get; set; } = [];
}

/// <summary>
/// 面向门店终端的安全审计投影。刻意不包含 propertiesJson、授权材料和支付提供方引用。
/// </summary>
public sealed class OperationAuditReadRecordDto
{
    public Guid EventId { get; set; }

    public string OccurredAtIso { get; set; } = string.Empty;

    public string OperationType { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? CashierName { get; set; }

    public string StoreCode { get; set; } = string.Empty;

    public string DeviceCode { get; set; } = string.Empty;

    public string? OrderGuid { get; set; }

    public string? ReceiptNumber { get; set; }

    public string? CorrelationId { get; set; }

    public string? SafeMessage { get; set; }

    public long? PaymentAmountCents { get; set; }

    public int ProductCount { get; set; }

    public string? PrimaryProduct { get; set; }

    public string UploadState { get; set; } = "uploaded";

    public List<OperationAuditReadItemDto> Items { get; set; } = [];
}

public sealed class OperationAuditReadItemDto
{
    public int LineIndex { get; set; }

    public string? ProductCode { get; set; }

    public string? DisplayName { get; set; }

    public string? QuantityDelta { get; set; }

    public long? ActualAmountDeltaCents { get; set; }
}
