using System.Security.Claims;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
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
    public void BuildAppMenu_HidesUsersForNonStoreManagerWithUsersViewPermission()
    {
        var user = CreateUser(new Claim("permission", Permissions.Users.View));

        var menu = _service.BuildAppMenu(user);

        Assert.DoesNotContain(menu, item => item.RouteName == "users");
    }

    [Fact]
    public void BuildAppMenu_ShowsDeviceManagementForAdmin()
    {
        var user = CreateUser(new Claim(ClaimTypes.Role, "Admin"));

        var menu = _service.BuildAppMenu(user);

        var item = Assert.Single(menu, item => item.RouteName == "device-management");
        Assert.Equal("tabs.deviceManagement", item.TitleKey);
        Assert.Equal("cellphone-cog", item.Icon);
        Assert.Equal(58, item.Order);
        Assert.Null(item.Permission);
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
    [InlineData(nameof(ReactLocalSupplierInvoicesController.CheckProducts))]
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

}
