using System.Security.Claims;
using BlazorApp.Api.Interfaces;
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
        private readonly IRoleService? _roleService;

        private sealed class NavigationPermissionContext
        {
            public bool IsAdmin { get; init; }
            public HashSet<string> PermissionCodes { get; init; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class AppNavigationDefinition
        {
            public string RouteName { get; init; } = string.Empty;
            public string TitleKey { get; init; } = string.Empty;
            public string Icon { get; init; } = string.Empty;
            public string? Permission { get; init; }
            public IReadOnlyCollection<string>? AnyPermissions { get; init; }
            public int Order { get; init; }
        }

        private static readonly string[] AttendanceManagementPermissions =
        {
            Permissions.Attendance.Schedule.ViewStore,
            Permissions.Attendance.Schedule.EditManagedStore,
            Permissions.Attendance.Availability.ViewManagedStore,
            Permissions.Attendance.Punch.ViewManagedStore,
            Permissions.Attendance.Approval.ViewManagedStore,
            Permissions.Attendance.Approval.ReviewManagedStore,
            Permissions.Attendance.Holiday.ViewStore,
            Permissions.Attendance.Holiday.EditManagedStore,
            Permissions.Attendance.Leave.ViewManagedStore,
            Permissions.Attendance.Leave.ReviewManagedStore,
            Permissions.Attendance.Settings.Edit,
            Permissions.Attendance.Admin.View,
        };

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
                Permission = Permissions.Dashboard.View,
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
                    new() { Path = "/system/employee-profiles", TitleKey = "menu.employeeProfiles", Icon = "IdcardOutlined", Permission = Permissions.EmployeeProfiles.View },
                    new() { Path = "/system/roles",    TitleKey = "menu.systemRoles",       Icon = "TeamOutlined",     Permission = Permissions.Roles.View },
                    new() { Path = "/system/permissions", TitleKey = "menu.systemPermissions", Icon = "KeyOutlined", Permission = Permissions.Roles.View },
                    new() { Path = "/system/device-registration", TitleKey = "menu.deviceRegistration", Icon = "BuildOutlined", Permission = Permissions.DeviceRegistration.View },
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
                    new() { Path = "/pos-admin/products",              TitleKey = "menu.productManagement",      Icon = "AppstoreOutlined",           Permission = Permissions.PosProducts.View },
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
                RouteName = "local-supplier-invoices",
                TitleKey = "tabs.localSupplierInvoices",
                Icon = "receipt-text-outline",
                Permission = Permissions.LocalPurchase.View,
                Order = 46,
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
                RouteName = "installment-orders",
                TitleKey = "tabs.installmentOrders",
                Icon = "cash-clock",
                Permission = Permissions.InstallmentOrders.View,
                Order = 51,
            },
            new()
            {
                RouteName = "store-vouchers",
                TitleKey = "tabs.storeVouchers",
                Icon = "ticket-percent-outline",
                Permission = Permissions.StoreVouchers.View,
                Order = 52,
            },
            new()
            {
                RouteName = "attendance-personal",
                TitleKey = "tabs.attendancePersonal",
                Icon = "calendar-clock",
                Permission = Permissions.Attendance.Schedule.ViewSelf,
                Order = 55,
            },
            new()
            {
                RouteName = "attendance-management",
                TitleKey = "tabs.attendanceManagement",
                Icon = "calendar-clock",
                Permission = Permissions.Attendance.Schedule.ViewStore,
                AnyPermissions = AttendanceManagementPermissions,
                Order = 56,
            },
            new()
            {
                RouteName = "seasonal-cards",
                TitleKey = "tabs.seasonalCards",
                Icon = "gift-outline",
                Permission = Permissions.SeasonalCards.Remaining.ViewManagedStore,
                AnyPermissions = new[]
                {
                    Permissions.SeasonalCards.Remaining.ViewManagedStore,
                    Permissions.SeasonalCards.Remaining.SubmitManagedStore,
                },
                Order = 56,
            },
            new()
            {
                RouteName = "users",
                TitleKey = "tabs.users",
                Icon = "account-group-outline",
                Permission = Permissions.Users.View,
                Order = 57,
            },
            new()
            {
                RouteName = "employee-profile",
                TitleKey = "tabs.employeeProfile",
                Icon = "card-account-details-outline",
                Permission = Permissions.EmployeeProfiles.View,
                Order = 58,
            },
            new()
            {
                RouteName = "device-management",
                TitleKey = "tabs.deviceManagement",
                Icon = "cellphone-cog",
                Permission = Permissions.DeviceRegistration.View,
                Order = 59,
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

        public NavigationService()
        {
        }

        public NavigationService(IRoleService roleService)
        {
            _roleService = roleService;
        }

        public List<NavigationMenuDto> BuildMenu(ClaimsPrincipal user)
        {
            var context = ResolvePermissionContext(user);

            if (context.IsAdmin)
            {
                return FullMenu;
            }

            var hasDashboardAccess = HasPermission(context, Permissions.Dashboard.View);
            if (!hasDashboardAccess)
            {
                return new List<NavigationMenuDto>();
            }

            return FilterMenu(FullMenu, context);
        }

        public List<AppNavigationMenuDto> BuildAppMenu(ClaimsPrincipal user)
        {
            var context = ResolvePermissionContext(user);

            if (context.IsAdmin)
            {
                return FullAppMenu.OrderBy(node => node.Order).Select(ToAppNavigationMenuDto).ToList();
            }

            return FullAppMenu
                .Where(node => CanAccess(node, context))
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

        private static List<NavigationMenuDto> FilterMenu(
            List<NavigationMenuDto> nodes,
            NavigationPermissionContext context
        )
        {
            var result = new List<NavigationMenuDto>();

            foreach (var node in nodes)
            {
                var hasChildren = node.Children != null && node.Children.Count > 0;
                var filteredChildren = hasChildren
                    ? FilterMenu(node.Children!, context)
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
                    if (CanAccess(node, context))
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

        private static bool CanAccess(NavigationMenuDto node, NavigationPermissionContext context)
        {
            if (string.IsNullOrEmpty(node.Permission))
            {
                return true;
            }

            if (context.IsAdmin || HasPermission(context, node.Permission))
            {
                return true;
            }

            return false;
        }

        private static bool CanAccess(
            AppNavigationDefinition node,
            NavigationPermissionContext context
        )
        {
            if (string.IsNullOrEmpty(node.Permission) && (node.AnyPermissions == null || node.AnyPermissions.Count == 0))
            {
                return true;
            }

            if (context.IsAdmin)
            {
                return true;
            }

            if (HasPermission(context, node.Permission))
            {
                return true;
            }

            if (
                node.AnyPermissions != null
                && node.AnyPermissions.Any(permission => HasPermission(context, permission))
            )
            {
                return true;
            }

            return false;
        }

        private NavigationPermissionContext ResolvePermissionContext(ClaimsPrincipal user)
        {
            if (_roleService == null)
            {
                return BuildClaimPermissionContext(user);
            }

            var userId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BuildClaimPermissionContext(user);
            }

            var result = _roleService
                .GetUserPermissionSnapshotAsync(userId)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            if (!result.Success || result.Data == null)
            {
                return BuildClaimPermissionContext(user);
            }

            return new NavigationPermissionContext
            {
                IsAdmin = result.Data.IsSuperAdmin,
                PermissionCodes = new HashSet<string>(
                    Permissions.ExpandPermissionCodes(result.Data.PermissionCodes),
                    StringComparer.OrdinalIgnoreCase
                ),
            };
        }

        private static NavigationPermissionContext BuildClaimPermissionContext(ClaimsPrincipal user)
        {
            return new NavigationPermissionContext
            {
                IsAdmin = user.IsInRole("Admin") || user.IsInRole("管理员"),
                PermissionCodes = new HashSet<string>(
                    Permissions.ExpandPermissionCodes(
                        user.Claims
                            .Where(claim =>
                                string.Equals(
                                    claim.Type,
                                    "permission",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            .Select(claim => claim.Value)
                    ),
                    StringComparer.OrdinalIgnoreCase
                ),
            };
        }

        private static string? GetUserId(ClaimsPrincipal user)
        {
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("nameidentifier")?.Value
                ?? user.FindFirst("userId")?.Value
                ?? user.FindFirst("userGuid")?.Value
                ?? user.FindFirst(ClaimTypes.Name)?.Value
                ?? user.FindFirst("sub")?.Value;
        }

        private static bool HasPermission(NavigationPermissionContext context, string? permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                return false;
            }

            return Permissions.GetEquivalentPermissionCodes(permission)
                .Any(code => context.PermissionCodes.Contains(code));
        }

        private static bool HasPermissionClaim(ClaimsPrincipal user, string? permission)
        {
            return Permissions.GetEquivalentPermissionCodes(permission)
                .Any(code => user.HasClaim("permission", code));
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
