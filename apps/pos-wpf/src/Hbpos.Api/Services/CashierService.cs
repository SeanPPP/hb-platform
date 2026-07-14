using BlazorApp.Shared.Models;
using BlazorApp.Shared.Constants;
using Hbpos.Api.Auth;
using Hbpos.Api.Data;
using Hbpos.Contracts.Cashiers;

namespace Hbpos.Api.Services;

public interface ICashierService
{
    Task<CashierSessionDto?> BarcodeLoginAsync(
        CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken);

    Task<bool> HasAnyPermissionAsync(
        string userGuid,
        string storeCode,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken);
}

public sealed class CashierService(
    HbposSqlSugarContext dbContext,
    ICashierAuthorizationTicketService ticketService) : ICashierService
{
    public async Task<CashierSessionDto?> BarcodeLoginAsync(
        CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken)
    {
        var userBarcode = request.UserBarcode?.Trim();
        var storeCode = request.StoreCode?.Trim();
        var deviceCode = request.DeviceCode?.Trim();
        if (string.IsNullOrWhiteSpace(userBarcode) ||
            string.IsNullOrWhiteSpace(storeCode) ||
            string.IsNullOrWhiteSpace(deviceCode))
        {
            return null;
        }

        var cashier = await dbContext.MainDb.Queryable<CashRegisterUser>()
            .FirstAsync(
                x => x.UserBarcode == userBarcode && x.Status,
                cancellationToken);

        var cashierUserGuid = cashier?.UserGUID?.Trim();
        if (cashier is null || string.IsNullOrWhiteSpace(cashierUserGuid))
        {
            return null;
        }

        var user = await dbContext.MainDb.Queryable<User>()
            .FirstAsync(
                x => x.UserGUID == cashierUserGuid && x.IsActive && !x.IsDeleted,
                cancellationToken);
        if (user is null)
        {
            return null;
        }

        var allowedStoreCodes = await dbContext.MainDb.Queryable<UserStore>()
            .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
            .Where((us, s) =>
                us.UserGUID == user.UserGUID
                && !us.IsDeleted
                && s.IsActive
                && !s.IsDeleted)
            .Select((us, s) => s.StoreCode)
            .ToListAsync(cancellationToken);
        if (!allowedStoreCodes.Contains(storeCode, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var snapshot = await GetPermissionSnapshotAsync(user.UserGUID, cancellationToken);
        var cashierId = string.IsNullOrWhiteSpace(cashier.HGUID) ? cashier.Id.ToString() : cashier.HGUID;
        var authorization = ticketService.Issue(cashierId, cashierUserGuid, storeCode, deviceCode);

        return new CashierSessionDto(
            cashierId,
            cashierUserGuid,
            string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            storeCode,
            deviceCode,
            snapshot.RoleNames,
            snapshot.PermissionCodes,
            allowedStoreCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            snapshot.IsSuperAdmin,
            IsOfflineCached: false,
            IsEmergencyOverride: false,
            AuthorizationToken: authorization.Token,
            AuthorizationExpiresAtUtc: authorization.ExpiresAtUtc);
    }

    public async Task<bool> HasAnyPermissionAsync(
        string userGuid,
        string storeCode,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userGuid) ||
            string.IsNullOrWhiteSpace(storeCode) ||
            permissionCodes.Count == 0)
        {
            return false;
        }

        var user = await dbContext.MainDb.Queryable<User>()
            .FirstAsync(x => x.UserGUID == userGuid && x.IsActive && !x.IsDeleted, cancellationToken);
        if (user is null)
        {
            return false;
        }

        var allowedStoreCodes = await dbContext.MainDb.Queryable<UserStore>()
            .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
            .Where((us, s) =>
                us.UserGUID == userGuid &&
                !us.IsDeleted &&
                s.IsActive &&
                !s.IsDeleted)
            .Select((us, s) => s.StoreCode)
            .ToListAsync(cancellationToken);
        if (!allowedStoreCodes.Contains(storeCode, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // 每次敏感请求都重新读取角色和权限，停用、调店或撤权立即生效。
        var snapshot = await GetPermissionSnapshotAsync(userGuid, cancellationToken);
        return snapshot.IsSuperAdmin ||
            snapshot.PermissionCodes.Intersect(permissionCodes, StringComparer.OrdinalIgnoreCase).Any();
    }

    private async Task<CashierPermissionSnapshot> GetPermissionSnapshotAsync(
        string userGuid,
        CancellationToken cancellationToken)
    {
        var roleEntries = await dbContext.MainDb.Queryable<UserRole>()
            .InnerJoin<Role>((ur, r) => ur.RoleGUID == r.RoleGUID)
            .Where((ur, r) =>
                ur.UserGUID == userGuid
                && !ur.IsDeleted
                && r.IsActive
                && !r.IsDeleted)
            .Select((ur, r) => new { r.RoleGUID, r.RoleName })
            .ToListAsync(cancellationToken);
        var roleNames = roleEntries
            .Select(x => x.RoleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var isSuperAdmin = roleNames.Any(Permissions.IsSuperAdminRole);

        string[] permissionCodes;
        if (isSuperAdmin)
        {
            permissionCodes = (await dbContext.MainDb.Queryable<SysPermission>()
                    .Where(x => !x.IsDeleted)
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        else
        {
            var roleGuids = roleEntries
                .Select(x => x.RoleGUID)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rolePermissionCodes = roleGuids.Count == 0
                ? []
                : await dbContext.MainDb.Queryable<SysRolePermission>()
                    .Where(x => roleGuids.Contains(x.RoleGuid) && !x.IsDeleted)
                    .Select(x => x.PermissionCode)
                    .ToListAsync(cancellationToken);
            var directPermissionCodes = await dbContext.MainDb.Queryable<SysUserPermission>()
                .Where(x => x.UserGuid == userGuid && !x.IsDeleted)
                .Select(x => x.PermissionCode)
                .ToListAsync(cancellationToken);

            // 关键逻辑：权限别名展开保持和后台 RoleService 快照一致。
            permissionCodes = Permissions.ExpandPermissionCodes(
                    rolePermissionCodes.Concat(directPermissionCodes))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return new CashierPermissionSnapshot(roleNames, permissionCodes, isSuperAdmin);
    }

    private sealed record CashierPermissionSnapshot(
        string[] RoleNames,
        string[] PermissionCodes,
        bool IsSuperAdmin);
}
