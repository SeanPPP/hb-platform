using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 仅用于分店进货单对外 DTO 的审计时间，确保 JSON 明确使用 UTC Z 后缀。
/// </summary>
public sealed class LocalSupplierInvoiceAuditUtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => reader.GetDateTime();

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(NormalizeToUtc(value));

    internal static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // SQL Server 读取出的审计时间没有 Kind，但该字段的存储契约是 UTC。
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
    }
}

public sealed class NullableLocalSupplierInvoiceAuditUtcDateTimeJsonConverter
    : JsonConverter<DateTime?>
{
    public override DateTime? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime();

    public override void Write(
        Utf8JsonWriter writer,
        DateTime? value,
        JsonSerializerOptions options
    )
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(
            LocalSupplierInvoiceAuditUtcDateTimeJsonConverter.NormalizeToUtc(value.Value)
        );
    }
}
