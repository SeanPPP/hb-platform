using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hbpos.Contracts.HeldOrders;

/// <summary>
/// publish/prepare/recovery 的 versioned payload 序列化：
/// 读时按 JSON 内 version 分派 V1/V2，写时保持实际运行时类型。
/// 旧 V1 JSON 的 version=1 形状不变。
/// </summary>
public sealed class SharedSaleCartPayloadJsonConverter : JsonConverter<object>
{
    public override object? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out var versionElement) ||
            !versionElement.TryGetInt32(out var version))
        {
            throw new JsonException("Shared sale cart payload must contain a numeric version.");
        }

        var json = root.GetRawText();
        return version switch
        {
            SharedSaleCartV1Constants.PayloadVersion =>
                JsonSerializer.Deserialize<SharedSaleCartV1>(json, options)
                ?? throw new JsonException("Shared sale cart V1 payload is null."),
            SharedSaleCartV2Constants.PayloadVersion =>
                DeserializeV2(json, options),
            _ => throw new JsonException($"Unsupported shared sale cart payload version: {version}.")
        };
    }

    private static SharedSaleCartV2 DeserializeV2(string json, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(json);
        SharedSaleCartV2JsonContract.EnsureCatalogBasisPointsPresent(document.RootElement);
        return JsonSerializer.Deserialize<SharedSaleCartV2>(json, options)
            ?? throw new JsonException("Shared sale cart V2 payload is null.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        object value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
