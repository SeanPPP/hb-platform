namespace BlazorApp.Shared.Constants
{
    /// <summary>
    /// 系统权限常量定义
    /// 命名规范: {模块}.{操作} (PascalCase)
    /// 前后端共享此规范，前端 hasPermission('Users.View') 对齐后端 [Authorize(Policy = "Users.View")]
    /// </summary>
    public static class Permissions
    {
        public static readonly string[] SuperAdminRoleNames = ["Admin", "管理员"];

        public static class Users
        {
            public const string View = "Users.View";
            public const string Create = "Users.Create";
            public const string Edit = "Users.Edit";
            public const string Delete = "Users.Delete";
            public const string ManageRoles = "Users.ManageRoles";
            public const string ManageStores = "Users.ManageStores";
            public const string ResetPassword = "Users.ResetPassword";
        }

        public static class EmployeeProfiles
        {
            public const string View = "EmployeeProfiles.View";
            public const string Edit = "EmployeeProfiles.Edit";
        }

        public static class Roles
        {
            public const string View = "Roles.View";
            public const string Create = "Roles.Create";
            public const string Edit = "Roles.Edit";
            public const string Delete = "Roles.Delete";
            public const string ManagePermissions = "Roles.ManagePermissions";
            public const string ManageUsers = "Roles.ManageUsers";
        }

        public static class Stores
        {
            public const string View = "Stores.View";
            public const string Create = "Stores.Create";
            public const string Edit = "Stores.Edit";
            public const string Delete = "Stores.Delete";
            public const string Sync = "Stores.Sync";
        }

        public static class Products
        {
            public const string View = "Products.View";
            public const string Create = "Products.Create";
            public const string Edit = "Products.Edit";
            public const string Delete = "Products.Delete";
        }

        public static class PosProducts
        {
            public const string View = "PosProducts.View";
            public const string Manage = "PosProducts.Manage";
        }

        public static class Orders
        {
            public const string View = "Orders.View";
            public const string Create = "Orders.Create";
            public const string Edit = "Orders.Edit";
            public const string Delete = "Orders.Delete";
        }

        public static class InstallmentOrders
        {
            public const string View = "InstallmentOrders.View";
        }

        public static class StoreVouchers
        {
            public const string View = "StoreVouchers.View";
        }

        public static class Container
        {
            public const string View = "Container.View";
            public const string Create = "Container.Create";
            public const string Edit = "Container.Edit";
            public const string Delete = "Container.Delete";
        }

        public static class Warehouse
        {
            public const string View = "Warehouse.View";
            public const string Manage = "Warehouse.Manage";
            public const string ManageProducts = "Warehouse.ManageProducts";
            public const string ManageCategories = "Warehouse.ManageCategories";
            public const string ManageLocations = "Warehouse.ManageLocations";
            public const string ManageOrders = "Warehouse.ManageOrders";
        }

        public static class DomesticPurchase
        {
            public const string View = "DomesticPurchase.View";
            public const string ManageSuppliers = "DomesticPurchase.ManageSuppliers";
            public const string ManageProducts = "DomesticPurchase.ManageProducts";
            public const string ManagePrefixCodes = "DomesticPurchase.ManagePrefixCodes";
        }

        public static class Prices
        {
            public const string View = "Prices.View";
            public const string Modify = "Prices.Modify";
            public const string Delete = "Prices.Delete";
        }

        public static class Reports
        {
            public const string View = "Reports.View";
            public const string Export = "Reports.Export";
            public const string ProductMovementView = "Reports.ProductMovement.View";
        }

        public static class StoreProducts
        {
            public const string View = "StoreProducts.View";
            public const string Create = "StoreProducts.Create";
            public const string Edit = "StoreProducts.Edit";
        }

        public static class Promotions
        {
            public const string View = "Promotions.View";
            public const string Edit = "Promotions.Edit";
        }

        public static class Advertisements
        {
            public const string View = "Advertisements.View";
            public const string Edit = "Advertisements.Edit";
        }

        public static class PricingStrategy
        {
            public const string View = "PricingStrategy.View";
            public const string Edit = "PricingStrategy.Edit";
        }

        public static class DeviceRegistration
        {
            public const string View = "DeviceRegistration.View";
            public const string Manage = "DeviceRegistration.Manage";
        }

        public static class LocalPurchase
        {
            public const string View = "LocalPurchase.View";
            public const string Edit = "LocalPurchase.Edit";
            public const string PushToHq = "LocalPurchase.PushToHq";
        }

        private static readonly IReadOnlyDictionary<string, string[]> PermissionAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [LocalPurchase.View] = ["LocalInvocie.View"],
                [LocalPurchase.Edit] = ["LocalInvocie.Edit"],
                [Reports.ProductMovementView] = [Reports.View],
                // 管理下载权限天然包含查看下载，保证菜单可见性和列表 GET 授权一致。
                [System.ViewAppDownloads] = [System.ManageAppDownloads],
            };

        public static class AustralianSuppliers
        {
            public const string View = "AustralianSuppliers.View";
            public const string Edit = "AustralianSuppliers.Edit";
        }

        public static class Store
        {
            public const string ManageOperations = "Store.ManageOperations";
            public const string ManageInfo = "Store.ManageInfo";
        }

        public static class Attendance
        {
            public static class Schedule
            {
                public const string ViewSelf = "Attendance.Schedule.ViewSelf";
                public const string ViewStore = "Attendance.Schedule.ViewStore";
                public const string EditManagedStore = "Attendance.Schedule.EditManagedStore";
            }

            public static class Availability
            {
                public const string SubmitSelf = "Attendance.Availability.SubmitSelf";
                public const string ViewManagedStore = "Attendance.Availability.ViewManagedStore";
            }

            public static class Punch
            {
                public const string Self = "Attendance.Punch.Self";
                public const string ViewManagedStore = "Attendance.Punch.ViewManagedStore";
            }

            public static class Approval
            {
                public const string ViewManagedStore = "Attendance.Approval.ViewManagedStore";
                public const string ReviewManagedStore = "Attendance.Approval.ReviewManagedStore";
            }

            public static class Holiday
            {
                public const string ViewStore = "Attendance.Holiday.ViewStore";
                public const string EditManagedStore = "Attendance.Holiday.EditManagedStore";
            }

            public static class Leave
            {
                public const string ApplySelf = "Attendance.Leave.ApplySelf";
                public const string ViewManagedStore = "Attendance.Leave.ViewManagedStore";
                public const string ReviewManagedStore = "Attendance.Leave.ReviewManagedStore";
            }

            public static class Settings
            {
                public const string Edit = "Attendance.Settings.Edit";
            }

            public static class Admin
            {
                public const string View = "Attendance.Admin.View";
            }
        }

        public static class SeasonalCards
        {
            public static class Remaining
            {
                public const string ViewManagedStore = "SeasonalCards.Remaining.ViewManagedStore";
                public const string SubmitManagedStore = "SeasonalCards.Remaining.SubmitManagedStore";
            }
        }

        public static class System
        {
            public const string ViewLogs = "System.ViewLogs";
            public const string ManageScheduledTasks = "System.ManageScheduledTasks";
            public const string ManageSettings = "System.ManageSettings";
            public const string ViewAppDownloads = "System.ViewAppDownloads";
            public const string ManageAppDownloads = "System.ManageAppDownloads";
        }

        public static class Dashboard
        {
            public const string View = "Dashboard";
        }

        public static class OrderFront
        {
            public const string View = "OrderFront";
        }

        public static bool IsSuperAdminRole(string? roleName)
        {
            return !string.IsNullOrWhiteSpace(roleName)
                && SuperAdminRoleNames.Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public static IReadOnlyDictionary<string, string[]> GetPermissionAliases()
        {
            return PermissionAliases;
        }

        public static bool IsAttendanceSelfServiceGranted(string? permission)
        {
            return permission is
                Attendance.Schedule.ViewSelf
                or Attendance.Availability.SubmitSelf
                or Attendance.Punch.Self
                or Attendance.Leave.ApplySelf;
        }

        public static IReadOnlyCollection<string> GetEquivalentPermissionCodes(string? permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                return Array.Empty<string>();
            }

            var codes = new List<string> { permission };
            if (PermissionAliases.TryGetValue(permission, out var aliases))
            {
                codes.AddRange(aliases);
            }

            return codes;
        }

        public static IReadOnlyCollection<string> ExpandPermissionCodes(
            IEnumerable<string>? permissions
        )
        {
            if (permissions == null)
            {
                return Array.Empty<string>();
            }

            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var permission in permissions.Where(permission => !string.IsNullOrWhiteSpace(permission)))
            {
                codes.Add(permission);

                foreach (var alias in PermissionAliases)
                {
                    if (alias.Value.Contains(permission, StringComparer.OrdinalIgnoreCase))
                    {
                        codes.Add(alias.Key);
                    }
                }
            }

            return codes.ToList();
        }

        public static IEnumerable<(string Code, string Name, string Category)> GetAllPermissions() =>
            PermissionSeedData.AllPermissions
                .Select(seed => (seed.Code, seed.Name, seed.Category))
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
    }
}
