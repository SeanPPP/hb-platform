using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

// 生产服务与 SQL Server 方言契约共用同一查询表达式，避免验证逻辑和运行路径漂移。
internal static class MobileDeviceActivationQueries
{
    internal static ISugarQueryable<MobileDeviceRegistrationStateRow> BuildRegistrationStateQuery(
        ISqlSugarClient db,
        int registrationId) =>
        db.Queryable<POSM_设备注册信息表>()
            .Where(device => device.ID == registrationId)
            .Select(device => new MobileDeviceRegistrationStateRow
            {
                DeviceRegistrationId = device.ID,
                HardwareId = device.设备硬件识别码,
                DeviceCode = device.系统设备编号,
                StoreCode = device.分店代码,
                DeviceSystem = device.设备系统,
                DeviceType = device.设备类型,
                DeviceStatus = device.设备状态,
            });

    internal static ISugarQueryable<ActivationTargetRow> BuildActiveTargetQuery(
        ISqlSugarClient db,
        string storeCode,
        string userGuid) =>
        db.Queryable<User>()
            .InnerJoin<UserStore>((user, userStore) => user.UserGUID == userStore.UserGUID)
            .InnerJoin<Store>((user, userStore, store) => userStore.StoreGUID == store.StoreGUID)
            .Where((user, userStore, store) =>
                user.UserGUID == userGuid
                && user.IsActive
                && !user.IsDeleted
                && !userStore.IsDeleted
                && store.StoreCode == storeCode
                && store.IsActive
                && !store.IsDeleted)
            .Select((user, userStore, store) => new ActivationTargetRow
            {
                UserGuid = user.UserGUID,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                StoreCode = store.StoreCode,
                StoreName = store.StoreName,
            });

    internal static ISugarQueryable<string> BuildAssignedStoreCountQuery(
        ISqlSugarClient db,
        string userGuid) =>
        db.Queryable<UserStore>()
            .InnerJoin<Store>((userStore, store) => userStore.StoreGUID == store.StoreGUID)
            .Where((userStore, store) =>
                userStore.UserGUID == userGuid
                && !userStore.IsDeleted
                && store.IsActive
                && !store.IsDeleted)
            .Select((userStore, store) => userStore.StoreGUID)
            .Distinct();

    internal static ISugarQueryable<string> BuildActiveRolesQuery(
        ISqlSugarClient db,
        string userGuid) =>
        db.Queryable<UserRole>()
            .InnerJoin<Role>((userRole, role) => userRole.RoleGUID == role.RoleGUID)
            .Where((userRole, role) =>
                userRole.UserGUID == userGuid
                && !userRole.IsDeleted
                && role.IsActive
                && !role.IsDeleted)
            .Select((userRole, role) => role.RoleName)
            .Distinct();

    internal static ISugarQueryable<MobileDeviceSessionStoreRow> BuildAssignedStoresQuery(
        ISqlSugarClient db,
        string userGuid) =>
        db.Queryable<UserStore>()
            .InnerJoin<Store>((userStore, store) => userStore.StoreGUID == store.StoreGUID)
            .Where((userStore, store) =>
                userStore.UserGUID == userGuid
                && !userStore.IsDeleted
                && store.IsActive
                && !store.IsDeleted)
            .OrderBy((userStore, store) => userStore.IsPrimary ? 0 : 1)
            .OrderBy((userStore, store) => store.StoreCode)
            .Select((userStore, store) => new MobileDeviceSessionStoreRow
            {
                StoreGuid = store.StoreGUID,
                StoreCode = store.StoreCode,
                StoreName = store.StoreName,
                IsPrimary = userStore.IsPrimary,
            });

    internal static ISugarQueryable<MobileDeviceManageableAccountRow> BuildManageableAccountsQuery(
        ISqlSugarClient db,
        string storeGuid) =>
        db.Queryable<User>()
            .InnerJoin<UserStore>((user, userStore) => user.UserGUID == userStore.UserGUID)
            .Where((user, userStore) =>
                user.IsActive
                && !user.IsDeleted
                && !userStore.IsDeleted
                && userStore.StoreGUID == storeGuid)
            .Select((user, userStore) => new MobileDeviceManageableAccountRow
            {
                UserGUID = user.UserGUID,
                Username = user.Username,
                FullName = user.FullName,
            })
            .Distinct();
}

internal sealed class ActivationTargetRow
{
    public string UserGuid { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
}

internal sealed class MobileDeviceSessionStoreRow
{
    public string StoreGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

internal sealed class MobileDeviceRegistrationStateRow
{
    public int DeviceRegistrationId { get; set; }
    public string HardwareId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string? StoreCode { get; set; }
    public string DeviceSystem { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public int DeviceStatus { get; set; }
}

internal sealed class MobileDeviceManageableAccountRow
{
    public string UserGUID { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
}
