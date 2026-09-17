namespace BlazorApp.Shared.DTOs;

public sealed class OperationAuditQueryDto
{
    public DateTimeOffset? FromUtc { get; set; }

    public DateTimeOffset? ToUtc { get; set; }

    public string? StoreCode { get; set; }

    public string? CashierKeyword { get; set; }

    public string? DeviceCode { get; set; }

    public string? DeviceSystem { get; set; }

    public string? OperationType { get; set; }

    public string? Outcome { get; set; }

    /// <summary>为 null 时不过滤；true/false 时只返回紧急覆盖标记等于该值的记录。</summary>
    public bool? IsEmergencyOverride { get; set; }

    /// <summary>为 null 时不过滤；true/false 时只返回离线缓存标记等于该值的记录。</summary>
    public bool? IsOfflineCached { get; set; }

    public string? ProductKeyword { get; set; }

    public string? OrderGuid { get; set; }

    public string? Keyword { get; set; }

    public string? SortBy { get; set; }

    public string? SortOrder { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

/// <summary>
/// 操作日志汇总计数。移动端用它作为“快捷过滤”入口，
/// 因此计数只按门店权限与基础筛选条件统计，不受 Outcome / 布尔标记过滤影响。
/// </summary>
public sealed class OperationAuditSummaryDto
{
    public int Total { get; set; }

    public int Succeeded { get; set; }

    public int Denied { get; set; }

    public int Failed { get; set; }

    public int EmergencyOverride { get; set; }

    public int OfflineCached { get; set; }
}

public class OperationAuditListItemDto
{
    public Guid EventId { get; set; }

    public int SchemaVersion { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public string OperationType { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? CashierId { get; set; }

    public string? UserGuid { get; set; }

    public string? CashierName { get; set; }

    public bool IsOfflineCached { get; set; }

    public bool IsEmergencyOverride { get; set; }

    public string StoreCode { get; set; } = string.Empty;

    public string DeviceCode { get; set; } = string.Empty;

    public string? DeviceSystem { get; set; }

    public string? AppVersion { get; set; }

    public string? InstanceId { get; set; }

    public string? OrderGuid { get; set; }

    public string? ReceiptNumber { get; set; }

    public string? CorrelationId { get; set; }

    public string? TraceId { get; set; }

    public string? PaymentMethod { get; set; }

    public string? ReasonCode { get; set; }

    public string? SafeMessage { get; set; }

    public string CurrencyCode { get; set; } = "AUD";

    public decimal? PaymentAmount { get; set; }

    public decimal? BeforeGross { get; set; }

    public decimal? AfterGross { get; set; }

    public decimal? BeforeDiscount { get; set; }

    public decimal? AfterDiscount { get; set; }

    public decimal? BeforeActual { get; set; }

    public decimal? AfterActual { get; set; }

    public decimal? AmountDelta { get; set; }

    public int ProductCount { get; set; }

    public string? PrimaryProduct { get; set; }
}

public sealed class OperationAuditDetailDto : OperationAuditListItemDto
{
    public string? PropertiesJson { get; set; }

    public List<OperationAuditDetailItemDto> Items { get; set; } = [];
}

public sealed class OperationAuditDetailItemDto
{
    public Guid EventId { get; set; }

    public int LineIndex { get; set; }

    public string? ProductCode { get; set; }

    public string? ItemNumber { get; set; }

    public string? ReferenceCode { get; set; }

    public string? LookupCode { get; set; }

    public string? DisplayName { get; set; }

    public string? LineKind { get; set; }

    public decimal? BeforeQuantity { get; set; }

    public decimal? AfterQuantity { get; set; }

    public decimal? QuantityDelta { get; set; }

    public decimal? BeforeUnitPrice { get; set; }

    public decimal? AfterUnitPrice { get; set; }

    public decimal? UnitPriceDelta { get; set; }

    public decimal? BeforeDiscountAmount { get; set; }

    public decimal? AfterDiscountAmount { get; set; }

    public decimal? DiscountAmountDelta { get; set; }

    public decimal? BeforeGrossAmount { get; set; }

    public decimal? AfterGrossAmount { get; set; }

    public decimal? GrossAmountDelta { get; set; }

    public decimal? BeforeActualAmount { get; set; }

    public decimal? AfterActualAmount { get; set; }

    public decimal? ActualAmountDelta { get; set; }
}
