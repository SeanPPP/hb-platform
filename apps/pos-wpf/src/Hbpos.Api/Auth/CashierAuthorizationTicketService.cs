using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api.Auth;

public sealed record CashierAuthorizationTicket(
    string CashierId,
    string UserGuid,
    string StoreCode,
    string DeviceCode,
    DateTimeOffset ExpiresAtUtc,
    string? HardwareId = null)
{
    // 旧票据没有该字段；它们仍可用于原接口，但不能通过要求重新扫码的高危操作。
    public DateTimeOffset? IssuedAtUtc { get; init; }

    // 刷新会话必须继承原始扫码时间；缺失时只能用于普通接口，不能授权高危操作。
    public DateTimeOffset? BarcodeAuthenticatedAtUtc { get; init; }
}

public interface ICashierAuthorizationTicketService
{
    (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode);

    (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        string? hardwareId)
    {
        if (!string.IsNullOrWhiteSpace(hardwareId))
        {
            throw new NotSupportedException("Hardware-bound cashier tickets require an explicit implementation.");
        }

        return Issue(cashierId, userGuid, storeCode, deviceCode);
    }

    (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        string? hardwareId,
        DateTimeOffset? barcodeAuthenticatedAtUtc) =>
        throw new NotSupportedException("Cashier ticket provenance refresh requires an explicit implementation.");

    CashierAuthorizationTicket? Validate(string? token);
}

public sealed class CashierAuthorizationTicketService : ICashierAuthorizationTicketService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public CashierAuthorizationTicketService(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider? timeProvider = null)
    {
        _protector = dataProtectionProvider.CreateProtector("Hbpos.CashierAuthorization.v1");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode) => IssueFresh(cashierId, userGuid, storeCode, deviceCode, null);

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        string? hardwareId) => IssueFresh(cashierId, userGuid, storeCode, deviceCode, hardwareId);

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        string? hardwareId,
        DateTimeOffset? barcodeAuthenticatedAtUtc)
    {
        var issuedAtUtc = _timeProvider.GetUtcNow();
        return IssueCore(
            cashierId,
            userGuid,
            storeCode,
            deviceCode,
            hardwareId,
            issuedAtUtc,
            barcodeAuthenticatedAtUtc);
    }

    private (string Token, DateTimeOffset ExpiresAtUtc) IssueFresh(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        string? hardwareId)
    {
        var issuedAtUtc = _timeProvider.GetUtcNow();
        return IssueCore(
            cashierId,
            userGuid,
            storeCode,
            deviceCode,
            hardwareId,
            issuedAtUtc,
            issuedAtUtc);
    }

    private (string Token, DateTimeOffset ExpiresAtUtc) IssueCore(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        string? hardwareId,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset? barcodeAuthenticatedAtUtc)
    {
        var expiresAtUtc = issuedAtUtc.AddHours(24);
        var ticket = new CashierAuthorizationTicket(
            cashierId,
            userGuid,
            storeCode,
            deviceCode,
            expiresAtUtc,
            string.IsNullOrWhiteSpace(hardwareId) ? null : hardwareId.Trim())
        {
            IssuedAtUtc = issuedAtUtc,
            BarcodeAuthenticatedAtUtc = barcodeAuthenticatedAtUtc
        };
        return (_protector.Protect(JsonSerializer.Serialize(ticket, JsonOptions)), expiresAtUtc);
    }

    public CashierAuthorizationTicket? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var ticket = JsonSerializer.Deserialize<CashierAuthorizationTicket>(
                _protector.Unprotect(token),
                JsonOptions);
            return ticket is not null && ticket.ExpiresAtUtc > _timeProvider.GetUtcNow()
                ? ticket
                : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
