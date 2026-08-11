using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;

namespace Hbpos.Api.Services;

public interface ISharedHeldOrderIdentityResolver
{
    Task<SharedHeldOrderIdentity?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

/// <summary>
/// 权威 store/device 来自设备 claims；cashier 信息只信任已验票票据并刷新后的服务端 session，
/// 不信任客户端 body 快照。
/// </summary>
public sealed class SharedHeldOrderIdentityResolver(
    ICashierAuthorizationTicketService ticketService,
    ICashierService cashierService) : ISharedHeldOrderIdentityResolver
{
    public async Task<SharedHeldOrderIdentity?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var storeCode = httpContext.User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim)?.Trim();
        var deviceCode = httpContext.User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim)?.Trim();
        if (string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(deviceCode))
        {
            return null;
        }

        var token = httpContext.Request.Headers[CashierAuthorizationConstants.HeaderName].ToString();
        var ticket = ticketService.Validate(token);
        if (ticket is null ||
            !string.Equals(ticket.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ticket.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var session = await cashierService.RefreshSessionAsync(ticket, cancellationToken);
        if (session is null ||
            !string.Equals(session.CashierId, ticket.CashierId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(session.UserGuid, ticket.UserGuid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(session.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(session.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(session.CashierName) ||
            string.IsNullOrWhiteSpace(session.UserGuid) ||
            session.PermissionCodes is null)
        {
            return null;
        }

        return new SharedHeldOrderIdentity(
            storeCode,
            deviceCode,
            session.CashierId.Trim(),
            session.CashierName.Trim(),
            session.PermissionCodes,
            session.UserGuid.Trim());
    }
}
