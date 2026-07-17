using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
            typeof(Store),
            typeof(UserStore),
            typeof(SysPermission),
            typeof(SysRolePermission),
            typeof(SysUserPermission)
        );
    }

    [Fact]
    public void PermissionSeedData_IncludesFineGrainedPosTerminalPermissions()
    {
        var requiredCodes = new[]
        {
            Permissions.PosTerminal.Sales.View,
            Permissions.PosTerminal.Sales.AddItem,
            Permissions.PosTerminal.Sales.AddOpenItem,
            Permissions.PosTerminal.Sales.RemoveLine,
            Permissions.PosTerminal.Sales.ChangeQuantity,
            Permissions.PosTerminal.Sales.ChangePrice,
            // 折扣权限按行/整单及操作方式细分，避免继续依赖已废弃的聚合权限。
            Permissions.PosTerminal.Sales.LineManualDiscount,
            Permissions.PosTerminal.Sales.LineQuickDiscount10Percent,
            Permissions.PosTerminal.Sales.LineQuickDiscount20Percent,
            Permissions.PosTerminal.Sales.LineQuickDiscount30Percent,
            Permissions.PosTerminal.Sales.LineQuickDiscount40Percent,
            Permissions.PosTerminal.Sales.LineQuickDiscount50Percent,
            Permissions.PosTerminal.Sales.OrderManualDiscount,
            Permissions.PosTerminal.Sales.OrderQuickDiscount10Percent,
            Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent,
            Permissions.PosTerminal.Sales.OrderQuickDiscount30Percent,
            Permissions.PosTerminal.Sales.OrderQuickDiscount40Percent,
            Permissions.PosTerminal.Sales.OrderQuickDiscount50Percent,
            Permissions.PosTerminal.Sales.ClearCart,
            Permissions.PosTerminal.Sales.HoldOrder,
            Permissions.PosTerminal.Sales.RecallOrder,
            Permissions.PosTerminal.Payment.View,
            Permissions.PosTerminal.Payment.TakeCash,
            Permissions.PosTerminal.Payment.TakeCard,
            Permissions.PosTerminal.Payment.TakeVoucher,
            Permissions.PosTerminal.Payment.RemoveTender,
            Permissions.PosTerminal.Payment.Confirm,
            Permissions.PosTerminal.Returns.View,
            Permissions.PosTerminal.Returns.AddReceiptLine,
            Permissions.PosTerminal.Returns.AddNoReceiptItem,
            Permissions.PosTerminal.Returns.Confirm,
            Permissions.PosTerminal.SpecialProducts.View,
            Permissions.PosTerminal.SpecialProducts.AddToCart,
            Permissions.PosTerminal.SpecialProducts.Manage,
            Permissions.PosTerminal.History.View,
            Permissions.PosTerminal.History.Recall,
            Permissions.PosTerminal.History.Reprint,
            Permissions.PosTerminal.DailyClose.View,
            Permissions.PosTerminal.DailyClose.Save,
            Permissions.PosTerminal.DailyClose.Reprint,
            Permissions.PosTerminal.Installments.View,
            Permissions.PosTerminal.Installments.Create,
            Permissions.PosTerminal.Installments.AddRepayment,
            Permissions.PosTerminal.Installments.Cancel,
            Permissions.PosTerminal.Installments.ConfirmPickup,
            Permissions.PosTerminal.Settings.View,
            Permissions.PosTerminal.Settings.PaymentTerminal,
            Permissions.PosTerminal.Settings.ReceiptPrinter,
            Permissions.PosTerminal.Settings.CatalogDownload,
            Permissions.PosTerminal.Settings.CatalogReset,
            Permissions.PosTerminal.Settings.TestDataReset,
            Permissions.PosTerminal.Settings.DeviceRegistration,
            Permissions.PosTerminal.Settings.AppUpdate,
            Permissions.PosTerminal.CashDrawer.Open,
            Permissions.PosTerminal.Receipt.PrintLast,
            Permissions.PosTerminal.CustomerDisplay.Manage,
            Permissions.PosTerminal.System.Sync,
            "Permissions.PosTerminal.Audit.View",
        };
        var seedsByCode = PermissionSeedData.AllPermissions.ToDictionary(
            seed => seed.Code,
            StringComparer.OrdinalIgnoreCase
        );

        Assert.True(requiredCodes.Length > 5);
        foreach (var code in requiredCodes)
        {
            var seed = Assert.Contains(code, seedsByCode);
            Assert.StartsWith("POS ", seed.Category);
            Assert.Contains("收银端", seed.Description);
            Assert.False(string.IsNullOrWhiteSpace(seed.Name));
        }

        // 旧聚合权限仅用于兼容迁移，不得再次成为可分配的活动种子。
        Assert.DoesNotContain(Permissions.PosTerminal.Sales.LineDiscount, seedsByCode.Keys);
        Assert.DoesNotContain(Permissions.PosTerminal.Sales.OrderDiscount, seedsByCode.Keys);
        Assert.Contains(
            Permissions.PosTerminal.Sales.LineDiscount,
            PermissionSeedData.DeprecatedPermissionCodes
        );
        Assert.Contains(
            Permissions.PosTerminal.Sales.OrderDiscount,
            PermissionSeedData.DeprecatedPermissionCodes
        );

        Assert.DoesNotContain(
            PermissionSeedData.AllPermissions,
            seed => seed.Code.Contains("History.Refund", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(PermissionSeedData.AllPermissions, seed => seed.Code == "Permissions.PosTerminal.Sell");
        Assert.DoesNotContain(PermissionSeedData.AllPermissions, seed => seed.Code == "Permissions.PosTerminal.Discount");
        Assert.DoesNotContain(PermissionSeedData.AllPermissions, seed => seed.Code == "Permissions.PosTerminal.OpenCashDrawer");
        Assert.DoesNotContain(PermissionSeedData.AllPermissions, seed => seed.Code == "Permissions.PosTerminal.DailyClose");
        Assert.DoesNotContain(PermissionSeedData.AllPermissions, seed => seed.Code == "Permissions.PosTerminal.ManageDevices");
    }

    [Fact]
    public void PermissionSeedData_StoreManagerAliasesIncludeOperationAuditView()
    {
        foreach (var roleName in new[] { "StoreManager", "店长", "经理" })
        {
            var template = Assert.Single(
                PermissionSeedData.RolePermissionTemplates,
                item => item.RoleName.Equals(roleName, StringComparison.OrdinalIgnoreCase)
            );

            Assert.Contains("Permissions.PosTerminal.Audit.View", template.PermissionCodes);
            if (!roleName.Equals("StoreManager", StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain(Permissions.DeviceRegistration.Manage, template.PermissionCodes);
            }
        }
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

        var result = await (await CreateAdminServiceAsync()).GetRolePermissionsAsync("role-admin");

        var permissions = Assert.IsType<List<string>>(result.Data);
        Assert.Contains(Permissions.Users.View, permissions);
        Assert.Contains(Permissions.Users.Edit, permissions);
        Assert.DoesNotContain(Permissions.Users.Delete, permissions);
    }

    [Fact]
    public async Task AssignPermissionsToRoleAsync_AdminDoesNotCreateExplicitRolePermissions()
    {
        await InsertRoleAsync("role-admin", "Admin");

        var result = await (await CreateAdminServiceAsync())
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

        var result = await (await CreateAdminServiceAsync())
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
        var result = await (await CreateAdminServiceAsync()).GetPermissionCatalogAsync();

        Assert.NotNull(result.Data);
        Assert.Contains("Admin", result.Data.SuperAdminRoleNames);
        Assert.Contains("管理员", result.Data.SuperAdminRoleNames);
        Assert.Contains("SuperAdmin", result.Data.SuperAdminRoleNames);
        Assert.Contains("超级管理员", result.Data.SuperAdminRoleNames);
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

        var result = await (await CreateAdminServiceAsync()).GetRolePermissionStateAsync("role-admin");

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

        var result = await (await CreateAdminServiceAsync()).GetRolePermissionStateAsync("role-user");

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

        var result = await (await CreateAdminServiceAsync()).GetUserPermissionStateAsync("user-1");

        Assert.NotNull(result.Data);
        Assert.Contains(Permissions.Attendance.Schedule.ViewStore, result.Data.InheritedPermissionCodes);
        Assert.Contains(Permissions.Reports.View, result.Data.DirectPermissionCodes);
        Assert.Contains(Permissions.Attendance.Schedule.ViewStore, result.Data.EffectivePermissionCodes);
        Assert.Contains(Permissions.Reports.View, result.Data.EffectivePermissionCodes);
        var source = Assert.Single(result.Data.InheritedSources);
        Assert.Equal("StoreManager", source.RoleName);
        Assert.Contains(Permissions.Attendance.Schedule.ViewStore, source.PermissionCodes);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("管理员")]
    [InlineData("SuperAdmin")]
    [InlineData("超级管理员")]
    public async Task GetUserPermissionStateAsync_SuperAdminReportsImplicitAll(string roleName)
    {
        await SeedUserWithRoleAsync("admin-user", $"role-{roleName}", roleName);
        await InsertPermissionAsync(Permissions.Users.View);

        var result = await (await CreateAdminServiceAsync()).GetUserPermissionStateAsync("admin-user");

        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsSuperAdmin);
        Assert.True(result.Data.ImplicitAllPermissions);
        Assert.Contains(Permissions.Users.View, result.Data.EffectivePermissionCodes);
    }

    [Fact]
    public void RolesController_GetActiveRoles_UsesAuthenticatedRuntimeOrAuthorization()
    {
        var method = typeof(RolesController).GetMethod(nameof(RolesController.GetActiveRoles));

        var authorize = method?.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Null(authorize!.Policy);
    }

    [Fact]
    public async Task RolesController_GetActiveRoles_AllowsDelegatedManageRolesPermission()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasPermissionAsync(
                "delegated-1",
                Permissions.Roles.View
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        roleService
            .Setup(service => service.UserHasPermissionAsync(
                "delegated-1",
                Permissions.Users.ManageRoles
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(true));
        roleService
            .Setup(service => service.GetActiveRolesAsync())
            .ReturnsAsync(ApiResponse<List<RoleDto>>.OK(new List<RoleDto>()));
        var controller = new RolesController(
            roleService.Object,
            NullLogger<RolesController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "delegated-1") },
                        "TestAuth"
                    )),
                },
            },
        };

        var result = await controller.GetActiveRoles();

        Assert.IsType<OkObjectResult>(result);
        roleService.Verify(service => service.UserHasPermissionAsync(
            "delegated-1",
            Permissions.Users.ManageRoles
        ), Times.Once);
    }

    [Fact]
    public void UsersController_ExposesScopedAccessPermissionsEndpoint()
    {
        var method = typeof(UsersController).GetMethod("GetUserAccessPermissions");

        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpGetAttribute>();
        Assert.Equal("guid/{guid}/access-permissions", route?.Template);
    }

    [Fact]
    public async Task RolesController_GlobalWriteAdminRequiredReturns403WithStableErrorBody()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.CreateRoleAsync(It.IsAny<CreateRoleDto>()))
            .ReturnsAsync(ApiResponse<RoleDto>.Error(
                "只有管理员可以维护全局角色与权限定义",
                "ADMIN_REQUIRED"
            ));
        var controller = new RolesController(
            roleService.Object,
            NullLogger<RolesController>.Instance
        );

        var action = await controller.CreateRole(new CreateRoleDto
        {
            RoleName = "ForgedRole",
            IsActive = true,
        });

        var forbidden = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var body = Assert.IsType<ApiResponse<RoleDto>>(forbidden.Value);
        Assert.Equal("ADMIN_REQUIRED", body.ErrorCode);
    }

    [Fact]
    public async Task RolesController_DuplicateAdminRequiredReturns403WithStableErrorBody()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.DuplicateRoleAsync("role-source", "CopiedRole", null))
            .ReturnsAsync(ApiResponse<RoleDto>.Error("只有管理员可以复制角色", "ADMIN_REQUIRED"));
        var controller = new RolesController(
            roleService.Object,
            NullLogger<RolesController>.Instance
        );

        var action = await controller.DuplicateRole(
            "role-source",
            new DuplicateRoleDto { NewRoleName = "CopiedRole" }
        );

        var forbidden = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(
            "ADMIN_REQUIRED",
            Assert.IsType<ApiResponse<RoleDto>>(forbidden.Value).ErrorCode
        );
    }

    [Fact]
    public async Task RolesController_LegacyPermissionReadAdminRequiredReturns403WithStableErrorBody()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.GetSysPermissionsAsync())
            .ReturnsAsync(ApiResponse<List<SysPermission>>.Error(
                "只有管理员可以读取全局角色权限",
                "ADMIN_REQUIRED"
            ));
        var controller = new RolesController(
            roleService.Object,
            NullLogger<RolesController>.Instance
        );

        var action = await controller.GetSysPermissions();

        var forbidden = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(
            "ADMIN_REQUIRED",
            Assert.IsType<ApiResponse<List<SysPermission>>>(forbidden.Value).ErrorCode
        );
    }

    [Fact]
    public async Task RolesController_CheckUserEndpoints_EmployeeCannotProbeArbitraryUser()
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasRoleAsync("employee-1", It.IsAny<string>()))
            .ReturnsAsync(ApiResponse<bool>.OK(false));
        var controller = new RolesController(
            roleService.Object,
            NullLogger<RolesController>.Instance
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "employee-1") },
                        "TestAuth"
                    )),
                },
            },
        };

        var roleResult = await controller.CheckUserHasRole("target-1", "Admin");
        var permissionResult = await controller.CheckUserHasPermission(
            "target-1",
            Permissions.Users.View
        );

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(roleResult).StatusCode
        );
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(permissionResult).StatusCode
        );
        roleService.Verify(
            service => service.UserHasRoleAsync("target-1", It.IsAny<string>()),
            Times.Never
        );
        roleService.Verify(
            service => service.UserHasPermissionAsync("target-1", It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_EmployeeCannotDelegateEvenWithOwnedPermission()
    {
        await SeedUserWithRoleAsync("employee-1", "role-employee", "User");
        await _db.Insertable(new User
        {
            UserGUID = "staff-1",
            Username = "staff-1",
            Email = "staff-1@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            // 刻意制造员工拥有主分店及管理权限的异常数据，验证角色门禁不能被脏数据绕过。
            CreateUserStore("employee-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertRolePermissionAsync("role-employee", Permissions.Users.View);

        var result = await CreateService(
            "employee-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "employee-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "staff-1",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string> { Permissions.Users.View },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("ACCESS_DELEGATOR_DENIED", result.ErrorCode);
        Assert.False(await _db.Queryable<SysUserPermission>().AnyAsync(item =>
            item.UserGuid == "staff-1"
        ));
    }

    [Fact]
    public async Task GetUserAccessPermissionsAsync_StoreManagerReceivesOnlyOwnedPermissionIntersection()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await SeedUserWithRoleAsync("staff-1", "role-staff", "User");
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertPermissionAsync(Permissions.Reports.View);
        await InsertRolePermissionAsync("role-manager", Permissions.Users.View);
        await InsertRolePermissionAsync("role-staff", Permissions.Reports.View);
        await InsertUserPermissionAsync("staff-1", Permissions.Users.View);

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).GetUserAccessPermissionsAsync("manager-1", "staff-1");

        Assert.True(result.Success);
        var data = Assert.IsType<UserAccessPermissionDto>(result.Data);
        Assert.Equal(new[] { Permissions.Users.View }, data.State.DirectPermissionCodes);
        Assert.Equal(new[] { Permissions.Users.View }, data.State.EffectivePermissionCodes);
        Assert.Empty(data.State.InheritedPermissionCodes);
        var permission = Assert.Single(Assert.Single(data.Categories).Permissions);
        Assert.Equal(Permissions.Users.View, permission.Name);
    }

    [Fact]
    public async Task GetUserAccessPermissionsAsync_AdminReceivesFullStateAndCatalog()
    {
        await SeedUserWithRoleAsync("admin-1", "role-admin", "Admin");
        await SeedUserWithRoleAsync("staff-1", "role-staff", "User");
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertPermissionAsync(Permissions.Reports.View);
        await InsertRolePermissionAsync("role-staff", Permissions.Reports.View);
        await InsertUserPermissionAsync("staff-1", Permissions.Users.View);

        var result = await CreateService(
            "admin-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                IsAdmin = true,
                UserGuid = "admin-1",
            })
        ).GetUserAccessPermissionsAsync("admin-1", "staff-1");

        Assert.True(result.Success);
        var data = Assert.IsType<UserAccessPermissionDto>(result.Data);
        Assert.Contains(Permissions.Users.View, data.State.DirectPermissionCodes);
        Assert.Contains(Permissions.Reports.View, data.State.InheritedPermissionCodes);
        var permissionCodes = data.Categories
            .SelectMany(category => category.Permissions)
            .Select(permission => permission.Name)
            .ToList();
        Assert.Contains(Permissions.Users.View, permissionCodes);
        Assert.Contains(Permissions.Reports.View, permissionCodes);
    }

    [Fact]
    public async Task GetUserAccessPermissionsAsync_StoreManagerWithInactivePrimaryStoreIsDenied()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await SeedUserWithRoleAsync("staff-1", "role-staff", "User");
        await _db.Insertable(new Store
        {
            StoreGUID = "store-inactive",
            StoreCode = "S000",
            StoreName = "Inactive Store",
            IsActive = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-inactive", true),
            CreateUserStore("staff-1", "store-inactive", false),
        }).ExecuteCommandAsync();

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-inactive" },
            })
        ).GetUserAccessPermissionsAsync("manager-1", "staff-1");

        Assert.Equal("ACCESS_DELEGATOR_DENIED", result.ErrorCode);
    }

    [Fact]
    public async Task AddUsersToRoleAsync_EmployeeCannotUseRoleSideBypass()
    {
        await SeedUserWithRoleAsync("employee-1", "role-employee", "User");
        await _db.Insertable(new User
        {
            UserGUID = "target-1",
            Username = "target-1",
            Email = "target-1@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();

        var result = await CreateService(
            "employee-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "employee-1",
            })
        ).AddUsersToRoleAsync("role-employee", new List<string> { "target-1" });

        Assert.False(result.Success);
        Assert.Equal("ADMIN_REQUIRED", result.ErrorCode);
        Assert.False(await _db.Queryable<UserRole>().AnyAsync(item =>
            item.UserGUID == "target-1" && item.RoleGUID == "role-employee"
        ));
    }

    [Fact]
    public async Task RoleDefinitionWrites_EmployeeWithForgedRolePermissionsCannotMutateGlobalDefinitions()
    {
        await SeedUserWithRoleAsync("employee-1", "role-employee", "User");
        await InsertRoleAsync("role-target", "TargetRole");
        await InsertPermissionAsync("Permissions.Test.Delete");
        await InsertRolePermissionAsync("role-target", "Permissions.Test.Delete");
        var service = CreateService(
            "employee-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "employee-1",
            })
        );

        var results = new[]
        {
            (await service.CreateRoleAsync(new CreateRoleDto
            {
                RoleName = "ForgedRole",
                IsActive = true,
            })).ErrorCode,
            (await service.UpdateRoleByGuidAsync("role-target", new UpdateRoleDto
            {
                RoleName = "ChangedRole",
                IsActive = true,
            })).ErrorCode,
            (await service.DeleteRoleByGuidAsync("role-target")).ErrorCode,
            (await service.UpdateRoleStatusByGuidAsync("role-target", false)).ErrorCode,
            (await service.BatchManageRolesAsync(new BatchRoleOperationDto
            {
                Operation = "deactivate",
                RoleGuids = new List<string> { "role-target" },
            })).ErrorCode,
            (await service.AssignRolesToPermissionAsync(
                "Permissions.Test.Delete",
                new List<string>()
            )).ErrorCode,
            (await service.CreatePermissionAsync(new CreateSysPermissionDto
            {
                Code = "Permissions.Test.Create",
                Name = "Test Create",
                Category = "test",
            })).ErrorCode,
            (await service.DeletePermissionAsync("Permissions.Test.Delete")).ErrorCode,
        };

        Assert.All(results, errorCode => Assert.Equal("ADMIN_REQUIRED", errorCode));
        Assert.False(await _db.Queryable<Role>().AnyAsync(item => item.RoleName == "ForgedRole"));
        Assert.True(await _db.Queryable<Role>().AnyAsync(item =>
            item.RoleGUID == "role-target" && item.RoleName == "TargetRole" && item.IsActive
        ));
        Assert.True(await _db.Queryable<SysPermission>().AnyAsync(item =>
            item.Code == "Permissions.Test.Delete" && !item.IsDeleted
        ));
    }

    [Fact]
    public async Task DuplicateRoleAsync_EmployeeWithForgedCreatePermissionCannotCopyRole()
    {
        await SeedUserWithRoleAsync("employee-1", "role-employee", "User");
        await InsertRoleAsync("role-source", "SourceRole");
        var service = CreateService(
            "employee-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "employee-1",
            })
        );

        var result = await service.DuplicateRoleAsync("role-source", "CopiedRole");

        Assert.Equal("ADMIN_REQUIRED", result.ErrorCode);
        Assert.False(await _db.Queryable<Role>().AnyAsync(item => item.RoleName == "CopiedRole"));
    }

    [Fact]
    public async Task LegacyPermissionStateAndCatalog_StoreManagerCannotBypassScopedEndpoint()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await SeedUserWithRoleAsync("staff-1", "role-staff", "User");
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        var service = CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        );

        var state = await service.GetUserPermissionStateAsync("staff-1");
        var categories = await service.GetPermissionsAsync();
        var catalog = await service.GetPermissionCatalogAsync();
        var rolePermissions = await service.GetRolePermissionsAsync("role-staff");
        var roleState = await service.GetRolePermissionStateAsync("role-staff");
        var systemPermissions = await service.GetSysPermissionsAsync();
        var permissionRoles = await service.GetPermissionRolesAsync(Permissions.Users.View);

        Assert.Equal("ADMIN_REQUIRED", state.ErrorCode);
        Assert.Equal("ADMIN_REQUIRED", categories.ErrorCode);
        Assert.Equal("ADMIN_REQUIRED", catalog.ErrorCode);
        Assert.Equal("ADMIN_REQUIRED", rolePermissions.ErrorCode);
        Assert.Equal("ADMIN_REQUIRED", roleState.ErrorCode);
        Assert.Equal("ADMIN_REQUIRED", systemPermissions.ErrorCode);
        Assert.Equal("ADMIN_REQUIRED", permissionRoles.ErrorCode);
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_ReplacesOnlyDirectUserPermissions()
    {
        await SeedUserWithRoleAsync("user-1", "role-store", "StoreManager");
        await InsertPermissionAsync(Permissions.Reports.View);
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertRolePermissionAsync("role-store", Permissions.Attendance.Schedule.ViewStore);
        await InsertUserPermissionAsync("user-1", Permissions.Reports.View);

        var result = await (await CreateAdminServiceAsync())
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

    [Fact]
    public async Task AssignPermissionsToUserAsync_AdminCanModifyOwnDirectPermissions()
    {
        await SeedUserWithRoleAsync("admin-self", "role-admin-self", "Admin");
        await InsertPermissionAsync(Permissions.Users.View);
        var service = CreateService(
            "admin-self",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                IsAdmin = true,
                UserGuid = "admin-self",
            })
        );

        var result = await service.AssignPermissionsToUserAsync(
            "admin-self",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string> { Permissions.Users.View },
            }
        );

        Assert.True(result.Success);
        Assert.True(await _db.Queryable<SysUserPermission>().AnyAsync(item =>
            item.UserGuid == "admin-self" && item.PermissionCode == Permissions.Users.View
        ));
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_NonAdminCannotGrantPermissionItDoesNotOwn()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await _db.Insertable(new User
        {
            UserGUID = "staff-1",
            Username = "staff-1",
            Email = "staff-1@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.Users.ManageRoles);

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "staff-1",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string> { Permissions.Users.ManageRoles },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("PERMISSION_ESCALATION_DENIED", result.ErrorCode);
        Assert.False(await _db.Queryable<SysUserPermission>().AnyAsync(item =>
            item.UserGuid == "staff-1"
        ));
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_NonAdminPreservesExistingPermissionItDoesNotOwn()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await _db.Insertable(new User
        {
            UserGUID = "staff-1",
            Username = "staff-1",
            Email = "staff-1@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertPermissionAsync(Permissions.Reports.View);
        await InsertRolePermissionAsync("role-manager", Permissions.Users.View);
        await InsertUserPermissionAsync("staff-1", Permissions.Reports.View);

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "staff-1",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string> { Permissions.Users.View },
            }
        );

        Assert.True(result.Success);
        var permissionCodes = await _db.Queryable<SysUserPermission>()
            .Where(item => item.UserGuid == "staff-1")
            .Select(item => item.PermissionCode)
            .ToListAsync();
        Assert.Contains(Permissions.Users.View, permissionCodes);
        Assert.Contains(Permissions.Reports.View, permissionCodes);
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_NonAdminCannotEchoExistingPermissionItDoesNotOwn()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await _db.Insertable(new User
        {
            UserGUID = "staff-1",
            Username = "staff-1",
            Email = "staff-1@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertPermissionAsync(Permissions.Reports.View);
        await InsertRolePermissionAsync("role-manager", Permissions.Users.View);
        await InsertUserPermissionAsync("staff-1", Permissions.Reports.View);

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "staff-1",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string>
                {
                    Permissions.Users.View,
                    Permissions.Reports.View,
                },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("PERMISSION_ESCALATION_DENIED", result.ErrorCode);
        var permissionCodes = await _db.Queryable<SysUserPermission>()
            .Where(item => item.UserGuid == "staff-1")
            .Select(item => item.PermissionCode)
            .ToListAsync();
        Assert.DoesNotContain(Permissions.Users.View, permissionCodes);
        Assert.Contains(Permissions.Reports.View, permissionCodes);
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_NonAdminCannotUseAliasEchoToAddCanonicalPermission()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await SeedUserWithRoleAsync("staff-1", "role-staff", "User");
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.LocalPurchase.View);
        await InsertPermissionAsync("LocalInvocie.View");
        await InsertUserPermissionAsync("staff-1", "LocalInvocie.View");

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "staff-1",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string> { Permissions.LocalPurchase.View },
            }
        );

        Assert.Equal("PERMISSION_ESCALATION_DENIED", result.ErrorCode);
        var storedCodes = await _db.Queryable<SysUserPermission>()
            .Where(item => item.UserGuid == "staff-1")
            .Select(item => item.PermissionCode)
            .ToListAsync();
        Assert.Equal(new[] { "LocalInvocie.View" }, storedCodes);
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_NonAdminCannotModifyCrossStoreTargetGlobally()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await _db.Insertable(new User
        {
            UserGUID = "staff-1",
            Username = "staff-1",
            Email = "staff-1@example.test",
            PasswordHash = "hash",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            new Store
            {
                StoreGUID = "store-1",
                StoreCode = "S001",
                StoreName = "Store 1",
                IsActive = true,
            },
            new Store
            {
                StoreGUID = "store-2",
                StoreCode = "S002",
                StoreName = "Store 2",
                IsActive = true,
            },
        }).ExecuteCommandAsync();
        await _db.Insertable(new[]
        {
            CreateUserStore("manager-1", "store-1", true),
            CreateUserStore("staff-1", "store-1", false),
            CreateUserStore("staff-1", "store-2", false),
        }).ExecuteCommandAsync();
        await InsertPermissionAsync(Permissions.Users.View);
        await InsertRolePermissionAsync("role-manager", Permissions.Users.View);

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "staff-1",
            new UserPermissionAssignmentDto
            {
                Permissions = new List<string> { Permissions.Users.View },
            }
        );

        Assert.False(result.Success);
        Assert.Equal("USER_SCOPE_DENIED", result.ErrorCode);
    }

    [Fact]
    public async Task AssignPermissionsToUserAsync_CannotModifySelf()
    {
        await SeedUserWithRoleAsync("manager-1", "role-manager", "StoreManager");
        await _db.Insertable(new Store
        {
            StoreGUID = "store-1",
            StoreCode = "S001",
            StoreName = "Store 1",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(CreateUserStore("manager-1", "store-1", true))
            .ExecuteCommandAsync();

        var result = await CreateService(
            "manager-1",
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                UserGuid = "manager-1",
                StoreGuids = new[] { "store-1" },
            })
        ).AssignPermissionsToUserAsync(
            "manager-1",
            new UserPermissionAssignmentDto()
        );

        Assert.False(result.Success);
        Assert.Equal("SELF_ACCESS_MANAGEMENT_DENIED", result.ErrorCode);
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

    private RoleService CreateService(
        string? currentUserGuid = null,
        ICurrentUserManageableStoreScopeService? manageableStoreScopeService = null
    )
    {
        var accessor = new HttpContextAccessor();
        if (!string.IsNullOrWhiteSpace(currentUserGuid))
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[]
                        {
                            new System.Security.Claims.Claim(
                                System.Security.Claims.ClaimTypes.NameIdentifier,
                                currentUserGuid
                            ),
                        },
                        "TestAuth"
                    )
                ),
            };
        }

        return new RoleService(
            CreateSqlSugarContext(_db),
            NullLogger<RoleService>.Instance,
            accessor,
            manageableStoreScopeService
        );
    }

    private async Task<RoleService> CreateAdminServiceAsync()
    {
        const string adminUserGuid = "__test-admin";
        if (!await _db.Queryable<User>().AnyAsync(item => item.UserGUID == adminUserGuid))
        {
            await SeedUserWithRoleAsync(adminUserGuid, "__test-admin-role", "Admin");
        }

        return CreateService(
            adminUserGuid,
            new FakeManageableStoreScopeService(new CurrentUserManageableStoreScope
            {
                IsAllowed = true,
                IsAuthenticated = true,
                IsAdmin = true,
                UserGuid = adminUserGuid,
            })
        );
    }

    private static UserStore CreateUserStore(
        string userGuid,
        string storeGuid,
        bool isPrimary
    ) => new()
    {
        UserStoreGUID = Guid.NewGuid().ToString(),
        UserGUID = userGuid,
        StoreGUID = storeGuid,
        IsPrimary = isPrimary,
        IsDeleted = false,
    };

    private sealed class FakeManageableStoreScopeService
        : ICurrentUserManageableStoreScopeService
    {
        private readonly CurrentUserManageableStoreScope _scope;

        public FakeManageableStoreScopeService(CurrentUserManageableStoreScope scope)
        {
            _scope = scope;
        }

        public Task<CurrentUserManageableStoreScope> GetScopeAsync() => Task.FromResult(_scope);
        public Task<IReadOnlyList<string>> GetAccessibleStoreCodesAsync() =>
            Task.FromResult(_scope.StoreCodes);
        public Task<bool> CanAccessStoreCodeAsync(string storeCode) =>
            Task.FromResult(_scope.CanAccessStoreCode(storeCode));
        public Task<bool> CanAccessOrderAsync(string orderGuid) => Task.FromResult(false);
        public Task<bool> CanManageStoreAsync(string storeGuid) =>
            Task.FromResult(_scope.CanAccessStoreGuid(storeGuid));
        public Task<bool> CanManageUserAsync(string userGuid) => Task.FromResult(false);
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
