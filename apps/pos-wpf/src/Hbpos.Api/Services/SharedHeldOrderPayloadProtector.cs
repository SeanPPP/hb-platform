using System.Text.Json;
using Hbpos.Contracts.HeldOrders;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api.Services;

public interface ISharedHeldOrderPayloadProtector
{
    string Protect(SharedSaleCartV1 payload);

    SharedSaleCartV1 Unprotect(string ciphertext);
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

    public string Protect(SharedSaleCartV1 payload) =>
        _protector.Protect(JsonSerializer.Serialize(payload));

    public SharedSaleCartV1 Unprotect(string ciphertext) =>
        JsonSerializer.Deserialize<SharedSaleCartV1>(_protector.Unprotect(ciphertext))
        ?? throw new InvalidOperationException("Shared held order payload failed to decrypt.");
}
