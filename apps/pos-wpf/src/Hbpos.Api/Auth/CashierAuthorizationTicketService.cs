using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Hbpos.Api.Auth;

public sealed record CashierAuthorizationTicket(
    string CashierId,
    string UserGuid,
    string StoreCode,
    string DeviceCode,
    DateTimeOffset ExpiresAtUtc);

public interface ICashierAuthorizationTicketService
{
    (string Token, DateTimeOffset ExpiresAtUtc) Issue(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode);

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
        string deviceCode)
    {
        var expiresAtUtc = _timeProvider.GetUtcNow().AddHours(24);
        var ticket = new CashierAuthorizationTicket(
            cashierId,
            userGuid,
            storeCode,
            deviceCode,
            expiresAtUtc);
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
