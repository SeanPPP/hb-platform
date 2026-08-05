using System.Security.Claims;
using Hbpos.Api.Auth;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;

namespace Hbpos.Api.Services;

public interface IInstallmentRepaymentClaimIdentityResolver
{
    Task<InstallmentRepaymentClaimIdentity?> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public sealed class InstallmentRepaymentClaimIdentityResolver(
    ICashierAuthorizationTicketService ticketService,
    ICashierService cashierService) : IInstallmentRepaymentClaimIdentityResolver
{
    public async Task<InstallmentRepaymentClaimIdentity?> ResolveAsync(
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

        // 重新从服务端读取 session，CashierName 不从 body 或客户端缓存取得。
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

        return new InstallmentRepaymentClaimIdentity(
            storeCode,
            deviceCode,
            session.CashierId.Trim(),
            session.CashierName.Trim(),
            session.PermissionCodes,
            session.UserGuid.Trim());
    }
}
