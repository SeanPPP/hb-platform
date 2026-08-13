using System.Text.Json;
using Hbpos.Contracts.HeldOrders;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api.Services;

public interface ISharedHeldOrderPayloadProtector
{
    string Protect(object payload);

    object Unprotect(string ciphertext);

    object Unprotect(string ciphertext, int payloadVersion);
}

/// <summary>
/// 专用 DataProtection purpose：Hbpos.SharedHeldOrders.Payload.v1。
/// 数据库只保存 ciphertext，日志/列表 DTO 一律不接触明文 payload。
/// </summary>
public sealed class SharedHeldOrderPayloadProtector(
    IDataProtectionProvider dataProtectionProvider) : ISharedHeldOrderPayloadProtector
{
    public const string Purpose = "Hbpos.SharedHeldOrders.Payload.v1";
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(object payload) =>
        _protector.Protect(JsonSerializer.Serialize(payload, payload.GetType()));

    public object Unprotect(string ciphertext)
    {
        var json = _protector.Unprotect(ciphertext);
        using var document = JsonDocument.Parse(json);
        var version = ReadVersion(document.RootElement);
        return DeserializeVersioned(json, version);
    }

    public object Unprotect(string ciphertext, int payloadVersion) =>
        DeserializeVersioned(_protector.Unprotect(ciphertext), payloadVersion);

    private static object DeserializeVersioned(string json, int payloadVersion) =>
        payloadVersion switch
        {
            SharedSaleCartV1Constants.PayloadVersion =>
                JsonSerializer.Deserialize<SharedSaleCartV1>(json)
                ?? throw new InvalidOperationException("Shared held order V1 payload failed to decrypt."),
            SharedSaleCartV2Constants.PayloadVersion => DeserializeV2(json),
            _ => throw new InvalidOperationException(
                $"Unsupported shared held order payload version: {payloadVersion}.")
        };

    private static SharedSaleCartV2 DeserializeV2(string json)
    {
        using var document = JsonDocument.Parse(json);
        SharedSaleCartV2JsonContract.EnsureCatalogBasisPointsPresent(document.RootElement);
        return JsonSerializer.Deserialize<SharedSaleCartV2>(json)
            ?? throw new InvalidOperationException("Shared held order V2 payload failed to decrypt.");
    }

    private static int ReadVersion(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out var version))
                {
                    return version;
                }
            }
        }

        throw new InvalidOperationException("Shared held order payload version is missing.");
    }
}
