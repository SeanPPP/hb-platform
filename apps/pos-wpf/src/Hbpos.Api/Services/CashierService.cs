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

    Task<CashierSessionDto?> RefreshSessionAsync(
        CashierAuthorizationTicket ticket,
        CancellationToken cancellationToken);
}

public sealed class CashierService(
    HbposSqlSugarContext dbContext,
    ICashierAuthorizationTicketService ticketService,
    ILogger<CashierService> logger) : ICashierService
{
    private static readonly string[] LineDiscountPermissions =
    [
        Permissions.PosTerminal.Sales.LineManualDiscount,
        Permissions.PosTerminal.Sales.LineQuickDiscount10Percent,
        Permissions.PosTerminal.Sales.LineQuickDiscount20Percent,
        Permissions.PosTerminal.Sales.LineQuickDiscount30Percent,
        Permissions.PosTerminal.Sales.LineQuickDiscount40Percent,
        Permissions.PosTerminal.Sales.LineQuickDiscount50Percent,
    ];

    private static readonly string[] OrderDiscountPermissions =
    [
        Permissions.PosTerminal.Sales.OrderManualDiscount,
        Permissions.PosTerminal.Sales.OrderQuickDiscount10Percent,
        Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent,
        Permissions.PosTerminal.Sales.OrderQuickDiscount30Percent,
        Permissions.PosTerminal.Sales.OrderQuickDiscount40Percent,
        Permissions.PosTerminal.Sales.OrderQuickDiscount50Percent,
    ];

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

        var employeeCashier = await dbContext.MainDb.Queryable<EmployeeCashierBarcode>()
            .FirstAsync(
                x => x.Barcode == userBarcode && x.Status,
                cancellationToken);
        if (cashier is not null && employeeCashier is not null)
        {
            // 关键逻辑：双表同时有效表示数据唯一性已损坏，禁止任意选择身份继续授权。
            var barcodeSuffix = userBarcode.Length <= 4 ? "***" : $"***{userBarcode[^4..]}";
            logger.LogCritical(
                "收银条码同时命中 legacy 与 employee 表，已拒绝登录。BarcodeSuffix={BarcodeSuffix}, Legacy={LegacyHguid}, Employee={EmployeeHguid}",
                barcodeSuffix,
                cashier.HGUID,
                employeeCashier.HGUID);
            return null;
        }
        var cashierUserGuid = cashier?.UserGUID?.Trim() ?? employeeCashier?.UserGUID?.Trim();
        if (string.IsNullOrWhiteSpace(cashierUserGuid))
        {
            return null;
        }

        var cashierId = employeeCashier?.HGUID
            ?? (string.IsNullOrWhiteSpace(cashier!.HGUID) ? cashier.Id.ToString() : cashier.HGUID);
        return await CreateSessionAsync(
            cashierId,
            cashierUserGuid,
            storeCode,
            deviceCode,
            cancellationToken);
    }

    public Task<CashierSessionDto?> RefreshSessionAsync(
        CashierAuthorizationTicket ticket,
        CancellationToken cancellationToken)
    {
        return CreateSessionAsync(
            ticket.CashierId,
            ticket.UserGuid,
            ticket.StoreCode,
            ticket.DeviceCode,
            cancellationToken);
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

        var allowedStores = await dbContext.MainDb.Queryable<UserStore>()
            .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
            .Where((us, s) =>
                us.UserGUID == userGuid &&
                !us.IsDeleted &&
                s.IsActive &&
                !s.IsDeleted)
            .Select((us, s) => new { s.StoreGUID, s.StoreCode })
            .ToListAsync(cancellationToken);
        var allowedStore = allowedStores.FirstOrDefault(store =>
            string.Equals(store.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase));
        if (allowedStore is null)
        {
            return false;
        }

        // 每次敏感请求都重新读取角色和权限，停用、调店或撤权立即生效。
        var snapshot = await GetPermissionSnapshotAsync(userGuid, allowedStore.StoreGUID, cancellationToken);
        return snapshot.IsSuperAdmin ||
            snapshot.PermissionCodes.Intersect(permissionCodes, StringComparer.OrdinalIgnoreCase).Any();
    }

    private async Task<CashierPermissionSnapshot> GetPermissionSnapshotAsync(
        string userGuid,
        string storeGuid,
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

        if (!isSuperAdmin)
        {
            var storeOverrides = await dbContext.MainDb.Queryable<SysUserStorePosPermission>()
                .Where(x =>
                    x.UserGuid == userGuid &&
                    x.StoreGuid == storeGuid &&
                    !x.IsDeleted)
                .Select(x => new { x.PermissionCode, x.IsGranted })
                .ToListAsync(cancellationToken);
            var effectivePermissions = permissionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var storeOverride in storeOverrides)
            {
                // 只允许分店覆盖 POS 权限，避免历史脏数据影响后台权限。
                if (!storeOverride.PermissionCode.StartsWith(
                        "Permissions.PosTerminal.",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (storeOverride.IsGranted)
                {
                    effectivePermissions.Add(storeOverride.PermissionCode);
                }
                else
                {
                    effectivePermissions.Remove(storeOverride.PermissionCode);
                }
            }

            permissionCodes = effectivePermissions.ToArray();
        }

        permissionCodes = AddLegacyDiscountCompatibility(permissionCodes);
        return new CashierPermissionSnapshot(roleNames, permissionCodes, isSuperAdmin);
    }

    private async Task<CashierSessionDto?> CreateSessionAsync(
        string cashierId,
        string userGuid,
        string storeCode,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.MainDb.Queryable<User>()
            .FirstAsync(
                x => x.UserGUID == userGuid && x.IsActive && !x.IsDeleted,
                cancellationToken);
        if (user is null)
        {
            return null;
        }

        var allowedStores = await dbContext.MainDb.Queryable<UserStore>()
            .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
            .Where((us, s) =>
                us.UserGUID == userGuid &&
                !us.IsDeleted &&
                s.IsActive &&
                !s.IsDeleted)
            .Select((us, s) => new { s.StoreGUID, s.StoreCode })
            .ToListAsync(cancellationToken);
        var currentStore = allowedStores.FirstOrDefault(store =>
            string.Equals(store.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase));
        if (currentStore is null)
        {
            return null;
        }

        var snapshot = await GetPermissionSnapshotAsync(userGuid, currentStore.StoreGUID, cancellationToken);
        var authorization = ticketService.Issue(cashierId, userGuid, storeCode, deviceCode);
        return new CashierSessionDto(
            cashierId,
            userGuid,
            string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            storeCode,
            deviceCode,
            snapshot.RoleNames,
            snapshot.PermissionCodes,
            allowedStores
                .Select(store => store.StoreCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            snapshot.IsSuperAdmin,
            IsOfflineCached: false,
            IsEmergencyOverride: false,
            AuthorizationToken: authorization.Token,
            AuthorizationExpiresAtUtc: authorization.ExpiresAtUtc);
    }

    private static string[] AddLegacyDiscountCompatibility(IEnumerable<string> permissionCodes)
    {
        var effectivePermissions = permissionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (LineDiscountPermissions.All(effectivePermissions.Contains))
        {
            effectivePermissions.Add(Permissions.PosTerminal.Sales.LineDiscount);
        }

        if (OrderDiscountPermissions.All(effectivePermissions.Contains))
        {
            effectivePermissions.Add(Permissions.PosTerminal.Sales.OrderDiscount);
        }

        return effectivePermissions.ToArray();
    }

    private sealed record CashierPermissionSnapshot(
        string[] RoleNames,
        string[] PermissionCodes,
        bool IsSuperAdmin);
}
