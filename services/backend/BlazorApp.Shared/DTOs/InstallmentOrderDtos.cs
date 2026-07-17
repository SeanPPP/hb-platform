using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 分期状态值必须与 WPF Hbpos.Contracts.Installments.InstallmentStatus 保持一致。
/// </summary>
public enum InstallmentOrderStatus
{
    Active = 1,
    PaidOff = 2,
    PickedUp = 3,
    Cancelled = 4,
}

public sealed class InstallmentOrderQueryParams
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? BranchCode { get; set; }
    public List<string>? StoreCodes { get; set; }
    public InstallmentOrderStatus? Status { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class InstallmentOrderSummaryDto
{
    public string InstallmentGuid { get; set; } = string.Empty;
    public string InstallmentNumber { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string? StoreName { get; set; }
    public string? ABN { get; set; }
    public string? BrandName { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    [JsonConverter(typeof(UtcDateTimeOffsetJsonConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    public decimal TotalAmount { get; set; }
    public decimal MinimumDownPayment { get; set; }
    public decimal DownPaymentAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public InstallmentOrderStatus Status { get; set; }

    [JsonConverter(typeof(UtcDateTimeOffsetJsonConverter))]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class InstallmentOrderDetailDto : InstallmentOrderSummaryDto
{
    public string DeviceCode { get; set; } = string.Empty;
    public string CashierId { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class InstallmentPickupInfoDto
{
    [JsonConverter(typeof(NullableUtcDateTimeOffsetJsonConverter))]
    public DateTimeOffset? PickedUpAt { get; set; }

    public string? PickedUpBy { get; set; }
    public string? PickupNote { get; set; }
}

public sealed class InstallmentCancellationInfoDto
{
    public int? CancellationKind { get; set; }

    [JsonConverter(typeof(NullableUtcDateTimeOffsetJsonConverter))]
    public DateTimeOffset? CancelledAt { get; set; }

    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
}

public sealed class InstallmentOrderLineDto
{
    public string InstallmentLineGuid { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string LookupCode { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ActualAmount { get; set; }
}

public sealed class InstallmentPaymentDto
{
    public string PaymentGuid { get; set; } = string.Empty;
    public int Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public int Status { get; set; }

    [JsonConverter(typeof(UtcDateTimeOffsetJsonConverter))]
    public DateTimeOffset RecordedAt { get; set; }

    public string CashierId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
}

public sealed class InstallmentOrderDetailResponse
{
    public InstallmentOrderDetailDto Order { get; set; } = new();
    public List<InstallmentOrderLineDto> Lines { get; set; } = new();
    public List<InstallmentPaymentDto> Payments { get; set; } = new();
    public InstallmentPickupInfoDto? PickupInfo { get; set; }
    public InstallmentCancellationInfoDto? CancellationInfo { get; set; }
}

/// <summary>
/// 将数据库无 Kind 的 UTC 时间统一输出为带 Z 的 JSON，避免移动端按本地时间二次偏移。
/// </summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.UtcDateTime);
}

public sealed class NullableUtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset? value,
        JsonSerializerOptions options
    )
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.UtcDateTime);
    }
}
