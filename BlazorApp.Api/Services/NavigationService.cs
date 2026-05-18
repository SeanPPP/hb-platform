using System.Security.Claims;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services
{
    public interface INavigationService
    {
        List<NavigationMenuDto> BuildMenu(ClaimsPrincipal user);
    }

    public class NavigationService : INavigationService
    {
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
                    new() { Path = "/pos-admin/sales-orders",          TitleKey = "menu.salesOrders",            Icon = "FileDoneOutlined",           Permission = Permissions.Orders.View },
                    new() { Path = "/pos-admin/local-supplier-invoices", TitleKey = "menu.localSupplierInvoices", Icon = "ReconciliationOutlined",     Permission = Permissions.LocalPurchase.View },
                },
            },
        };

        public List<NavigationMenuDto> BuildMenu(ClaimsPrincipal user)
        {
            var isAdmin = user.IsInRole("Admin") || user.IsInRole("管理员");
            if (isAdmin)
            {
                return FullMenu;
            }

            return FilterMenu(FullMenu, user);
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
    }
}
