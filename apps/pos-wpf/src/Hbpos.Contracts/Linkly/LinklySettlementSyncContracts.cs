using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hbpos.Contracts.Linkly;

[JsonConverter(typeof(ProviderSubmissionStateJsonConverter))]
public enum ProviderSubmissionState
{
    NotSubmitted,
    Submitted,
    Unknown
}

/// <summary>
/// 强制状态契约以字符串传输，拒绝数值枚举，避免不同客户端对枚举值漂移产生歧义。
/// </summary>
public sealed class ProviderSubmissionStateJsonConverter : JsonConverter<ProviderSubmissionState>
{
    public override ProviderSubmissionState Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("providerSubmissionState 必须是受支持的字符串枚举值。");
        }

        return reader.GetString() switch
        {
            nameof(ProviderSubmissionState.NotSubmitted) => ProviderSubmissionState.NotSubmitted,
            nameof(ProviderSubmissionState.Submitted) => ProviderSubmissionState.Submitted,
            nameof(ProviderSubmissionState.Unknown) => ProviderSubmissionState.Unknown,
            _ => throw new JsonException("providerSubmissionState 必须是受支持的字符串枚举值。")
        };
    }

    public override void Write(Utf8JsonWriter writer, ProviderSubmissionState value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ProviderSubmissionState.NotSubmitted => nameof(ProviderSubmissionState.NotSubmitted),
            ProviderSubmissionState.Submitted => nameof(ProviderSubmissionState.Submitted),
            ProviderSubmissionState.Unknown => nameof(ProviderSubmissionState.Unknown),
            _ => throw new JsonException("providerSubmissionState 必须是受支持的字符串枚举值。")
        });
    }
}

public sealed record LinklySettlementSyncRequest(
    int SchemaVersion,
    Guid SettlementGuid,
    string StoreCode,
    string DeviceCode,
    DateOnly BusinessDate,
    string ConnectionMode,
    string Environment,
    string? ProviderSessionId,
    string Status,
    string? ResponseCode,
    string? ResponseText,
    string? SettlementData,
    IReadOnlyList<string> ReceiptTexts,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FirstPrintedAt,
    DateTimeOffset? LastPrintedAt,
    int PrintCount,
    string? LastPrintError,
    long ClientRevision,
    ProviderSubmissionState? ProviderSubmissionState = null);

public sealed record LinklySettlementSyncResponse(
    bool Accepted,
    bool AlreadySynced,
    long AcceptedRevision);
