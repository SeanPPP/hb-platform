using SqlSugar;

namespace BlazorApp.Shared.Models.POSM;

[SugarTable("pos_operation_audit"), Tenant("HBPOSM")]
public sealed class PosOperationAudit
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true, IsNullable = false)]
    public Guid EventId { get; set; }

    [SugarColumn(ColumnName = "schema_version", IsNullable = false)]
    public int SchemaVersion { get; set; }

    [SugarColumn(ColumnName = "occurred_at_utc", IsNullable = false)]
    public DateTime OccurredAtUtc { get; set; }

    [SugarColumn(ColumnName = "received_at_utc", IsNullable = false)]
    public DateTime ReceivedAtUtc { get; set; }

    [SugarColumn(ColumnName = "operation_type", Length = 64, IsNullable = false)]
    public string OperationType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "outcome", Length = 16, IsNullable = false)]
    public string Outcome { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "cashier_id", Length = 100, IsNullable = true)]
    public string? CashierId { get; set; }

    [SugarColumn(ColumnName = "user_guid", Length = 100, IsNullable = true)]
    public string? UserGuid { get; set; }

    [SugarColumn(ColumnName = "cashier_name", Length = 128, IsNullable = true)]
    public string? CashierName { get; set; }

    [SugarColumn(ColumnName = "is_offline_cached", IsNullable = false)]
    public bool IsOfflineCached { get; set; }

    [SugarColumn(ColumnName = "is_emergency_override", IsNullable = false)]
    public bool IsEmergencyOverride { get; set; }

    [SugarColumn(ColumnName = "store_code", Length = 50, IsNullable = false)]
    public string StoreCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "device_code", Length = 64, IsNullable = false)]
    public string DeviceCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "app_version", Length = 32, IsNullable = true)]
    public string? AppVersion { get; set; }

    [SugarColumn(ColumnName = "instance_id", Length = 64, IsNullable = true)]
    public string? InstanceId { get; set; }

    [SugarColumn(ColumnName = "order_guid", Length = 100, IsNullable = true)]
    public string? OrderGuid { get; set; }

    [SugarColumn(ColumnName = "receipt_number", Length = 100, IsNullable = true)]
    public string? ReceiptNumber { get; set; }

    [SugarColumn(ColumnName = "correlation_id", Length = 100, IsNullable = true)]
    public string? CorrelationId { get; set; }

    [SugarColumn(ColumnName = "trace_id", Length = 100, IsNullable = true)]
    public string? TraceId { get; set; }

    [SugarColumn(ColumnName = "payment_method", Length = 32, IsNullable = true)]
    public string? PaymentMethod { get; set; }

    [SugarColumn(ColumnName = "reason_code", Length = 64, IsNullable = true)]
    public string? ReasonCode { get; set; }

    [SugarColumn(ColumnName = "safe_message", Length = 1000, IsNullable = true)]
    public string? SafeMessage { get; set; }

    [SugarColumn(ColumnName = "currency_code", Length = 3, IsNullable = false)]
    public string CurrencyCode { get; set; } = "AUD";

    [SugarColumn(ColumnName = "payment_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? PaymentAmount { get; set; }

    [SugarColumn(ColumnName = "before_gross", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeGross { get; set; }

    [SugarColumn(ColumnName = "after_gross", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterGross { get; set; }

    [SugarColumn(ColumnName = "before_discount", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeDiscount { get; set; }

    [SugarColumn(ColumnName = "after_discount", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterDiscount { get; set; }

    [SugarColumn(ColumnName = "before_actual", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeActual { get; set; }

    [SugarColumn(ColumnName = "after_actual", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterActual { get; set; }

    [SugarColumn(ColumnName = "amount_delta", DecimalDigits = 2, IsNullable = true)]
    public decimal? AmountDelta { get; set; }

    [SugarColumn(ColumnName = "product_count", IsNullable = false)]
    public int ProductCount { get; set; }

    [SugarColumn(ColumnName = "primary_product", Length = 255, IsNullable = true)]
    public string? PrimaryProduct { get; set; }

    [SugarColumn(ColumnName = "properties_json", Length = 4000, IsNullable = true)]
    public string? PropertiesJson { get; set; }
}

[SugarTable("pos_operation_audit_item"), Tenant("HBPOSM")]
public sealed class PosOperationAuditItem
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true, IsNullable = false)]
    public Guid EventId { get; set; }

    [SugarColumn(ColumnName = "line_index", IsPrimaryKey = true, IsNullable = false)]
    public int LineIndex { get; set; }

    [SugarColumn(ColumnName = "product_code", Length = 100, IsNullable = true)]
    public string? ProductCode { get; set; }

    [SugarColumn(ColumnName = "item_number", Length = 100, IsNullable = true)]
    public string? ItemNumber { get; set; }

    [SugarColumn(ColumnName = "reference_code", Length = 100, IsNullable = true)]
    public string? ReferenceCode { get; set; }

    [SugarColumn(ColumnName = "lookup_code", Length = 100, IsNullable = true)]
    public string? LookupCode { get; set; }

    [SugarColumn(ColumnName = "display_name", Length = 255, IsNullable = true)]
    public string? DisplayName { get; set; }

    [SugarColumn(ColumnName = "line_kind", Length = 32, IsNullable = true)]
    public string? LineKind { get; set; }

    [SugarColumn(ColumnName = "before_quantity", DecimalDigits = 3, IsNullable = true)]
    public decimal? BeforeQuantity { get; set; }

    [SugarColumn(ColumnName = "after_quantity", DecimalDigits = 3, IsNullable = true)]
    public decimal? AfterQuantity { get; set; }

    [SugarColumn(ColumnName = "quantity_delta", DecimalDigits = 3, IsNullable = true)]
    public decimal? QuantityDelta { get; set; }

    [SugarColumn(ColumnName = "before_unit_price", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeUnitPrice { get; set; }

    [SugarColumn(ColumnName = "after_unit_price", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterUnitPrice { get; set; }

    [SugarColumn(ColumnName = "unit_price_delta", DecimalDigits = 2, IsNullable = true)]
    public decimal? UnitPriceDelta { get; set; }

    [SugarColumn(ColumnName = "before_discount_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeDiscountAmount { get; set; }

    [SugarColumn(ColumnName = "after_discount_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterDiscountAmount { get; set; }

    [SugarColumn(ColumnName = "discount_amount_delta", DecimalDigits = 2, IsNullable = true)]
    public decimal? DiscountAmountDelta { get; set; }

    [SugarColumn(ColumnName = "before_gross_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeGrossAmount { get; set; }

    [SugarColumn(ColumnName = "after_gross_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterGrossAmount { get; set; }

    [SugarColumn(ColumnName = "gross_amount_delta", DecimalDigits = 2, IsNullable = true)]
    public decimal? GrossAmountDelta { get; set; }

    [SugarColumn(ColumnName = "before_actual_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? BeforeActualAmount { get; set; }

    [SugarColumn(ColumnName = "after_actual_amount", DecimalDigits = 2, IsNullable = true)]
    public decimal? AfterActualAmount { get; set; }

    [SugarColumn(ColumnName = "actual_amount_delta", DecimalDigits = 2, IsNullable = true)]
    public decimal? ActualAmountDelta { get; set; }
}
