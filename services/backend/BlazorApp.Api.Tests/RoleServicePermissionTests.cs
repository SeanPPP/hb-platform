using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RoleServicePermissionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public RoleServicePermissionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });

        _db.CodeFirst.InitTables(
            typeof(User),
            typeof(Role),
            typeof(UserRole),
            typeof(SysPermission),
            typeof(SysRolePermission),
            typeof(SysUserPermission)
        );
    }

    [Fact]
    public async Task UserHasPermissionAsync_AdminRoleImplicitlyGrantsAnyPermission()
    {
        await SeedUserWithRoleAsync("user-1", "role-admin", "Admin");

        var result = await CreateService().UserHasPermissionAsync("user-1", "Any.Permission");

        Assert.True(result.Data);
    }

    [Fact]
    public async Task UserHasPermissionAsync_LegacyLocalInvocieGrantAllowsCanonicalLocalPurchase()
    {
        await SeedUserWithRoleAsync("user-1", "role-user", "User");
        await InsertRolePermissionAsync("role-user", "LocalInvocie.View");

        var result = await CreateService()
            .UserHasPermissionAsync("user-1", Permissions.LocalPurchase.View);

        Assert.True(result.Data);
    }

    [Fact]
    public async Task UserHasPermissionAsync_DirectUserPermissionGrantsAccess()
    {
        await SeedUserWithRoleAsync("user-1", "role-user", "User");
        await InsertUserPermissionAsync("user-1", Permissions.Reports.View);

        var result = await CreateService().UserHasPermissionAsync("user-1", Permissions.Reports.View);

        Assert.True(result.Data);
    }

    [Fact]
    public async Task GetUserPermissionSnapshotAsync_AdminRoleReturnsImplicitAllPermissions()
    {
        await SeedUserWithRoleAsync("user-1", "role-admin", "Admin");
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertPermissionAsync(Permissions.Users.Edit);

        var result = await CreateService().GetUserPermissionSnapshotAsync("user-1");

        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsSuperAdmin);
        Assert.Contains("Admin", result.Data.RoleNames);
        Assert.Contains(Permissions.Users.View, result.Data.PermissionCodes);
        Assert.Contains(Permissions.Users.Edit, result.Data.PermissionCodes);
    }

    [Fact]
    public async Task GetUserPermissionSnapshotAsync_ExpandsLegacyAliasesToCanonicalPermissions()
    {
        await SeedUserWithRoleAsync("user-1", "role-user", "User");
        await InsertRolePermissionAsync("role-user", "LocalInvocie.View");
        await InsertUserPermissionAsync("user-1", Permissions.Reports.View);

        var result = await CreateService().GetUserPermissionSnapshotAsync("user-1");

        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsSuperAdmin);
        Assert.Contains("User", result.Data.RoleNames);
        Assert.Contains("LocalInvocie.View", result.Data.PermissionCodes);
        Assert.Contains(Permissions.LocalPurchase.View, result.Data.PermissionCodes);
        Assert.Contains(Permissions.Reports.View, result.Data.PermissionCodes);
    }

    [Fact]
    public async Task GetRolePermissionsAsync_AdminReturnsAllActivePermissionsWithoutExplicitLinks()
    {
        await InsertRoleAsync("role-admin", "管理员");
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertPermissionAsync(Permissions.Users.Edit);
        await InsertPermissionAsync(Permissions.Users.Delete, isDeleted: true);

        var result = await CreateService().GetRolePermissionsAsync("role-admin");

        var permissions = Assert.IsType<List<string>>(result.Data);
        Assert.Contains(Permissions.Users.View, permissions);
        Assert.Contains(Permissions.Users.Edit, permissions);
        Assert.DoesNotContain(Permissions.Users.Delete, permissions);
    }

    [Fact]
    public async Task AssignPermissionsToRoleAsync_AdminDoesNotCreateExplicitRolePermissions()
    {
        await InsertRoleAsync("role-admin", "Admin");

        var result = await CreateService()
            .AssignPermissionsToRoleAsync(
                "role-admin",
                new RolePermissionAssignmentDto
                {
                    Permissions = new List<string> { Permissions.Users.View, Permissions.Users.Edit },
                }
            );

        var links = await _db.Queryable<SysRolePermission>()
            .Where(item => item.RoleGuid == "role-admin")
            .ToListAsync();

        Assert.True(result.Data);
        Assert.Empty(links);
    }

    [Fact]
    public async Task AssignRolesToPermissionAsync_SkipsAdminRoles()
    {
        await InsertRoleAsync("role-admin", "Admin");
        await InsertRoleAsync("role-user", "User");

        var result = await CreateService()
            .AssignRolesToPermissionAsync(
                Permissions.Users.View,
                new List<string> { "role-admin", "role-user" }
            );

        var links = await _db.Queryable<SysRolePermission>()
            .Where(item => item.PermissionCode == Permissions.Users.View)
            .ToListAsync();

        Assert.True(result.Data);
        var link = Assert.Single(links);
        Assert.Equal("role-user", link.RoleGuid);
    }

    [Fact]
    public async Task GetPermissionCatalogAsync_ReturnsAliasesTemplatesAndSuperAdminRoles()
    {
        var result = await CreateService().GetPermissionCatalogAsync();

        Assert.NotNull(result.Data);
        Assert.Contains("Admin", result.Data.SuperAdminRoleNames);
        Assert.Contains("管理员", result.Data.SuperAdminRoleNames);
        Assert.Contains(
            result.Data.PermissionAliases,
            item =>
                item.CanonicalCode == Permissions.LocalPurchase.View
                && item.AliasCodes.Contains("LocalInvocie.View")
        );
        Assert.Contains(result.Data.RoleTemplates, item => item.RoleName == "WarehouseManager");
        Assert.Contains(result.Data.RoleTemplates, item => item.RoleName == "StoreManager");
    }

    [Fact]
    public async Task GetRolePermissionStateAsync_AdminReportsImplicitAllWithoutExplicitLinks()
    {
        await InsertRoleAsync("role-admin", "Admin");
        await InsertPermissionAsync(Permissions.Users.View);

        var result = await CreateService().GetRolePermissionStateAsync("role-admin");

        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsSuperAdmin);
        Assert.True(result.Data.ImplicitAllPermissions);
        Assert.Empty(result.Data.ExplicitPermissionCodes);
        Assert.Contains(Permissions.Users.View, result.Data.EffectivePermissionCodes);
    }

    [Fact]
    public async Task GetRolePermissionStateAsync_NormalRoleSeparatesExplicitAndEffective()
    {
        await InsertRoleAsync("role-user", "User");
        await InsertRolePermissionAsync("role-user", Permissions.Attendance.Punch.Self);

        var result = await CreateService().GetRolePermissionStateAsync("role-user");

        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsSuperAdmin);
        Assert.False(result.Data.ImplicitAllPermissions);
        Assert.Contains(Permissions.Attendance.Punch.Self, result.Data.ExplicitPermissionCodes);
        Assert.Contains(Permissions.Attendance.Punch.Self, result.Data.EffectivePermissionCodes);
    }

    [Fact]
    public async Task GetUserPermissionStateAsync_SeparatesInheritedDirectAndEffectivePermissions()
    {
        await SeedUserWithRoleAsync("user-1", "role-store", "StoreManager");
        await InsertRolePermissionAsync("role-store", Permissions.Attendance.Schedule.ViewStore);
        await InsertUserPermissionAsync("user-1", Permissions.Reports.View);

        var result = await CreateService().GetUserPermissionStateAsync("user-1");

        Assert.NotNull(result.Data);
        Assert.Contains(Permissions.Attendance.Schedule.ViewStore, result.Data.InheritedPermissionCodes);
        Assert.Contains(Permissions.Reports.View, result.Data.DirectPermissionCodes);
        Assert.Contains(Permissions.Attendance.Schedule.ViewStore, result.Data.EffectivePermissionCodes);
        Assert.Contains(Permissions.Reports.View, result.Data.EffectivePermissionCodes);
        var source = Assert.Single(result.Data.InheritedSources);
        Assert.Equal("StoreManager", source.RoleName);
        Assert.Contains(Permissions.Attendance.Schedule.ViewStore, source.PermissionCodes);
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_ReplacesOnlyDirectUserPermissions()
    {
        await SeedUserWithRoleAsync("user-1", "role-store", "StoreManager");
        await InsertPermissionAsync(Permissions.Reports.View);
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertRolePermissionAsync("role-store", Permissions.Attendance.Schedule.ViewStore);
        await InsertUserPermissionAsync("user-1", Permissions.Reports.View);

        var result = await CreateService()
            .AssignPermissionsToUserAsync(
                "user-1",
                new UserPermissionAssignmentDto
                {
                    Permissions = new List<string>
                    {
                        Permissions.Users.View,
                        "Missing.Permission",
                        Permissions.Users.View,
                    },
                }
            );

        var directLinks = await _db.Queryable<SysUserPermission>()
            .Where(item => item.UserGuid == "user-1")
            .ToListAsync();
        var roleLinks = await _db.Queryable<SysRolePermission>()
            .Where(item => item.RoleGuid == "role-store")
            .ToListAsync();

        Assert.True(result.Data);
        var directLink = Assert.Single(directLinks);
        Assert.Equal(Permissions.Users.View, directLink.PermissionCode);
        Assert.Contains(roleLinks, item => item.PermissionCode == Permissions.Attendance.Schedule.ViewStore);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();

        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private async Task SeedUserWithRoleAsync(string userGuid, string roleGuid, string roleName)
    {
        await _db.Insertable(new User
        {
            UserGUID = userGuid,
            Username = userGuid,
            Email = $"{userGuid}@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();
        await InsertRoleAsync(roleGuid, roleName);
        await _db.Insertable(new UserRole
        {
            UserRoleGUID = $"{userGuid}-{roleGuid}",
            UserGUID = userGuid,
            RoleGUID = roleGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertRoleAsync(
        string roleGuid,
        string roleName,
        bool isActive = true,
        bool isDeleted = false
    )
    {
        await _db.Insertable(new Role
        {
            RoleGUID = roleGuid,
            RoleName = roleName,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task InsertPermissionAsync(string code, bool isDeleted = false)
    {
        await _db.Insertable(new SysPermission
        {
            Id = code,
            Code = code,
            Name = code,
            Category = "test",
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task InsertRolePermissionAsync(string roleGuid, string permissionCode)
    {
        await _db.Insertable(new SysRolePermission
        {
            Id = $"{roleGuid}-{permissionCode}",
            RoleGuid = roleGuid,
            PermissionCode = permissionCode,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertUserPermissionAsync(string userGuid, string permissionCode)
    {
        await _db.Insertable(new SysUserPermission
        {
            Id = $"{userGuid}-{permissionCode}",
            UserGuid = userGuid,
            PermissionCode = permissionCode,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private RoleService CreateService()
    {
        return new RoleService(
            CreateSqlSugarContext(_db),
            NullLogger<RoleService>.Instance,
            new HttpContextAccessor()
        );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );

        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);

        return context;
    }
}
