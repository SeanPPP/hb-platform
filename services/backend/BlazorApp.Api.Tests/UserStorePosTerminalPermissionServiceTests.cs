using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class UserStorePosTerminalPermissionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public UserStorePosTerminalPermissionServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables<User, Store, UserStore, Role>();
        _db.CodeFirst.InitTables<UserRole, SysRolePermission, SysUserPermission>();
        _db.CodeFirst.InitTables<SysUserStorePosPermission>();
    }

    [Fact]
    public async Task StoreManager_空快照更新和删除分别覆盖与恢复继承()
    {
        await SeedActorAndTargetAsync();
        await _db.Insertable(new SysRolePermission
        {
            Id = "target-line-permission",
            RoleGuid = "role-staff",
            PermissionCode = Permissions.PosTerminal.Sales.LineManualDiscount,
        }).ExecuteCommandAsync();
        var service = CreateService("store-a");

        var inherited = await service.GetAsync("target", "store-a");
        Assert.Equal("Inherited", inherited.Data!.Mode);
        Assert.Contains(Permissions.PosTerminal.Sales.LineManualDiscount, inherited.Data.EffectivePermissionCodes);

        var updated = await service.UpdateAsync(
            "target",
            "store-a",
            new UpdateUserStorePosTerminalPermissionsRequest
            {
                GrantedPermissionCodes =
                [Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent],
            }
        );
        Assert.True(updated.Success);
        Assert.Equal("Override", updated.Data!.Mode);
        Assert.Equal(2, updated.Data.OverriddenPermissionCodes.Count);
        Assert.DoesNotContain(Permissions.PosTerminal.Sales.LineManualDiscount, updated.Data.EffectivePermissionCodes);
        Assert.Contains(Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent, updated.Data.EffectivePermissionCodes);

        var firstRows = await _db.Queryable<SysUserStorePosPermission>()
            .Where(item => item.UserGuid == "target" && item.StoreGuid == "store-a")
            .ToListAsync();
        var firstAudit = firstRows.ToDictionary(
            item => item.PermissionCode,
            item => (item.Id, item.CreatedAt),
            StringComparer.OrdinalIgnoreCase
        );

        var savedAgain = await service.UpdateAsync(
            "target",
            "store-a",
            new UpdateUserStorePosTerminalPermissionsRequest
            {
                GrantedPermissionCodes =
                [Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent],
            }
        );
        Assert.True(savedAgain.Success);
        var secondRows = await _db.Queryable<SysUserStorePosPermission>()
            .Where(item => item.UserGuid == "target" && item.StoreGuid == "store-a")
            .ToListAsync();
        Assert.Equal(firstRows.Count, secondRows.Count);
        Assert.All(secondRows, item =>
        {
            Assert.Equal(firstAudit[item.PermissionCode].Id, item.Id);
            Assert.Equal(firstAudit[item.PermissionCode].CreatedAt, item.CreatedAt);
        });

        var deleted = await service.DeleteAsync("target", "store-a");
        Assert.True(deleted.Success);
        Assert.Equal("Inherited", deleted.Data!.Mode);
        Assert.Empty(deleted.Data.OverriddenPermissionCodes);
        Assert.Contains(Permissions.PosTerminal.Sales.LineManualDiscount, deleted.Data.EffectivePermissionCodes);
        var tombstones = await _db.Queryable<SysUserStorePosPermission>()
            .Where(item => item.UserGuid == "target" && item.StoreGuid == "store-a")
            .ToListAsync();
        Assert.Equal(secondRows.Count, tombstones.Count);
        Assert.All(tombstones, item =>
        {
            Assert.True(item.IsDeleted);
            Assert.Equal("actor", item.UpdatedBy);
            Assert.Equal(firstAudit[item.PermissionCode].Id, item.Id);
            Assert.Equal(firstAudit[item.PermissionCode].CreatedAt, item.CreatedAt);
        });

        var restoredOverride = await service.UpdateAsync(
            "target",
            "store-a",
            new UpdateUserStorePosTerminalPermissionsRequest
            {
                GrantedPermissionCodes =
                [Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent],
            }
        );
        Assert.True(restoredOverride.Success);
        var resurrectedRows = await _db.Queryable<SysUserStorePosPermission>()
            .Where(item => item.UserGuid == "target" && item.StoreGuid == "store-a")
            .ToListAsync();
        Assert.Equal(tombstones.Count, resurrectedRows.Count);
        Assert.All(resurrectedRows, item =>
        {
            Assert.False(item.IsDeleted);
            Assert.Equal(firstAudit[item.PermissionCode].Id, item.Id);
            Assert.Equal(firstAudit[item.PermissionCode].CreatedAt, item.CreatedAt);
        });
    }

    [Fact]
    public async Task StoreManager_拒绝非管理分店和高权目标()
    {
        await SeedActorAndTargetAsync();
        await SeedStoreAsync("store-b");
        await SeedUserStoreAsync("target", "store-b");

        var foreign = await CreateService("store-a").GetAsync("target", "store-b");
        Assert.False(foreign.Success);
        Assert.Equal("POS_PERMISSION_FORBIDDEN", foreign.ErrorCode);

        await _db.Insertable(new UserRole
        {
            UserRoleGUID = "target-manager-role",
            UserGUID = "target",
            RoleGUID = "role-manager",
        }).ExecuteCommandAsync();

        var privileged = await CreateService("store-a").GetAsync("target", "store-a");
        Assert.False(privileged.Success);
        Assert.Equal("POS_PERMISSION_FORBIDDEN", privileged.ErrorCode);
    }

    [Fact]
    public async Task StoreManager_只替换业务白名单并保留系统级覆盖()
    {
        await SeedActorAndTargetAsync();
        await _db.Insertable(new SysUserStorePosPermission
        {
            Id = "admin-system-override",
            UserGuid = "target",
            StoreGuid = "store-a",
            PermissionCode = Permissions.PosTerminal.Settings.AppUpdate,
            IsGranted = true,
        }).ExecuteCommandAsync();

        var result = await CreateService("store-a").UpdateAsync(
            "target",
            "store-a",
            new UpdateUserStorePosTerminalPermissionsRequest()
        );

        Assert.True(result.Success);
        Assert.True(await _db.Queryable<SysUserStorePosPermission>().AnyAsync(item =>
            item.Id == "admin-system-override" && item.IsGranted));
        Assert.DoesNotContain(
            Permissions.PosTerminal.Settings.AppUpdate,
            result.Data!.EffectivePermissionCodes
        );
    }

    [Fact]
    public async Task StoreManager_只能覆盖本人有效的Pos收银权限()
    {
        await SeedActorAndTargetAsync();
        var service = CreateService("store-a");

        var state = await service.GetAsync("target", "store-a");
        var denied = await service.UpdateAsync(
            "target",
            "store-a",
            new UpdateUserStorePosTerminalPermissionsRequest
            {
                GrantedPermissionCodes = [Permissions.PosTerminal.Sales.AddOpenItem],
            }
        );

        Assert.True(state.Success);
        Assert.Equal(
            new[]
            {
                Permissions.PosTerminal.Sales.LineManualDiscount,
                Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent,
            }.OrderBy(item => item),
            state.Data!.AssignablePermissions.Select(item => item.Code).OrderBy(item => item)
        );
        Assert.False(denied.Success);
        Assert.Equal("POS_PERMISSION_INVALID_CODES", denied.ErrorCode);
    }

    [Fact]
    public async Task StoreManager_非员工目标的查询和更新均拒绝()
    {
        await SeedActorAndTargetAsync();
        await _db.Deleteable<UserRole>()
            .Where(item => item.UserGUID == "target" && item.RoleGUID == "role-staff")
            .ExecuteCommandAsync();
        var service = CreateService("store-a");

        var getResult = await service.GetAsync("target", "store-a");
        var updateResult = await service.UpdateAsync(
            "target",
            "store-a",
            new UpdateUserStorePosTerminalPermissionsRequest()
        );

        Assert.False(getResult.Success);
        Assert.Equal("EMPLOYEE_TARGET_REQUIRED", getResult.ErrorCode);
        Assert.False(updateResult.Success);
        Assert.Equal("EMPLOYEE_TARGET_REQUIRED", updateResult.ErrorCode);
    }

    [Fact]
    public async Task StoreManager_可维护自己分店内的跨店员工Pos覆盖()
    {
        await SeedActorAndTargetAsync();
        await SeedStoreAsync("store-b");
        await SeedUserStoreAsync("target", "store-b");

        var result = await CreateService("store-a").GetAsync("target", "store-a");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    private async Task SeedActorAndTargetAsync()
    {
        await SeedStoreAsync("store-a");
        await _db.Insertable(new[]
        {
            CreateUser("actor"),
            CreateUser("target"),
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new Role { RoleGUID = "role-manager", RoleName = "StoreManager", IsActive = true },
            new Role { RoleGUID = "role-staff", RoleName = "StoreStaff", IsActive = true },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new UserRole { UserRoleGUID = "actor-role", UserGUID = "actor", RoleGUID = "role-manager" },
            new UserRole { UserRoleGUID = "target-role", UserGUID = "target", RoleGUID = "role-staff" },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new SysRolePermission
            {
                Id = "actor-line-discount",
                RoleGuid = "role-manager",
                PermissionCode = Permissions.PosTerminal.Sales.LineManualDiscount,
            },
            new SysRolePermission
            {
                Id = "actor-order-discount",
                RoleGuid = "role-manager",
                PermissionCode = Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent,
            },
        }).ExecuteCommandAsync();
        await SeedUserStoreAsync("actor", "store-a", true);
        await SeedUserStoreAsync("target", "store-a");
    }

    private async Task SeedStoreAsync(string storeGuid) =>
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeGuid,
            StoreName = storeGuid,
            IsActive = true,
        }).ExecuteCommandAsync();

    private async Task SeedUserStoreAsync(string userGuid, string storeGuid, bool primary = false) =>
        await _db.Insertable(new UserStore
        {
            UserStoreGUID = $"{userGuid}-{storeGuid}",
            UserGUID = userGuid,
            StoreGUID = storeGuid,
            IsPrimary = primary,
        }).ExecuteCommandAsync();

    private static User CreateUser(string guid) => new()
    {
        UserGUID = guid,
        Username = guid,
        Email = $"{guid}@example.com",
        PasswordHash = "hash",
        IsActive = true,
    };

    private UserStorePosTerminalPermissionService CreateService(params string[] storeGuids) =>
        new(
            CreateContext(_db),
            new FakeScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "actor",
                StoreGuids = storeGuids,
            }),
            new FakeCurrentUserService()
        );

    private static SqlSugarContext CreateContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeCurrentUserService : ICurrentUserService
    {
        public string GetCurrentUsername() => "actor";
        public string GetCurrentUserGuid() => "actor";
    }

    private sealed class FakeScopeService(CurrentUserManageableStoreScope scope)
        : ICurrentUserManageableStoreScopeService
    {
        public Task<CurrentUserManageableStoreScope> GetScopeAsync() => Task.FromResult(scope);
        public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() => throw new NotSupportedException();
        public Task<bool> CanAccessStoreCodeAsync(string storeCode) => throw new NotSupportedException();
        public Task<bool> CanAccessOrderAsync(string orderGuid) => throw new NotSupportedException();
        public Task<bool> CanManageStoreAsync(string storeGuid) => throw new NotSupportedException();
        public Task<bool> CanManageUserAsync(string userGuid) => throw new NotSupportedException();
    }
}
