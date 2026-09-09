using Hbpos.Api.Auth;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Api.Services;

public interface IAttendanceFaceActorResolver
{
    Task<AttendanceFaceActor?> ResolveAsync(HttpContext context, AttendanceFaceDeviceScope device,
        string permission, CancellationToken cancellationToken);
}

/// <summary>人脸资料管理始终验票并实时查权限，不能进入收银 Audit/紧急登录放行分支。</summary>
public sealed class AttendanceFaceActorResolver(
    ICashierAuthorizationTicketService tickets, ICashierService cashiers,
    TimeProvider? timeProvider = null) : IAttendanceFaceActorResolver
{
    public async Task<AttendanceFaceActor?> ResolveAsync(HttpContext context, AttendanceFaceDeviceScope device,
        string permission, CancellationToken cancellationToken)
    {
        var ticket = tickets.Validate(context.Request.Headers[CashierAuthorizationConstants.HeaderName].ToString());
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        if (ticket is null || string.IsNullOrWhiteSpace(ticket.UserGuid)
            || !string.Equals(ticket.StoreCode, device.StoreCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ticket.DeviceCode, device.DeviceCode, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(ticket.HardwareId)
            || !string.Equals(ticket.HardwareId, device.HardwareId, StringComparison.Ordinal)
            || ticket.BarcodeAuthenticatedAtUtc is not { } authenticated
            || authenticated < now.AddMinutes(-2) || authenticated > now.AddSeconds(30)) return null;

        return await cashiers.HasAnyPermissionAsync(ticket.UserGuid, device.StoreCode, [permission], cancellationToken)
            ? new AttendanceFaceActor(ticket.UserGuid, authenticated)
            : null;
    }
}
