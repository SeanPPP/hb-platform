using System.Security.Claims;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services
{
    public interface INavigationService
    {
        List<NavigationMenuDto> BuildMenu(ClaimsPrincipal user);
        List<AppNavigationMenuDto> BuildAppMenu(ClaimsPrincipal user);
        List<AppNavigationMenuDto> BuildDeviceAppMenu(string? deviceType);
    }

    public class NavigationService : INavigationService
    {
        private sealed class AppNavigationDefinition
        {
            public string RouteName { get; init; } = string.Empty;
            public string TitleKey { get; init; } = string.Empty;
            public string Icon { get; init; } = string.Empty;
            public string? Permission { get; init; }
            public string[] Roles { get; init; } = Array.Empty<string>();
            public bool RequireAdmin { get; init; }
            public int Order { get; init; }
        }

        /// <summary>
        /// 完整导航树（与前端 routes.tsx 结构一致）
        /// Permission 字段为空表示所有人可见
        /// </summary>
        private static readonly List<NavigationMenuDto> FullMenu = new()
        {
            new()
            {
                Path = "/dashboard",
                TitleKey = "menu.dashboard",
                Icon = "DashboardOutlined",
            },
            new()
            {
                Path = "/system",
                TitleKey = "menu.system",
                Icon = "SettingOutlined",
                Children = new List<NavigationMenuDto>
                {
                    new() { Path = "/system/stores",   TitleKey = "menu.systemStores",      Icon = "ShopOutlined",    Permission = Permissions.Stores.View },
                    new() { Path = "/system/users",    TitleKey = "menu.systemUsers",       Icon = "UserOutlined",     Permission = Permissions.Users.View },
                    new() { Path = "/system/employee-profiles", TitleKey = "menu.employeeProfiles", Icon = "IdcardOutlined", Permission = Permissions.EmployeeProfiles.View, RequireAdmin = true },
                    new() { Path = "/system/roles",    TitleKey = "menu.systemRoles",       Icon = "TeamOutlined",     Permission = Permissions.Roles.View },
                    new() { Path = "/system/permissions", TitleKey = "menu.systemPermissions", Icon = "KeyOutlined", Permission = Permissions.Roles.View },
                    new() { Path = "/system/device-registration", TitleKey = "menu.deviceRegistration", Icon = "BuildOutlined", Permission = Permissions.Store.ManageOperations },
                },
            },
            new()
            {
                Path = "/warehouse",
                TitleKey = "menu.warehouse",
                Icon = "DatabaseOutlined",
                Children = new List<NavigationMenuDto>
                {
                    new() { Path = "/warehouse/store-orders", TitleKey = "menu.storeOrders", Icon = "ReconciliationOutlined", Permission = Permissions.Warehouse.ManageOrders },
                    new() { Path = "/warehouse/containers",   TitleKey = "menu.containers", Icon = "InboxOutlined",          Permission = Permissions.Container.View },
                    new() { Path = "/warehouse/products",     TitleKey = "menu.warehouseProducts", Icon = "AppstoreOutlined",   Permission = Permissions.Warehouse.ManageProducts },
                    new() { Path = "/warehouse/categories",   TitleKey = "menu.categories",        Icon = "TagsOutlined",       Permission = Permissions.Warehouse.ManageCategories },
                    new() { Path = "/warehouse/locations",    TitleKey = "menu.warehouseLocations", Icon = "EnvironmentOutlined", Permission = Permissions.Warehouse.ManageLocations },
                    new() { Path = "/warehouse/product-grade-management", TitleKey = "menu.productGradeManagement", Icon = "TrophyOutlined", Permission = Permissions.Warehouse.ManageProducts },
                },
            },
            new()
            {
                Path = "/domestic-purchase",
                TitleKey = "menu.domesticPurchase",
                Icon = "ShoppingCartOutlined",
                Children = new List<NavigationMenuDto>
                {
                    new() { Path = "/domestic-purchase/china-suppliers",       TitleKey = "menu.chinaSuppliers",        Icon = "BankOutlined",       Permission = Permissions.DomesticPurchase.ManageSuppliers },
                    new() { Path = "/domestic-purchase/domestic-products",     TitleKey = "menu.domesticProducts",      Icon = "AppstoreOutlined",   Permission = Permissions.Products.View },
                    new() { Path = "/domestic-purchase/prefix-code-management", TitleKey = "menu.prefixCodeManagement",  Icon = "NumberOutlined",     Permission = Permissions.DomesticPurchase.ManagePrefixCodes },
                    new() { Path = "/domestic-purchase/product-creation",       TitleKey = "menu.productCreation",       Icon = "BuildOutlined",      Permission = Permissions.DomesticPurchase.ManageProducts },
                    new() { Path = "/domestic-purchase/product-import",        TitleKey = "menu.productImport",         Icon = "InboxOutlined",      Permission = Permissions.DomesticPurchase.ManageProducts },
                },
            },
            new()
            {
                Path = "/executive-sales-intelligence",
                TitleKey = "menu.executiveSalesIntelligence",
                Icon = "BarChartOutlined",
                Children = new List<NavigationMenuDto>
                {
                    new() { Path = "/executive-sales-intelligence/overview",       TitleKey = "menu.salesData",   Icon = "DashboardOutlined", Permission = Permissions.Reports.View },
                    new() { Path = "/executive-sales-intelligence/sales-detail-v2", TitleKey = "menu.salesDetail", Icon = "FileTextOutlined",  Permission = Permissions.Reports.View },
                },
            },
            new()
            {
                Path = "/pos-admin",
                TitleKey = "menu.posAdmin",
                Icon = "WalletOutlined",
                Children = new List<NavigationMenuDto>
                {
                    new() { Path = "/pos-admin/suppliers",              TitleKey = "menu.suppliers",              Icon = "ShopOutlined",               Permission = Permissions.AustralianSuppliers.View },
                    new() { Path = "/pos-admin/products",              TitleKey = "menu.productManagement",      Icon = "AppstoreOutlined",           RequireAdmin = true },
                    new() { Path = "/pos-admin/store-product-price",   TitleKey = "menu.storeProductPrice",      Icon = "DollarOutlined",             Permission = Permissions.StoreProducts.View },
                    new() { Path = "/pos-admin/pricing-strategies",    TitleKey = "menu.pricingStrategies",      Icon = "FileTextOutlined",           Permission = Permissions.PricingStrategy.View },
                    new() { Path = "/pos-admin/promotions",            TitleKey = "menu.promotions",             Icon = "GiftOutlined",               Permission = Permissions.Promotions.View },
                    new() { Path = "/pos-admin/cash-register-users",   TitleKey = "menu.cashRegisterUsers",      Icon = "UserOutlined",               Permission = Permissions.Store.ManageOperations },
                    new() { Path = "/pos-admin/schedule-attendance",   TitleKey = "menu.scheduleAttendance",     Icon = "CalendarOutlined",           Permission = Permissions.Attendance.Schedule.ViewStore },
                    new() { Path = "/pos-admin/sales-orders",          TitleKey = "menu.salesOrders",            Icon = "FileDoneOutlined",           Permission = Permissions.Orders.View },
                    new() { Path = "/pos-admin/local-supplier-invoices", TitleKey = "menu.localSupplierInvoices", Icon = "ReconciliationOutlined",     Permission = Permissions.LocalPurchase.View },
                },
            },
        };

        private static readonly List<AppNavigationDefinition> FullAppMenu = new()
        {
            new()
            {
                RouteName = "home",
                TitleKey = "tabs.home",
                Icon = "home",
                Permission = Permissions.Orders.Create,
                Order = 10,
            },
            new()
            {
                RouteName = "orders",
                TitleKey = "tabs.orders",
                Icon = "clipboard-list",
                Permission = Permissions.Orders.View,
                Order = 20,
            },
            new()
            {
                RouteName = "cart",
                TitleKey = "tabs.cart",
                Icon = "cart-outline",
                Permission = Permissions.Orders.Create,
                Order = 30,
            },
            new()
            {
                RouteName = "warehouse",
                TitleKey = "tabs.warehouse",
                Icon = "warehouse",
                Permission = Permissions.Warehouse.ManageProducts,
                Order = 40,
            },
            new()
            {
                RouteName = "domestic-purchase",
                TitleKey = "tabs.domesticPurchase",
                Icon = "shopping-outline",
                Permission = Permissions.DomesticPurchase.ManageProducts,
                Order = 45,
            },
            new()
            {
                RouteName = "product-query",
                TitleKey = "tabs.productQuery",
                Icon = "barcode-scan",
                Permission = Permissions.StoreProducts.View,
                Order = 50,
            },
            new()
            {
                RouteName = "attendance",
                TitleKey = "tabs.attendance",
                Icon = "calendar-clock",
                Permission = Permissions.Attendance.Schedule.ViewSelf,
                Order = 55,
            },
            new()
            {
                RouteName = "users",
                TitleKey = "tabs.users",
                Icon = "account-group-outline",
                Roles = new[] { "Admin", "管理员", "StoreManager" },
                Permission = Permissions.Users.View,
                Order = 56,
            },
            new()
            {
                RouteName = "employee-profile",
                TitleKey = "tabs.employeeProfile",
                Icon = "card-account-details-outline",
                Permission = Permissions.EmployeeProfiles.View,
                Order = 57,
            },
            new()
            {
                RouteName = "settings",
                TitleKey = "tabs.settings",
                Icon = "account-circle-outline",
                Order = 60,
            },
        };

        private static readonly HashSet<string> DeviceBaseRouteNames = new(
            new[] { "home", "orders", "cart", "product-query", "settings" },
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly HashSet<string> WarehouseDeviceTypes = new(
            new[] { "Warehouse", "PDA-Warehouse", "WarehousePDA", "PDAWarehouse", "仓库", "仓库设备" },
            StringComparer.OrdinalIgnoreCase
        );

        public List<NavigationMenuDto> BuildMenu(ClaimsPrincipal user)
        {
            var isAdmin = user.IsInRole("Admin") || user.IsInRole("管理员");
            if (isAdmin)
            {
                return FullMenu;
            }

            return FilterMenu(FullMenu, user);
        }

        public List<AppNavigationMenuDto> BuildAppMenu(ClaimsPrincipal user)
        {
            return FullAppMenu
                .Where(node => CanAccess(node, user))
                .OrderBy(node => node.Order)
                .Select(ToAppNavigationMenuDto)
                .ToList();
        }

        public List<AppNavigationMenuDto> BuildDeviceAppMenu(string? deviceType)
        {
            var isWarehouseDevice = IsWarehouseDevice(deviceType);

            return FullAppMenu
                .Where(node =>
                    DeviceBaseRouteNames.Contains(node.RouteName)
                    || (isWarehouseDevice && node.RouteName.Equals("warehouse", StringComparison.OrdinalIgnoreCase))
                )
                .OrderBy(node => node.Order)
                .Select(ToAppNavigationMenuDto)
                .ToList();
        }

        private static List<NavigationMenuDto> FilterMenu(List<NavigationMenuDto> nodes, ClaimsPrincipal user)
        {
            var result = new List<NavigationMenuDto>();

            foreach (var node in nodes)
            {
                var hasChildren = node.Children != null && node.Children.Count > 0;
                var filteredChildren = hasChildren
                    ? FilterMenu(node.Children!, user)
                    : null;

                if (hasChildren)
                {
                    if (filteredChildren!.Count > 0)
                    {
                        result.Add(new NavigationMenuDto
                        {
                            Path = node.Path,
                            TitleKey = node.TitleKey,
                            Icon = node.Icon,
                            Permission = node.Permission,
                            Children = filteredChildren,
                        });
                    }
                }
                else
                {
                    if (CanAccess(node, user))
                    {
                        result.Add(new NavigationMenuDto
                        {
                            Path = node.Path,
                            TitleKey = node.TitleKey,
                            Icon = node.Icon,
                            Permission = node.Permission,
                            Children = null,
                        });
                    }
                }
            }

            return result;
        }

        private static bool CanAccess(NavigationMenuDto node, ClaimsPrincipal user)
        {
            if (node.RequireAdmin && !user.IsInRole("Admin") && !user.IsInRole("管理员"))
            {
                return false;
            }

            if (string.IsNullOrEmpty(node.Permission))
            {
                return true;
            }

            if (
                user.HasClaim(ClaimTypes.Role, "WarehouseManager")
                && Permissions.IsWarehouseManagerGranted(node.Permission)
            )
            {
                return true;
            }

            if (user.HasClaim("permission", node.Permission)
                || user.HasClaim(ClaimTypes.Role, "Admin")
                || user.HasClaim(ClaimTypes.Role, "管理员"))
            {
                return true;
            }

            if (node.Permission == "LocalPurchase.View" && user.HasClaim("permission", "LocalInvocie.View"))
                return true;
            if (node.Permission == "LocalPurchase.Edit" && user.HasClaim("permission", "LocalInvocie.Edit"))
                return true;

            return false;
        }

        private static bool CanAccess(AppNavigationDefinition node, ClaimsPrincipal user)
        {
            var isAdmin = user.IsInRole("Admin") || user.IsInRole("管理员");

            if (node.RequireAdmin && !isAdmin)
            {
                return false;
            }

            if (node.Roles.Length > 0 && !node.Roles.Any(user.IsInRole))
            {
                return false;
            }

            if (string.IsNullOrEmpty(node.Permission))
            {
                return true;
            }

            if (isAdmin)
            {
                return true;
            }

            if (
                user.HasClaim(ClaimTypes.Role, "WarehouseManager")
                && Permissions.IsWarehouseManagerGranted(node.Permission)
            )
            {
                return true;
            }

            return user.HasClaim("permission", node.Permission);
        }

        private static bool IsWarehouseDevice(string? deviceType)
        {
            return !string.IsNullOrWhiteSpace(deviceType)
                && WarehouseDeviceTypes.Contains(deviceType.Trim());
        }

        private static AppNavigationMenuDto ToAppNavigationMenuDto(AppNavigationDefinition node)
        {
            return new AppNavigationMenuDto
            {
                RouteName = node.RouteName,
                TitleKey = node.TitleKey,
                Icon = node.Icon,
                Permission = node.Permission,
                Order = node.Order,
            };
        }
    }
}
