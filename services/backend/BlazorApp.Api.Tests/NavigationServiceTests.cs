using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public class NavigationServiceTests
{
    private const string InstallmentOrdersPermission = "InstallmentOrders.View";
    private const string StoreVouchersPermission = "StoreVouchers.View";

    private readonly NavigationService _service = new();

    [Fact]
    public void BuildMenu_HidesBackendNavigationWithoutDashboardPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Attendance.Schedule.ViewStore));

        var menu = _service.BuildMenu(user);

        Assert.Empty(menu);
    }

    [Fact]
    public async Task BuildMenu_UsesDatabasePermissionsInsteadOfStalePermissionClaims()
    {
        using var harness = new NavigationTestHarness();
        await harness.SeedUserWithRoleAsync("user-1", "role-store", "StoreManager");

        var service = harness.CreateNavigationService();
        var user = CreateUserWithId(
            "user-1",
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.Attendance.Schedule.ViewStore)
        );

        var menu = service.BuildMenu(user);

        Assert.Empty(menu);
    }

    [Fact]
    public async Task BuildMenu_UsesDatabaseAggregatedPermissionsForAuthenticatedUser()
    {
        using var harness = new NavigationTestHarness();
        await harness.SeedUserWithRoleAsync(
            "user-1",
            "role-store",
            "StoreManager",
            Permissions.Dashboard.View,
            Permissions.Attendance.Schedule.ViewStore
        );

        var service = harness.CreateNavigationService();
        var user = CreateUserWithId("user-1");

        var menu = service.BuildMenu(user);

        Assert.Contains(menu, item => item.Path == "/dashboard");
        var posAdmin = Assert.Single(menu, item => item.Path == "/pos-admin");
        Assert.Contains(posAdmin.Children!, item => item.Path == "/pos-admin/schedule-attendance");
    }

    [Fact]
    public async Task BuildMenu_UsesDatabaseAdminRoleWithoutExplicitPermissions()
    {
        using var harness = new NavigationTestHarness();
        await harness.SeedUserWithRoleAsync("user-1", "role-admin", "管理员");

        var service = harness.CreateNavigationService();
        var user = CreateUserWithId("user-1");

        var menu = service.BuildMenu(user);

        Assert.Equal(6, menu.Count);
        Assert.Contains(menu, item => item.Path == "/dashboard");
        Assert.Contains(menu, item => item.Path == "/system");
        Assert.Contains(menu, item => item.Path == "/warehouse");
        Assert.Contains(menu, item => item.Path == "/domestic-purchase");
        Assert.Contains(menu, item => item.Path == "/executive-sales-intelligence");
        Assert.Contains(menu, item => item.Path == "/pos-admin");
    }

    [Fact]
    public void BuildMenu_ShowsFullMenuForAdmin()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "Admin"));

        var menu = _service.BuildMenu(user);

        Assert.Equal(6, menu.Count);
        Assert.Contains(menu, item => item.Path == "/dashboard");
        Assert.Contains(menu, item => item.Path == "/system");
        Assert.Contains(menu, item => item.Path == "/warehouse");
        Assert.Contains(menu, item => item.Path == "/domestic-purchase");
        Assert.Contains(menu, item => item.Path == "/executive-sales-intelligence");
        Assert.Contains(menu, item => item.Path == "/pos-admin");

        var systemMenu = Assert.Single(menu, item => item.Path == "/system");
        Assert.Contains(systemMenu.Children!, item => item.Path == "/system/employee-profiles");

        var posAdminMenu = Assert.Single(menu, item => item.Path == "/pos-admin");
        Assert.Contains(posAdminMenu.Children!, item => item.Path == "/pos-admin/products");
        Assert.Contains(posAdminMenu.Children!, item => item.Path == "/pos-admin/local-supplier-invoices");
    }

    [Fact]
    public void BuildMenu_ShowsDashboardAndAuthorizedModuleWithDashboardPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.Attendance.Schedule.ViewStore)
        );

        var menu = _service.BuildMenu(user);

        Assert.Contains(menu, item => item.Path == "/dashboard");
        var posAdmin = Assert.Single(menu, item => item.Path == "/pos-admin");
        Assert.Contains(posAdmin.Children!, item => item.Path == "/pos-admin/schedule-attendance");
        Assert.DoesNotContain(posAdmin.Children!, item => item.Path == "/pos-admin/sales-orders");
    }

    [Fact]
    public void BuildMenu_ShowsAdvertisementsWithAdvertisementViewPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.Advertisements.View)
        );

        var menu = _service.BuildMenu(user);

        var posAdmin = Assert.Single(menu, item => item.Path == "/pos-admin");
        Assert.Contains(
            posAdmin.Children!,
            item =>
                item.Path == "/pos-admin/advertisements"
                && item.Permission == Permissions.Advertisements.View
        );
    }

    [Fact]
    public void BuildMenu_DoesNotUnlockNavigationForWarehouseManagerRoleWithoutPermissionClaims()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "WarehouseManager"));

        var menu = _service.BuildMenu(user);

        Assert.Empty(menu);
    }

    [Fact]
    public void BuildMenu_LimitsWarehouseStaffClaimsToStoreOrdersOnly()
    {
        var user = CreateUser(
            new Claim(ClaimTypes.Role, "WarehouseStaff"),
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.Warehouse.Manage),
            new Claim("permission", Permissions.Warehouse.ManageProducts),
            new Claim("permission", Permissions.Warehouse.ManageLocations),
            new Claim("permission", Permissions.Orders.View)
        );

        var menu = _service.BuildMenu(user);

        AssertWarehouseStaffStoreOrderMenuOnly(menu);
    }

    [Fact]
    public async Task BuildMenu_LimitsWarehouseStaffDatabasePermissionsToStoreOrdersOnly()
    {
        using var harness = new NavigationTestHarness();
        await harness.SeedUserWithRoleAsync(
            "user-1",
            "role-warehouse-staff",
            "WarehouseStaff",
            Permissions.Dashboard.View,
            Permissions.Warehouse.Manage,
            Permissions.Warehouse.ManageProducts,
            Permissions.Warehouse.ManageLocations,
            Permissions.Orders.View
        );

        var service = harness.CreateNavigationService();
        var user = CreateUserWithId("user-1");

        var menu = service.BuildMenu(user);

        AssertWarehouseStaffStoreOrderMenuOnly(menu);
    }

    [Fact]
    public void BuildMenu_ShowsDeviceRegistrationWithDeviceRegistrationViewPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.DeviceRegistration.View)
        );

        var menu = _service.BuildMenu(user);

        var systemMenu = Assert.Single(menu, item => item.Path == "/system");
        var item = Assert.Single(
            systemMenu.Children!,
            child => child.Path == "/system/device-registration"
        );
        Assert.Equal(Permissions.DeviceRegistration.View, item.Permission);
    }

    [Fact]
    public void BuildMenu_ShowsAppDownloadsWithAppDownloadsPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.System.ViewAppDownloads)
        );

        var menu = _service.BuildMenu(user);

        var systemMenu = Assert.Single(menu, item => item.Path == "/system");
        var item = Assert.Single(systemMenu.Children!, child => child.Path == "/system/app-downloads");
        Assert.Equal(Permissions.System.ViewAppDownloads, item.Permission);
    }

    [Fact]
    public void BuildMenu_HidesAppDownloadsWithoutAppDownloadsPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.DeviceRegistration.View)
        );

        var menu = _service.BuildMenu(user);

        var systemMenu = Assert.Single(menu, item => item.Path == "/system");
        Assert.DoesNotContain(systemMenu.Children!, child => child.Path == "/system/app-downloads");
    }

    [Fact]
    public void BuildMenu_ShowsEmployeeProfilesWithEmployeeProfileViewPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.EmployeeProfiles.View)
        );

        var menu = _service.BuildMenu(user);

        var systemMenu = Assert.Single(menu, item => item.Path == "/system");
        Assert.Contains(
            systemMenu.Children!,
            item =>
                item.Path == "/system/employee-profiles"
                && item.Permission == Permissions.EmployeeProfiles.View
        );
    }

    [Fact]
    public void BuildMenu_ShowsPosProductsWithPosProductsViewPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.PosProducts.View)
        );

        var menu = _service.BuildMenu(user);

        var posAdminMenu = Assert.Single(menu, item => item.Path == "/pos-admin");
        Assert.Contains(
            posAdminMenu.Children!,
            item =>
                item.Path == "/pos-admin/products"
                && item.Permission == Permissions.PosProducts.View
        );
    }

    [Fact]
    public void BuildMenu_HidesDeviceRegistrationWithStoreManageOperationsPermissionOnly()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Dashboard.View),
            new Claim("permission", Permissions.Store.ManageOperations)
        );

        var menu = _service.BuildMenu(user);

        var systemMenu = menu.SingleOrDefault(item => item.Path == "/system");
        Assert.True(
            systemMenu is null
                || systemMenu.Children is null
                || !systemMenu.Children.Any(child => child.Path == "/system/device-registration")
        );
    }

    [Fact]
    public void BuildMenu_HidesBackendNavigationForOrderFrontOnlyUser()
    {
        var user = CreateUser(new Claim("permission", Permissions.OrderFront.View));

        var menu = _service.BuildMenu(user);

        Assert.Empty(menu);
    }

    [Fact]
    public void BuildAppMenu_HidesEmployeeProfileWithoutEmployeeProfilePermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Orders.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "employee-profile");
    }

    [Fact]
    public async Task BuildAppMenu_UsesDatabasePermissionsInsteadOfStaleClaims()
    {
        using var harness = new NavigationTestHarness();
        await harness.SeedUserWithRoleAsync("user-1", "role-user", "User");

        var service = harness.CreateNavigationService();
        var user = CreateUserWithId("user-1", new Claim("permission", Permissions.LocalPurchase.View));

        var menu = service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "local-supplier-invoices");
    }

    [Fact]
    public async Task BuildAppMenu_UsesDatabaseAliasPermissionsForAuthenticatedUser()
    {
        using var harness = new NavigationTestHarness();
        await harness.SeedUserWithRoleAsync(
            "user-1",
            "role-user",
            "User",
            "LocalInvocie.View"
        );

        var service = harness.CreateNavigationService();
        var user = CreateUserWithId("user-1");

        var menu = service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "local-supplier-invoices");
    }

    [Fact]
    public void BuildAppMenu_ShowsFullMenuForAdmin()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "Admin"));

        var menu = _service.BuildAppMenu(user);

        Assert.Equal(18, menu.Count);
        Assert.Contains(menu, item => item.RouteName == "users");
        Assert.Contains(menu, item => item.RouteName == "employee-profile");
        Assert.Contains(menu, item => item.RouteName == "device-management");
        Assert.Contains(menu, item => item.RouteName == "attendance-personal");
        Assert.Contains(menu, item => item.RouteName == "attendance-management");
        Assert.Contains(menu, item => item.RouteName == "seasonal-cards");
        Assert.Contains(menu, item => item.RouteName == "advertisements");
        Assert.Contains(menu, item => item.RouteName == "promotions");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance");
        Assert.Contains(menu, item => item.RouteName == "local-supplier-invoices");
        Assert.Contains(menu, item => item.RouteName == "warehouse");
    }

    [Fact]
    public void BuildAppMenu_ShowsEmployeeProfileWithEmployeeProfileViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.EmployeeProfiles.View));

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "employee-profile");
    }

    [Fact]
    public void BuildAppMenu_ShowsLocalSupplierInvoicesWithLocalPurchaseViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.LocalPurchase.View));

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "local-supplier-invoices");
        Assert.Equal("tabs.localSupplierInvoices", item.TitleKey);
        Assert.Equal("receipt-text-outline", item.Icon);
        Assert.Equal(Permissions.LocalPurchase.View, item.Permission);
    }

    [Fact]
    public void BuildAppMenu_ShowsLocalSupplierInvoicesWithLegacyLocalInvoiceViewPermission()
    {
        var user = CreateUser(new Claim("permission", "LocalInvocie.View"));

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "local-supplier-invoices");
    }

    [Fact]
    public void BuildAppMenu_HidesLocalSupplierInvoicesWithoutLocalPurchaseViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Orders.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "local-supplier-invoices");
    }

    [Fact]
    public void BuildAppMenu_ShowsAdvertisementsWithAdvertisementViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Advertisements.View));

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "advertisements");
        Assert.Equal("tabs.advertisements", item.TitleKey);
        Assert.Equal(Permissions.Advertisements.View, item.Permission);
    }

    [Fact]
    public void BuildAppMenu_ShowsAttendancePersonalWithViewSelfPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Attendance.Schedule.ViewSelf)
        );

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "attendance-personal");
        Assert.Equal("tabs.attendancePersonal", item.TitleKey);
        Assert.Equal("calendar-clock", item.Icon);
        Assert.Equal(Permissions.Attendance.Schedule.ViewSelf, item.Permission);
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-management");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance");
    }

    [Fact]
    public void BuildAppMenu_ShowsAttendanceManagementWithManagedAttendancePermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.Attendance.Approval.ReviewManagedStore)
        );

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "attendance-management");
        Assert.Equal("tabs.attendanceManagement", item.TitleKey);
        Assert.Equal("calendar-clock", item.Icon);
        Assert.Equal(Permissions.Attendance.Schedule.ViewStore, item.Permission);
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-personal");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance");
    }

    [Fact]
    public void BuildAppMenu_HidesAttendanceRoutesWithoutAttendancePermissions()
    {
        var user = CreateUser(new Claim("permission", Permissions.Orders.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-personal");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-management");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance");
    }

    [Fact]
    public void BuildAppMenu_ShowsOrderRoutesWithoutAttendanceForOrderPermissions()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.OrderFront.View),
            new Claim("permission", Permissions.Orders.View),
            new Claim("permission", Permissions.Orders.Create)
        );

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "home");
        Assert.Contains(menu, item => item.RouteName == "orders");
        Assert.Contains(menu, item => item.RouteName == "cart");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-personal");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-management");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance");
    }

    [Fact]
    public void BuildAppMenu_ShowsSeasonalCardsWithViewPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.SeasonalCards.Remaining.ViewManagedStore)
        );

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, node => node.RouteName == "seasonal-cards");
        Assert.Equal("tabs.seasonalCards", item.TitleKey);
        Assert.Equal(Permissions.SeasonalCards.Remaining.ViewManagedStore, item.Permission);
    }

    [Fact]
    public void BuildAppMenu_ShowsSeasonalCardsWithSubmitPermission()
    {
        var user = CreateUser(
            new Claim("permission", Permissions.SeasonalCards.Remaining.SubmitManagedStore)
        );

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "seasonal-cards");
    }

    [Fact]
    public void BuildDeviceAppMenu_HidesLocalSupplierInvoicesForDeviceMode()
    {
        var menu = _service.BuildDeviceAppMenu("Mobile");

        Assert.DoesNotContain(menu, item => item.RouteName == "local-supplier-invoices");
    }

    [Fact]
    public void BuildAppMenu_HidesStoreFinanceRoutesWithoutDedicatedPermissions()
    {
        var user = CreateUser(new Claim("permission", Permissions.Orders.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "installment-orders");
        Assert.DoesNotContain(menu, item => item.RouteName == "store-vouchers");
    }

    [Fact]
    public void BuildAppMenu_ShowsStoreFinanceRoutesWithDedicatedPermissions()
    {
        var user = CreateUser(
            new Claim("permission", InstallmentOrdersPermission),
            new Claim("permission", StoreVouchersPermission)
        );

        var menu = _service.BuildAppMenu(user);

        var installmentOrders = Assert.Single(menu, item => item.RouteName == "installment-orders");
        Assert.Equal("tabs.installmentOrders", installmentOrders.TitleKey);
        Assert.Equal("cash-clock", installmentOrders.Icon);
        Assert.Equal(InstallmentOrdersPermission, installmentOrders.Permission);

        var storeVouchers = Assert.Single(menu, item => item.RouteName == "store-vouchers");
        Assert.Equal("tabs.storeVouchers", storeVouchers.TitleKey);
        Assert.Equal("ticket-percent-outline", storeVouchers.Icon);
        Assert.Equal(StoreVouchersPermission, storeVouchers.Permission);
    }

    [Fact]
    public void BuildAppMenu_HidesUsersForStoreManagerWithoutUsersViewPermission()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "StoreManager"));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void BuildAppMenu_DoesNotUnlockWarehouseForWarehouseManagerRoleWithoutPermissionClaims()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "WarehouseManager"));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "warehouse");
    }

    [Fact]
    public void BuildAppMenu_ShowsUsersForStoreManagerWithUsersViewPermission()
    {
        var user = CreateUser(
            new Claim(ClaimTypes.Role, "StoreManager"),
            new Claim("permission", Permissions.Users.View)
        );

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void BuildAppMenu_ShowsUsersWithUsersViewPermissionWithoutRoleGate()
    {
        var user = CreateUser(new Claim("permission", Permissions.Users.View));

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void BuildAppMenu_ShowsDeviceManagementForAdmin()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "Admin"));

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "device-management");
        Assert.Equal("tabs.deviceManagement", item.TitleKey);
        Assert.Equal("cellphone-cog", item.Icon);
        Assert.Equal(59, item.Order);
        Assert.Equal(Permissions.DeviceRegistration.View, item.Permission);
    }

    [Fact]
    public void BuildAppMenu_ShowsDeviceManagementWithDeviceRegistrationViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.DeviceRegistration.View));

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "device-management");
        Assert.Equal(Permissions.DeviceRegistration.View, item.Permission);
    }

    [Fact]
    public void BuildAppMenu_ShowsDeviceManagementForChineseAdmin()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "管理员"));

        var menu = _service.BuildAppMenu(user);

        Assert.Contains(menu, item => item.RouteName == "device-management");
    }

    [Fact]
    public void BuildAppMenu_HidesDeviceManagementForStoreManagerWithManageOperationsPermission()
    {
        var user = CreateUser(
            new Claim(ClaimTypes.Role, "StoreManager"),
            new Claim("permission", Permissions.Store.ManageOperations)
        );

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "device-management");
    }

    [Fact]
    public void BuildAppMenu_HidesDeviceManagementForUserWithManageOperationsPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Store.ManageOperations));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "device-management");
    }

    [Fact]
    public void BuildDeviceAppMenu_HidesDeviceManagementForMobileDevice()
    {
        var menu = _service.BuildDeviceAppMenu("Mobile");

        Assert.DoesNotContain(menu, item => item.RouteName == "device-management");
    }

    [Fact]
    public void BuildDeviceAppMenu_HidesAttendanceRoutesForDeviceMode()
    {
        var menu = _service.BuildDeviceAppMenu("Mobile");

        Assert.DoesNotContain(menu, item => item.RouteName == "attendance");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-personal");
        Assert.DoesNotContain(menu, item => item.RouteName == "attendance-management");
    }

    [Fact]
    public void BuildDeviceAppMenu_HidesDeviceManagementForWarehousePdaButKeepsWarehouse()
    {
        var menu = _service.BuildDeviceAppMenu("WarehousePDA");

        Assert.DoesNotContain(menu, item => item.RouteName == "device-management");
        Assert.Contains(menu, item => item.RouteName == "warehouse");
    }

    [Fact]
    public void EmployeeProfileSelfRead_RequiresEmployeeProfileViewPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(nameof(EmployeeProfilesController.GetSelf));

        Assert.Equal(Permissions.EmployeeProfiles.View, authorizeAttribute.Policy);
    }

    [Fact]
    public void EmployeeProfileSelfSave_RequiresEmployeeProfileEditPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(nameof(EmployeeProfilesController.UpsertSelf));

        Assert.Equal(Permissions.EmployeeProfiles.Edit, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersGrid_RequiresUsersViewPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.Grid)
        );

        Assert.Equal(Permissions.Users.View, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersCreate_RequiresUsersCreatePermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.Create)
        );

        Assert.Equal(Permissions.Users.Create, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersUpdate_RequiresUsersEditPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.Update)
        );

        Assert.Equal(Permissions.Users.Edit, authorizeAttribute.Policy);
    }

    [Fact]
    public void StoreUsersPassword_RequiresUsersResetPasswordPermission()
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactStoreUsersController),
            nameof(ReactStoreUsersController.UpdatePassword)
        );

        Assert.Equal(Permissions.Users.ResetPassword, authorizeAttribute.Policy);
    }

    [Theory]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.Grid))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetInvoice))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetDetailsGrid))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetBarcodeAbnormalDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetProductsByBarcode))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetProductsByProductCode))]
    public void LocalSupplierInvoiceReadEndpoints_RequireLocalPurchaseViewPermission(
        string methodName
    )
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactLocalSupplierInvoicesController),
            methodName
        );

        Assert.Equal(Permissions.LocalPurchase.View, authorizeAttribute.Policy);
    }

    [Theory]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.Create))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.Update))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.BatchUpsertDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.Delete))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.DetectSupplierItem))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.DetectBarcode))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.UpdateToStorePrices))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.StartUpdateToStorePricesJob))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetUpdateToStorePricesJob))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.CheckProducts))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.PasteDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.UpdateDetailAction))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.BatchUpdateDetailAction))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.DeleteDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.CheckInvoiceNoExists))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.BatchExecuteActions))]
    public void LocalSupplierInvoiceWriteEndpoints_RequireLocalPurchaseEditPermission(
        string methodName
    )
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(ReactLocalSupplierInvoicesController),
            methodName
        );

        Assert.Equal(Permissions.LocalPurchase.Edit, authorizeAttribute.Policy);
    }

    [Fact]
    public void WarehouseCategoryTree_AllowsAnonymousReadForDeviceHome()
    {
        var method = typeof(ReactWarehouseCategoriesController).GetMethod(
            nameof(ReactWarehouseCategoriesController.GetTree)
        ) ?? throw new InvalidOperationException("GetTree was not found.");

        Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false));
    }

    [Theory]
    [InlineData(nameof(DeviceRegistrationController.GetDevicesPaged), Permissions.DeviceRegistration.View)]
    [InlineData(nameof(DeviceRegistrationController.GetMobileAdminDevicesPaged), Permissions.DeviceRegistration.View)]
    [InlineData(nameof(DeviceRegistrationController.ActivateDevice), Permissions.DeviceRegistration.Manage)]
    [InlineData(nameof(DeviceRegistrationController.ActivateMobileAdminDevice), Permissions.DeviceRegistration.Manage)]
    [InlineData(nameof(DeviceRegistrationController.DisableDevice), Permissions.DeviceRegistration.Manage)]
    [InlineData(nameof(DeviceRegistrationController.DisableMobileAdminDevice), Permissions.DeviceRegistration.Manage)]
    [InlineData(nameof(DeviceRegistrationController.LockDevice), Permissions.DeviceRegistration.Manage)]
    [InlineData(nameof(DeviceRegistrationController.LockMobileAdminDevice), Permissions.DeviceRegistration.Manage)]
    public void DeviceManagementEndpoints_RequireExpectedPermissionPolicy(
        string methodName,
        string expectedPolicy
    )
    {
        var authorizeAttribute = GetMethodAuthorizeAttribute(
            typeof(DeviceRegistrationController),
            methodName
        );

        Assert.Equal(expectedPolicy, authorizeAttribute.Policy);
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal CreateUserWithId(string userGuid, params Claim[] claims)
    {
        var identityClaims = new List<Claim> { new(ClaimTypes.NameIdentifier, userGuid) };
        identityClaims.AddRange(claims);
        return CreateUser(identityClaims.ToArray());
    }

    private static void AssertWarehouseStaffStoreOrderMenuOnly(List<NavigationMenuDto> menu)
    {
        var warehouseMenu = Assert.Single(menu);
        Assert.Equal("/warehouse", warehouseMenu.Path);

        var storeOrderMenu = Assert.Single(warehouseMenu.Children!);
        Assert.Equal("/warehouse/store-orders", storeOrderMenu.Path);
        Assert.DoesNotContain(menu, item => item.Path == "/dashboard");
        Assert.DoesNotContain(menu, item => item.Path == "/pos-admin");
        Assert.DoesNotContain(
            warehouseMenu.Children!,
            item => item.Path == "/warehouse/products"
        );
        Assert.DoesNotContain(
            warehouseMenu.Children!,
            item => item.Path == "/warehouse/categories"
        );
        Assert.DoesNotContain(
            warehouseMenu.Children!,
            item => item.Path == "/warehouse/locations"
        );
    }

    private static AuthorizeAttribute GetMethodAuthorizeAttribute(string methodName)
    {
        return GetMethodAuthorizeAttribute(typeof(EmployeeProfilesController), methodName);
    }

    private static AuthorizeAttribute GetMethodAuthorizeAttribute(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .OfType<AuthorizeAttribute>()
            .Single(item => !string.IsNullOrWhiteSpace(item.Policy));
    }

    private sealed class NavigationTestHarness : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public NavigationTestHarness()
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

        public NavigationService CreateNavigationService()
        {
            var roleService = new RoleService(
                CreateSqlSugarContext(_db),
                NullLogger<RoleService>.Instance,
                new HttpContextAccessor()
            );

            return new NavigationService(roleService);
        }

        public async Task SeedUserWithRoleAsync(
            string userGuid,
            string roleGuid,
            string roleName,
            params string[] permissions
        )
        {
            await _db.Insertable(new User
            {
                UserGUID = userGuid,
                Username = userGuid,
                Email = $"{userGuid}@example.test",
                PasswordHash = "hash",
                IsActive = true,
            }).ExecuteCommandAsync();

            await _db.Insertable(new Role
            {
                RoleGUID = roleGuid,
                RoleName = roleName,
                IsActive = true,
            }).ExecuteCommandAsync();

            await _db.Insertable(new UserRole
            {
                UserRoleGUID = $"{userGuid}-{roleGuid}",
                UserGUID = userGuid,
                RoleGUID = roleGuid,
                IsDeleted = false,
            }).ExecuteCommandAsync();

            if (permissions.Length == 0)
            {
                return;
            }

            var rolePermissions = permissions
                .Select(permission => new SysRolePermission
                {
                    Id = $"{roleGuid}-{permission}",
                    RoleGuid = roleGuid,
                    PermissionCode = permission,
                    IsDeleted = false,
                })
                .ToList();

            await _db.Insertable(rolePermissions).ExecuteCommandAsync();
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

}
